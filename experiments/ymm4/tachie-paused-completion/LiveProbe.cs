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
    internal void Arm(bool notify,Dispatcher owner)
    {
        owner.VerifyAccess();if(armed)return;armed=true;var stamp=++generation;
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
                    if(retired||stamp!=generation)return;
                    Pixels=pixels;Volatile.Write(ref ready,true);
                    Harness.Log("preparation-ready",new{notify});
                    if(notify){Notices++;OnPropertyChanged(nameof(File));Harness.Log("parameter-notification",new{Notices});}
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
        Harness.Parameter=p;
        Harness.Log("host-update",new{Id,usage=description.Usage.ToString(),position=description.TimelinePosition.ToString(),ready=p.Ready});
        if(p.Ready&&!applied){var next=Bitmap(p.Pixels!);transform.SetInput(0,next,true);bitmap.Dispose();bitmap=next;applied=true;Harness.Log("gpu-input-applied",new{Id});}
    }
    public void Update(TimeSpan a,TimeSpan b,TimeSpan c,TimeSpan d,ITachieCharacterParameter character,ITachieItemParameter item,ITachieFaceParameter face,double mouth)
        =>throw new NotSupportedException("Legacy update lacks independent Usage evidence");
    public void Dispose(){if(disposed)return;disposed=true;parameter?.Retire();output.Dispose();transform.Dispose();bitmap.Dispose();devices.Dispose();Harness.Log("source-disposed",new{Id});}
}
internal static class Fixture
{
    internal static byte[] Solid(byte b,byte g,byte r){var bytes=new byte[256*128*4];for(var i=0;i<bytes.Length;i+=4){bytes[i]=b;bytes[i+1]=g;bytes[i+2]=r;bytes[i+3]=255;}return bytes;}
    internal static void Seed(string directory)
    {
        Directory.CreateDirectory(directory);var png=Path.Combine(directory,"synthetic-green.png");
        Capture.Save(png,Solid(0,255,0),256,128);
        var parameter=new ItemParameter{File=png};
        var character=new Character{Name="Lab synthetic",TachieType=typeof(SyntheticTachie),TachieCharacterParameter=new CharacterParameter(),TachieDefaultItemParameter=new ItemParameter{File=png},TachieDefaultFaceParameter=new FaceParameter()};
        var item=new TachieItem(character){Frame=0,Length=300,Layer=0,TachieItemParameter=parameter};
        var timeline=new Timeline{Name="Synthetic stopped player",Items=ImmutableList.Create<IItem>(item)};
        timeline.VideoInfo.Width=256;timeline.VideoInfo.Height=128;timeline.VideoInfo.FPS=30;timeline.VideoInfo.BackgroundColor=System.Windows.Media.Colors.Black;timeline.RefreshTimelineLengthAndMaxLayer();
        var project=new Project(new[]{character},Path.Combine(directory,"synthetic.ymmp"));project.Timelines.Clear();project.Timelines.Add(timeline);
        NativeJson.Save(project,project.FilePath,null);
    }
}
public sealed class Startup:ILocalizePlugin
{
    static bool started;public string Name=>"Lab paused completion notice observer";
    public void SetCulture(CultureInfo culture)
    {
        if(started||string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LAB_PAUSED_OUTPUT")))return;started=true;
        Application.Current.Dispatcher.BeginInvoke(new Action(()=>Harness.Start()),DispatcherPriority.ApplicationIdle);
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
    public void SetTimelineToolInfo(TimelineToolInfo info)=>Harness.Info=info;
    public ToolState SaveState()=>new(){Title=Title};public void LoadState(ToolState state){}
    public event PropertyChangedEventHandler? PropertyChanged{add{}remove{}}
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested{add{}remove{}}
}
internal static class Harness
{
    static readonly object gate=new();static readonly List<object> events=[];
    internal static int NextSource;internal static ItemParameter? Parameter;internal static string? Error;internal static TimelineToolInfo? Info;
    static string Output=>Environment.GetEnvironmentVariable("LAB_PAUSED_OUTPUT")!;
    internal static void Log(string name,object details){lock(gate)events.Add(new{milliseconds=Environment.TickCount64,name,details});}
    internal static void Start()
    {
        _=Run();
        async Task Run()
        {
            try
            {
                if(Environment.GetEnvironmentVariable("LAB_PAUSED_PHASE")=="seed")
                {Fixture.Seed(Environment.GetEnvironmentVariable("LAB_PAUSED_WORK")!);Result("SEEDED","Synthetic normal project generated",null);return;}
                var notify=Environment.GetEnvironmentVariable("LAB_PAUSED_PHASE")=="notify";
                FrameworkElement? surface=null;object? preview=null;Window? window=null;Capture.Frame? baseline=null;
                var candidates=new List<object>();
                var toolOpened=false;
                for(var i=0;i<100;i++)
                {
                    if(Error!=null)throw new InvalidOperationException(Error);
                    foreach(Window dialog in Application.Current.Windows)
                    if(dialog.Title is "Confirm" or "確認" or "Terms" or "License" or "利用規約")
                    {Result("BLOCKED","Unrecognized consent/Confirm; no response sent",new{dialog.Title,text=Elements(dialog).OfType<TextBlock>().Select(t=>t.Text).Where(t=>!string.IsNullOrWhiteSpace(t)).Take(12).ToArray()});return;}
                    if(!toolOpened)toolOpened=OpenObserverTool();
                    (surface,preview,window)=FindSurface(candidates);
                    if(surface!=null&&Parameter!=null&&Info?.Timeline.Items.OfType<TachieItem>().Any(item=>ReferenceEquals(item.TachieItemParameter,Parameter))==true)
                    {
                        baseline=Capture.Read(surface);
                        if(baseline.RedFraction>.65)break;
                    }
                    await Task.Delay(250);
                }
                if(surface==null||Parameter==null||Info==null||baseline==null||baseline.RedFraction<=.65)
                {Result("BLOCKED","No live player with verified red synthetic preview baseline",new{candidates,sourceCount=NextSource,baselineRed=baseline?.RedFraction,baselineGreen=baseline?.GreenFraction});return;}
                var beforeState=State(preview!,window!);Capture.Save(Path.Combine(Output,"baseline.png"),baseline.Pixels,baseline.Width,baseline.Height);
                if(beforeState.IsPlaying!=false||beforeState.Frame==null)
                {Result("BLOCKED","Cannot independently establish paused state and frame through public UI surface",new{beforeState});return;}
                var owner=Parameter;var savedBefore=JsonConvert.SerializeObject(owner);var commandsBefore=owner.UndoCommands;
                var historyEvents=0;EventHandler historyObserver=(_,_)=>historyEvents++;Info.UndoRedoManager.HistoryChanged+=historyObserver;
                var undoableBefore=Info.UndoRedoManager.IsUndoable;var redoableBefore=Info.UndoRedoManager.IsRedoable;
                int updateBefore;lock(gate)updateBefore=events.Count;
                Log("baseline-established",new{notify,beforeState});
                owner.Arm(notify,Application.Current.Dispatcher);
                // From this point there is no seek, play, selection, edit, command or direct host Update.
                Capture.Frame final=baseline;var samples=new List<object>();
                for(var i=0;i<36;i++)
                {
                    await Task.Delay(250);var state=State(preview!,window!);final=Capture.Read(surface);
                    samples.Add(new{ready=owner.Ready,state,final.RedFraction,final.GreenFraction});
                    if(state.IsPlaying!=false||state.Frame!=beforeState.Frame)throw new InvalidOperationException("Frame/paused state changed during completion observation");
                    if(Error!=null)throw new InvalidOperationException(Error);
                }
                Capture.Save(Path.Combine(Output,"after.png"),final.Pixels,final.Width,final.Height);
                var afterState=State(preview!,window!);
                object[] traffic;lock(gate)traffic=events.ToArray();
                var persistedUnchanged=JsonConvert.SerializeObject(owner)==savedBefore;
                var undoUnchanged=commandsBefore==owner.UndoCommands&&historyEvents==0&&undoableBefore==Info.UndoRedoManager.IsUndoable&&redoableBefore==Info.UndoRedoManager.IsRedoable;
                Info.UndoRedoManager.HistoryChanged-=historyObserver;
                var signalCorrect=owner.Ready&&owner.Notices==(notify?1:0);
                var changed=final.GreenFraction>.65;
                var stayedRed=final.RedFraction>.65&&final.GreenFraction<.05;
                var validPixels=changed||stayedRed;
                var status=!validPixels?"BLOCKED":!signalCorrect||!persistedUnchanged||!undoUnchanged?"FAIL":notify?changed?"PASS_NOTIFY_REPAINT":"OBSERVED_NO_REPAINT":stayedRed?"PASS_CONTROL_NO_REPAINT":"OBSERVED_CONTROL_REPAINT";
                Result(status,"Real stopped player; completion-only observation",new{notify,owner.Ready,owner.Notices,undoUnchanged,historyEvents,undoableBefore,redoableBefore,persistedUnchanged,beforeState,afterState,changed,stayedRed,baseline=new{baseline.RedFraction,baseline.GreenFraction},final=new{final.RedFraction,final.GreenFraction},samples,eventsAfterBaseline=traffic.Skip(updateBefore).ToArray(),liveUndoHistoryMeasured=true,liveDirtyFlagMeasured=false,windowTitleUnchanged=beforeState.Title==afterState.Title});
            }
            catch(Exception e){Result("BLOCKED","Harness/host boundary: "+e.GetType().Name+": "+e.Message,null);}
        }
    }
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
        System.IO.File.WriteAllText(Path.Combine(Output,"result.json"),System.Text.Json.JsonSerializer.Serialize(new{schema="lab.paused-tachie-notice.v1",status,reason,phase=Environment.GetEnvironmentVariable("LAB_PAUSED_PHASE"),sourceHead=Environment.GetEnvironmentVariable("SOURCE_HEAD"),runId=Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),assemblySha256=Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(typeof(Startup).Assembly.Location))),observation,events=traffic,privateInvocation=false,directHostUpdate=false},new JsonSerializerOptions{WriteIndented=true}));
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
