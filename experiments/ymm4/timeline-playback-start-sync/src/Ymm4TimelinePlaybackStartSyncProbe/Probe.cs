using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4TimelinePlaybackStartSyncProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL Timeline Playback Start Sync Probe";
    public void SetCulture(CultureInfo cultureInfo) => Bootstrap.Schedule();
}
public sealed class ProbeTool : IToolPlugin
{
    public string Name => "CNWL Timeline Playback Start Sync Probe";
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
    public string Title=>"CNWL Timeline Playback Start Sync Probe"; public bool CanSuspend=>false;
    public void SetTimelineToolInfo(TimelineToolInfo info)=>PlaybackProbe.Start(info.Timeline);
    public ToolState SaveState()=>new(){Title=Title}; public void LoadState(ToolState stateData){}
    public event PropertyChangedEventHandler? PropertyChanged { add{} remove{} }
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested { add{} remove{} }
}

internal static class Bootstrap
{
    static bool scheduled,created,opened;
    public static void Schedule()
    {
        if(scheduled||string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CNWL_YMM4_PLAYBACK_SYNC_DIR")))return;
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
                    if(ticks>120){timer.Stop();PlaybackProbe.Fail("Tool open/bootstrap timeout");}
                }catch(Exception ex){timer.Stop();PlaybackProbe.Fail(ex.ToString());}
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
            if(label.Contains("CNWL Timeline Playback Start Sync Probe",StringComparison.Ordinal)){
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
    internal const uint LD=0x0002,LU=0x0004;
}

internal sealed record PlaybackCase(string name,int displayedFrame,int firstPlaybackFrame,int[] playbackFrames,bool startedNearDisplayedFrame,Dictionary<string,string> beforeState,Dictionary<string,string> afterNavigateState);
internal sealed record Result(string schema,string status,string host,string playbackTrigger,PlaybackCase programmatic,PlaybackCase ruler,string[] previewSurface,string? error);

internal static class PlaybackProbe
{
    static bool started;
    static Timeline? timeline;
    static readonly List<int> Frames=[];
    static string phase="";
    static string OutDir=>Path.GetFullPath(Environment.GetEnvironmentVariable("CNWL_YMM4_PLAYBACK_SYNC_DIR")!);

    public static void Start(Timeline t){if(started)return;started=true;timeline=t;_=RunAsync();}
    public static void Fail(string error)
    {
        try{
            Directory.CreateDirectory(OutDir);File.WriteAllText(Path.Combine(OutDir,"error.txt"),error,new UTF8Encoding(false));
            var e=new PlaybackCase("error",0,0,[],false,[],[]);
            File.WriteAllText(Path.Combine(OutDir,"result.json"),JsonSerializer.Serialize(new Result("cnwl.timeline-playback-start-sync.v1","FAIL_EXCEPTION","4.55.1.1 Lite","",e,e,[],error),new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
        }catch{}
    }

    static async Task RunAsync()
    {
        try{
            Directory.CreateDirectory(OutDir);
            var t=timeline!;
            var main=Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w.DataContext?.GetType().FullName=="YukkuriMovieMaker.ViewModels.MainViewModel")
                ??throw new InvalidOperationException("Main window not found");
            main.WindowState=WindowState.Maximized;main.Activate();Native.SetForegroundWindow(new WindowInteropHelper(main).Handle);await Task.Delay(1000);

            var character=new Character{Name="CNWL_PlaybackSync"};
            var voice=t.Items.OfType<VoiceItem>().FirstOrDefault(x=>x.Remark=="CNWL_PLAYBACK_SYNC")??new VoiceItem(character){Frame=180,Length=90,Layer=1,Serif="playback sync",Remark="CNWL_PLAYBACK_SYNC"};
            if(!t.Items.Contains(voice)&&!t.TryAddItems([voice],voice.Frame,voice.Layer))throw new InvalidOperationException("Could not insert Voice fixture");
            t.RefreshTimelineLengthAndMaxLayer();t.CurrentFrame=0;t.SelectedItems=ImmutableList<IItem>.Empty;await Task.Delay(1000);

            var timelineView=FindLargest(main,fe=>fe.GetType().FullName=="YukkuriMovieMaker.Views.TimelineView"||fe.DataContext?.GetType().FullName=="YukkuriMovieMaker.ViewModels.TimelineViewModel")
                ??throw new InvalidOperationException("TimelineView missing");
            var ruler=FindLargest(main,fe=>(fe.GetType().FullName??"").Contains("TimelineScaleView",StringComparison.OrdinalIgnoreCase)||
                (fe.DataContext?.GetType().FullName??"").Contains("TimelineScaleViewModel",StringComparison.OrdinalIgnoreCase))
                ??throw new InvalidOperationException("Timeline ruler missing");
            var previewVm=Elements(main).Select(x=>x.DataContext).FirstOrDefault(x=>(x?.GetType().FullName??"").Contains("PreviewViewModel",StringComparison.OrdinalIgnoreCase))
                ??throw new InvalidOperationException("PreviewViewModel missing");
            var surface=DescribeSurface(previewVm).ToArray();
            File.WriteAllLines(Path.Combine(OutDir,"preview-surface.txt"),surface,new UTF8Encoding(false));

            var (playCommand,playName)=FindPlayCommand(previewVm);
            if(playCommand==null)throw new InvalidOperationException("No executable public/visible Play command found. See preview-surface.txt.");
            File.WriteAllText(Path.Combine(OutDir,"trigger.txt"),playName,new UTF8Encoding(false));

            if(t is INotifyPropertyChanged npc)npc.PropertyChanged+=TimelineChanged;

            // Case A: plugin owns focus; only public Timeline state is changed.
            var button=ProbeView.Current?.FocusTarget??throw new InvalidOperationException("Probe focus target missing");
            Window.GetWindow(button)?.Activate();button.Focus();Keyboard.Focus(button);await Task.Delay(200);
            var beforeA=State(previewVm);
            t.CurrentFrame=voice.Frame;t.SelectItem(voice);await Task.Delay(250);
            var afterA=State(previewVm);
            var program=await MeasurePlayback("programmatic",t.CurrentFrame,playCommand);

            // Case B: real ruler click establishes a native host position, then start playback.
            var active=timelineView.DataContext;
            var scrollFrame=active?.GetType().GetMethod("ScrollFrame",BindingFlags.Instance|BindingFlags.Public,null,[typeof(int)],null);
            scrollFrame?.Invoke(active,[300]);await Task.Delay(350);
            main.Activate();Native.SetForegroundWindow(new WindowInteropHelper(main).Handle);
            var rb=Box(ruler);var click=new Point(rb.Left+rb.Width*.55,rb.Top+rb.Height*.5);
            await Click(click);await Task.Delay(450);
            var rulerFrame=t.CurrentFrame;
            var beforeB=State(previewVm);
            var rulerCase=await MeasurePlayback("ruler",rulerFrame,playCommand);
            rulerCase=rulerCase with{beforeState=beforeB,afterNavigateState=State(previewVm)};

            program=program with{beforeState=beforeA,afterNavigateState=afterA};
            if(t is INotifyPropertyChanged npc2)npc2.PropertyChanged-=TimelineChanged;

            var result=new Result("cnwl.timeline-playback-start-sync.v1","PASS_PLAYBACK_START_SYNC_OBSERVATION","4.55.1.1 Lite",playName,program,rulerCase,surface,null);
            File.WriteAllText(Path.Combine(OutDir,"trace.txt"),string.Join(Environment.NewLine,new[]{
                $"programmatic displayed={program.displayedFrame} first={program.firstPlaybackFrame} frames={string.Join(",",program.playbackFrames)} near={program.startedNearDisplayedFrame}",
                $"ruler displayed={rulerCase.displayedFrame} first={rulerCase.firstPlaybackFrame} frames={string.Join(",",rulerCase.playbackFrames)} near={rulerCase.startedNearDisplayedFrame}"
            }),new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(OutDir,"result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
        }catch(Exception ex){Fail(ex.ToString());}
    }

    static void TimelineChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(Timeline.CurrentFrame)&&phase.Length>0&&timeline!=null)lock(Frames)Frames.Add(timeline.CurrentFrame);
    }
    static async Task<PlaybackCase> MeasurePlayback(string name,int displayed,ICommand play)
    {
        lock(Frames)Frames.Clear();phase=name;
        if(!play.CanExecute(null))throw new InvalidOperationException("Play command cannot execute for "+name);
        play.Execute(null);await Task.Delay(650);
        var observed=Frames.ToArray();
        // Most native play commands are toggles; invoke again to stop when still executable.
        try{if(play.CanExecute(null))play.Execute(null);}catch{}
        await Task.Delay(250);phase="";
        observed=observed.Length==0?Frames.ToArray():observed;
        var first=observed.FirstOrDefault();
        var near=observed.Length>0&&Math.Abs(first-displayed)<=15;
        return new(name,displayed,first,observed.Take(30).ToArray(),near,[],[]);
    }

    static (ICommand? Command,string Name) FindPlayCommand(object preview)
    {
        var candidates=new List<(ICommand Command,string Name,int Score)>();
        foreach(var p in preview.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>p.CanRead)){
            object? v=null;try{v=p.GetValue(preview);}catch{}
            if(v is not ICommand c)continue;
            int score=0;var n=p.Name;
            if(n.Contains("Play",StringComparison.OrdinalIgnoreCase))score+=10;
            if(n.Contains("Pause",StringComparison.OrdinalIgnoreCase)||n.Contains("Stop",StringComparison.OrdinalIgnoreCase)||n.Contains("Rate",StringComparison.OrdinalIgnoreCase))score-=5;
            try{if(c.CanExecute(null))score+=2;}catch{}
            candidates.Add((c,"PreviewViewModel."+p.Name,score));
        }
        var best=candidates.OrderByDescending(x=>x.Score).FirstOrDefault(x=>x.Score>0);
        if(best.Command!=null)return(best.Command,best.Name);
        foreach(var b in Application.Current.Windows.Cast<Window>().SelectMany(Elements).OfType<ButtonBase>().Where(x=>x.IsVisible)){
            if(b.Command==null)continue;
            var text=(b.ToolTip?.ToString()??"")+" "+(b is Button btn?btn.Content?.ToString():"");
            if(text.Contains("再生",StringComparison.OrdinalIgnoreCase)||text.Contains("play",StringComparison.OrdinalIgnoreCase))
                return(b.Command,"VisualButton:"+text.Trim());
        }
        return(null,"<none>");
    }

    static Dictionary<string,string> State(object preview)
    {
        var d=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var p in preview.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>p.CanRead&&Relevant(p.Name))){
            try{
                var v=p.GetValue(preview);if(v==null)continue;
                var value=Unwrap(v);
                if(value is int or long or double or float or bool or TimeSpan or string or Enum)d[p.Name]=Convert.ToString(value,CultureInfo.InvariantCulture)??"";
            }catch{}
        }
        return d;
    }
    static object? Unwrap(object v)
    {
        var p=v.GetType().GetProperty("Value",BindingFlags.Instance|BindingFlags.Public);
        if(p?.CanRead==true)try{return p.GetValue(v);}catch{}
        return v;
    }
    static bool Relevant(string n)=>new[]{"Play","Pause","Stop","Seek","Frame","Current","Position","Time"}.Any(x=>n.Contains(x,StringComparison.OrdinalIgnoreCase));
    static IEnumerable<string> DescribeSurface(object preview)
    {
        var t=preview.GetType();
        foreach(var p in t.GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>Relevant(p.Name)).OrderBy(p=>p.Name)){
            string val;try{val=Convert.ToString(Unwrap(p.GetValue(preview)!),CultureInfo.InvariantCulture)??"<null>";}catch{val="<throw>";}
            yield return $"PROPERTY {t.FullName}.{p.Name}:{p.PropertyType.FullName} value={val}";
        }
        foreach(var m in t.GetMethods(BindingFlags.Instance|BindingFlags.Public).Where(m=>Relevant(m.Name)).OrderBy(m=>m.Name))
            yield return $"METHOD {t.FullName}.{m.Name}({string.Join(",",m.GetParameters().Select(p=>p.ParameterType.FullName))}):{m.ReturnType.FullName}";
    }

    static async Task Click(Point p){Native.SetCursorPos((int)Math.Round(p.X),(int)Math.Round(p.Y));await Task.Delay(100);Native.mouse_event(Native.LD,0,0,0,0);await Task.Delay(60);Native.mouse_event(Native.LU,0,0,0,0);}
    static (double Left,double Top,double Width,double Height) Box(FrameworkElement fe){var p=fe.PointToScreen(new Point(0,0));return(p.X,p.Y,fe.ActualWidth,fe.ActualHeight);}
    static FrameworkElement? FindLargest(DependencyObject root,Func<FrameworkElement,bool> pred)=>Elements(root).Where(x=>x.IsVisible&&x.ActualWidth>20&&x.ActualHeight>10&&pred(x)).OrderByDescending(x=>x.ActualWidth*x.ActualHeight).FirstOrDefault();
    static IEnumerable<FrameworkElement> Elements(DependencyObject root){if(root is FrameworkElement fe)yield return fe;int n=0;try{n=VisualTreeHelper.GetChildrenCount(root);}catch{}for(int i=0;i<n;i++)foreach(var x in Elements(VisualTreeHelper.GetChild(root,i)))yield return x;}
}
