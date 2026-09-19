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
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] internal static extern void mouse_event(uint flags,uint dx,uint dy,uint data,nuint extra);
    [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr hWnd,IntPtr hDC);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleBitmap(IntPtr hdc,int nWidth,int nHeight);
    [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr hdc,IntPtr h);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr ho);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] internal static extern bool BitBlt(IntPtr hdcDest,int x,int y,int cx,int cy,IntPtr hdcSrc,int x1,int y1,uint rop);
    [DllImport("gdi32.dll")] internal static extern int GetDIBits(IntPtr hdc,IntPtr hbmp,uint start,uint lines,byte[] bits,ref BITMAPINFO bmi,uint usage);
    internal const uint LD=0x0002,LU=0x0004,SRCCOPY=0x00CC0020,BI_RGB=0,DIB_RGB_COLORS=0;
    [StructLayout(LayoutKind.Sequential)] internal struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth; public int biHeight; public ushort biPlanes; public ushort biBitCount;
        public uint biCompression; public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter; public uint biClrUsed; public uint biClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }
}

internal readonly record struct ScreenBox(int X,int Y,int Width,int Height)
{
    public int CenterX=>X+Width/2; public int CenterY=>Y+Height/2; public bool Valid=>Width>10&&Height>10;
}
internal sealed record ColorSample(int R,int G,int B,int RedPixels,int GreenPixels,int PixelCount,bool IsRed,bool IsGreen);
internal sealed record Result(
    string schema,string status,string host,int fps,bool pluginFocusObserved,
    bool realTimelineClickFocusObserved,bool realTimelineClickFrameChanged,string observedTimelineFocusTarget,
    bool previewPixelProbeUsable,bool directNavigationPreviewUpdated,bool realTimelineClickUpdatesPreview,
    bool programmaticObservedTargetFocusSucceeded,bool programmaticFocusUpdatesPreview,
    ColorSample initial,ColorSample afterDirect,ColorSample afterRealClick,ColorSample afterDirect2,ColorSample afterProgrammaticFocus,
    bool scrollFramePublic,bool containFrameInViewportPublic,bool nearContainsNear,bool nearContainsFar,bool directFarContainsFar,bool afterScrollContainsFar,
    bool scrollFrameMovesViewport,bool scrollFrameMovesCurrentFrame,bool directFarNavigationMovesViewport,
    int publicVisibleRangeCandidateCount,string[] publicSurface,string? error);

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
            var z=new ColorSample(0,0,0,0,0,0,false,false);
            File.WriteAllText(Path.Combine(OutDir,"result.json"),JsonSerializer.Serialize(new Result(
                "cnwl.timeline-navigation-handoff.v1","FAIL_EXCEPTION","4.55.1.1 Lite",0,false,false,false,"",false,false,false,false,false,
                z,z,z,z,z,false,false,false,false,false,false,false,false,false,0,[],error),new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
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

            var videoInfo=t.GetType().GetProperty("VideoInfo",BindingFlags.Instance|BindingFlags.Public)?.GetValue(t);
            var fps=Convert.ToInt32(videoInfo?.GetType().GetProperty("FPS",BindingFlags.Instance|BindingFlags.Public)?.GetValue(videoInfo)??60,CultureInfo.InvariantCulture);
            if(fps<=0)throw new InvalidOperationException("FPS unavailable");
            var redFrame=Math.Max(1,fps/2);var greenFrame=fps*3;var length=fps*4;var farFrame=fps*50;

            var video=t.Items.OfType<VideoItem>().FirstOrDefault(x=>x.Remark=="CNWL_NAV_COLOR")??new VideoItem{
                FilePath=Media,Frame=0,Length=length,Layer=1,ContentOffset=TimeSpan.Zero,Remark="CNWL_NAV_COLOR"};
            video.PlaybackRate2.SetFirstValue(100);video.PlaybackRate2.SetAnimationParameters(video.Length,fps);
            if(!t.Items.Contains(video)&&!t.TryAddItems([video],video.Frame,video.Layer))throw new InvalidOperationException("Could not insert color VideoItem");
            var far=t.Items.OfType<VideoItem>().FirstOrDefault(x=>x.Remark=="CNWL_NAV_FAR")??new VideoItem{
                FilePath=Media,Frame=farFrame,Length=fps*2,Layer=2,ContentOffset=TimeSpan.Zero,Remark="CNWL_NAV_FAR"};
            far.PlaybackRate2.SetFirstValue(100);far.PlaybackRate2.SetAnimationParameters(far.Length,fps);
            if(!t.Items.Contains(far)&&!t.TryAddItems([far],far.Frame,far.Layer))throw new InvalidOperationException("Could not insert far VideoItem");
            t.RefreshTimelineLengthAndMaxLayer();await Task.Delay(1800);

            var timelineView=FindLargest(main,fe=>fe.GetType().FullName=="YukkuriMovieMaker.Views.TimelineView")
                ??FindLargest(main,fe=>fe.DataContext?.GetType().FullName=="YukkuriMovieMaker.ViewModels.TimelineViewModel")
                ??throw new InvalidOperationException("TimelineView not found");
            var previewCandidates=Elements(main).Where(fe=>fe.IsVisible&&fe.ActualWidth>30&&fe.ActualHeight>30&&
                ((fe.GetType().FullName??"").Contains("Preview",StringComparison.OrdinalIgnoreCase)||
                 (fe.DataContext?.GetType().FullName??"").Contains("Preview",StringComparison.OrdinalIgnoreCase))).ToArray();
            File.WriteAllLines(Path.Combine(OutDir,"preview-candidates.txt"),previewCandidates.Select(DescribeElement),new UTF8Encoding(false));
            var preview=previewCandidates.Where(x=>x.GetType().FullName=="YukkuriMovieMaker.Views.PreviewView").OrderByDescending(Area).FirstOrDefault()
                ??previewCandidates.OrderByDescending(Area).FirstOrDefault()
                ??throw new InvalidOperationException("Preview visual not found");
            var active=timelineView.DataContext??throw new InvalidOperationException("TimelineView DataContext missing");
            var scrollFrame=active.GetType().GetMethod("ScrollFrame",BindingFlags.Instance|BindingFlags.Public,null,[typeof(int)],null);
            var containFrame=active.GetType().GetMethod("ContainFrameInViewport",BindingFlags.Instance|BindingFlags.Public,null,[typeof(int)],null);
            var scrollToItem=active.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public).FirstOrDefault(m=>m.Name=="ScrollToItem"&&m.GetParameters().Length==1);
            var publicSurface=PublicSurface(active).ToArray();
            var visibleCount=publicSurface.Count(x=>x.Contains("VISIBLE_RANGE_CANDIDATE",StringComparison.Ordinal)||x.Contains("VIEWPORT_CANDIDATE",StringComparison.Ordinal));
            var focusService=active.GetType().GetProperty("FocusService",BindingFlags.Instance|BindingFlags.Public)?.GetValue(active);
            var focusServiceSurface=focusService==null?[]:DescribePublicMembers(focusService.GetType()).ToArray();
            File.WriteAllLines(Path.Combine(OutDir,"focus-service.txt"),focusServiceSurface,new UTF8Encoding(false));

            if(scrollFrame!=null)scrollFrame.Invoke(active,[redFrame]); else scrollToItem?.Invoke(active,[video]);
            await Task.Delay(600);
            var itemElement=FindItemElement(timelineView,video)??throw new InvalidOperationException("Rendered VideoItem not found");
            t.CurrentFrame=redFrame;t.SelectItem(video);await Task.Delay(250);
            await Click(itemElement);await Task.Delay(1200);
            var initial=CapturePreview(preview);
            var previewUsable=initial.IsRed;

            var button=ProbeView.Current?.FocusTarget??throw new InvalidOperationException("Probe focus target missing");
            Window.GetWindow(button)?.Activate();button.Focus();Keyboard.Focus(button);await Task.Delay(250);
            var pluginFocus=ReferenceEquals(Keyboard.FocusedElement,button)||button.IsKeyboardFocusWithin;

            t.CurrentFrame=greenFrame;t.SelectItem(video);await Task.Delay(900);
            var afterDirect=CapturePreview(preview);
            var directUpdated=afterDirect.IsGreen;

            var frameBeforeClick=t.CurrentFrame;
            main.Activate();Native.SetForegroundWindow(new WindowInteropHelper(main).Handle);
            await Click(itemElement);await Task.Delay(1000);
            var frameChanged=t.CurrentFrame!=frameBeforeClick;
            var focusTarget=Keyboard.FocusedElement as IInputElement;
            var focusDo=focusTarget as DependencyObject;
            var realFocus=focusDo!=null&&IsWithin(focusDo,timelineView);
            var focusType=focusTarget?.GetType().FullName??"<null>";
            var afterRealClick=CapturePreview(preview);
            var clickUpdates=previewUsable&&!directUpdated&&!frameChanged&&afterRealClick.IsGreen;

            t.CurrentFrame=redFrame;t.SelectItem(video);await Task.Delay(250);
            await Click(itemElement);await Task.Delay(900);
            var reset=CapturePreview(preview);
            Window.GetWindow(button)?.Activate();button.Focus();Keyboard.Focus(button);await Task.Delay(200);
            t.CurrentFrame=greenFrame;t.SelectItem(video);await Task.Delay(800);
            var afterDirect2=CapturePreview(preview);
            var programFocus=false;
            if(focusTarget!=null){
                main.Activate();Native.SetForegroundWindow(new WindowInteropHelper(main).Handle);
                try{Keyboard.Focus(focusTarget);await Task.Delay(900);programFocus=Keyboard.FocusedElement is DependencyObject d&&IsWithin(d,timelineView);}catch{}
            }
            var afterProgramFocus=CapturePreview(preview);
            var programFocusUpdates=reset.IsRed&&!afterDirect2.IsGreen&&programFocus&&afterProgramFocus.IsGreen;

            var focusServiceCandidate=focusService?.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public)
                .Where(m=>m.GetParameters().Length==0&&
                    (m.Name.Equals("Focus",StringComparison.OrdinalIgnoreCase)||
                     m.Name.Equals("RequestFocus",StringComparison.OrdinalIgnoreCase)||
                     m.Name.Equals("SetFocus",StringComparison.OrdinalIgnoreCase)))
                .OrderBy(m=>m.Name,StringComparer.Ordinal).FirstOrDefault();
            var focusServiceInvoked=false;var focusServiceKeyboardTarget="<not-invoked>";
            if(focusServiceCandidate!=null){
                Window.GetWindow(button)?.Activate();button.Focus();Keyboard.Focus(button);await Task.Delay(150);
                t.CurrentFrame=greenFrame;t.SelectItem(video);await Task.Delay(250);
                try{
                    focusServiceCandidate.Invoke(focusService,null);focusServiceInvoked=true;await Task.Delay(600);
                    focusServiceKeyboardTarget=Keyboard.FocusedElement?.GetType().FullName??"<null>";
                }catch(Exception ex){focusServiceKeyboardTarget="THROW:"+ex.GetBaseException().GetType().FullName+":"+ex.GetBaseException().Message;}
            }
            File.AppendAllLines(Path.Combine(OutDir,"focus-service.txt"),[
                $"candidate={focusServiceCandidate?.Name??"<none>"}",
                $"invoked={focusServiceInvoked}",
                $"keyboard_target_after_invoke={focusServiceKeyboardTarget}"
            ],new UTF8Encoding(false));

            if(scrollFrame!=null)scrollFrame.Invoke(active,[redFrame]);await Task.Delay(500);
            var nearContainsNear=InvokeBool(containFrame,active,redFrame);
            var nearContainsFar=InvokeBool(containFrame,active,farFrame);
            Window.GetWindow(button)?.Activate();button.Focus();Keyboard.Focus(button);await Task.Delay(150);
            t.CurrentFrame=farFrame;t.SelectItem(far);await Task.Delay(500);
            var directFarContains=InvokeBool(containFrame,active,farFrame);
            var beforeScrollFrame=t.CurrentFrame;
            if(scrollFrame!=null)scrollFrame.Invoke(active,[farFrame]);
            await Task.Delay(650);
            var afterScrollContains=InvokeBool(containFrame,active,farFrame);
            var scrollMovesFrame=t.CurrentFrame!=beforeScrollFrame;
            var directMoves=!nearContainsFar&&directFarContains;
            var scrollMoves=!directFarContains&&afterScrollContains;

            File.WriteAllLines(Path.Combine(OutDir,"surface.txt"),publicSurface,new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(OutDir,"preview-samples.txt"),[
                DescribeColor("initial",initial),DescribeColor("after_direct",afterDirect),DescribeColor("after_real_click",afterRealClick),
                DescribeColor("reset",reset),DescribeColor("after_direct2",afterDirect2),DescribeColor("after_programmatic_focus",afterProgramFocus),
                $"focus_target={focusType} real_focus_within_timeline={realFocus} click_frame_changed={frameChanged} programmatic_focus_succeeded={programFocus}"
            ],new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(OutDir,"viewport.txt"),[
                $"red_frame={redFrame} far_frame={farFrame}",
                $"near_contains_near={nearContainsNear}",$"near_contains_far={nearContainsFar}",
                $"direct_far_contains_far={directFarContains}",$"after_scrollframe_contains_far={afterScrollContains}",
                $"direct_far_navigation_moves_viewport={directMoves}",$"scrollframe_moves_viewport={scrollMoves}",$"scrollframe_moves_currentframe={scrollMovesFrame}"
            ],new UTF8Encoding(false));

            var result=new Result("cnwl.timeline-navigation-handoff.v1","PASS_NAVIGATION_HANDOFF_OBSERVATION","4.55.1.1 Lite",fps,pluginFocus,
                realFocus,frameChanged,focusType,previewUsable,directUpdated,clickUpdates,programFocus,programFocusUpdates,
                initial,afterDirect,afterRealClick,afterDirect2,afterProgramFocus,
                scrollFrame!=null,containFrame!=null,nearContainsNear,nearContainsFar,directFarContains,afterScrollContains,scrollMoves,scrollMovesFrame,directMoves,
                visibleCount,publicSurface,null);
            File.WriteAllText(Path.Combine(OutDir,"result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
        }catch(Exception ex){Fail(ex.ToString());}
    }

    static bool InvokeBool(MethodInfo? method,object target,int frame)
    {
        if(method==null)return false;
        try{return method.Invoke(target,[frame]) is bool b&&b;}catch{return false;}
    }
    static async Task Click(FrameworkElement element)
    {
        var b=GetBox(element);if(!b.Valid)throw new InvalidOperationException("Click target geometry invalid");
        Native.SetCursorPos(b.CenterX,b.CenterY);await Task.Delay(100);Native.mouse_event(Native.LD,0,0,0,0);await Task.Delay(60);Native.mouse_event(Native.LU,0,0,0,0);
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
        var box=GetBox(preview);if(!box.Valid)throw new InvalidOperationException("Preview geometry invalid");
        var w=Math.Clamp(box.Width,20,1600);var h=Math.Clamp(box.Height,20,1000);
        var screen=Native.GetDC(IntPtr.Zero);if(screen==IntPtr.Zero)throw new InvalidOperationException("GetDC failed");
        var mem=Native.CreateCompatibleDC(screen);var bmp=Native.CreateCompatibleBitmap(screen,w,h);var old=Native.SelectObject(mem,bmp);
        try{
            if(!Native.BitBlt(mem,0,0,w,h,screen,box.X,box.Y,Native.SRCCOPY))throw new InvalidOperationException("BitBlt failed");
            var bmi=new Native.BITMAPINFO{bmiHeader=new Native.BITMAPINFOHEADER{biSize=(uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(),biWidth=w,biHeight=-h,biPlanes=1,biBitCount=32,biCompression=Native.BI_RGB}};
            var bytes=new byte[w*h*4];
            if(Native.GetDIBits(mem,bmp,0,(uint)h,bytes,ref bmi,Native.DIB_RGB_COLORS)==0)throw new InvalidOperationException("GetDIBits failed");
            long rr=0,gg=0,bb=0;var red=0;var green=0;
            for(var i=0;i<bytes.Length;i+=4){
                var b=bytes[i];var g=bytes[i+1];var r=bytes[i+2];bb+=b;gg+=g;rr+=r;
                if(r>120&&r>g+45&&r>b+45)red++;
                if(g>100&&g>r+35&&g>b+35)green++;
            }
            var n=w*h;var ar=(int)(rr/n);var ag=(int)(gg/n);var ab=(int)(bb/n);
            var threshold=Math.Max(100,n/50);
            return new(ar,ag,ab,red,green,n,red>threshold,green>threshold);
        }finally{
            Native.SelectObject(mem,old);Native.DeleteObject(bmp);Native.DeleteDC(mem);Native.ReleaseDC(IntPtr.Zero,screen);
        }
    }
    static string DescribeColor(string name,ColorSample c)=>$"{name} avg=({c.R},{c.G},{c.B}) red_pixels={c.RedPixels}/{c.PixelCount} green_pixels={c.GreenPixels}/{c.PixelCount} red={c.IsRed} green={c.IsGreen}";

    static IEnumerable<string> DescribePublicMembers(Type t)
    {
        foreach(var p in t.GetProperties(BindingFlags.Instance|BindingFlags.Public).OrderBy(p=>p.Name))
            yield return $"PROPERTY {t.FullName}.{p.Name}:{p.PropertyType.FullName} read={p.CanRead} write={p.CanWrite}";
        foreach(var m in t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly).OrderBy(m=>m.Name))
            yield return $"METHOD {t.FullName}.{m.Name}({string.Join(",",m.GetParameters().Select(p=>p.ParameterType.FullName))}):{m.ReturnType.FullName}";
        foreach(var e in t.GetEvents(BindingFlags.Instance|BindingFlags.Public).OrderBy(e=>e.Name))
            yield return $"EVENT {t.FullName}.{e.Name}:{e.EventHandlerType?.FullName}";
    }

    static IEnumerable<string> PublicSurface(object active)
    {
        var t=active.GetType();
        foreach(var p in t.GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>Relevant(p.Name)).OrderBy(p=>p.Name)){
            var prefix=p.Name.Equals("Viewport",StringComparison.OrdinalIgnoreCase)||p.Name.Contains("Visible",StringComparison.OrdinalIgnoreCase)?"VIEWPORT_CANDIDATE ":"";
            string value;try{value=p.CanRead?(p.GetValue(active)?.ToString()??"<null>"):"<write-only>";}catch{value="<throw>";}
            yield return $"{prefix}PROPERTY {t.FullName}.{p.Name}:{p.PropertyType.FullName} value={value}";
        }
        foreach(var m in t.GetMethods(BindingFlags.Instance|BindingFlags.Public).Where(m=>Relevant(m.Name)).OrderBy(m=>m.Name)){
            var prefix=m.Name.Equals("ContainFrameInViewport",StringComparison.Ordinal)?"VISIBLE_RANGE_CANDIDATE ":"";
            yield return $"{prefix}METHOD {t.FullName}.{m.Name}({string.Join(",",m.GetParameters().Select(p=>p.ParameterType.FullName))}):{m.ReturnType.FullName}";
        }
    }
    static bool Relevant(string n)=>new[]{"Scroll","Visible","Viewport","Frame","Offset","Scale","Focus","Active"}.Any(x=>n.Contains(x,StringComparison.OrdinalIgnoreCase));
    static FrameworkElement? FindItemElement(DependencyObject root,IItem item)=>Elements(root).Where(x=>x.IsVisible&&ReferencesItem(x.DataContext,item)&&GetBox(x).Valid).OrderByDescending(Area).FirstOrDefault();
    static bool ReferencesItem(object? dc,IItem item)
    {
        if(dc==null)return false;if(ReferenceEquals(dc,item))return true;
        foreach(var p in dc.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Where(p=>p.CanRead&&p.GetIndexParameters().Length==0&&(typeof(IItem).IsAssignableFrom(p.PropertyType)||p.Name.Contains("Item",StringComparison.OrdinalIgnoreCase))).Take(40)){
            try{
                var v=p.GetValue(dc);if(ReferenceEquals(v,item))return true;
                if(v is IEnumerable e&&v is not string)foreach(var x in e)if(ReferenceEquals(x,item))return true;
            }catch{}
        }
        return false;
    }
    static string DescribeElement(FrameworkElement fe){var b=GetBox(fe);return $"{fe.GetType().FullName} dc={fe.DataContext?.GetType().FullName??"<null>"} box={b.X},{b.Y},{b.Width},{b.Height} focusable={fe.Focusable}";}
    static double Area(FrameworkElement x)=>x.ActualWidth*x.ActualHeight;
    static FrameworkElement? FindLargest(DependencyObject root,Func<FrameworkElement,bool> pred)=>Elements(root).Where(x=>x.IsVisible&&x.ActualWidth>30&&x.ActualHeight>30&&pred(x)).OrderByDescending(Area).FirstOrDefault();
    static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        if(root is FrameworkElement fe)yield return fe;int n=0;try{n=VisualTreeHelper.GetChildrenCount(root);}catch{}
        for(var i=0;i<n;i++)foreach(var x in Elements(VisualTreeHelper.GetChild(root,i)))yield return x;
    }
    static ScreenBox GetBox(FrameworkElement fe)
    {
        try{
            var a=fe.PointToScreen(new Point(0,0));var b=fe.PointToScreen(new Point(fe.ActualWidth,fe.ActualHeight));
            return new((int)Math.Round(a.X),(int)Math.Round(a.Y),(int)Math.Round(b.X-a.X),(int)Math.Round(b.Y-a.Y));
        }catch{return default;}
    }
}
