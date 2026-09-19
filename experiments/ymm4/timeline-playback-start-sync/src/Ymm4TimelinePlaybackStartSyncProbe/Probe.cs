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

internal sealed record PlaybackCase(string name,int displayedFrame,int firstPlaybackFrame,int[] playbackFrames,bool startedNearDisplayedFrame,
    Dictionary<string,string> beforeState,Dictionary<string,string> afterNavigateState,Dictionary<string,string> afterSyncState);
internal sealed record Result(string schema,string status,string host,string playbackTrigger,string syncRoute,
    PlaybackCase timelineOnly,PlaybackCase timelinePlusSeek,PlaybackCase ruler,string[] previewSurface,string? error);

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
            var e=new PlaybackCase("error",0,0,[],false,[],[],[]);
            File.WriteAllText(Path.Combine(OutDir,"result.json"),JsonSerializer.Serialize(new Result("cnwl.timeline-playback-start-sync.v1","FAIL_EXCEPTION","4.55.1.1 Lite","","",e,e,e,[],error),new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
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

            var togglePlay=previewVm.GetType().GetMethod("TogglePlayAsync",BindingFlags.Instance|BindingFlags.Public,null,Type.EmptyTypes,null)
                ??throw new InvalidOperationException("PreviewViewModel.TogglePlayAsync() not found");
            var stop=previewVm.GetType().GetMethod("StopAsync",BindingFlags.Instance|BindingFlags.Public,null,Type.EmptyTypes,null)
                ??throw new InvalidOperationException("PreviewViewModel.StopAsync() not found");
            var seek=previewVm.GetType().GetMethod("SeekAsync",BindingFlags.Instance|BindingFlags.Public,null,[typeof(int)],null)
                ??throw new InvalidOperationException("PreviewViewModel.SeekAsync(int) not found");
            var playName="PreviewViewModel.TogglePlayAsync()";
            var syncName="PreviewViewModel.SeekAsync(int)";
            File.WriteAllText(Path.Combine(OutDir,"trigger.txt"),playName+"\n"+syncName,new UTF8Encoding(false));

            if(t is INotifyPropertyChanged npc)npc.PropertyChanged+=TimelineChanged;
            var button=ProbeView.Current?.FocusTarget??throw new InvalidOperationException("Probe focus target missing");

            // Case A: Tool focus + Timeline.CurrentFrame only.
            await InvokeTask(seek,previewVm,0);await Task.Delay(250);
            Window.GetWindow(button)?.Activate();button.Focus();Keyboard.Focus(button);await Task.Delay(150);
            var beforeA=State(previewVm);
            t.CurrentFrame=voice.Frame;t.SelectItem(voice);await Task.Delay(250);
            var afterA=State(previewVm);
            var timelineOnly=await MeasurePlayback("timeline-only",t.CurrentFrame,previewVm,togglePlay,stop);
            timelineOnly=timelineOnly with{beforeState=beforeA,afterNavigateState=afterA,afterSyncState=afterA};

            // Case B: same Tool-focus navigation, then the public Preview seek surface.
            await InvokeTask(seek,previewVm,0);await Task.Delay(250);
            Window.GetWindow(button)?.Activate();button.Focus();Keyboard.Focus(button);await Task.Delay(150);
            var beforeSeek=State(previewVm);
            t.CurrentFrame=voice.Frame;t.SelectItem(voice);await Task.Delay(200);
            var afterTimeline=State(previewVm);
            await InvokeTask(seek,previewVm,voice.Frame);await Task.Delay(350);
            var afterSeek=State(previewVm);
            var timelinePlusSeek=await MeasurePlayback("timeline-plus-seek",t.CurrentFrame,previewVm,togglePlay,stop);
            timelinePlusSeek=timelinePlusSeek with{beforeState=beforeSeek,afterNavigateState=afterTimeline,afterSyncState=afterSeek};

            // Case C: real ruler click as native-control behavior.
            await InvokeTask(seek,previewVm,0);await Task.Delay(250);
            var active=timelineView.DataContext;
            var scrollFrame=active?.GetType().GetMethod("ScrollFrame",BindingFlags.Instance|BindingFlags.Public,null,[typeof(int)],null);
            scrollFrame?.Invoke(active,[300]);await Task.Delay(350);
            main.Activate();Native.SetForegroundWindow(new WindowInteropHelper(main).Handle);
            var rb=Box(ruler);var click=new Point(rb.Left+rb.Width*.55,rb.Top+rb.Height*.5);
            await Click(click);await Task.Delay(450);
            var rulerFrame=t.CurrentFrame;
            var rulerState=State(previewVm);
            var rulerCase=await MeasurePlayback("ruler",rulerFrame,previewVm,togglePlay,stop);
            rulerCase=rulerCase with{beforeState=rulerState,afterNavigateState=rulerState,afterSyncState=State(previewVm)};

            if(t is INotifyPropertyChanged npc2)npc2.PropertyChanged-=TimelineChanged;

            var result=new Result("cnwl.timeline-playback-start-sync.v1","PASS_PLAYBACK_START_SYNC_OBSERVATION","4.55.1.1 Lite",playName,syncName,timelineOnly,timelinePlusSeek,rulerCase,surface,null);
            File.WriteAllText(Path.Combine(OutDir,"trace.txt"),string.Join(Environment.NewLine,new[]{
                $"timeline_only displayed={timelineOnly.displayedFrame} first={timelineOnly.firstPlaybackFrame} frames={string.Join(",",timelineOnly.playbackFrames)} near={timelineOnly.startedNearDisplayedFrame}",
                $"timeline_plus_seek displayed={timelinePlusSeek.displayedFrame} first={timelinePlusSeek.firstPlaybackFrame} frames={string.Join(",",timelinePlusSeek.playbackFrames)} near={timelinePlusSeek.startedNearDisplayedFrame}",
                $"ruler displayed={rulerCase.displayedFrame} first={rulerCase.firstPlaybackFrame} frames={string.Join(",",rulerCase.playbackFrames)} near={rulerCase.startedNearDisplayedFrame}"
            }),new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(OutDir,"result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
        }catch(Exception ex){Fail(ex.ToString());}
    }

    static void TimelineChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(Timeline.CurrentFrame)&&phase.Length>0&&timeline!=null)lock(Frames)Frames.Add(timeline.CurrentFrame);
    }
    static async Task<PlaybackCase> MeasurePlayback(string name,int displayed,object preview,MethodInfo togglePlay,MethodInfo stop)
    {
        lock(Frames)Frames.Clear();phase=name;
        await InvokeTask(togglePlay,preview);await Task.Delay(650);
        var observed=Frames.ToArray();
        await InvokeTask(stop,preview);await Task.Delay(250);phase="";
        observed=observed.Length==0?Frames.ToArray():observed;
        var first=observed.FirstOrDefault();
        var near=observed.Length>0&&Math.Abs(first-displayed)<=15;
        return new(name,displayed,first,observed.Take(30).ToArray(),near,[],[],[]);
    }
    static async Task InvokeTask(MethodInfo method,object target,params object?[] args)
    {
        var value=method.Invoke(target,args);
        if(value is Task task)await task;
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
