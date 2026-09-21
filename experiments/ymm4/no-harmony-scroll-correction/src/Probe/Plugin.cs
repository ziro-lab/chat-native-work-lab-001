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

namespace Ymm4NoHarmonyScrollCorrectionProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL YMM4 No-Harmony Scroll Correction Probe";
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
    static int h;
    const int FoldStart=1;
    const int FoldEnd=10;
    const int HiddenCount=FoldEnd-FoldStart;

    public static void Schedule()
    {
        var dir=Environment.GetEnvironmentVariable("CNWL_YMM4_NO_HARMONY_SCROLL_DIR");
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
            try{
                ticks++;
                foreach(Window w in Application.Current.Windows){
                    var main=w.DataContext;
                    if(main?.GetType().FullName!="YukkuriMovieMaker.ViewModels.MainViewModel")continue;
                    var active=main.GetType().GetProperty("ActiveTimelineViewModel",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(main);
                    if(active is null&&!created){created=true;main.GetType().GetMethod("CreateProject",Type.EmptyTypes)?.Invoke(main,null);return;}
                    if(active is not TimelineViewModel typed||running)continue;
                    var t=active.GetType().GetProperty("Timeline",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(active) as Timeline
                        ?? active.GetType().GetField("timeline",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                    if(t is null)continue;
                    running=true;timer.Stop();timeline=t;vm=typed;_=RunAsync(w,t,typed);return;
                }
                if(ticks>100){timer.Stop();WriteResult("FAIL_BOOTSTRAP_TIMEOUT",[]);}
            }catch(Exception ex){timer.Stop();Fail(ex);}
        };
        timer.Start();
    }

    static async Task RunAsync(Window mainWindow,Timeline t,TimelineViewModel viewModel)
    {
        try{
            mainWindow.WindowState=System.Windows.WindowState.Maximized;
            mainWindow.Activate();
            await Task.Delay(900);

            var ch=new Character{Name="CNWL_SCROLL"};
            var top=new VoiceItem(ch){Frame=20,Length=30,Layer=1,Serif="top",Remark="CNWL_SCROLL_TOP"};
            var target=new VoiceItem(ch){Frame=100,Length=80,Layer=20,Serif="target",Remark="CNWL_SCROLL_TARGET"};
            if(!t.TryAddItems([top],top.Frame,top.Layer)||!t.TryAddItems([target],target.Frame,target.Layer))
                throw new InvalidOperationException("fixture insert failed");
            t.CurrentFrame=0;
            await Task.Delay(1300);

            timelineView=FindLargest(mainWindow,x=>x.GetType().Name=="TimelineView")
                ??throw new InvalidOperationException("TimelineView missing");
            h=SettingsBase<YMMSettings>.Default.LayerHeight;
            if(h<=0)throw new InvalidOperationException("LayerHeight invalid");

            var viewportProperty=typeof(TimelineViewModel).GetProperty("Viewport",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            var viewportHolder=viewportProperty?.GetValue(viewModel);
            var valueProperty=viewportHolder?.GetType().GetProperty("Value",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            var viewportGetterPublic=viewportProperty?.GetGetMethod(true)?.IsPublic==true;
            var viewportValueSetterPublic=valueProperty?.GetSetMethod(true)?.IsPublic==true;

            var initial=viewModel.Viewport.Value;

            // Native behavior first.
            viewModel.ScrollToItem(target);
            await Task.Delay(500);
            var native=viewModel.Viewport.Value;
            ApplyVisualFold();
            await Task.Delay(250);
            var nativeView=FindItemView(timelineView,target);
            var nativeVisible=nativeView is not null && Box(nativeView).Intersects(Box(timelineView));

            // Reset then apply only public Viewport.Value correction.
            viewModel.Viewport.Value=new Rect(new Point(initial.X,0),initial.Size);
            await Task.Delay(250);

            var displayRow=RowOf(target.Layer);
            var correctedY=Math.Max(0,displayRow*h);
            viewModel.Viewport.Value=new Rect(new Point(initial.X,correctedY),initial.Size);
            await Task.Delay(450);
            ApplyVisualFold();
            await Task.Delay(250);

            var corrected=viewModel.Viewport.Value;
            var correctedView=FindItemView(timelineView,target);
            var correctedVisible=correctedView is not null && Box(correctedView).Intersects(Box(timelineView));

            File.WriteAllLines(Path.Combine(output,"geometry.txt"),[
                $"initial_viewport={Fmt(initial)}",
                $"native_viewport={Fmt(native)}",
                $"corrected_viewport={Fmt(corrected)}",
                $"target_logical_layer={target.Layer}",
                $"target_display_row={displayRow}",
                $"layer_height={h}",
                $"expected_corrected_y={correctedY}",
                $"native_target_box={(nativeView is null?"<missing>":Fmt(Box(nativeView)))}",
                $"corrected_target_box={(correctedView is null?"<missing>":Fmt(Box(correctedView)))}",
                $"timeline_box={Fmt(Box(timelineView))}"
            ],new UTF8Encoding(false));

            WriteResult("PASS_NO_HARMONY_SCROLL_OBSERVATION",[
                "harmony_reference_present=False",
                $"viewport_property_getter_public={viewportGetterPublic}",
                $"viewport_value_setter_public={viewportValueSetterPublic}",
                $"native_viewport_y={native.Y:F2}",
                $"corrected_viewport_y={corrected.Y:F2}",
                $"native_target_visible_after_fold={nativeVisible}",
                $"corrected_target_visible_after_fold={correctedVisible}",
                $"corrected_matches_display_row={Math.Abs(corrected.Y-correctedY)<1.0}"
            ]);
        }catch(Exception ex){Fail(ex);}
    }

    static int RowOf(int layer)
    {
        if(layer<=FoldStart)return layer;
        if(layer<=FoldEnd)return FoldStart;
        return layer-HiddenCount;
    }

    static void ApplyVisualFold()
    {
        if(timelineView is null)return;
        foreach(var fe in Elements(timelineView).Where(x=>x.GetType().Name=="TimelineItemView")){
            var item=ItemOf(fe.DataContext);if(item is null)continue;
            fe.RenderTransform=item.Layer>FoldEnd
                ?new TranslateTransform(0,-HiddenCount*h)
                :item.Layer>FoldStart
                    ?new TranslateTransform(0,(FoldStart-item.Layer)*h)
                    :Transform.Identity;
        }
    }

    static FrameworkElement? FindItemView(DependencyObject r,IItem i)=>Elements(r)
        .Where(x=>x.IsVisible&&x.GetType().Name=="TimelineItemView"&&ReferenceEquals(ItemOf(x.DataContext),i))
        .OrderByDescending(x=>x.ActualWidth*x.ActualHeight).FirstOrDefault();

    static FrameworkElement? FindLargest(DependencyObject r,Func<FrameworkElement,bool> p)=>Elements(r)
        .Where(x=>x.IsVisible&&x.ActualWidth>5&&x.ActualHeight>5&&p(x))
        .OrderByDescending(x=>x.ActualWidth*x.ActualHeight).FirstOrDefault();

    static IEnumerable<FrameworkElement> Elements(DependencyObject r)
    {
        if(r is FrameworkElement fe)yield return fe;
        int c;try{c=VisualTreeHelper.GetChildrenCount(r);}catch{yield break;}
        for(var j=0;j<c;j++)foreach(var x in Elements(VisualTreeHelper.GetChild(r,j)))yield return x;
    }

    static IItem? ItemOf(object? dc)
    {
        if(dc is IItem d)return d;
        if(dc is null)return null;
        foreach(var p in dc.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
            .Where(p=>p.GetIndexParameters().Length==0&&p.CanRead&&(typeof(IItem).IsAssignableFrom(p.PropertyType)||p.Name.Contains("Item",StringComparison.OrdinalIgnoreCase)||p.Name is "Model" or "Source" or "Value")).Take(40))
        {
            try{
                var v=p.GetValue(dc);
                if(v is IItem i)return i;
                if(v is IEnumerable en&&v is not string)foreach(var x in en)if(x is IItem ni)return ni;
            }catch{}
        }
        return null;
    }

    static ScreenBox Box(FrameworkElement fe){try{var p=fe.PointToScreen(new Point());return new(p.X,p.Y,fe.ActualWidth,fe.ActualHeight);}catch{return default;}}
    static string Fmt(Rect r)=>$"{r.X:F2},{r.Y:F2},{r.Width:F2},{r.Height:F2}";
    static string Fmt(ScreenBox b)=>$"{b.Left:F2},{b.Top:F2},{b.Width:F2},{b.Height:F2}";
    static void WriteResult(string s,IEnumerable<string>d)=>File.WriteAllLines(Path.Combine(output,"result.txt"),new[]{"status="+s}.Concat(d),new UTF8Encoding(false));
    static void Fail(Exception ex){try{File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString(),new UTF8Encoding(false));WriteResult("FAIL_EXCEPTION",["message="+ex.GetBaseException().Message]);}catch{}}
}
