using System.Collections.Immutable;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Tachie;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Settings;
using NativeJson=YukkuriMovieMaker.Json.Json;

namespace Lab.TachiePausedNotice;

public sealed class ItemParameter:TachieItemParameterBase
{
    string file="";
    public string File {get=>file;set=>Set(ref file,value);}
    [JsonIgnore] public bool Ready=>Volatile.Read(ref ready);
    [JsonIgnore] public byte[]? Pixels {get;private set;}
    [JsonIgnore] public int Notices {get;private set;}
    [JsonIgnore] public int UndoCommands {get;private set;}
    bool ready,armed,retired;
    long generation;
    protected override IEnumerable<IAnimatable> GetAnimatables()=>[];
    internal void ObserveUndo(){UndoRedoCommandCreated+=(_,_)=>UndoCommands++;}
    internal void Retire(){retired=true;generation++;}
    internal ItemParameter CreateReadyClone()
    {
        if(!Ready||Pixels is null)throw new InvalidOperationException("Cannot clone before preparation is ready.");
        var clone=new ItemParameter{File=File,Pixels=Pixels.ToArray()};
        Volatile.Write(ref clone.ready,true);
        return clone;
    }
    internal void Arm(bool notify,Dispatcher owner,Action? afterReady=null)
    {
        owner.VerifyAccess();if(armed)return;armed=true;var stamp=++generation;Harness.Log("preparation-armed",new{notify,generation=stamp,retired});
        _=Prepare();
        async Task Prepare()
        {
            try
            {
                var pixels=await Task.Run(async()=>
                {
                    await Task.Delay(2000);
                    using var input=System.IO.File.OpenRead(File);
                    var decoder=new PngBitmapDecoder(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                    var frame=new FormatConvertedBitmap(decoder.Frames[0],PixelFormats.Pbgra32,null,0);
                    if(frame.PixelWidth!=256||frame.PixelHeight!=128)throw new InvalidDataException("Synthetic fixture dimensions changed");
                    var data=new byte[256*128*4];frame.CopyPixels(data,256*4,0);return data;
                });
                await owner.InvokeAsync(()=>
                {
                    if(retired||stamp!=generation){Harness.Log("preparation-stale",new{retired,generation,stamp});return;}
                    Pixels=pixels;Volatile.Write(ref ready,true);
                    Harness.Log("preparation-ready",new{notify});
                    if(notify){Notices++;OnPropertyChanged(nameof(File));Harness.Log("parameter-notification",new{Notices});}
                    afterReady?.Invoke();
                });
            }
            catch(Exception e){Harness.Error=e.GetType().Name+": "+e.Message;}
        }
    }
}
public sealed class CharacterParameter:TachieCharacterParameterBase{}
public sealed class FaceParameter:TachieFaceParameterBase{protected override IEnumerable<IAnimatable> GetAnimatables()=>[];}
public sealed class SyntheticTachie:ITachiePlugin
{
    public string Name=>"Lab synthetic delayed tachie";
    public ITachieCharacterParameter CreateCharacterParameter()=>new CharacterParameter();
    public ITachieItemParameter CreateItemParameter()=>new ItemParameter();
    public ITachieFaceParameter CreateFaceParameter()=>new FaceParameter();
    public ITachieSource CreateTachieSource(IGraphicsDevicesAndContext devices)=>new SyntheticSource(devices);
    public bool HasScriptFile=>false;public void CreateScriptFile(string directory){}
    public IEnumerable<ExoItem> CreateExoItems(int fps,IEnumerable<TachieItemExoDescription> items,IEnumerable<TachieFaceItemExoDescription> faces,IEnumerable<TachieVoiceItemExoDescription> voices)=>[];
}
// The host exclusively calls Update. The observer and completion worker never invoke it or touch GPU state.
internal sealed class SyntheticSource:ITachieSource2
{
    readonly IGraphicsDevicesAndContext devices;
    readonly AffineTransform2D transform;
    readonly ID2D1Image output;
    ID2D1Bitmap1 bitmap;
    ItemParameter? parameter;bool applied,disposed;
    internal int Id {get;}=Interlocked.Increment(ref Harness.NextSource);
    public ID2D1Image Output=>output;
    internal SyntheticSource(IGraphicsDevicesAndContext caller)
    {
        devices=caller.CreateContext();bitmap=Bitmap(Fixture.Solid(0,0,255));
        transform=new AffineTransform2D(devices.DeviceContext){TransformMatrix=Matrix3x2.CreateTranslation(-128,-64)};
        transform.SetInput(0,bitmap,true);output=transform.Output;
        Harness.Log("source-created",new{Id});
    }
    ID2D1Bitmap1 Bitmap(byte[] bytes)
    {
        var handle=GCHandle.Alloc(bytes,GCHandleType.Pinned);
        try{return devices.DeviceContext.CreateBitmap(new SizeI(256,128),handle.AddrOfPinnedObject(),1024,new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm,Vortice.DCommon.AlphaMode.Premultiplied),96,96));}
        finally{handle.Free();}
    }
    public void Update(TachieSourceDescription description)
    {
        var p=description.Tachie.ItemParameter as ItemParameter;
        if(p==null)throw new InvalidOperationException("Host did not supply probe parameter");
        if(parameter==null){parameter=p;p.ObserveUndo();}
        Harness.Parameter=p;Harness.Connect(Id,p);Harness.ObserveUpdate(Id,p,description.TimelinePosition.Frame,description.Usage);
        if(description.Usage==TimelineSourceUsage.Paused)Volatile.Write(ref Harness.LastHostFrame,description.TimelinePosition.Frame);
        Harness.Log("host-update",new{Id,usage=description.Usage.ToString(),position=description.TimelinePosition.Frame,ready=p.Ready});
        if(p.Ready&&!applied){var next=Bitmap(p.Pixels!);transform.SetInput(0,next,true);bitmap.Dispose();bitmap=next;applied=true;Harness.Log("gpu-input-applied",new{Id});}
    }
    public void Update(TimeSpan a,TimeSpan b,TimeSpan c,TimeSpan d,ITachieCharacterParameter character,ITachieItemParameter item,ITachieFaceParameter face,double mouth)
        =>throw new NotSupportedException("Legacy update lacks independent Usage evidence");
    // The timeline parameter is shared across host source instances. Initial source replacement must not cancel its owner preparation.
    public void Dispose(){if(disposed)return;disposed=true;Harness.Disconnect(Id);output.Dispose();transform.Dispose();bitmap.Dispose();devices.Dispose();Harness.Log("source-disposed",new{Id});}
}
internal static class Fixture
{
    internal static byte[] Solid(byte b,byte g,byte r){var bytes=new byte[256*128*4];for(var i=0;i<bytes.Length;i+=4){bytes[i]=b;bytes[i+1]=g;bytes[i+2]=r;bytes[i+3]=255;}return bytes;}
    internal static void Seed(string directory)
    {
        Harness.Log("seed-enter",new{});Directory.CreateDirectory(directory);var png=Path.Combine(directory,"synthetic-green.png");
        Capture.Save(png,Solid(0,255,0),256,128);
        Harness.Log("seed-png-written",new{});var parameter=new ItemParameter{File=png};
        var character=new Character{Name="Lab synthetic",TachieType=typeof(SyntheticTachie),TachieCharacterParameter=new CharacterParameter(),TachieDefaultItemParameter=new ItemParameter{File=png},TachieDefaultFaceParameter=new FaceParameter()};
        var item=new TachieItem(character){Frame=0,Length=300,Layer=0,TachieItemParameter=parameter};
        var timeline=new Timeline{Name="Synthetic stopped player",Items=ImmutableList.Create<IItem>(item)};
        timeline.VideoInfo.Width=256;timeline.VideoInfo.Height=128;timeline.VideoInfo.FPS=30;timeline.VideoInfo.BackgroundColor=System.Windows.Media.Colors.Black;timeline.RefreshTimelineLengthAndMaxLayer();
        var path=Path.Combine(directory,"synthetic.ymmp");
        var project=new Project(new[]{character},path);project.Timelines.Clear();project.Timelines.Add(timeline);
        Harness.Log("seed-project-created",new{itemCount=timeline.Items.Count});NativeJson.Save(project,path,null);Harness.Log("seed-project-saved",new{bytes=new FileInfo(path).Length});
    }
}
public sealed class Startup:ILocalizePlugin
{
    public Startup(){Harness.Log("plugin-constructed",new{});}
    static bool started;public string Name=>"Lab paused completion notice observer";
    public void SetCulture(CultureInfo culture)
    {
        Harness.Log("plugin-set-culture",new{dispatcherAvailable=Application.Current!=null});
        if(started||string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LAB_PAUSED_OUTPUT")))return;started=true;
        Harness.Log("plugin-dispatch-scheduled",new{priority="Normal"});
        var app=Application.Current;if(app==null){Harness.Log("plugin-no-application",new{});return;}
        app.Dispatcher.BeginInvoke(new Action(()=>Harness.Start()),DispatcherPriority.Normal);
    }
}
public sealed class ObserverTool:IToolPlugin
{
    public string Name=>"Lab paused observer";public Type ViewModelType=>typeof(ObserverVm);public Type ViewType=>typeof(ObserverView);public bool AllowMultipleInstances=>false;
}
public sealed class ObserverView:UserControl{public ObserverView(){Content=new TextBlock{Text="Read-only stopped-player observation"};}}
public sealed class ObserverVm:ITimelineToolViewModel,IToolViewModel
{
    public string Title=>"Lab paused observer";public bool CanSuspend=>false;
    public void SetTimelineToolInfo(TimelineToolInfo info){Harness.SetInfo(info);Harness.Log("timeline-tool-connected",new{items=info.Timeline.Items.Count,syntheticItems=info.Timeline.Items.OfType<TachieItem>().Count(x=>x.TachieItemParameter is ItemParameter)});}
    public ToolState SaveState()=>new(){Title=Title};public void LoadState(ToolState state){}
    public event PropertyChangedEventHandler? PropertyChanged{add{}remove{}}
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested{add{}remove{}}
}
internal static class Harness
{
    static readonly object gate=new();static readonly List<object> events=[];
    internal static int NextSource;internal static ItemParameter? Parameter;internal static string? Error;internal static TimelineToolInfo? Info;
    private static readonly Dictionary<int,ItemParameter> activeOwners=[];
    private sealed record UpdateStamp(int SourceId,int Frame,int Count,string Usage);
    private static readonly Dictionary<ItemParameter,UpdateStamp> updates=new(ReferenceEqualityComparer.Instance);
    internal static int LastHostFrame=-1;
    private static long navigationVersion;private static FrameActionResult? frameAction;private static int frameActionCalls;
    private sealed record ItemRefreshResult(string Mode,string Status,int OriginalSourceId,int FinalSourceId,int SourceCreatesBefore,int SourceCreatesAfter,int UpdatesBefore,int UpdatesAfter,int Commands,string? Error);
    private static ItemRefreshResult? itemRefresh;private static int itemRefreshCalls;
    private sealed record RoutedSeekResult(string Status,int Original,int Target,int Final,bool TargetUpdateObserved,bool RestoreUpdateObserved,int Commands,string? Error);
    private static RoutedSeekResult? routedSeek;private static int routedSeekCalls;
    internal static void Connect(int id,ItemParameter parameter){lock(gate)activeOwners[id]=parameter;}
    internal static void Disconnect(int id){lock(gate)activeOwners.Remove(id);}
    internal static void ObserveUpdate(int id,ItemParameter parameter,int frame,TimelineSourceUsage usage)
    {
        lock(gate)
        {
            var count=updates.TryGetValue(parameter,out var old)?old.Count+1:1;
            updates[parameter]=new(id,frame,count,usage.ToString());
        }
    }
    static UpdateStamp? Stamp(ItemParameter parameter){lock(gate)return updates.TryGetValue(parameter,out var value)?value:null;}
    internal static void SetInfo(TimelineToolInfo info)
    {
        if(Info!=null)((INotifyPropertyChanged)Info.Timeline).PropertyChanged-=FrameChanged;
        Info=info;navigationVersion++;((INotifyPropertyChanged)info.Timeline).PropertyChanged+=FrameChanged;
    }
    static void FrameChanged(object? sender,PropertyChangedEventArgs e)
    {if(e.PropertyName==nameof(Timeline.CurrentFrame)){navigationVersion++;Log("timeline-frame-changed",new{frame=Info?.Timeline.CurrentFrame,navigationVersion});}}
    sealed class FrameAccess(ItemParameter owner,object preview):IFrameAccess
    {
        public int Frame{get=>Info?.Timeline.CurrentFrame??-1;set{
            var command=CommandSettings.Default.GetCommand(CommandType.SeekWithoutSnap);
            var target=Application.Current.MainWindow;
            if(command==null||target==null||!command.CanExecute(value,target))throw new InvalidOperationException("Public SeekWithoutSnap unavailable");
            command.Execute(value,target);
            Log("public-seek-command-issued",new{frame=value,command="SeekWithoutSnap",completionAwaitable=false});
        }}
        public int LastFrame=>Info!.Timeline.Length-1;
        static readonly object MissingScope=new();public object Scope=>Info?.Timeline??MissingScope;
        public long NavigationVersion=>navigationVersion;
        public bool CanAct{get{lock(gate)return Info!=null&&Public(preview,"IsPlaying") is false&&Info.Timeline.Items.OfType<TachieItem>().Any(x=>ReferenceEquals(x.TachieItemParameter,owner))&&activeOwners.Values.Any(x=>ReferenceEquals(x,owner));}}
    }
    static void CompleteFrameAction(ItemParameter owner,object preview,string mode)
    {
        Application.Current.Dispatcher.VerifyAccess();if(++frameActionCalls!=1)throw new InvalidOperationException("Frame refresh reentered");
        var access=new FrameAccess(owner,preview);
        var clock=System.Diagnostics.Stopwatch.StartNew();
        frameAction=mode.StartsWith("same-",StringComparison.Ordinal)?FrameRefresh.Same(access):FrameRefresh.Nudge(access,mode.Contains("error",StringComparison.Ordinal)?()=>throw new InvalidOperationException("Injected after temporary move"):null);
        Log("completion-frame-action",new{mode,frameAction,frameActionCalls,actionElapsedMs=clock.Elapsed.TotalMilliseconds,sameUiTurn=true});
    }
    static void BeginRoutedSeekRoundTrip(ItemParameter owner,object preview)
    {
        Application.Current.Dispatcher.VerifyAccess();
        if(++routedSeekCalls!=1)throw new InvalidOperationException("Routed seek refresh reentered");
        _=Run();
        async Task Run()
        {
            var timeline=Info?.Timeline??throw new InvalidOperationException("Timeline unavailable");
            var original=timeline.CurrentFrame;
            if(original<0||timeline.Length<2){routedSeek=new("SKIPPED_BOUNDARY",original,original,original,false,false,0,null);return;}
            var target=original<timeline.Length-1?original+1:original-1;
            var commands=0;string? error=null;var targetObserved=false;var restoreObserved=false;
            bool StillOwned()
            {
                lock(gate)return ReferenceEquals(Info?.Timeline,timeline)
                    && Public(preview,"IsPlaying") is false
                    && timeline.Items.OfType<TachieItem>().Any(x=>ReferenceEquals(x.TachieItemParameter,owner))
                    && activeOwners.Values.Any(x=>ReferenceEquals(x,owner));
            }
            void Seek(int frame)
            {
                var command=CommandSettings.Default.GetCommand(CommandType.SeekWithoutSnap);
                var window=Application.Current.MainWindow;
                if(command==null||window==null||!command.CanExecute(frame,window))throw new InvalidOperationException("Public SeekWithoutSnap unavailable");
                commands++;command.Execute(frame,window);
                Log("public-seek-command-issued",new{frame,command="SeekWithoutSnap",commands});
            }
            async Task<bool> WaitFor(int frame,TimeSpan timeout)
            {
                var until=DateTimeOffset.UtcNow+timeout;
                while(DateTimeOffset.UtcNow<until)
                {
                    if(!StillOwned())return false;
                    var current=timeline.CurrentFrame;
                    if(current!=original&&current!=target)return false;
                    if(current==frame&&Volatile.Read(ref LastHostFrame)==frame)return true;
                    await Task.Delay(25);
                }
                return false;
            }
            try
            {
                if(!StillOwned()){routedSeek=new("SKIPPED_STATE",original,target,timeline.CurrentFrame,false,false,0,null);return;}
                Seek(target);
                targetObserved=await WaitFor(target,TimeSpan.FromSeconds(4));
                if(!targetObserved)
                {
                    error="Target frame was not observed by real ITachieSource2.Update";
                    if(StillOwned()&&timeline.CurrentFrame==target)
                    {
                        Seek(original);
                        restoreObserved=await WaitFor(original,TimeSpan.FromSeconds(4));
                    }
                    routedSeek=new(restoreObserved?"TARGET_NOT_OBSERVED_RESTORED":"TARGET_NOT_OBSERVED",original,target,timeline.CurrentFrame,false,restoreObserved,commands,error);
                    Log("routed-seek-roundtrip",routedSeek);return;
                }
                Log("routed-seek-target-observed",new{original,target,current=timeline.CurrentFrame,lastHostFrame=Volatile.Read(ref LastHostFrame)});
                if(!StillOwned()||timeline.CurrentFrame!=target)
                {
                    routedSeek=new("STALE_BEFORE_RESTORE",original,target,timeline.CurrentFrame,true,false,commands,null);
                    Log("routed-seek-roundtrip",routedSeek);return;
                }
                Seek(original);
                restoreObserved=await WaitFor(original,TimeSpan.FromSeconds(4));
                routedSeek=new(restoreObserved?"RESTORED_AFTER_REAL_UPDATES":"RESTORE_NOT_OBSERVED",original,target,timeline.CurrentFrame,true,restoreObserved,commands,
                    restoreObserved?null:"Original frame was not observed by real ITachieSource2.Update");
                Log("routed-seek-roundtrip",routedSeek);
            }
            catch(Exception e)
            {
                error=e.GetType().Name+": "+e.Message;
                if(StillOwned()&&timeline.CurrentFrame==target)
                {
                    try{Seek(original);restoreObserved=await WaitFor(original,TimeSpan.FromSeconds(4));}
                    catch(Exception restore){error+="; restore "+restore.GetType().Name+": "+restore.Message;}
                }
                routedSeek=new(restoreObserved?"ERROR_RESTORED":"ERROR",original,target,timeline.CurrentFrame,targetObserved,restoreObserved,commands,error);
                Log("routed-seek-roundtrip",routedSeek);
            }
        }
    }
    static void BeginItemRefreshAction(ItemParameter owner,object preview,string mode)
    {
        Application.Current.Dispatcher.VerifyAccess();
        if(++itemRefreshCalls!=1)throw new InvalidOperationException("Item refresh action reentered");
        _=Run();
        async Task Run()
        {
            var timeline=Info?.Timeline??throw new InvalidOperationException("Timeline unavailable");
            var item=timeline.Items.OfType<TachieItem>().Single(x=>ReferenceEquals(x.TachieItemParameter,owner));
            var originalStamp=Stamp(owner);
            var originalSource=originalStamp?.SourceId??-1;
            var beforeCount=originalStamp?.Count??0;
            var sourceCreatesBefore=NextSource;
            var commands=0;string? error=null;
            bool Stable()=>ReferenceEquals(Info?.Timeline,timeline)&&Public(preview,"IsPlaying") is false;
            async Task<UpdateStamp?> WaitUpdate(ItemParameter parameter,int afterCount,int milliseconds=3500)
            {
                var end=DateTimeOffset.UtcNow.AddMilliseconds(milliseconds);
                while(DateTimeOffset.UtcNow<end)
                {
                    if(!Stable())return null;
                    var stamp=Stamp(parameter);
                    if(stamp is not null&&stamp.Count>afterCount)return stamp;
                    await Task.Delay(25);
                }
                return null;
            }
            try
            {
                if(!Stable())throw new InvalidOperationException("Player/timeline not stable");
                if(mode=="param-replace")
                {
                    var clone=owner.CreateReadyClone();
                    commands++;item.TachieItemParameter=clone;
                    var seen=await WaitUpdate(clone,0);
                    itemRefresh=new(mode,seen is null?"NO_UPDATE":"UPDATE_OBSERVED",originalSource,seen?.SourceId??-1,sourceCreatesBefore,NextSource,beforeCount,seen?.Count??0,commands,null);
                }
                else if(mode=="item-replace")
                {
                    var index=timeline.Items.IndexOf(item);
                    var clone=item.GetClone() as TachieItem??throw new InvalidOperationException("TachieItem.GetClone did not return TachieItem");
                    var parameter=owner.CreateReadyClone();clone.TachieItemParameter=parameter;
                    commands++;timeline.Items=timeline.Items.SetItem(index,clone);
                    var seen=await WaitUpdate(parameter,0);
                    itemRefresh=new(mode,seen is null?"NO_UPDATE":"UPDATE_OBSERVED",originalSource,seen?.SourceId??-1,sourceCreatesBefore,NextSource,beforeCount,seen?.Count??0,commands,null);
                }
                else if(mode=="add-remove")
                {
                    var clone=item.GetClone() as TachieItem??throw new InvalidOperationException("TachieItem.GetClone did not return TachieItem");
                    var parameter=owner.CreateReadyClone();clone.TachieItemParameter=parameter;clone.Layer=item.Layer+1;
                    commands++;timeline.Items=timeline.Items.Add(clone);
                    var added=await WaitUpdate(parameter,0);
                    if(added is null)
                    {
                        timeline.Items=timeline.Items.Remove(clone);commands++;
                        itemRefresh=new(mode,"ADDED_NOT_OBSERVED",originalSource,-1,sourceCreatesBefore,NextSource,beforeCount,Stamp(owner)?.Count??beforeCount,commands,null);
                    }
                    else
                    {
                        commands++;timeline.Items=timeline.Items.Remove(clone);
                        var restored=await WaitUpdate(owner,beforeCount);
                        itemRefresh=new(mode,restored is null?"ORIGINAL_NOT_REOBSERVED":"ORIGINAL_REOBSERVED",originalSource,restored?.SourceId??-1,sourceCreatesBefore,NextSource,beforeCount,restored?.Count??beforeCount,commands,null);
                    }
                }
                else throw new InvalidOperationException("Unknown item refresh mode: "+mode);
            }
            catch(Exception e)
            {
                error=e.GetType().Name+": "+e.Message;
                itemRefresh=new(mode,"ERROR",originalSource,Stamp(owner)?.SourceId??-1,sourceCreatesBefore,NextSource,beforeCount,Stamp(owner)?.Count??beforeCount,commands,error);
            }
            Log("item-refresh-action",itemRefresh);
        }
    }
    static string Output=>Environment.GetEnvironmentVariable("LAB_PAUSED_OUTPUT")!;
    internal static void Log(string name,object details)
    {
        var entry=new{utc=DateTimeOffset.UtcNow,milliseconds=Environment.TickCount64,name,details};
        lock(gate){events.Add(entry);if(!string.IsNullOrEmpty(Output)){Directory.CreateDirectory(Output);System.IO.File.AppendAllText(Path.Combine(Output,"plugin-events.jsonl"),System.Text.Json.JsonSerializer.Serialize(entry)+"\n");}}
    }
    internal static void Start()
    {
        Log("plugin-dispatch-enter",new{});_=Run();
        async Task Run()
        {
            try
            {
                if(Environment.GetEnvironmentVariable("LAB_PAUSED_PHASE")=="seed")
                {Fixture.Seed(Environment.GetEnvironmentVariable("LAB_PAUSED_WORK")!);Result("SEEDED","Synthetic normal project generated",null);return;}
                var mode=Environment.GetEnvironmentVariable("LAB_PAUSED_PHASE")!;var notify=false;
                var itemMode=mode is "param-replace" or "item-replace" or "add-remove";
                var routedMode=mode.StartsWith("routed-",StringComparison.Ordinal);
                var frameMode=mode.StartsWith("same-",StringComparison.Ordinal)||mode.StartsWith("nudge-",StringComparison.Ordinal);
                FrameworkElement? surface=null;object? preview=null;Window? window=null;Capture.Frame? baseline=null;
                var candidates=new List<object>();
                var toolOpened=false;var initialFrameSet=false;
                var startupDeadline=DateTimeOffset.UtcNow.AddSeconds(100);
                for(var i=0;DateTimeOffset.UtcNow<startupDeadline;i++)
                {
                    if(Error!=null)throw new InvalidOperationException(Error);
                    if(System.IO.File.Exists(Path.Combine(Output,"result.json")))return;
                    if(!DialogFree().Clear){await Task.Delay(250);continue;}
                    // Do not activate a Tool while ordinary command-line project opening is still starting.
                    if(!toolOpened&&Parameter!=null)toolOpened=OpenObserverTool();
                    (surface,preview,window)=FindSurface(candidates);
                    if(surface!=null&&Parameter!=null&&Info?.Timeline.Items.OfType<TachieItem>().Any(item=>ReferenceEquals(item.TachieItemParameter,Parameter))==true)
                    {
                        if(!initialFrameSet)
                        {
                            initialFrameSet=true;
                            if(mode.EndsWith("-end",StringComparison.Ordinal)){Info.Timeline.CurrentFrame=Info.Timeline.Length-1;Log("setup-end-frame-before-baseline",new{frame=Info.Timeline.CurrentFrame});await Task.Delay(250);continue;}
                        }
                        if(Volatile.Read(ref LastHostFrame)!=Info.Timeline.CurrentFrame){await Task.Delay(250);continue;}
                        baseline=Capture.Read(surface);
                        if(baseline.RedFraction>.95)
                        {
                            if(!System.IO.File.Exists(Path.Combine(Output,"baseline-requested.txt"))){System.IO.File.WriteAllText(Path.Combine(Output,"baseline-requested.txt"),"Public live owner matched; awaiting outer dialog-free barrier");Log("baseline-barrier-requested",new{});}
                            if(System.IO.File.Exists(Path.Combine(Output,"baseline-permitted.json"))&&DialogFree().Clear)break;
                        }
                    }
                    await Task.Delay(250);
                }
                if(surface==null||Parameter==null||Info==null||baseline==null||baseline.RedFraction<=.95||!System.IO.File.Exists(Path.Combine(Output,"baseline-permitted.json"))||!DialogFree().Clear)
                {Result("BLOCKED","No live player with verified red synthetic preview baseline",new{candidates,sourceCount=NextSource,toolOpened,toolInfoAvailable=Info!=null,liveOwnerMatched=Info?.Timeline.Items.OfType<TachieItem>().Any(item=>ReferenceEquals(item.TachieItemParameter,Parameter)),baselineRed=baseline?.RedFraction,baselineGreen=baseline?.GreenFraction});return;}
                var dialogState=DialogFree();if(!dialogState.Clear){Result("BLOCKED","Popup/main-disabled at baseline",dialogState);return;}
                var beforeState=State(preview!,window!);Capture.Save(Path.Combine(Output,"baseline.png"),baseline.Pixels,baseline.Width,baseline.Height);
                if(beforeState.IsPlaying!=false||beforeState.Frame==null)
                {Result("BLOCKED","Cannot independently establish paused state and frame through public UI surface",new{beforeState});return;}
                var owner=Parameter;var savedBefore=JsonConvert.SerializeObject(owner);var commandsBefore=owner.UndoCommands;
                var timelineBefore=JsonConvert.SerializeObject(Info.Timeline);
                var selectedBefore=Info.Timeline.SelectedItems.Select(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode).ToArray();
                var inputProject=Path.Combine(Environment.GetEnvironmentVariable("LAB_PAUSED_WORK")!,"synthetic.ymmp");
                var projectHashBefore=Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(inputProject)));
                var historyEvents=0;EventHandler historyObserver=(_,_)=>historyEvents++;Info.UndoRedoManager.HistoryChanged+=historyObserver;
                var undoableBefore=Info.UndoRedoManager.IsUndoable;var redoableBefore=Info.UndoRedoManager.IsRedoable;
                int updateBefore;lock(gate)updateBefore=events.Count;
                Log("baseline-established",new{notify,beforeState,dialogState,barrier=System.Text.Json.JsonSerializer.Deserialize<JsonElement>(System.IO.File.ReadAllText(Path.Combine(Output,"baseline-permitted.json")))});
                System.IO.File.WriteAllText(Path.Combine(Output,"observing.txt"),"No UI interaction beyond this marker");
                owner.Arm(notify,Application.Current.Dispatcher,itemMode?()=>BeginItemRefreshAction(owner,preview!,mode):routedMode?()=>BeginRoutedSeekRoundTrip(owner,preview!):frameMode?()=>CompleteFrameAction(owner,preview!,mode):null);
                // Routed mode may temporarily visit exactly one neighbor. Only real host Update observes completion and permits restoration.
                Capture.Frame final=baseline;var samples=new List<object>();
                for(var i=0;i<36;i++)
                {
                    await Task.Delay(250);if(System.IO.File.Exists(Path.Combine(Output,"result.json")))return;var clear=DialogFree();if(!clear.Clear){Result("BLOCKED","Popup/main-disabled during observation; no UI action",clear);return;}var state=State(preview!,window!);final=Capture.Read(surface);
                    samples.Add(new{ready=owner.Ready,state,final.RedFraction,final.GreenFraction,routedSeek,itemRefresh});
                    if(state.IsPlaying!=false)throw new InvalidOperationException("Playback started during completion observation");
                    if(!routedMode&&state.Frame!=beforeState.Frame)throw new InvalidOperationException("Frame changed during completion observation");
                    if(routedMode&&routedSeek is not null&&routedSeek.Status=="RESTORED_AFTER_REAL_UPDATES"&&state.Frame!=beforeState.Frame)
                        throw new InvalidOperationException("Routed seek did not finish at the original frame");
                    if(Error!=null)throw new InvalidOperationException(Error);
                }
                Capture.Save(Path.Combine(Output,"after.png"),final.Pixels,final.Width,final.Height);
                var afterState=State(preview!,window!);
                object[] traffic;lock(gate)traffic=events.ToArray();
                var persistedUnchanged=JsonConvert.SerializeObject(owner)==savedBefore;
                var timelineUnchanged=JsonConvert.SerializeObject(Info.Timeline)==timelineBefore;
                var selectedUnchanged=selectedBefore.SequenceEqual(Info.Timeline.SelectedItems.Select(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode));
                var inputProjectUnchanged=Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(inputProject)))==projectHashBefore;
                var frameActionValid=!frameMode||frameActionCalls==1&&frameAction?.Final==Info.Timeline.CurrentFrame&&(mode.StartsWith("same-",StringComparison.Ordinal)?frameAction.Status=="SAME_ASSIGNED":mode.Contains("error",StringComparison.Ordinal)?frameAction.Status=="ERROR_RESTORED":frameAction.Status=="RESTORED");
                var routedValid=!routedMode||routedSeekCalls==1&&routedSeek?.Status=="RESTORED_AFTER_REAL_UPDATES"&&routedSeek.TargetUpdateObserved&&routedSeek.RestoreUpdateObserved&&routedSeek.Commands==2&&routedSeek.Final==Info.Timeline.CurrentFrame&&Info.Timeline.CurrentFrame.ToString(CultureInfo.InvariantCulture)==beforeState.Frame;
                var itemActionExecuted=!itemMode||itemRefreshCalls==1&&itemRefresh is not null&&itemRefresh.Status!="ERROR";
                var undoUnchanged=commandsBefore==owner.UndoCommands&&historyEvents==0&&undoableBefore==Info.UndoRedoManager.IsUndoable&&redoableBefore==Info.UndoRedoManager.IsRedoable;
                Info.UndoRedoManager.HistoryChanged-=historyObserver;
                var signalCorrect=owner.Ready&&owner.Notices==0&&frameActionValid&&routedValid&&itemActionExecuted;
                var changed=final.GreenFraction>.65;
                var stayedRed=final.RedFraction>.65&&final.GreenFraction<.05;
                var validPixels=changed||stayedRed;
                var clean=itemMode&&timelineUnchanged&&selectedUnchanged&&inputProjectUnchanged&&undoUnchanged&&persistedUnchanged;
                var status=!validPixels?"BLOCKED":!signalCorrect?"FAIL":itemMode?changed?(clean?"OBSERVED_ITEM_REPAINT_CLEAN":"OBSERVED_ITEM_REPAINT_WITH_SIDE_EFFECTS"):"OBSERVED_NO_REPAINT":!persistedUnchanged||!timelineUnchanged||!selectedUnchanged||!inputProjectUnchanged||!undoUnchanged?"FAIL":routedMode?changed?"PASS_ROUTED_TWO_STAGE_REPAINT":"OBSERVED_NO_REPAINT":frameMode?changed?"PASS_FRAME_ACTION_REPAINT":"OBSERVED_NO_REPAINT":stayedRed?"PASS_CONTROL_NO_REPAINT":"OBSERVED_CONTROL_REPAINT";
                Result(status,itemMode?"Real stopped player; item/parameter replacement refresh":routedMode?"Real stopped player; routed SeekWithoutSnap two-stage refresh":"Real stopped player; public frame action after readiness",new{mode,frameAction,frameActionCalls,routedSeek,routedSeekCalls,itemRefresh,itemRefreshCalls,clean,timelineUnchanged,selectedUnchanged,inputProjectUnchanged,notify,owner.Ready,owner.Notices,undoUnchanged,historyEvents,undoableBefore,redoableBefore,persistedUnchanged,beforeState,afterState,changed,stayedRed,baseline=new{baseline.RedFraction,baseline.GreenFraction},final=new{final.RedFraction,final.GreenFraction},samples,eventsAfterBaseline=traffic.Skip(updateBefore).ToArray(),dialogFreeBaseline=true,dialogFreeThroughout=true,liveUndoHistoryMeasured=true,liveDirtyFlagMeasured=false,windowTitleUnchanged=beforeState.Title==afterState.Title});
            }
            catch(Exception e){Result("BLOCKED","Harness/host boundary: "+e.GetType().Name+": "+e.Message,new{stack=e.StackTrace?.Split('\n').Take(8).ToArray()});}
            finally{Parameter?.Retire();}
        }
    }
    internal sealed record WindowEvidence(string Title,string Type,bool Enabled,bool Main,long Handle);
    internal sealed record DialogEvidence(bool Clear,WindowEvidence[] Windows);
    static DialogEvidence DialogFree()
    {
        var rows=Application.Current.Windows.Cast<Window>().Where(w=>w.IsVisible).Select(w=>
        {
            var handle=new WindowInteropHelper(w).Handle;
            return new WindowEvidence(Path.GetFileName(w.Title),w.GetType().FullName??"",w.IsEnabled&&IsWindowEnabled(handle),w.DataContext?.GetType().FullName=="YukkuriMovieMaker.ViewModels.MainViewModel",handle.ToInt64());
        }).ToArray();
        return new(rows.Any(x=>x.Main&&x.Enabled)&&rows.All(x=>x.Main||x.Title=="Lab paused observer"),rows);
    }
    [DllImport("user32.dll")]static extern bool IsWindowEnabled(IntPtr window);
    internal record Snapshot(bool? IsPlaying,string? Frame,string Title);
    static Snapshot State(object preview,Window window)
    {
        var playing=Public(preview,"IsPlaying") as bool?;
        var frame=Info?.Timeline.CurrentFrame.ToString(CultureInfo.InvariantCulture);
        return new(playing,frame,Path.GetFileName(window.Title));
    }
    static object? Public(object instance,string name)=>instance.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.Instance)?.GetValue(instance);
    static bool OpenObserverTool()
    {
        foreach(Window window in Application.Current.Windows)
        {
            var root=window.DataContext;if(root?.GetType().FullName!="YukkuriMovieMaker.ViewModels.MainViewModel")continue;
            if(Public(root,"ToolMenuItems") is not IEnumerable items)continue;
            foreach(var item in items)if(item!=null&&Visit(item,0))return true;
        }
        return false;
        bool Visit(object item,int depth)
        {
            if(depth>6)return false;
            var title=(Public(item,"Header")??Public(item,"Title")??Public(item,"Name"))?.ToString();
            if(title=="Lab paused observer"&&Public(item,"Command") is ICommand command)
            {var argument=Public(item,"CommandParameter");if(command.CanExecute(argument)){command.Execute(argument);return true;}}
            if((Public(item,"Children")??Public(item,"Items")) is IEnumerable children)foreach(var child in children)if(child!=null&&Visit(child,depth+1))return true;
            return false;
        }
    }
    static (FrameworkElement?,object?,Window?) FindSurface(List<object> candidates)
    {
        foreach(Window w in Application.Current.Windows)
        foreach(var element in Elements(w))
        {
            if(element.DataContext?.GetType().FullName!="YukkuriMovieMaker.ViewModels.PreviewViewModel")continue;
            if(element.ActualWidth<100||element.ActualHeight<50||!element.IsVisible)continue;
            var type=element.GetType().FullName??"";
            if(candidates.Count<40&&!candidates.Any(c=>c.ToString()==type))candidates.Add(new{type,element.Name,element.ActualWidth,element.ActualHeight});
            if(Public(element.DataContext,"Host") is FrameworkElement host&&host.IsVisible&&host.ActualWidth>=100&&host.ActualHeight>=50)
                return(host,element.DataContext,w);
            if(element is Image image&&image.Source is D3DImage || type.Contains("D3DImage",StringComparison.Ordinal)||type.Contains("VideoView",StringComparison.Ordinal)||type.Contains("PlayerView",StringComparison.Ordinal))
                return(element,element.DataContext,w);
        }
        return(null,null,null);
    }
    static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        if(root is FrameworkElement element)yield return element;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Elements(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    static void Result(string status,string reason,object? observation)
    {
        Directory.CreateDirectory(Output);object[] traffic;lock(gate)traffic=events.ToArray();
        System.IO.File.WriteAllText(Path.Combine(Output,"result.json"),System.Text.Json.JsonSerializer.Serialize(new{schema="lab.paused-tachie-notice.v1",status,reason,phase=Environment.GetEnvironmentVariable("LAB_PAUSED_PHASE"),sourceHead=Environment.GetEnvironmentVariable("SOURCE_HEAD"),runId=Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),runAttempt=Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT"),assemblySha256=Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(typeof(Startup).Assembly.Location))),observation,events=traffic,privateInvocation=false,directHostUpdate=false},new JsonSerializerOptions{WriteIndented=true}));
    }
}
internal static class Capture
{
    internal sealed record Frame(int Width,int Height,byte[] Pixels,double RedFraction,double GreenFraction);
    internal static Frame Read(FrameworkElement element)
    {
        var first=element.PointToScreen(new System.Windows.Point(0,0));var last=element.PointToScreen(new System.Windows.Point(element.ActualWidth,element.ActualHeight));
        var width=(int)(last.X-first.X);var height=(int)(last.Y-first.Y);if(width<=0||height<=0||width>4096||height>2160)throw new InvalidOperationException("Invalid preview screen rectangle");
        var screen=GetDC(IntPtr.Zero);var dc=CreateCompatibleDC(screen);IntPtr bitmap=IntPtr.Zero,previous=IntPtr.Zero;
        try
        {
            var info=new BitmapInfo{Size=40,Width=width,Height=-height,Planes=1,BitCount=32};
            bitmap=CreateDIBSection(screen,ref info,0,out var bits,IntPtr.Zero,0);if(bitmap==IntPtr.Zero)throw new InvalidOperationException("DIB capture unavailable");
            previous=SelectObject(dc,bitmap);if(!BitBlt(dc,0,0,width,height,screen,(int)first.X,(int)first.Y,0x00CC0020|0x40000000))throw new InvalidOperationException("Screen capture failed");
            var pixels=new byte[width*height*4];Marshal.Copy(bits,pixels,0,pixels.Length);var red=0;var green=0;
            // Measure central half, away from player chrome, borders and interpolation edges.
            for(var y=height/4;y<height*3/4;y++)for(var x=width/4;x<width*3/4;x++){var p=(y*width+x)*4;if(pixels[p+2]>180&&pixels[p+1]<70&&pixels[p]<70)red++;if(pixels[p+1]>180&&pixels[p+2]<70&&pixels[p]<70)green++;}
            var area=(height*3/4-height/4)*(width*3/4-width/4);return new(width,height,pixels,(double)red/area,(double)green/area);
        }
        finally{if(previous!=IntPtr.Zero)SelectObject(dc,previous);if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);DeleteDC(dc);ReleaseDC(IntPtr.Zero,screen);}
    }
    internal static void Save(string path,byte[] pixels,int width,int height)
    {var bitmap=System.Windows.Media.Imaging.BitmapSource.Create(width,height,96,96,PixelFormats.Bgr32,null,pixels,width*4);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=System.IO.File.Create(path);png.Save(output);}
    [StructLayout(LayoutKind.Sequential)]struct BitmapInfo{public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,ImageSize;public int X,Y;public uint Used,Important,Color;}
    [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapInfo info,uint usage,out IntPtr bits,IntPtr section,uint offset);
    [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]static extern bool BitBlt(IntPtr dc,int x,int y,int width,int height,IntPtr source,int sx,int sy,uint mode);
}
