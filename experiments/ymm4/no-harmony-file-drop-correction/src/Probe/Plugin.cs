using System.Collections;
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

namespace Ymm4NoHarmonyFileDropCorrectionProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL YMM4 No-Harmony File Drop Correction Probe";
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
    public double Right=>Left+Width;
    public double Bottom=>Top+Height;
    public Point Center=>new(Left+Width/2,Top+Height/2);
    public bool Valid=>Width>2&&Height>2;
}

internal readonly record struct DropObservation(
    bool Attempted,
    string Effect,
    bool Added,
    int Count,
    string Layers,
    string Frames);

internal static class Probe
{
    static bool scheduled,running;
    static string output="";
    static Timeline? timeline;
    static object? timelineVm;
    static FrameworkElement? timelineView,cursorSource;
    static int h;
    static bool correctionEnabled;
    static bool dragEnterObserved,dragOverObserved,dropObserved;
    static readonly List<string> trace=[];

    public static void Schedule()
    {
        var dir=Environment.GetEnvironmentVariable("CNWL_YMM4_NO_HARMONY_FILEDROP_DIR");
        if(scheduled||string.IsNullOrWhiteSpace(dir))return;
        scheduled=true;output=Path.GetFullPath(dir);Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap),DispatcherPriority.ApplicationIdle);
    }

    static void Bootstrap()
    {
        var ticks=0;
        var created=0;
        var timer=new DispatcherTimer(DispatcherPriority.ApplicationIdle){Interval=TimeSpan.FromMilliseconds(400)};
        timer.Tick+=(_,_)=>{
            try{
                ticks++;
                foreach(Window w in Application.Current.Windows){
                    var main=w.DataContext;if(main?.GetType().FullName!="YukkuriMovieMaker.ViewModels.MainViewModel")continue;
                    var active=main.GetType().GetProperty("ActiveTimelineViewModel",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(main);
                    if(active is null&&created++==0){main.GetType().GetMethod("CreateProject",Type.EmptyTypes)?.Invoke(main,null);return;}
                    if(active is null||running)continue;
                    var t=active.GetType().GetProperty("Timeline",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(active) as Timeline
                        ?? active.GetType().GetField("timeline",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                    if(t is null)continue;
                    running=true;timer.Stop();timeline=t;timelineVm=active;_=RunAsync(w,t);return;
                }
                if(ticks>100){timer.Stop();WriteResult("FAIL_BOOTSTRAP_TIMEOUT",[]);}
            }catch(Exception ex){timer.Stop();Fail(ex);}
        };timer.Start();
    }

    static async Task RunAsync(Window mainWindow,Timeline t)
    {
        try{
            mainWindow.WindowState=System.Windows.WindowState.Maximized;
            mainWindow.Activate();Native.SetForegroundWindow(new WindowInteropHelper(mainWindow).Handle);
            await Task.Delay(900);

            var ch=new Character{Name="CNWL_FILEDROP"};
            var l1=new VoiceItem(ch){Frame=40,Length=50,Layer=1,Serif="L1",Remark="CNWL_DROP_L1"};
            var l3=new VoiceItem(ch){Frame=40,Length=50,Layer=3,Serif="L3",Remark="CNWL_DROP_L3"};
            if(!t.TryAddItems([l1],l1.Frame,l1.Layer)||!t.TryAddItems([l3],l3.Frame,l3.Layer))throw new InvalidOperationException("fixture");
            t.CurrentFrame=0;await Task.Delay(1300);

            timelineView=FindLargest(mainWindow,x=>x.GetType().Name=="TimelineView")??throw new InvalidOperationException("TimelineView");
            var l3View=FindItemView(timelineView,l3)??throw new InvalidOperationException("L3 view");
            var scroll=FindAncestor<ScrollViewer>(l3View)??throw new InvalidOperationException("ScrollViewer");
            cursorSource=scroll.Content as FrameworkElement??throw new InvalidOperationException("cursor source");
            h=SettingsBase<YMMSettings>.Default.LayerHeight;
            ApplyFold();await Task.Delay(350);
            l3View=FindItemView(timelineView,l3)??l3View;
            var l3Box=Box(l3View);if(!l3Box.Valid)throw new InvalidOperationException("L3 geometry");

            timelineView.AddHandler(DragDrop.PreviewDragEnterEvent,new DragEventHandler(OnPreviewDragEnter),true);
            timelineView.AddHandler(DragDrop.PreviewDragOverEvent,new DragEventHandler(OnPreviewDragOver),true);
            timelineView.AddHandler(DragDrop.PreviewDropEvent,new DragEventHandler(OnPreviewDrop),true);

            var tv=Box(timelineView);
            var y=l3Box.Center.Y;
            var baselinePoint=new Point(Math.Min(tv.Right-100,Math.Max(520,l3Box.Right+220)),y);
            var correctedPoint=new Point(Math.Min(tv.Right-60,baselinePoint.X+160),y);

            correctionEnabled=false;
            ResetRouteFlags();
            var baseline=await DoFileDrop(mainWindow,t,baselinePoint,"baseline");

            correctionEnabled=true;
            ResetRouteFlags();
            var corrected=await DoFileDrop(mainWindow,t,correctedPoint,"corrected");

            var correctedLayers=corrected.Layers.Split(',',StringSplitOptions.RemoveEmptyEntries);
            var correctedMatchesFold=correctedLayers.Contains("3");

            File.WriteAllLines(Path.Combine(output,"trace.txt"),trace,new UTF8Encoding(false));
            WriteResult("PASS_NO_HARMONY_FILEDROP_OBSERVATION",[
                "harmony_reference_present=False",
                $"baseline_added={baseline.Added}",
                $"baseline_layers={baseline.Layers}",
                $"baseline_frames={baseline.Frames}",
                $"corrected_added={corrected.Added}",
                $"corrected_layers={corrected.Layers}",
                $"corrected_frames={corrected.Frames}",
                $"corrected_matches_fold_layer={correctedMatchesFold}",
                $"drag_enter_observed={dragEnterObserved}",
                $"drag_over_observed={dragOverObserved}",
                $"drop_observed={dropObserved}"
            ]);
        }catch(Exception ex){Fail(ex);}
    }

    static void ResetRouteFlags(){dragEnterObserved=dragOverObserved=dropObserved=false;}

    static void OnPreviewDragEnter(object sender,DragEventArgs e)
    {
        dragEnterObserved=true;
        CorrectCursor(e,"enter");
    }
    static void OnPreviewDragOver(object sender,DragEventArgs e)
    {
        dragOverObserved=true;
        CorrectCursor(e,"over");
    }
    static void OnPreviewDrop(object sender,DragEventArgs e)
    {
        dropObserved=true;
        CorrectCursor(e,"drop");
    }

    static void CorrectCursor(DragEventArgs e,string phase)
    {
        if(cursorSource is null)return;
        var raw=e.GetPosition(cursorSource);
        var mapped=MapDisplayPointToLogical(raw);
        if(correctionEnabled)
            SetReactivePoint(timelineVm,"TimelineCursorPosition",mapped);
        trace.Add($"{phase} correction={correctionEnabled} raw={Fmt(raw)} mapped={Fmt(mapped)} effects={e.Effects}");
        // Do not handle: YMM4 remains the actual file-drop implementation.
    }

    static Point MapDisplayPointToLogical(Point p)
    {
        var display=(int)Math.Floor(p.Y/h);
        var offset=p.Y-display*h;
        var logical=display>=2?display+1:display;
        return new Point(p.X,logical*h+offset);
    }

    static bool SetReactivePoint(object? instance,string propertyName,Point value)
    {
        try{
            if(instance is null)return false;
            var holder=instance.GetType().GetProperty(propertyName,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(instance);
            var prop=holder?.GetType().GetProperty("Value",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(prop is null||!prop.CanWrite)return false;
            prop.SetValue(holder,value);return true;
        }catch{return false;}
    }

    static async Task<DropObservation> DoFileDrop(Window mainWindow,Timeline t,Point target,string label)
    {
        var png=Path.Combine(output,$"{label}-1x1.png");
        File.WriteAllBytes(png,Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZQ1sAAAAASUVORK5CYII="));
        var before=t.Items.ToArray();

        var sourceBorder=new Border{Width=64,Height=64,Background=Brushes.Gray};
        var sourceWindow=new Window{
            Width=80,Height=80,Left=20,Top=20,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
            ShowInTaskbar=false,Topmost=true,Content=sourceBorder
        };
        sourceWindow.Show();sourceWindow.Activate();await Task.Delay(250);

        var start=sourceBorder.PointToScreen(new Point(sourceBorder.ActualWidth/2,sourceBorder.ActualHeight/2));
        Native.SetCursorPos((int)start.X,(int)start.Y);await Task.Delay(100);Native.mouse_event(Native.LD,0,0,0,0);await Task.Delay(80);

        var data=new DataObject();data.SetData(DataFormats.FileDrop,new[]{png});
        using var cancel=new CancellationTokenSource();
        var mover=Task.Run(async()=>{
            try{
                await Task.Delay(250,cancel.Token);
                for(var i=1;i<=12;i++){
                    Native.SetCursorPos(
                        (int)Math.Round(start.X+(target.X-start.X)*i/12.0),
                        (int)Math.Round(start.Y+(target.Y-start.Y)*i/12.0));
                    await Task.Delay(75,cancel.Token);
                }
                await Task.Delay(180,cancel.Token);
                Native.mouse_event(Native.LU,0,0,0,0);
                await Task.Delay(1800,cancel.Token);
                Native.keybd_event(0x1B,0,0,0);Native.keybd_event(0x1B,0,Native.KEYUP,0);
            }catch(OperationCanceledException){}
        });

        DragDropEffects effect;
        try{effect=System.Windows.DragDrop.DoDragDrop(sourceBorder,data,DragDropEffects.Copy);}
        finally{
            cancel.Cancel();Native.mouse_event(Native.LU,0,0,0,0);
            sourceWindow.Close();mainWindow.Activate();Native.SetForegroundWindow(new WindowInteropHelper(mainWindow).Handle);
        }
        try{await mover;}catch(OperationCanceledException){}
        await Task.Delay(1400);

        var added=t.Items.Where(x=>!before.Any(b=>ReferenceEquals(b,x))).ToArray();
        trace.Add($"{label}_result effect={effect} added={string.Join("|",added.Select(x=>x.GetType().Name+"@L"+x.Layer+":F"+x.Frame))}");
        return new(true,effect.ToString(),added.Length>0,added.Length,
            string.Join(",",added.Select(x=>x.Layer).OrderBy(x=>x)),
            string.Join(",",added.Select(x=>x.Frame).OrderBy(x=>x)));
    }

    static void ApplyFold()
    {
        if(timelineView is null)return;
        foreach(var fe in Elements(timelineView).Where(x=>x.GetType().Name=="TimelineItemView")){
            var i=ItemOf(fe.DataContext);if(i is null)continue;
            fe.RenderTransform=i.Layer>=3?new TranslateTransform(0,-h):Transform.Identity;
        }
    }

    static FrameworkElement? FindItemView(DependencyObject r,IItem i)=>Elements(r).Where(x=>x.IsVisible&&x.GetType().Name=="TimelineItemView"&&ReferenceEquals(ItemOf(x.DataContext),i)).OrderByDescending(x=>x.ActualWidth*x.ActualHeight).FirstOrDefault();
    static FrameworkElement? FindLargest(DependencyObject r,Func<FrameworkElement,bool> p)=>Elements(r).Where(x=>x.IsVisible&&x.ActualWidth>5&&x.ActualHeight>5&&p(x)).OrderByDescending(x=>x.ActualWidth*x.ActualHeight).FirstOrDefault();
    static IEnumerable<FrameworkElement> Elements(DependencyObject r){if(r is FrameworkElement fe)yield return fe;int c;try{c=VisualTreeHelper.GetChildrenCount(r);}catch{yield break;}for(var j=0;j<c;j++)foreach(var x in Elements(VisualTreeHelper.GetChild(r,j)))yield return x;}
    static T? FindAncestor<T>(DependencyObject? o)where T:DependencyObject{var c=o;while(c is not null){if(c is T t)return t;try{c=VisualTreeHelper.GetParent(c);}catch{break;}}return null;}
    static IItem? ItemOf(object? dc){if(dc is IItem d)return d;if(dc is null)return null;foreach(var p in dc.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Where(p=>p.GetIndexParameters().Length==0&&p.CanRead&&(typeof(IItem).IsAssignableFrom(p.PropertyType)||p.Name.Contains("Item",StringComparison.OrdinalIgnoreCase)||p.Name is "Model" or "Source" or "Value")).Take(40)){try{var v=p.GetValue(dc);if(v is IItem i)return i;if(v is IEnumerable en&&v is not string)foreach(var x in en)if(x is IItem ni)return ni;}catch{}}return null;}
    static ScreenBox Box(FrameworkElement fe){try{var p=fe.PointToScreen(new Point());return new(p.X,p.Y,fe.ActualWidth,fe.ActualHeight);}catch{return default;}}
    static string Fmt(Point p)=>$"{p.X:F2},{p.Y:F2}";
    static void WriteResult(string s,IEnumerable<string>d)=>File.WriteAllLines(Path.Combine(output,"result.txt"),new[]{"status="+s}.Concat(d),new UTF8Encoding(false));
    static void Fail(Exception ex){try{File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString(),new UTF8Encoding(false));WriteResult("FAIL_EXCEPTION",["message="+ex.GetBaseException().Message]);}catch{}}
}
