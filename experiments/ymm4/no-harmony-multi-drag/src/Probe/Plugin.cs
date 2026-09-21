using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;

namespace Ymm4NoHarmonyMultiDragProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL YMM4 No-Harmony Multi Drag Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Native
{
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] internal static extern void mouse_event(uint flags,uint dx,uint dy,uint data,nuint extra);
    [DllImport("user32.dll")] internal static extern void keybd_event(byte vk,byte scan,uint flags,nuint extra);
    internal const uint LD=2,LU=4,KEYUP=2;
}
internal readonly record struct ScreenBox(double Left,double Top,double Width,double Height)
{
    public Point Center=>new(Left+Width/2,Top+Height/2);
    public bool Valid=>Width>2&&Height>2;
}

internal static class Probe
{
    static bool scheduled,running;
    static string output="";
    static Timeline? timeline;
    static FrameworkElement? timelineView,cursorSource;
    static int h;

    static IItem? activeItem;
    static int activeOriginalLayer;
    static readonly Dictionary<IItem,int> groupOriginalLayers=new(ReferenceEqualityComparer.Instance);
    static readonly List<string> trace=[];
    static readonly List<string> groupSnapshots=[];
    static bool bubbleSeen;

    public static void Schedule()
    {
        var dir=Environment.GetEnvironmentVariable("CNWL_YMM4_NO_HARMONY_MULTIDRAG_DIR");
        if(scheduled||string.IsNullOrWhiteSpace(dir))return;
        scheduled=true;output=Path.GetFullPath(dir);Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap),DispatcherPriority.ApplicationIdle);
    }
    static void Bootstrap()
    {
        var ticks=0;var created=false;var timer=new DispatcherTimer(DispatcherPriority.ApplicationIdle){Interval=TimeSpan.FromMilliseconds(400)};
        timer.Tick+=(_,_)=>{
            try{
                ticks++;
                foreach(Window w in Application.Current.Windows){
                    var main=w.DataContext;if(main?.GetType().FullName!="YukkuriMovieMaker.ViewModels.MainViewModel")continue;
                    var active=main.GetType().GetProperty("ActiveTimelineViewModel",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(main);
                    if(active is null&&!created){created=true;main.GetType().GetMethod("CreateProject",Type.EmptyTypes)?.Invoke(main,null);return;}
                    if(active is null||running)continue;
                    var t=active.GetType().GetProperty("Timeline",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(active) as Timeline
                      ?? active.GetType().GetField("timeline",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                    if(t is null)continue;
                    running=true;timer.Stop();timeline=t;_=RunAsync(w,t);return;
                }
                if(ticks>100){timer.Stop();WriteResult("FAIL_BOOTSTRAP_TIMEOUT",[]);}
            }catch(Exception ex){timer.Stop();Fail(ex);}
        };timer.Start();
    }

    static async Task RunAsync(Window mainWindow,Timeline t)
    {
        try{
            mainWindow.WindowState=System.Windows.WindowState.Maximized;mainWindow.Activate();Native.SetForegroundWindow(new WindowInteropHelper(mainWindow).Handle);await Task.Delay(900);
            var ch=new Character{Name="CNWL_MULTIDRAG"};
            var a=new VoiceItem(ch){Frame=80,Length=70,Layer=1,Serif="A",Remark="CNWL_MULTI_A"};
            var b=new VoiceItem(ch){Frame=180,Length=70,Layer=3,Serif="B",Remark="CNWL_MULTI_B"};
            var anchor=new VoiceItem(ch){Frame=300,Length=40,Layer=6,Serif="anchor",Remark="CNWL_MULTI_ANCHOR"};
            foreach(var i in new IItem[]{a,b,anchor})if(!t.TryAddItems([i],i.Frame,i.Layer))throw new InvalidOperationException("fixture insert");
            t.CurrentFrame=0;await Task.Delay(1300);

            timelineView=FindLargest(mainWindow,x=>x.GetType().Name=="TimelineView")??throw new InvalidOperationException("TimelineView");
            var av=FindItemView(timelineView,a)??throw new InvalidOperationException("A view");
            var scroll=FindAncestor<ScrollViewer>(av)??throw new InvalidOperationException("scroll");
            cursorSource=scroll.Content as FrameworkElement??throw new InvalidOperationException("cursor source");
            h=SettingsBase<YMMSettings>.Default.LayerHeight;
            ApplyFold();await Task.Delay(350);av=FindItemView(timelineView,a)??av;var box=Box(av);if(!box.Valid)throw new InvalidOperationException("geometry");

            cursorSource.AddHandler(Mouse.PreviewMouseDownEvent,new MouseButtonEventHandler(OnPreviewDown),true);
            cursorSource.AddHandler(Mouse.MouseMoveEvent,new MouseEventHandler(OnBubbleMove),true);
            cursorSource.AddHandler(Mouse.MouseUpEvent,new MouseButtonEventHandler(OnBubbleUp),true);

            t.SelectItems([a,b]);await Task.Delay(250);
            var origA=(Layer:a.Layer,Frame:a.Frame);var origB=(Layer:b.Layer,Frame:b.Frame);

            await Drag(box.Center,new Point(box.Center.X+64,box.Center.Y+h));
            await Task.Delay(850);

            var finalA=(Layer:a.Layer,Frame:a.Frame);var finalB=(Layer:b.Layer,Frame:b.Frame);
            var logicalDelta=finalA.Layer-origA.Layer;
            var groupLayerCorrect=finalA.Layer==3 && finalB.Layer==5 && logicalDelta==2;
            var frameDeltaA=finalA.Frame-origA.Frame;
            var frameDeltaB=finalB.Frame-origB.Frame;
            var groupFrameDeltaSame=frameDeltaA==frameDeltaB && frameDeltaA!=0;

            mainWindow.Activate();Native.SetForegroundWindow(new WindowInteropHelper(mainWindow).Handle);await Task.Delay(120);
            await Shortcut(0x5A);await Task.Delay(700);
            var undoOk=a.Layer==origA.Layer&&a.Frame==origA.Frame&&b.Layer==origB.Layer&&b.Frame==origB.Frame;

            await Shortcut(0x59);await Task.Delay(700);
            var redoOk=a.Layer==finalA.Layer&&a.Frame==finalA.Frame&&b.Layer==finalB.Layer&&b.Frame==finalB.Frame;

            File.WriteAllLines(Path.Combine(output,"trace.txt"),trace,new UTF8Encoding(false));
            WriteResult("PASS_NO_HARMONY_MULTIDRAG_OBSERVATION",[
                "harmony_reference_present=False",
                $"bubble_move_observed={bubbleSeen}",
                $"original_a=L{origA.Layer}:F{origA.Frame}",
                $"original_b=L{origB.Layer}:F{origB.Frame}",
                $"final_a=L{finalA.Layer}:F{finalA.Frame}",
                $"final_b=L{finalB.Layer}:F{finalB.Frame}",
                $"logical_layer_delta={logicalDelta}",
                $"group_layers_match_fold={groupLayerCorrect}",
                $"frame_delta_a={frameDeltaA}",
                $"frame_delta_b={frameDeltaB}",
                $"group_frame_delta_same={groupFrameDeltaSame}",
                $"undo_restored_both={undoOk}",
                $"redo_restored_both={redoOk}",
                $"snapshots={string.Join(";",groupSnapshots)}"
            ]);
        }catch(Exception ex){Fail(ex);}
    }

    static void OnPreviewDown(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Left||cursorSource is null||timeline is null)return;
        var v=FindAncestorByName(e.OriginalSource as DependencyObject,"TimelineItemView") as FrameworkElement;var item=v is null?null:ItemOf(v.DataContext);if(item is null)return;
        activeItem=item;activeOriginalLayer=item.Layer;groupOriginalLayers.Clear();
        var selected=timeline.SelectedItems.Any(x=>ReferenceEquals(x,item))?timeline.SelectedItems:new[]{item};
        foreach(var x in selected)groupOriginalLayers[x]=x.Layer;
        trace.Add($"down active=L{item.Layer} group={string.Join("|",groupOriginalLayers.Select(x=>"L"+x.Value))}");
    }

    static void OnBubbleMove(object sender,MouseEventArgs e)
    {
        if(activeItem is null||cursorSource is null||e.LeftButton!=MouseButtonState.Pressed)return;
        bubbleSeen=true;
        var p=Mouse.GetPosition(cursorSource);var desired=MapDisplayYToLogicalLayer(p.Y);var delta=desired-activeOriginalLayer;
        var before=string.Join("|",groupOriginalLayers.Keys.Select(x=>"L"+x.Layer+":F"+x.Frame));
        foreach(var (item,origLayer) in groupOriginalLayers)
        {
            var target=Math.Max(0,origLayer+delta);
            if(item.Layer!=target)item.Layer=target;
        }
        ApplyFold();
        var after=string.Join("|",groupOriginalLayers.Keys.Select(x=>"L"+x.Layer+":F"+x.Frame));
        groupSnapshots.Add($"{before}>{after}");
    }

    static void OnBubbleUp(object sender,MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Left)return;activeItem=null;groupOriginalLayers.Clear();ApplyFold();
    }

    static int MapDisplayYToLogicalLayer(double y){var d=(int)Math.Floor(y/h);return d>=2?d+1:d;}
    static void ApplyFold(){if(timelineView is null)return;foreach(var fe in Elements(timelineView).Where(x=>x.GetType().Name=="TimelineItemView")){var i=ItemOf(fe.DataContext);if(i is null)continue;fe.RenderTransform=i.Layer>=3?new TranslateTransform(0,-h):Transform.Identity;}}

    static async Task Shortcut(byte key){const byte ctrl=0x11;Native.keybd_event(ctrl,0,0,0);await Task.Delay(60);Native.keybd_event(key,0,0,0);await Task.Delay(60);Native.keybd_event(key,0,Native.KEYUP,0);Native.keybd_event(ctrl,0,Native.KEYUP,0);}
    static async Task Drag(Point a,Point b){Native.SetCursorPos((int)a.X,(int)a.Y);await Task.Delay(100);Native.mouse_event(Native.LD,0,0,0,0);await Task.Delay(120);for(var i=1;i<=10;i++){Native.SetCursorPos((int)Math.Round(a.X+(b.X-a.X)*i/10.0),(int)Math.Round(a.Y+(b.Y-a.Y)*i/10.0));await Task.Delay(70);}Native.mouse_event(Native.LU,0,0,0,0);}

    static FrameworkElement? FindItemView(DependencyObject r,IItem item)=>Elements(r).Where(x=>x.IsVisible&&x.GetType().Name=="TimelineItemView"&&ReferenceEquals(ItemOf(x.DataContext),item)).OrderByDescending(x=>x.ActualWidth*x.ActualHeight).FirstOrDefault();
    static FrameworkElement? FindLargest(DependencyObject r,Func<FrameworkElement,bool> p)=>Elements(r).Where(x=>x.IsVisible&&x.ActualWidth>5&&x.ActualHeight>5&&p(x)).OrderByDescending(x=>x.ActualWidth*x.ActualHeight).FirstOrDefault();
    static IEnumerable<FrameworkElement> Elements(DependencyObject r){if(r is FrameworkElement fe)yield return fe;int c;try{c=VisualTreeHelper.GetChildrenCount(r);}catch{yield break;}for(var i=0;i<c;i++)foreach(var x in Elements(VisualTreeHelper.GetChild(r,i)))yield return x;}
    static T? FindAncestor<T>(DependencyObject? o)where T:DependencyObject{var c=o;while(c is not null){if(c is T t)return t;try{c=VisualTreeHelper.GetParent(c);}catch{break;}}return null;}
    static DependencyObject? FindAncestorByName(DependencyObject? o,string n){var c=o;while(c is not null){if(c.GetType().Name==n)return c;try{c=VisualTreeHelper.GetParent(c);}catch{break;}}return null;}
    static IItem? ItemOf(object? dc){if(dc is IItem d)return d;if(dc is null)return null;foreach(var p in dc.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Where(p=>p.GetIndexParameters().Length==0&&p.CanRead&&(typeof(IItem).IsAssignableFrom(p.PropertyType)||p.Name.Contains("Item",StringComparison.OrdinalIgnoreCase)||p.Name is "Model" or "Source" or "Value")).Take(40)){try{var v=p.GetValue(dc);if(v is IItem i)return i;if(v is IEnumerable en&&v is not string)foreach(var x in en)if(x is IItem ni)return ni;}catch{}}return null;}
    static ScreenBox Box(FrameworkElement fe){try{var p=fe.PointToScreen(new Point());return new(p.X,p.Y,fe.ActualWidth,fe.ActualHeight);}catch{return default;}}
    static void WriteResult(string s,IEnumerable<string>d)=>File.WriteAllLines(Path.Combine(output,"result.txt"),new[]{"status="+s}.Concat(d),new UTF8Encoding(false));
    static void Fail(Exception ex){try{File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString(),new UTF8Encoding(false));WriteResult("FAIL_EXCEPTION",["message="+ex.GetBaseException().Message]);}catch{}}
}
