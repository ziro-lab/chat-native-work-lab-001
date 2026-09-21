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

namespace Ymm4NoHarmonyCanvasHeightProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL YMM4 No-Harmony Canvas Height Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
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
        var dir=Environment.GetEnvironmentVariable("CNWL_YMM4_NO_HARMONY_CANVAS_HEIGHT_DIR");
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

            var ch=new Character{Name="CNWL_CANVAS_HEIGHT"};
            var top=new VoiceItem(ch){Frame=20,Length=40,Layer=1,Serif="top",Remark="CNWL_CANVAS_TOP"};
            var hidden=new VoiceItem(ch){Frame=100,Length=40,Layer=5,Serif="hidden",Remark="CNWL_CANVAS_HIDDEN"};
            var low=new VoiceItem(ch){Frame=180,Length=40,Layer=20,Serif="low",Remark="CNWL_CANVAS_LOW"};
            foreach(var item in new IItem[]{top,hidden,low})
                if(!t.TryAddItems([item],item.Frame,item.Layer))
                    throw new InvalidOperationException("fixture insert failed");

            t.RefreshTimelineLengthAndMaxLayer();
            t.CurrentFrame=0;
            await Task.Delay(1400);

            timelineView=FindLargest(mainWindow,x=>x.GetType().Name=="TimelineView")
                ??throw new InvalidOperationException("TimelineView missing");
            scrollViewer=Elements(timelineView)
                .OfType<ScrollViewer>()
                .Where(x=>x.IsVisible&&x.ActualHeight>50)
                .OrderByDescending(x=>x.ActualWidth*x.ActualHeight)
                .FirstOrDefault()
                ??throw new InvalidOperationException("Timeline ScrollViewer missing");

            h=SettingsBase<YMMSettings>.Default.LayerHeight;
            if(h<=0)throw new InvalidOperationException("LayerHeight invalid");

            var labels=GetList(viewModel,"LayerLabels")
                ??throw new MissingMemberException("LayerLabels");
            var lines=GetList(viewModel,"LayerLines")
                ??throw new MissingMemberException("LayerLines");

            var nativeExtent=scrollViewer.ExtentHeight;
            var nativeCanvasHeight=viewModel.CanvasHeight.Value;
            var expectedDisplayRows=Math.Max(1,labels.Count-HiddenCount);
            var expectedCanvasHeight=expectedDisplayRows*h;

            ApplyItemGeometry(viewModel);
            ApplyRows(labels);
            ApplyRows(lines);
            var updateCount=ForceFastCanvasUpdateAll(timelineView);
            await Task.Delay(400);

            var preCanvasCorrectionExtent=scrollViewer.ExtentHeight;

            var holder=(object)viewModel.CanvasHeight;
            var valueProp=holder.GetType().GetProperty("Value",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            var setter=valueProp?.GetSetMethod(true);
            var canSet=valueProp is not null&&setter is not null;

            string setError="";
            if(canSet)
            {
                try
                {
                    valueProp!.SetValue(holder,(double)expectedCanvasHeight);
                }
                catch(Exception ex)
                {
                    setError=ex.GetBaseException().GetType().Name+":"+ex.GetBaseException().Message;
                    canSet=false;
                }
            }

            updateCount+=ForceFastCanvasUpdateAll(timelineView);
            await Task.Delay(650);

            var afterCanvasHeight=viewModel.CanvasHeight.Value;
            var afterExtent=scrollViewer.ExtentHeight;
            var afterScrollable=scrollViewer.ScrollableHeight;

            var extentMatches=Math.Abs(afterExtent-expectedCanvasHeight)<1.0;
            var canvasMatches=Math.Abs(afterCanvasHeight-expectedCanvasHeight)<1.0;

            // See whether a normal Timeline refresh overwrites the manual compressed height.
            t.RefreshTimelineLengthAndMaxLayer();
            await Task.Delay(500);
            var afterRefreshCanvasHeight=viewModel.CanvasHeight.Value;
            var afterRefreshExtent=scrollViewer.ExtentHeight;
            var hostOverwrote=Math.Abs(afterRefreshCanvasHeight-expectedCanvasHeight)>1.0;

            File.WriteAllLines(
                Path.Combine(output,"geometry.txt"),
                [
                    $"layer_height={h}",
                    $"label_count={labels.Count}",
                    $"hidden_count={HiddenCount}",
                    $"expected_display_rows={expectedDisplayRows}",
                    $"native_canvas_height={nativeCanvasHeight:F2}",
                    $"native_extent={nativeExtent:F2}",
                    $"pre_canvas_correction_extent={preCanvasCorrectionExtent:F2}",
                    $"expected_canvas_height={expectedCanvasHeight:F2}",
                    $"after_canvas_height={afterCanvasHeight:F2}",
                    $"after_extent={afterExtent:F2}",
                    $"after_scrollable={afterScrollable:F2}",
                    $"after_refresh_canvas_height={afterRefreshCanvasHeight:F2}",
                    $"after_refresh_extent={afterRefreshExtent:F2}",
                    $"canvas_holder_type={holder.GetType().FullName}",
                    $"canvas_value_set_error={setError}"
                ],
                new UTF8Encoding(false));

            WriteResult(
                "PASS_NO_HARMONY_CANVAS_HEIGHT_OBSERVATION",
                [
                    "harmony_reference_present=False",
                    $"canvas_height_property_public={typeof(TimelineViewModel).GetProperty("CanvasHeight")?.GetGetMethod(true)?.IsPublic==true}",
                    $"canvas_value_setter_found={setter is not null}",
                    $"canvas_value_setter_public={setter?.IsPublic==true}",
                    $"canvas_value_set_succeeded={canSet}",
                    $"canvas_height_matches_folded_extent={canvasMatches}",
                    $"scroll_extent_matches_folded_extent={extentMatches}",
                    $"host_overwrote_canvas_height_after_refresh={hostOverwrote}",
                    $"fast_canvas_update_all_invocations={updateCount}"
                ]);
        }
        catch(Exception ex)
        {
            Fail(ex);
        }
    }

    static void ApplyItemGeometry(TimelineViewModel viewModel)
    {
        foreach(var itemVm in viewModel.Items)
        {
            var item=itemVm.Item;
            var top=IsHiddenLayer(item.Layer)
                ? -100000.0-item.Layer*h
                : RowOf(item.Layer)*h;
            SetPrivateProperty(itemVm,"Top",top);
            if(IsHiddenLayer(item.Layer))
                SetPrivateProperty(itemVm,"Height",6.0);
        }
    }

    static void ApplyRows(IList rows)
    {
        for(var layer=0;layer<rows.Count;layer++)
        {
            if(rows[layer] is not object row)continue;
            SetPrivateProperty(
                row,
                "Top",
                IsHiddenLayer(layer)?-100000.0-layer*h:RowOf(layer)*h);
            SetPrivateProperty(
                row,
                "Height",
                IsHiddenLayer(layer)?0.0:(double)h);
        }
    }

    static bool IsHiddenLayer(int layer)=>layer>FoldStart&&layer<=FoldEnd;

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

    static void SetPrivateProperty(object instance,string name,object value)
    {
        var prop=instance.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
            ??throw new MissingMemberException(instance.GetType().FullName,name);
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
