using System.Collections;
using System.IO;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
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

namespace Ymm4TimelineInputProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL Timeline Input Probe";
    public void SetCulture(CultureInfo cultureInfo) => Bootstrap.Schedule();
}
public sealed class ProbeTool : IToolPlugin
{
    public string Name => "CNWL Timeline Input Probe";
    public Type ViewModelType => typeof(ProbeVm);
    public Type ViewType => typeof(ProbeView);
    public bool AllowMultipleInstances => false;
}
public sealed class ProbeView : UserControl { public ProbeView()=>Content=new TextBlock{Text="CNWL timeline input probe"}; }
public sealed class ProbeVm : ITimelineToolViewModel, IToolViewModel
{
    public string Title=>"CNWL Timeline Input Probe"; public bool CanSuspend=>false;
    public void SetTimelineToolInfo(TimelineToolInfo info)=>InputProbe.Start(info.Timeline);
    public ToolState SaveState()=>new(){Title=Title}; public void LoadState(ToolState stateData){}
    public event PropertyChangedEventHandler? PropertyChanged { add{} remove{} }
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested { add{} remove{} }
}

internal static class Bootstrap
{
    static bool scheduled, created, opened;
    public static void Schedule()
    {
        if(scheduled || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CNWL_YMM4_INPUT_DIR"))) return;
        scheduled=true;
        Application.Current.Dispatcher.BeginInvoke(new Action(()=>{
            var timer=new DispatcherTimer(DispatcherPriority.ApplicationIdle){Interval=TimeSpan.FromMilliseconds(400)};
            var ticks=0;
            timer.Tick+=(_,_)=>{
                ticks++;
                foreach(Window w in Application.Current.Windows)
                {
                    var main=w.DataContext;
                    if(main?.GetType().FullName!="YukkuriMovieMaker.ViewModels.MainViewModel") continue;
                    var active=main.GetType().GetProperty("ActiveTimelineViewModel")?.GetValue(main);
                    if(active==null&&!created){created=true;main.GetType().GetMethod("CreateProject",Type.EmptyTypes)?.Invoke(main,null);return;}
                    if(active!=null&&!opened) opened=OpenTool(main);
                    if(opened){timer.Stop();return;}
                }
                if(ticks>80) timer.Stop();
            };
            timer.Start();
        }));
    }
    static bool OpenTool(object main)
    {
        if(main.GetType().GetProperty("ToolMenuItems")?.GetValue(main) is not IEnumerable items)return false;
        bool Visit(object x,int d)
        {
            if(d>8)return false;var t=x.GetType();
            var label=t.GetProperty("Header")?.GetValue(x)?.ToString()??t.GetProperty("Title")?.GetValue(x)?.ToString()??t.GetProperty("Name")?.GetValue(x)?.ToString()??"";
            if(label.Contains("CNWL Timeline Input Probe",StringComparison.Ordinal))
            {
                if(t.GetProperty("Command")?.GetValue(x) is ICommand c){var p=t.GetProperty("CommandParameter")?.GetValue(x);if(c.CanExecute(p)){c.Execute(p);return true;}}
                foreach(var n in new[]{"IsVisible","IsSelected","IsActive"})try{t.GetProperty(n)?.SetValue(x,true);}catch{}
                return true;
            }
            var ch=(t.GetProperty("Children")?.GetValue(x)??t.GetProperty("Items")?.GetValue(x)) as IEnumerable;
            if(ch!=null)foreach(var y in ch)if(y!=null&&Visit(y,d+1))return true;
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
    [DllImport("user32.dll")] internal static extern void keybd_event(byte vk,byte scan,uint flags,nuint extra);
    internal const uint LD=0x0002, LU=0x0004, KEYUP=0x0002;
}

internal readonly record struct ScreenBox(double Left,double Top,double Width,double Height)
{
    public double Right=>Left+Width; public double Bottom=>Top+Height;
    public System.Windows.Point Center=>new(Left+Width/2,Top+Height/2);
    public bool Valid=>Width>2&&Height>2;
}

internal static class InputProbe
{
    static bool started;
    static readonly object Gate=new();
    static readonly List<string> Events=[];
    static int seq;
    static string action="setup";
    static Timeline? timeline;
    static VoiceItem? voice;
    static string OutDir=>Path.GetFullPath(Environment.GetEnvironmentVariable("CNWL_YMM4_INPUT_DIR")!);

    public static void Start(Timeline value)
    {
        if(started)return;started=true;timeline=value;
        _=RunAsync();
    }

    static async Task RunAsync()
    {
        try
        {
            Directory.CreateDirectory(OutDir);
            var t=timeline!;
            var main=Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w.DataContext?.GetType().FullName=="YukkuriMovieMaker.ViewModels.MainViewModel")
                ?? throw new InvalidOperationException("Main window not found");
            main.WindowState=WindowState.Maximized;
            main.Activate();
            Native.SetForegroundWindow(new WindowInteropHelper(main).Handle);
            HideOwnTool(main.DataContext);
            await Task.Delay(900);

            var character=new Character{Name="CNWL_InputA"};
            voice=new VoiceItem(character){Frame=10,Length=60,Layer=1,Serif="input probe",Remark="CNWL_INPUT_FIXTURE"};
            if(!t.Items.Any(x=>x.Remark=="CNWL_INPUT_FIXTURE"))
                if(!t.TryAddItems([voice],voice.Frame,voice.Layer))throw new InvalidOperationException("Could not insert VoiceItem fixture");
            else voice=(VoiceItem)t.Items.First(x=>x.Remark=="CNWL_INPUT_FIXTURE");
            t.CurrentFrame=0;
            var active=main.DataContext?.GetType().GetProperty("ActiveTimelineViewModel",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(main.DataContext);
            var scrollToItem=active?.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public)
                .FirstOrDefault(m=>m.Name=="ScrollToItem" && m.GetParameters().Length==1 && m.GetParameters()[0].ParameterType.IsAssignableFrom(voice.GetType()));
            scrollToItem?.Invoke(active,[voice]);
            t.SelectedItems=ImmutableList.Create<IItem>(voice);
            await Task.Delay(1400);

            var timelineElement=FindLargest(main,fe=>fe.DataContext?.GetType().FullName=="YukkuriMovieMaker.ViewModels.TimelineViewModel"||fe.GetType().Name.Equals("TimelineView",StringComparison.OrdinalIgnoreCase))
                ??throw new InvalidOperationException("Timeline visual not found");
            var rulerElement=FindLargest(main,fe=>(fe.DataContext?.GetType().FullName?.Contains("TimelineScaleViewModel",StringComparison.Ordinal)??false)||fe.GetType().Name.Contains("TimelineScaleView",StringComparison.OrdinalIgnoreCase))
                ??throw new InvalidOperationException("Timeline ruler visual not found");
            DumpVisualTree(timelineElement,voice);

            var itemElement=FindItemElement(timelineElement,voice)??throw new InvalidOperationException("Rendered Timeline item element not found; see item-candidates.txt");
            t.SelectedItems=ImmutableList<IItem>.Empty;
            await Task.Delay(300);
            Attach(main,t);

            var ib=Box(itemElement);var tb=Box(timelineElement);var rb=Box(rulerElement);
            if(!ib.Valid||!tb.Valid||!rb.Valid)throw new InvalidOperationException("Invalid target geometry");
            var itemPoint=FindHitPoint(main,ib,"TimelineItemView") ?? ib.Center;
            var blankX=rb.Right-30;
            if(blankX>=ib.Left-5 && blankX<=ib.Right+5) blankX=rb.Left+30;
            if(blankX>=ib.Left-5 && blankX<=ib.Right+5) blankX=Math.Clamp(ib.Right+40,rb.Left+20,rb.Right-20);
            var blankBox=new ScreenBox(blankX-12,Math.Clamp(itemPoint.Y-12,rb.Bottom+5,tb.Bottom-30),24,24);
            var blankPoint=FindHitPoint(main,blankBox,"TimelineViewModel",exclude:"TimelineItemView") ?? blankBox.Center;
            var rulerPoint=FindHitPoint(main,rb,"TimelineScale") ?? rb.Center;

            File.WriteAllLines(Path.Combine(OutDir,"targets.txt"),
            [
                $"item={ib.Left:F1},{ib.Top:F1},{ib.Width:F1},{ib.Height:F1}",
                $"timeline={tb.Left:F1},{tb.Top:F1},{tb.Width:F1},{tb.Height:F1}",
                $"ruler={rb.Left:F1},{rb.Top:F1},{rb.Width:F1},{rb.Height:F1}",
                $"item_point={itemPoint.X:F1},{itemPoint.Y:F1}",
                $"blank_point={blankPoint.X:F1},{blankPoint.Y:F1}",
                $"ruler_point={rulerPoint.X:F1},{rulerPoint.Y:F1}"
            ],new UTF8Encoding(false));

            action="focus-warmup";
            Native.SetForegroundWindow(new WindowInteropHelper(main).Handle);
            await ClickCore(blankPoint);
            await Task.Delay(350);
            t.SelectedItems=ImmutableList<IItem>.Empty;
            t.CurrentFrame=0;
            await Task.Delay(200);

            await Click("item-click",itemPoint); Snapshot("after item-click");
            if(t.SelectedItems.Count==0)
            {
                await Click("item-click-retry",itemPoint); Snapshot("after item-click-retry");
            }
            await Click("item-reclick",itemPoint); Snapshot("after item-reclick");
            await Click("blank-click",blankPoint); Snapshot("after blank-click");
            await Click("ruler-click",rulerPoint); Snapshot("after ruler-click");

            action="ctrl-item-click"; Key(0x11,true); await Task.Delay(80); await ClickCore(itemPoint); Key(0x11,false); await Task.Delay(500); Snapshot("after ctrl-item-click");

            var dragTo=new System.Windows.Point(Math.Min(rb.Right-25,rulerPoint.X+120),rulerPoint.Y);
            await Drag("ruler-drag",rulerPoint,dragTo); Snapshot("after ruler-drag");

            action="programmatic-currentframe"; var beforeProgram=t.CurrentFrame; t.CurrentFrame=beforeProgram+7; await Task.Delay(400); Snapshot("after programmatic frame");

            action="keyboard-right"; var beforeRight=t.CurrentFrame; Key(0x27,true);Key(0x27,false);await Task.Delay(500);var afterRight=t.CurrentFrame;Snapshot("after keyboard-right");

            action="space-playback"; var beforePlay=t.CurrentFrame; Key(0x20,true);Key(0x20,false);await Task.Delay(1300);var midPlay=t.CurrentFrame;Key(0x20,true);Key(0x20,false);await Task.Delay(300);var afterPlay=t.CurrentFrame;Snapshot("after space-playback");

            string[] ev;lock(Gate)ev=Events.ToArray();
            File.WriteAllLines(Path.Combine(OutDir,"events.txt"),ev,new UTF8Encoding(false));

            bool Has(string a,string token)=>ev.Any(x=>x.Contains("action="+a+" ",StringComparison.Ordinal)&&x.Contains(token,StringComparison.Ordinal));
            var itemRoute=Has("item-click","ROUTED PreviewMouseDown")||Has("item-click-retry","ROUTED PreviewMouseDown");
            var itemSource=Has("item-click","TimelineItemView")||Has("item-click-retry","TimelineItemView");
            var result=new[]
            {
                "status=PASS_TIMELINE_INPUT_ROUTE",
                "item_pointer_route_observed="+itemRoute,
                "item_source_is_timeline_item="+itemSource,
                "item_reclick_pointer_route_observed="+Has("item-reclick","ROUTED PreviewMouseDown"),
                "item_reclick_source_is_timeline_item="+Has("item-reclick","TimelineItemView"),
                "item_reclick_selection_changed="+Has("item-reclick","TIMELINE PropertyChanged SelectedItems"),
                "blank_pointer_route_observed="+Has("blank-click","ROUTED PreviewMouseDown"),
                "blank_currentframe_changed="+Has("blank-click","TIMELINE PropertyChanged CurrentFrame"),
                "blank_source_is_timeline_item="+Has("blank-click","TimelineItemView"),
                "ruler_pointer_route_observed="+Has("ruler-click","ROUTED PreviewMouseDown"),
                "ruler_source_is_timeline_scale="+Has("ruler-click","TimelineScale"),
                "ruler_currentframe_changed="+Has("ruler-click","TIMELINE PropertyChanged CurrentFrame"),
                "ruler_drag_currentframe_changed="+Has("ruler-drag","TIMELINE PropertyChanged CurrentFrame"),
                "keyboard_right_frame_changed="+(afterRight!=beforeRight),
                "space_playback_frame_moved="+(midPlay!=beforePlay),
                "space_playback_frame_before="+beforePlay,
                "space_playback_frame_mid="+midPlay,
                "space_playback_frame_after="+afterPlay
            };
            File.WriteAllLines(Path.Combine(OutDir,"result.txt"),result,new UTF8Encoding(false));
        }
        catch(Exception ex)
        {
            try{File.WriteAllText(Path.Combine(OutDir,"error.txt"),ex.ToString(),new UTF8Encoding(false));File.WriteAllText(Path.Combine(OutDir,"result.txt"),"status=FAIL_EXCEPTION\n",new UTF8Encoding(false));}catch{}
        }
    }

    static void Attach(Window main,Timeline t)
    {
        InputManager.Current.PreProcessInput+=(_,e)=>{
            var input=e.StagingItem.Input;
            if(input is MouseButtonEventArgs me)Log("INPUT PreProcess "+me.RoutedEvent.Name+" button="+me.ChangedButton+" directlyOver="+Describe(Mouse.DirectlyOver));
            else if(input is KeyEventArgs ke&&ke.RoutedEvent==Keyboard.PreviewKeyDownEvent)Log("INPUT PreProcess KeyDown key="+ke.Key+" directlyOver="+Describe(Keyboard.FocusedElement));
        };
        main.AddHandler(Mouse.PreviewMouseDownEvent,new MouseButtonEventHandler((_,e)=>Log("ROUTED PreviewMouseDown source="+Describe(e.OriginalSource))),true);
        main.AddHandler(Mouse.MouseDownEvent,new MouseButtonEventHandler((_,e)=>Log("ROUTED MouseDown source="+Describe(e.OriginalSource))),true);
        if(t is INotifyPropertyChanging pc)pc.PropertyChanging+=(_,e)=>Log("TIMELINE PropertyChanging "+e.PropertyName);
        if(t is INotifyPropertyChanged pn)pn.PropertyChanged+=(_,e)=>Log("TIMELINE PropertyChanged "+e.PropertyName);
    }

    static async Task Click(string name,System.Windows.Point p){action=name;Log($"ACTION click {p.X:F1},{p.Y:F1}");await ClickCore(p);await Task.Delay(650);}
    static async Task ClickCore(System.Windows.Point p)
    {
        Native.SetCursorPos((int)Math.Round(p.X),(int)Math.Round(p.Y));await Task.Delay(90);
        Native.mouse_event(Native.LD,0,0,0,0);await Task.Delay(60);Native.mouse_event(Native.LU,0,0,0,0);
    }
    static async Task Drag(string name,System.Windows.Point a,System.Windows.Point b)
    {
        action=name;Log($"ACTION drag {a.X:F1},{a.Y:F1}->{b.X:F1},{b.Y:F1}");
        Native.SetCursorPos((int)a.X,(int)a.Y);await Task.Delay(80);Native.mouse_event(Native.LD,0,0,0,0);await Task.Delay(100);
        for(int i=1;i<=6;i++){var x=a.X+(b.X-a.X)*i/6;var y=a.Y+(b.Y-a.Y)*i/6;Native.SetCursorPos((int)x,(int)y);await Task.Delay(60);}
        Native.mouse_event(Native.LU,0,0,0,0);await Task.Delay(650);
    }
    static void Key(byte vk,bool down)=>Native.keybd_event(vk,0,down?0:Native.KEYUP,0);
    static void Snapshot(string label)=>Log($"SNAPSHOT {label} selected={timeline!.SelectedItems.Count} selectedType={timeline.SelectedItem?.GetType().Name??"<null>"} frame={timeline.CurrentFrame}");

    static void Log(string text)
    {
        lock(Gate)Events.Add($"{++seq:D5} action={action} frame={timeline?.CurrentFrame??-1} selected={timeline?.SelectedItems.Count??-1} {text}");
    }

    static string Describe(object? o)
    {
        if(o is not DependencyObject d)return o?.GetType().FullName??"<null>";
        var parts=new List<string>();
        for(int i=0;i<7&&d!=null;i++)
        {
            var s=d.GetType().Name;
            if(d is FrameworkElement fe&&fe.DataContext!=null)s+="[dc="+fe.DataContext.GetType().Name+"]";
            parts.Add(s);
            try{d=VisualTreeHelper.GetParent(d);}catch{break;}
        }
        return string.Join("<-",parts);
    }

    static void HideOwnTool(object? main)
    {
        if(main==null)return;
        if(main.GetType().GetProperty("ToolMenuItems")?.GetValue(main) is not IEnumerable items)return;
        void Visit(object x,int depth)
        {
            if(depth>8)return;
            var t=x.GetType();
            var label=t.GetProperty("Header")?.GetValue(x)?.ToString()??t.GetProperty("Title")?.GetValue(x)?.ToString()??t.GetProperty("Name")?.GetValue(x)?.ToString()??"";
            if(label.Contains("CNWL Timeline Input Probe",StringComparison.Ordinal))
            {
                foreach(var n in new[]{"IsActive","IsSelected","IsVisible"}) try{t.GetProperty(n)?.SetValue(x,false);}catch{}
            }
            var children=(t.GetProperty("Children")?.GetValue(x)??t.GetProperty("Items")?.GetValue(x)) as IEnumerable;
            if(children!=null)foreach(var child in children)if(child!=null)Visit(child,depth+1);
        }
        foreach(var x in items)if(x!=null)Visit(x,0);
    }

    static System.Windows.Point? FindHitPoint(Window main,ScreenBox box,string include,string? exclude=null)
    {
        var xs=new[]{0.5,0.25,0.75,0.1,0.9};
        var ys=new[]{0.5,0.3,0.7};
        foreach(var yf in ys)foreach(var xf in xs)
        {
            var p=new System.Windows.Point(box.Left+box.Width*xf,box.Top+box.Height*yf);
            try
            {
                var local=main.PointFromScreen(p);
                var hit=main.InputHitTest(local);
                var desc=Describe(hit);
                if(desc.Contains(include,StringComparison.OrdinalIgnoreCase)
                    && (exclude==null||!desc.Contains(exclude,StringComparison.OrdinalIgnoreCase))) return p;
            }
            catch{}
        }
        return null;
    }

    static FrameworkElement? FindItemElement(DependencyObject root,IItem item)
    {
        var matches=new List<FrameworkElement>();
        var candidates=new List<string>();
        foreach(var fe in Elements(root).Where(x=>x.IsVisible))
        {
            var b=Box(fe);
            var dc=fe.DataContext;
            var relation=ReferencesItem(dc,item,out var via);
            if(relation) matches.Add(fe);
            if(dc!=null && (relation || dc.GetType().Name.Contains("Item",StringComparison.OrdinalIgnoreCase)))
                candidates.Add($"{(relation?"MATCH":"CANDIDATE")} via={via??"-"} fe={fe.GetType().FullName} dc={dc.GetType().FullName} box={b.Left:F1},{b.Top:F1},{b.Width:F1},{b.Height:F1}");
        }
        File.WriteAllLines(Path.Combine(OutDir,"item-candidates.txt"),candidates,new UTF8Encoding(false));
        return matches.Where(fe=>Box(fe).Valid).OrderByDescending(fe=>fe.ActualWidth*fe.ActualHeight).FirstOrDefault();
    }

    static bool ReferencesItem(object? dc,IItem item,out string? via)
    {
        via=null;
        if(dc==null)return false;
        if(ReferenceEquals(dc,item)){via="DataContext";return true;}
        var type=dc.GetType();
        var props=type.GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
            .Where(p=>p.GetIndexParameters().Length==0 && p.CanRead
                && (typeof(IItem).IsAssignableFrom(p.PropertyType)
                    || p.Name.Contains("Item",StringComparison.OrdinalIgnoreCase)
                    || p.Name is "Model" or "Source" or "Value"))
            .Take(40);
        foreach(var p in props)
        {
            try
            {
                var value=p.GetValue(dc);
                if(ReferenceEquals(value,item)){via=p.Name;return true;}
                if(value is IEnumerable e && value is not string)
                    foreach(var x in e){if(ReferenceEquals(x,item)){via=p.Name+"[]";return true;}}
            }
            catch{}
        }
        return false;
    }
    static FrameworkElement? FindLargest(DependencyObject root,Func<FrameworkElement,bool> pred)=>Elements(root).Where(fe=>fe.IsVisible&&fe.ActualWidth>5&&fe.ActualHeight>5&&pred(fe)).OrderByDescending(fe=>fe.ActualWidth*fe.ActualHeight).FirstOrDefault();
    static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        if(root is FrameworkElement fe)yield return fe;
        int n=0;try{n=VisualTreeHelper.GetChildrenCount(root);}catch{}
        for(int i=0;i<n;i++)foreach(var x in Elements(VisualTreeHelper.GetChild(root,i)))yield return x;
    }
    static ScreenBox Box(FrameworkElement fe)
    {
        try{var p=fe.PointToScreen(new System.Windows.Point(0,0));return new(p.X,p.Y,fe.ActualWidth,fe.ActualHeight);}catch{return default;}
    }
    static void DumpVisualTree(FrameworkElement timelineRoot,IItem item)
    {
        var lines=new List<string>();
        foreach(var fe in Elements(timelineRoot).Where(fe=>fe.IsVisible).Take(1600))
        {
            var b=Box(fe);
            var dc=fe.DataContext;
            var match=ReferencesItem(dc,item,out var via);
            lines.Add($"{(match?"ITEM_MATCH ":"")}{fe.GetType().FullName} dc={dc?.GetType().FullName??"<null>"} via={via??"-"} box={b.Left:F1},{b.Top:F1},{b.Width:F1},{b.Height:F1}");
        }
        File.WriteAllLines(Path.Combine(OutDir,"visual-tree.txt"),lines,new UTF8Encoding(false));
    }
}
