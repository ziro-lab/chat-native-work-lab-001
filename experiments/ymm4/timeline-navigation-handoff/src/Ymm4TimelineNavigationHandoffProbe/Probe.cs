using System.Collections;
using System.IO;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4TimelineNavigationHandoffProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL Timeline Navigation Handoff Probe";
    public void SetCulture(CultureInfo cultureInfo) => Bootstrap.Schedule();
}
public sealed class ProbeTool : IToolPlugin
{
    public string Name => "CNWL Timeline Navigation Handoff Probe";
    public Type ViewModelType => typeof(ProbeVm);
    public Type ViewType => typeof(ProbeView);
    public bool AllowMultipleInstances => false;
}
public sealed class ProbeView : UserControl
{
    internal static ProbeView? Current;
    internal Button FocusTarget { get; } = new(){Content="CNWL plugin focus target",Focusable=true,Padding=new Thickness(8)};
    public ProbeView(){Current=this;Content=FocusTarget;}
}
public sealed class ProbeVm : ITimelineToolViewModel, IToolViewModel
{
    public string Title=>"CNWL Timeline Navigation Handoff Probe"; public bool CanSuspend=>false;
    public void SetTimelineToolInfo(TimelineToolInfo info)=>NavigationProbe.Start(info);
    public ToolState SaveState()=>new(){Title=Title}; public void LoadState(ToolState stateData){}
    public event PropertyChangedEventHandler? PropertyChanged { add{} remove{} }
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested { add{} remove{} }
}

internal static class Bootstrap
{
    static bool scheduled,created,opened;
    public static void Schedule()
    {
        if(scheduled||string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CNWL_YMM4_NAV_HANDOFF_DIR")))return;
        scheduled=true;
        Application.Current.Dispatcher.BeginInvoke(new Action(()=>{
            var timer=new DispatcherTimer(DispatcherPriority.ApplicationIdle){Interval=TimeSpan.FromMilliseconds(400)};
            var ticks=0;
            timer.Tick+=(_,_)=>{
                ticks++;
                try{
                    foreach(Window w in Application.Current.Windows){
                        var main=w.DataContext;
                        if(main?.GetType().FullName!="YukkuriMovieMaker.ViewModels.MainViewModel")continue;
                        var active=main.GetType().GetProperty("ActiveTimelineViewModel",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(main);
                        if(active==null&&!created){created=true;main.GetType().GetMethod("CreateProject",Type.EmptyTypes)?.Invoke(main,null);return;}
                        if(active!=null&&!opened)opened=OpenTool(main);
                        if(opened&&ProbeView.Current!=null){timer.Stop();return;}
                    }
                    if(ticks>120){timer.Stop();NavigationProbe.Fail("Tool open/bootstrap timeout");}
                }catch(Exception ex){timer.Stop();NavigationProbe.Fail(ex.ToString());}
            };
            timer.Start();
        }));
    }
    static bool OpenTool(object main)
    {
        if(main.GetType().GetProperty("ToolMenuItems")?.GetValue(main) is not IEnumerable items)return false;
        bool Visit(object x,int depth){
            if(depth>8)return false;var t=x.GetType();
            var label=t.GetProperty("Header")?.GetValue(x)?.ToString()??t.GetProperty("Title")?.GetValue(x)?.ToString()??t.GetProperty("Name")?.GetValue(x)?.ToString()??"";
            if(label.Contains("CNWL Timeline Navigation Handoff Probe",StringComparison.Ordinal)){
                if(t.GetProperty("Command")?.GetValue(x) is ICommand c){var p=t.GetProperty("CommandParameter")?.GetValue(x);if(c.CanExecute(p)){c.Execute(p);return true;}}
                foreach(var n in new[]{"IsVisible","IsSelected","IsActive"})try{t.GetProperty(n)?.SetValue(x,true);}catch{}
                return true;
            }
            var children=(t.GetProperty("Children")?.GetValue(x)??t.GetProperty("Items")?.GetValue(x)) as IEnumerable;
            if(children!=null)foreach(var y in children)if(y!=null&&Visit(y,depth+1))return true;
            return false;
        }
        foreach(var x in items)if(x!=null&&Visit(x,0))return true;return false;
    }
}

internal static class Native
{
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hWnd,IntPtr hDC);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleBitmap(IntPtr hdc,int nWidth,int nHeight);
    [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr hdc,IntPtr h);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr ho);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] internal static extern bool BitBlt(IntPtr hdcDest,int x,int y,int cx,int cy,IntPtr hdcSrc,int x1,int y1,uint rop);
    [DllImport("gdi32.dll")] internal static extern int GetDIBits(IntPtr hdc,IntPtr hbmp,uint start,uint lines,byte[] bits,ref BITMAPINFO bmi,uint usage);
    internal const uint SRCCOPY=0x00CC0020,BI_RGB=0,DIB_RGB_COLORS=0;
    [StructLayout(LayoutKind.Sequential)] internal struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth; public int biHeight; public ushort biPlanes; public ushort biBitCount;
        public uint biCompression; public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter; public uint biClrUsed; public uint biClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }
}

internal sealed record ColorSample(int R,int G,int B,bool IsRed,bool IsBlue);
internal sealed record ScrollState(int Index,double HorizontalOffset,double ScrollableWidth,double ViewportWidth);
internal sealed record Result(
    string schema,string status,string host,bool pluginFocusObserved,bool timelineFocusObserved,bool previewPixelProbeUsable,
    bool directNavigationPreviewUpdated,bool timelineFocusUpdatesPreview,ColorSample initial,ColorSample afterDirect,ColorSample afterFocus,
    bool scrollFramePublic,bool scrollFrameMovesViewport,bool scrollFrameMovesCurrentFrame,bool directFarNavigationMovesViewport,
    int publicVisibleRangeCandidateCount,string[] publicSurface,ScrollState[] nearScroll,ScrollState[] directFarScroll,ScrollState[] afterScrollFrame,string? error);

internal static class NavigationProbe
{
    static bool started;
    static string OutDir=>Path.GetFullPath(Environment.GetEnvironmentVariable("CNWL_YMM4_NAV_HANDOFF_DIR")!);
    static string Media=>Path.GetFullPath(Environment.GetEnvironmentVariable("CNWL_YMM4_NAV_HANDOFF_MEDIA")!);
    public static void Start(TimelineToolInfo info){if(started)return;started=true;_=RunAsync(info);}
    public static void Fail(string error)
    {
        try{
            Directory.CreateDirectory(OutDir);File.WriteAllText(Path.Combine(OutDir,"error.txt"),error,new UTF8Encoding(false));
            var z=new ColorSample(0,0,0,false,false);
            File.WriteAllText(Path.Combine(OutDir,"result.json"),JsonSerializer.Serialize(new Result("cnwl.timeline-navigation-handoff.v1","FAIL_EXCEPTION","4.55.1.1 Lite",false,false,false,false,false,z,z,z,false,false,false,false,0,[],[],[],[],error),new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
        }catch{}
    }

    static async Task RunAsync(TimelineToolInfo info)
    {
        try{
            Directory.CreateDirectory(OutDir);
            var t=info.Timeline;
            var main=Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w.DataContext?.GetType().FullName=="YukkuriMovieMaker.ViewModels.MainViewModel")
                ??throw new InvalidOperationException("Main window not found");
            main.WindowState=WindowState.Maximized;main.Activate();Native.SetForegroundWindow(new WindowInteropHelper(main).Handle);await Task.Delay(1200);
            if(!File.Exists(Media))throw new FileNotFoundException("Media fixture missing",Media);

            var fps=60;
            var video=t.Items.OfType<VideoItem>().FirstOrDefault(x=>x.Remark=="CNWL_NAV_COLOR")??new VideoItem{
                FilePath=Media,Frame=0,Length=240,Layer=1,ContentOffset=TimeSpan.Zero,Remark="CNWL_NAV_COLOR"};
            video.PlaybackRate2.SetFirstValue(100);video.PlaybackRate2.SetAnimationParameters(video.Length,fps);
            if(!t.Items.Contains(video)&&!t.TryAddItems([video],video.Frame,video.Layer))throw new InvalidOperationException("Could not insert color VideoItem");
            var far=t.Items.OfType<VideoItem>().FirstOrDefault(x=>x.Remark=="CNWL_NAV_FAR")??new VideoItem{
                FilePath=Media,Frame=3000,Length=120,Layer=2,ContentOffset=TimeSpan.Zero,Remark="CNWL_NAV_FAR"};
            far.PlaybackRate2.SetFirstValue(100);far.PlaybackRate2.SetAnimationParameters(far.Length,fps);
            if(!t.Items.Contains(far)&&!t.TryAddItems([far],far.Frame,far.Layer))throw new InvalidOperationException("Could not insert far VideoItem");
            t.RefreshTimelineLengthAndMaxLayer();await Task.Delay(1800);

            var timelineView=FindLargest(main,fe=>fe.GetType().FullName=="YukkuriMovieMaker.Views.TimelineView"||fe.DataContext?.GetType().FullName=="YukkuriMovieMaker.ViewModels.TimelineViewModel")
                ??throw new InvalidOperationException("TimelineView not found");
            var preview=FindLargest(main,fe=>{
                var n=fe.GetType().FullName??"";var d=fe.DataContext?.GetType().FullName??"";
                return !n.Contains(nameof(Ymm4TimelineNavigationHandoffProbe),StringComparison.Ordinal)&&
                    (n.Contains("PreviewView",StringComparison.OrdinalIgnoreCase)||d.Contains("PreviewViewModel",StringComparison.OrdinalIgnoreCase));
            })??throw new InvalidOperationException("Preview visual not found");
            var active=timelineView.DataContext??throw new InvalidOperationException("TimelineView DataContext missing");
            var scrollFrame=active.GetType().GetMethod("ScrollFrame",BindingFlags.Instance|BindingFlags.Public,null,[typeof(int)],null);
            var publicSurface=PublicSurface(active).ToArray();
            var visibleCount=publicSurface.Count(x=>x.Contains("VISIBLE_RANGE_CANDIDATE",StringComparison.Ordinal));

            bool timelineFocus=FocusTimeline(main,timelineView);await Task.Delay(250);
            t.CurrentFrame=30;t.SelectItem(video);scrollFrame?.Invoke(active,[30]);await Task.Delay(1400);
            var initial=CapturePreview(preview);
            var previewUsable=initial.IsRed;

            var button=ProbeView.Current?.FocusTarget??throw new InvalidOperationException("Probe focus target missing");
            Window.GetWindow(button)?.Activate();button.Focus();Keyboard.Focus(button);await Task.Delay(250);
            var pluginFocus=ReferenceEquals(Keyboard.FocusedElement,button)||button.IsKeyboardFocusWithin;

            t.CurrentFrame=180;t.SelectItem(video);await Task.Delay(900);
            var afterDirect=CapturePreview(preview);
            var directUpdated=afterDirect.IsBlue;

            main.Activate();Native.SetForegroundWindow(new WindowInteropHelper(main).Handle);
            timelineFocus=FocusTimeline(main,timelineView);await Task.Delay(1000);
            var afterFocus=CapturePreview(preview);
            var focusUpdates=previewUsable&&!directUpdated&&afterFocus.IsBlue;

            scrollFrame?.Invoke(active,[30]);await Task.Delay(450);
            var near=ScrollStates(timelineView);
            Window.GetWindow(button)?.Activate();button.Focus();Keyboard.Focus(button);await Task.Delay(150);
            t.CurrentFrame=3000;t.SelectItem(far);await Task.Delay(500);
            var directFar=ScrollStates(timelineView);
            var directFarMoved=OffsetsChanged(near,directFar);
            var beforeFrame=t.CurrentFrame;
            if(scrollFrame!=null)scrollFrame.Invoke(active,[3000]);
            await Task.Delay(650);
            var afterScroll=ScrollStates(timelineView);
            var scrollMoves=OffsetsChanged(directFar,afterScroll);
            var scrollMovesFrame=t.CurrentFrame!=beforeFrame;

            File.WriteAllLines(Path.Combine(OutDir,"surface.txt"),publicSurface,new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(OutDir,"preview-samples.txt"),[
                $"initial R={initial.R} G={initial.G} B={initial.B} red={initial.IsRed} blue={initial.IsBlue}",
                $"after_direct R={afterDirect.R} G={afterDirect.G} B={afterDirect.B} red={afterDirect.IsRed} blue={afterDirect.IsBlue}",
                $"after_focus R={afterFocus.R} G={afterFocus.G} B={afterFocus.B} red={afterFocus.IsRed} blue={afterFocus.IsBlue}"
            ],new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(OutDir,"scroll.txt"),DescribeScroll("near",near).Concat(DescribeScroll("direct_far",directFar)).Concat(DescribeScroll("after_scrollframe",afterScroll)),new UTF8Encoding(false));

            var result=new Result("cnwl.timeline-navigation-handoff.v1","PASS_NAVIGATION_HANDOFF_OBSERVATION","4.55.1.1 Lite",pluginFocus,timelineFocus,previewUsable,
                directUpdated,focusUpdates,initial,afterDirect,afterFocus,scrollFrame!=null,scrollMoves,scrollMovesFrame,directFarMoved,visibleCount,publicSurface,near,directFar,afterScroll,null);
            File.WriteAllText(Path.Combine(OutDir,"result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
        }catch(Exception ex){Fail(ex.ToString());}
    }

    static bool FocusTimeline(Window main,FrameworkElement timelineView)
    {
        main.Activate();Native.SetForegroundWindow(new WindowInteropHelper(main).Handle);
        foreach(var target in new[]{timelineView}.Concat(Elements(timelineView).Where(x=>x.Focusable&&x.IsVisible&&x.IsEnabled))){
            try{
                target.Focus();Keyboard.Focus(target);
                if(Keyboard.FocusedElement is DependencyObject d&&IsWithin(d,timelineView))return true;
            }catch{}
        }
        return false;
    }
    static bool IsWithin(DependencyObject d,DependencyObject root)
    {
        for(var x=d;x!=null;x=Parent(x))if(ReferenceEquals(x,root))return true;return false;
    }
    static DependencyObject? Parent(DependencyObject d)=>d switch{
        Visual or System.Windows.Media.Media3D.Visual3D=>VisualTreeHelper.GetParent(d),
        FrameworkContentElement c=>c.Parent,_=>null};

    static ColorSample CapturePreview(FrameworkElement preview)
    {
        var center=preview.PointToScreen(new Point(preview.ActualWidth/2,preview.ActualHeight/2));
        const int w=40,h=40;var x=(int)Math.Round(center.X)-w/2;var y=(int)Math.Round(center.Y)-h/2;
        var screen=Native.GetDC(IntPtr.Zero);if(screen==IntPtr.Zero)throw new InvalidOperationException("GetDC failed");
        var mem=Native.CreateCompatibleDC(screen);var bmp=Native.CreateCompatibleBitmap(screen,w,h);var old=Native.SelectObject(mem,bmp);
        try{
            if(!Native.BitBlt(mem,0,0,w,h,screen,x,y,Native.SRCCOPY))throw new InvalidOperationException("BitBlt failed");
            var bmi=new Native.BITMAPINFO{bmiHeader=new Native.BITMAPINFOHEADER{biSize=(uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(),biWidth=w,biHeight=-h,biPlanes=1,biBitCount=32,biCompression=Native.BI_RGB}};
            var bytes=new byte[w*h*4];
            if(Native.GetDIBits(mem,bmp,0,h,bytes,ref bmi,Native.DIB_RGB_COLORS)==0)throw new InvalidOperationException("GetDIBits failed");
            long rr=0,gg=0,bb=0;for(var i=0;i<bytes.Length;i+=4){bb+=bytes[i];gg+=bytes[i+1];rr+=bytes[i+2];}
            var n=w*h;var r=(int)(rr/n);var g=(int)(gg/n);var b=(int)(bb/n);
            return new(r,g,b,r>b+35&&r>g+25,b>r+35&&b>g+25);
        }finally{
            Native.SelectObject(mem,old);Native.DeleteObject(bmp);Native.DeleteDC(mem);Native.ReleaseDC(IntPtr.Zero,screen);
        }
    }

    static ScrollState[] ScrollStates(FrameworkElement timelineView)=>Elements(timelineView).OfType<ScrollViewer>().Where(x=>x.IsVisible)
        .Select((x,i)=>new ScrollState(i,x.HorizontalOffset,x.ScrollableWidth,x.ViewportWidth)).ToArray();
    static bool OffsetsChanged(ScrollState[] a,ScrollState[] b)=>a.Any(x=>b.FirstOrDefault(y=>y.Index==x.Index) is { } y&&Math.Abs(y.HorizontalOffset-x.HorizontalOffset)>1);
    static IEnumerable<string> DescribeScroll(string name,ScrollState[] s)=>s.Select(x=>$"{name}[{x.Index}] offset={x.HorizontalOffset:F2} scrollable={x.ScrollableWidth:F2} viewport={x.ViewportWidth:F2}");

    static IEnumerable<string> PublicSurface(object active)
    {
        var t=active.GetType();
        foreach(var p in t.GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>Relevant(p.Name)).OrderBy(p=>p.Name)){
            var prefix=(p.Name.Contains("Frame",StringComparison.OrdinalIgnoreCase)&&(p.Name.Contains("Visible",StringComparison.OrdinalIgnoreCase)||p.Name.Contains("Viewport",StringComparison.OrdinalIgnoreCase)))?"VISIBLE_RANGE_CANDIDATE ":"";
            string value;try{value=p.CanRead?(p.GetValue(active)?.ToString()??"<null>"):"<write-only>";}catch{value="<throw>";}
            yield return $"{prefix}PROPERTY {t.FullName}.{p.Name}:{p.PropertyType.FullName} value={value}";
        }
        foreach(var m in t.GetMethods(BindingFlags.Instance|BindingFlags.Public).Where(m=>Relevant(m.Name)).OrderBy(m=>m.Name)){
            var prefix=(m.Name.Contains("Frame",StringComparison.OrdinalIgnoreCase)&&(m.Name.Contains("Visible",StringComparison.OrdinalIgnoreCase)||m.Name.Contains("Viewport",StringComparison.OrdinalIgnoreCase)))?"VISIBLE_RANGE_CANDIDATE ":"";
            yield return $"{prefix}METHOD {t.FullName}.{m.Name}({string.Join(",",m.GetParameters().Select(p=>p.ParameterType.FullName))}):{m.ReturnType.FullName}";
        }
    }
    static bool Relevant(string n)=>new[]{"Scroll","Visible","Viewport","Frame","Offset","Scale"}.Any(x=>n.Contains(x,StringComparison.OrdinalIgnoreCase));
    static FrameworkElement? FindLargest(DependencyObject root,Func<FrameworkElement,bool> pred)=>Elements(root).Where(x=>x.IsVisible&&x.ActualWidth>30&&x.ActualHeight>30&&pred(x)).OrderByDescending(x=>x.ActualWidth*x.ActualHeight).FirstOrDefault();
    static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        if(root is FrameworkElement fe)yield return fe;int n=0;try{n=VisualTreeHelper.GetChildrenCount(root);}catch{}
        for(var i=0;i<n;i++)foreach(var x in Elements(VisualTreeHelper.GetChild(root,i)))yield return x;
    }
}
