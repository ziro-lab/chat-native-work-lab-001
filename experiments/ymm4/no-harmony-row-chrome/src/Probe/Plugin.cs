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

namespace Ymm4NoHarmonyRowChromeProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL YMM4 No-Harmony Row Chrome Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled,running;
    static string output="";
    static Timeline? timeline;
    static TimelineViewModel? vm;
    static FrameworkElement? timelineView;
    static ScrollViewer? timelineScroll;
    static int h;

    const int FoldStart=1;
    const int FoldEnd=10;
    const int HiddenCount=FoldEnd-FoldStart;

    public static void Schedule()
    {
        var dir=Environment.GetEnvironmentVariable("CNWL_YMM4_NO_HARMONY_ROW_CHROME_DIR");
        if(scheduled||string.IsNullOrWhiteSpace(dir))return;
        scheduled=true;
        output=Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
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

            var ch=new Character{Name="CNWL_ROW_CHROME"};
            var top=new VoiceItem(ch){Frame=20,Length=40,Layer=1,Serif="top",Remark="CNWL_ROW_TOP"};
            var hidden=new VoiceItem(ch){Frame=100,Length=40,Layer=5,Serif="hidden",Remark="CNWL_ROW_HIDDEN"};
            var low=new VoiceItem(ch){Frame=180,Length=40,Layer=20,Serif="low",Remark="CNWL_ROW_LOW"};
            foreach(var item in new IItem[]{top,hidden,low})
                if(!t.TryAddItems([item],item.Frame,item.Layer))
                    throw new InvalidOperationException("fixture insert failed");

            t.RefreshTimelineLengthAndMaxLayer();
            t.CurrentFrame=0;
            await Task.Delay(1400);

            timelineView=FindLargest(mainWindow,x=>x.GetType().Name=="TimelineView")
                ??throw new InvalidOperationException("TimelineView missing");

            timelineScroll=Elements(timelineView)
                .OfType<ScrollViewer>()
                .Where(x=>x.IsVisible&&x.ActualHeight>50)
                .OrderByDescending(x=>x.ActualWidth*x.ActualHeight)
                .FirstOrDefault()
                ??throw new InvalidOperationException("Timeline ScrollViewer missing");

            h=SettingsBase<YMMSettings>.Default.LayerHeight;
            if(h<=0)throw new InvalidOperationException("LayerHeight invalid");

            var labels=GetList(viewModel,"LayerLabels")
                ??throw new MissingMemberException("TimelineViewModel.LayerLabels");
            var lines=GetList(viewModel,"LayerLines")
                ??throw new MissingMemberException("TimelineViewModel.LayerLines");

            var nativeExtent=timelineScroll.ExtentHeight;
            var nativeScrollable=timelineScroll.ScrollableHeight;
            var nativeLabelCount=labels.Count;
            var nativeLineCount=lines.Count;

            var labelSample=labels.Count>20?labels[20]:labels.Cast<object?>().LastOrDefault(x=>x is not null);
            var lineSample=lines.Count>20?lines[20]:lines.Cast<object?>().LastOrDefault(x=>x is not null);
            if(labelSample is null||lineSample is null)
                throw new InvalidOperationException("row samples missing");

            var labelTop=GetProperty(labelSample,"Top")??throw new MissingMemberException("label.Top");
            var labelHeight=GetProperty(labelSample,"Height")??throw new MissingMemberException("label.Height");
            var lineTop=GetProperty(lineSample,"Top")??throw new MissingMemberException("line.Top");
            var lineHeight=GetProperty(lineSample,"Height")??throw new MissingMemberException("line.Height");

            var labelTopSetter=labelTop.GetSetMethod(true);
            var labelHeightSetter=labelHeight.GetSetMethod(true);
            var lineTopSetter=lineTop.GetSetMethod(true);
            var lineHeightSetter=lineHeight.GetSetMethod(true);
            if(labelTopSetter is null||labelHeightSetter is null||lineTopSetter is null||lineHeightSetter is null)
                throw new MissingMethodException("row Top/Height setter missing");

            var itemTopSet=0;
            var itemHeightSet=0;
            foreach(var itemVm in viewModel.Items)
            {
                var item=itemVm.Item;
                var topValue=MappedTop(item.Layer);
                SetPrivateProperty(itemVm,"Top",topValue);
                itemTopSet++;

                if(IsHiddenLayer(item.Layer))
                {
                    SetPrivateProperty(itemVm,"Height",6.0);
                    itemHeightSet++;
                }
            }

            var labelSet=ApplyRows(labels,true);
            var lineSet=ApplyRows(lines,false);

            var updateCount=ForceFastCanvasUpdateAll(timelineView);
            await Task.Delay(650);

            // Scroll to the compressed location of logical L20 and force one more virtualization pass.
            var expectedLowY=RowOf(low.Layer)*h;
            viewModel.Viewport.Value=new Rect(
                new Point(viewModel.Viewport.Value.X,expectedLowY),
                viewModel.Viewport.Value.Size);
            updateCount+=ForceFastCanvasUpdateAll(timelineView);
            await Task.Delay(650);

            var afterExtent=timelineScroll.ExtentHeight;
            var afterScrollable=timelineScroll.ScrollableHeight;

            var label20=labels.Count>20?labels[20]:null;
            var line20=lines.Count>20?lines[20]:null;
            var label5=labels.Count>5?labels[5]:null;
            var line5=lines.Count>5?lines[5]:null;

            var label20Top=label20 is null?double.NaN:Convert.ToDouble(GetProperty(label20,"Top")?.GetValue(label20),CultureInfo.InvariantCulture);
            var line20Top=line20 is null?double.NaN:Convert.ToDouble(GetProperty(line20,"Top")?.GetValue(line20),CultureInfo.InvariantCulture);
            var label5Height=label5 is null?double.NaN:Convert.ToDouble(GetProperty(label5,"Height")?.GetValue(label5),CultureInfo.InvariantCulture);
            var line5Height=line5 is null?double.NaN:Convert.ToDouble(GetProperty(line5,"Height")?.GetValue(line5),CultureInfo.InvariantCulture);

            var lowView=FindItemView(timelineView,low);
            var lowRealized=lowView is not null&&lowView.ActualWidth>2&&lowView.ActualHeight>2;

            var expectedShrink=HiddenCount*h;
            var extentShrink=nativeExtent-afterExtent;
            var extentShrankMeaningfully=extentShrink>expectedShrink*0.5;

            File.WriteAllLines(
                Path.Combine(output,"geometry.txt"),
                [
                    $"layer_height={h}",
                    $"fold={FoldStart}-{FoldEnd}",
                    $"hidden_count={HiddenCount}",
                    $"native_extent={nativeExtent:F2}",
                    $"after_extent={afterExtent:F2}",
                    $"extent_shrink={extentShrink:F2}",
                    $"expected_nominal_shrink={expectedShrink}",
                    $"native_scrollable={nativeScrollable:F2}",
                    $"after_scrollable={afterScrollable:F2}",
                    $"label_count={labels.Count}",
                    $"line_count={lines.Count}",
                    $"label20_top={label20Top:F2}",
                    $"line20_top={line20Top:F2}",
                    $"expected_l20_top={expectedLowY:F2}",
                    $"label5_height={label5Height:F2}",
                    $"line5_height={line5Height:F2}",
                    $"viewport={viewModel.Viewport.Value}"
                ],
                new UTF8Encoding(false));

            WriteResult(
                "PASS_NO_HARMONY_ROW_CHROME_OBSERVATION",
                [
                    "harmony_reference_present=False",
                    $"label_top_setter_nonpublic={!labelTopSetter.IsPublic}",
                    $"label_height_setter_nonpublic={!labelHeightSetter.IsPublic}",
                    $"line_top_setter_nonpublic={!lineTopSetter.IsPublic}",
                    $"line_height_setter_nonpublic={!lineHeightSetter.IsPublic}",
                    $"native_label_count={nativeLabelCount}",
                    $"native_line_count={nativeLineCount}",
                    $"item_top_set_count={itemTopSet}",
                    $"item_hidden_height_set_count={itemHeightSet}",
                    $"label_rows_set={labelSet}",
                    $"line_rows_set={lineSet}",
                    $"fast_canvas_update_all_invocations={updateCount}",
                    $"label20_matches_folded_top={!double.IsNaN(label20Top)&&Math.Abs(label20Top-expectedLowY)<0.5}",
                    $"line20_matches_folded_top={!double.IsNaN(line20Top)&&Math.Abs(line20Top-expectedLowY)<0.5}",
                    $"hidden_label_height_zero={!double.IsNaN(label5Height)&&Math.Abs(label5Height)<0.5}",
                    $"hidden_line_height_zero={!double.IsNaN(line5Height)&&Math.Abs(line5Height)<0.5}",
                    $"low_item_realized_at_folded_viewport={lowRealized}",
                    $"extent_shrank_meaningfully={extentShrankMeaningfully}"
                ]);
        }
        catch(Exception ex)
        {
            Fail(ex);
        }
    }

    static int ApplyRows(IList rows,bool isLabel)
    {
        var set=0;
        for(var layer=0;layer<rows.Count;layer++)
        {
            if(rows[layer] is not object row)continue;

            var top=IsHiddenLayer(layer)
                ? -100000.0-layer*h
                : RowOf(layer)*h;
            var height=IsHiddenLayer(layer)?0.0:h;

            SetPrivateProperty(row,"Top",top);
            SetPrivateProperty(row,"Height",height);
            set++;
        }
        return set;
    }

    static bool IsHiddenLayer(int layer)=>layer>FoldStart&&layer<=FoldEnd;

    static double MappedTop(int layer)=>
        IsHiddenLayer(layer)?-100000.0-layer*h:RowOf(layer)*h;

    static int RowOf(int layer)
    {
        if(layer<=FoldStart)return layer;
        if(layer<=FoldEnd)return FoldStart;
        return layer-HiddenCount;
    }

    static IList? GetList(object instance,string name)
    {
        try
        {
            return instance.GetType()
                .GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                ?.GetValue(instance) as IList;
        }
        catch{return null;}
    }

    static PropertyInfo? GetProperty(object instance,string name)=>
        instance.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);

    static void SetPrivateProperty(object instance,string name,object value)
    {
        var prop=GetProperty(instance,name)??throw new MissingMemberException(instance.GetType().FullName,name);
        prop.SetValue(instance,value);
    }

    static int ForceFastCanvasUpdateAll(DependencyObject root)
    {
        var count=0;
        foreach(var fe in Elements(root))
        {
            if(fe.GetType().Name!="FastCanvasItemsControl")continue;
            var method=fe.GetType().GetMethod("UpdateAll",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(method is null)continue;
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
        if(root is FrameworkElement fe)yield return fe;
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
