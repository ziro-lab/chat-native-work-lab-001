using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4NoHarmonyDirectLayoutProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL YMM4 No-Harmony Direct Layout Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal readonly record struct ScreenBox(double Left,double Top,double Width,double Height)
{
    public double Right=>Left+Width;
    public double Bottom=>Top+Height;
    public bool Valid=>Width>2&&Height>2;
    public bool Intersects(ScreenBox other)=>Left<other.Right&&Right>other.Left&&Top<other.Bottom&&Bottom>other.Top;
}

internal static class Probe
{
    static bool scheduled,running;
    static string output="";
    static Timeline? timeline;
    static TimelineViewModel? vm;
    static FrameworkElement? timelineView;
    static ScrollViewer? scrollViewer;
    static int h;

    const int FoldStart=1;
    const int FoldEnd=10;
    const int HiddenCount=FoldEnd-FoldStart;

    public static void Schedule()
    {
        var dir=Environment.GetEnvironmentVariable("CNWL_YMM4_NO_HARMONY_DIRECT_LAYOUT_DIR");
        if(scheduled||string.IsNullOrWhiteSpace(dir))return;
        scheduled=true;output=Path.GetFullPath(dir);Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap),DispatcherPriority.ApplicationIdle);
    }

    static void Bootstrap()
    {
        var ticks=0;
        var created=false;
        var timer=new DispatcherTimer(DispatcherPriority.ApplicationIdle){Interval=TimeSpan.FromMilliseconds(400)};
        timer.Tick+=(_,_)=>{
            try
            {
                ticks++;
                foreach(Window w in Application.Current.Windows)
                {
                    var main=w.DataContext;
                    if(main?.GetType().FullName!="YukkuriMovieMaker.ViewModels.MainViewModel")continue;
                    var active=main.GetType()
                        .GetProperty("ActiveTimelineViewModel",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                        ?.GetValue(main);

                    if(active is null&&!created)
                    {
                        created=true;
                        main.GetType().GetMethod("CreateProject",Type.EmptyTypes)?.Invoke(main,null);
                        return;
                    }

                    if(active is not TimelineViewModel typed||running)continue;

                    var t=active.GetType()
                        .GetProperty("Timeline",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                        ?.GetValue(active) as Timeline
                        ?? active.GetType()
                            .GetField("timeline",BindingFlags.Instance|BindingFlags.NonPublic)
                            ?.GetValue(active) as Timeline;
                    if(t is null)continue;

                    running=true;
                    timer.Stop();
                    timeline=t;
                    vm=typed;
                    _=RunAsync(w,t,typed);
                    return;
                }

                if(ticks>100)
                {
                    timer.Stop();
                    WriteResult("FAIL_BOOTSTRAP_TIMEOUT",[]);
                }
            }
            catch(Exception ex)
            {
                timer.Stop();
                Fail(ex);
            }
        };
        timer.Start();
    }

    static async Task RunAsync(Window mainWindow,Timeline t,TimelineViewModel viewModel)
    {
        try
        {
            mainWindow.WindowState=System.Windows.WindowState.Maximized;
            mainWindow.Activate();
            await Task.Delay(900);

            var ch=new Character{Name="CNWL_DIRECT_LAYOUT"};
            var head=new VoiceItem(ch){Frame=20,Length=50,Layer=1,Serif="head",Remark="CNWL_DIRECT_HEAD"};
            var hidden=new VoiceItem(ch){Frame=120,Length=50,Layer=5,Serif="hidden",Remark="CNWL_DIRECT_HIDDEN"};
            var target=new VoiceItem(ch){Frame=220,Length=80,Layer=20,Serif="target",Remark="CNWL_DIRECT_TARGET"};

            foreach(var item in new IItem[]{head,hidden,target})
                if(!t.TryAddItems([item],item.Frame,item.Layer))
                    throw new InvalidOperationException("fixture insert failed: "+item.Remark);

            t.CurrentFrame=0;
            await Task.Delay(1400);

            timelineView=FindLargest(mainWindow,x=>x.GetType().Name=="TimelineView")
                ??throw new InvalidOperationException("TimelineView missing");
            scrollViewer=FindLargest(timelineView,x=>x is ScrollViewer) as ScrollViewer
                ??throw new InvalidOperationException("ScrollViewer missing");
            h=SettingsBase<YMMSettings>.Default.LayerHeight;
            if(h<=0)throw new InvalidOperationException("LayerHeight invalid");

            var targetVm=FindItemVm(viewModel,target)??throw new InvalidOperationException("target VM missing");
            var hiddenVm=FindItemVm(viewModel,hidden)??throw new InvalidOperationException("hidden VM missing");

            var topProp=targetVm.GetType().GetProperty("Top",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                ??throw new MissingMemberException("TimelineItemViewModel.Top");
            var heightProp=targetVm.GetType().GetProperty("Height",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                ??throw new MissingMemberException("TimelineItemViewModel.Height");

            var topSetter=topProp.GetSetMethod(true);
            var heightSetter=heightProp.GetSetMethod(true);
            if(topSetter is null||heightSetter is null)
                throw new MissingMethodException("Top/Height setter missing");

            var targetNativeTop=Convert.ToDouble(topProp.GetValue(targetVm),CultureInfo.InvariantCulture);
            var hiddenNativeTop=Convert.ToDouble(topProp.GetValue(hiddenVm),CultureInfo.InvariantCulture);
            var targetExpectedTop=RowOf(target.Layer)*h;

            // Native ScrollToItem establishes the uncompressed host baseline.
            viewModel.ScrollToItem(target);
            await Task.Delay(450);
            var nativeViewport=viewModel.Viewport.Value;
            var nativeTargetView=FindItemView(timelineView,target);
            var nativeTargetRealized=nativeTargetView is not null && Box(nativeTargetView).Valid;

            // Return to top before rewriting geometry.
            viewModel.Viewport.Value=new Rect(new Point(0,0),viewModel.Viewport.Value.Size);
            await Task.Delay(250);

            var setTopCount=0;
            var setHeightCount=0;
            foreach(var itemVm in viewModel.Items)
            {
                if(itemVm.Item is not IItem item)
                    continue;

                var top=item.Layer>FoldEnd
                    ? RowOf(item.Layer)*h
                    : item.Layer>FoldStart
                        ? -100000.0-item.Layer*h
                        : item.Layer*h;

                topProp.SetValue(itemVm,top);
                setTopCount++;

                if(item.Layer>FoldStart&&item.Layer<=FoldEnd)
                {
                    heightProp.SetValue(itemVm,6.0);
                    setHeightCount++;
                }
            }

            var targetAfterSet=Convert.ToDouble(topProp.GetValue(targetVm),CultureInfo.InvariantCulture);
            var hiddenAfterSet=Convert.ToDouble(topProp.GetValue(hiddenVm),CultureInfo.InvariantCulture);
            var hiddenHeightAfterSet=Convert.ToDouble(heightProp.GetValue(hiddenVm),CultureInfo.InvariantCulture);

            var updateAllCount=ForceFastCanvasUpdateAll(timelineView);
            await Task.Delay(350);

            var correctedY=targetExpectedTop;
            viewModel.Viewport.Value=new Rect(
                new Point(viewModel.Viewport.Value.X,correctedY),
                viewModel.Viewport.Value.Size);
            updateAllCount+=ForceFastCanvasUpdateAll(timelineView);
            await Task.Delay(650);

            var correctedViewport=viewModel.Viewport.Value;
            var correctedTargetView=FindItemView(timelineView,target);
            var correctedTargetRealized=correctedTargetView is not null && Box(correctedTargetView).Valid;

            var viewportScreen=Box(scrollViewer);
            var correctedTargetVisible=correctedTargetView is not null &&
                Box(correctedTargetView).Valid &&
                Box(correctedTargetView).Intersects(viewportScreen);

            var hiddenView=FindItemView(timelineView,hidden);
            var hiddenStillRealized=hiddenView is not null && Box(hiddenView).Valid;

            File.WriteAllLines(
                Path.Combine(output,"geometry.txt"),
                [
                    $"layer_height={h}",
                    $"target_native_top={targetNativeTop:F2}",
                    $"target_expected_folded_top={targetExpectedTop:F2}",
                    $"target_after_set={targetAfterSet:F2}",
                    $"hidden_native_top={hiddenNativeTop:F2}",
                    $"hidden_after_set={hiddenAfterSet:F2}",
                    $"hidden_height_after_set={hiddenHeightAfterSet:F2}",
                    $"native_viewport={Fmt(nativeViewport)}",
                    $"corrected_viewport={Fmt(correctedViewport)}",
                    $"viewport_screen={Fmt(viewportScreen)}",
                    $"corrected_target_screen={(correctedTargetView is null?"<missing>":Fmt(Box(correctedTargetView)))}"
                ],
                new UTF8Encoding(false));

            WriteResult(
                "PASS_NO_HARMONY_DIRECT_LAYOUT_OBSERVATION",
                [
                    "harmony_reference_present=False",
                    $"top_setter_nonpublic={!topSetter.IsPublic}",
                    $"height_setter_nonpublic={!heightSetter.IsPublic}",
                    $"item_vm_count={viewModel.Items.Count}",
                    $"set_top_count={setTopCount}",
                    $"set_hidden_height_count={setHeightCount}",
                    $"fast_canvas_update_all_invocations={updateAllCount}",
                    $"native_target_realized={nativeTargetRealized}",
                    $"target_top_matches_folded_row={Math.Abs(targetAfterSet-targetExpectedTop)<0.5}",
                    $"hidden_top_moved_far_offscreen={hiddenAfterSet < -90000}",
                    $"hidden_height_is_safe_strip={Math.Abs(hiddenHeightAfterSet-6.0)<0.5}",
                    $"corrected_viewport_matches_folded_row={Math.Abs(correctedViewport.Y-correctedY)<0.5}",
                    $"corrected_target_realized={correctedTargetRealized}",
                    $"corrected_target_visible={correctedTargetVisible}",
                    $"hidden_item_not_realized={!hiddenStillRealized}"
                ]);
        }
        catch(Exception ex)
        {
            Fail(ex);
        }
    }

    static int RowOf(int layer)
    {
        if(layer<=FoldStart)return layer;
        if(layer<=FoldEnd)return FoldStart;
        return layer-HiddenCount;
    }

    static TimelineItemViewModel? FindItemVm(TimelineViewModel viewModel,IItem item)=>
        viewModel.Items.FirstOrDefault(x=>ReferenceEquals(x.Item,item));

    static int ForceFastCanvasUpdateAll(DependencyObject root)
    {
        var count=0;
        foreach(var fe in Elements(root))
        {
            if(fe.GetType().Name!="FastCanvasItemsControl")
                continue;

            var method=fe.GetType().GetMethod(
                "UpdateAll",
                BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(method is null)
                continue;

            method.Invoke(fe,null);
            count++;
        }
        return count;
    }

    static FrameworkElement? FindItemView(DependencyObject root,IItem item)=>
        Elements(root)
            .Where(x=>x.IsVisible&&x.GetType().Name=="TimelineItemView"&&ReferenceEquals(ItemOf(x.DataContext),item))
            .OrderByDescending(x=>x.ActualWidth*x.ActualHeight)
            .FirstOrDefault();

    static FrameworkElement? FindLargest(DependencyObject root,Func<FrameworkElement,bool> predicate)=>
        Elements(root)
            .Where(x=>x.IsVisible&&x.ActualWidth>5&&x.ActualHeight>5&&predicate(x))
            .OrderByDescending(x=>x.ActualWidth*x.ActualHeight)
            .FirstOrDefault();

    static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        if(root is FrameworkElement fe)
            yield return fe;

        int count;
        try{count=VisualTreeHelper.GetChildrenCount(root);}
        catch{yield break;}

        for(var i=0;i<count;i++)
            foreach(var child in Elements(VisualTreeHelper.GetChild(root,i)))
                yield return child;
    }

    static IItem? ItemOf(object? dc)
    {
        if(dc is IItem direct)return direct;
        if(dc is null)return null;

        foreach(var p in dc.GetType()
            .GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
            .Where(p=>p.GetIndexParameters().Length==0&&p.CanRead&&
                (typeof(IItem).IsAssignableFrom(p.PropertyType)||
                 p.Name.Contains("Item",StringComparison.OrdinalIgnoreCase)||
                 p.Name is "Model" or "Source" or "Value"))
            .Take(40))
        {
            try
            {
                var value=p.GetValue(dc);
                if(value is IItem item)return item;
                if(value is IEnumerable enumerable&&value is not string)
                    foreach(var x in enumerable)
                        if(x is IItem nested)return nested;
            }
            catch{}
        }
        return null;
    }

    static ScreenBox Box(FrameworkElement fe)
    {
        try
        {
            var p=fe.PointToScreen(new Point());
            return new ScreenBox(p.X,p.Y,fe.ActualWidth,fe.ActualHeight);
        }
        catch{return default;}
    }

    static string Fmt(Rect r)=>$"{r.X:F2},{r.Y:F2},{r.Width:F2},{r.Height:F2}";
    static string Fmt(ScreenBox b)=>$"{b.Left:F2},{b.Top:F2},{b.Width:F2},{b.Height:F2}";

    static void WriteResult(string status,IEnumerable<string> details)=>
        File.WriteAllLines(
            Path.Combine(output,"result.txt"),
            new[]{"status="+status}.Concat(details),
            new UTF8Encoding(false));

    static void Fail(Exception ex)
    {
        try
        {
            File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString(),new UTF8Encoding(false));
            WriteResult("FAIL_EXCEPTION",["message="+ex.GetBaseException().Message]);
        }
        catch{}
    }
}
