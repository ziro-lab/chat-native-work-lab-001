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

namespace Ymm4NoHarmonyNativeDragProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL YMM4 No-Harmony Native Drag Correction Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Native
{
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] internal static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);
    internal const uint LD = 0x0002;
    internal const uint LU = 0x0004;
}

internal readonly record struct ScreenBox(double Left,double Top,double Width,double Height)
{
    public Point Center => new(Left+Width/2,Top+Height/2);
    public bool Valid => Width>2 && Height>2;
}

internal static class Probe
{
    static bool scheduled, running;
    static string output="";
    static Timeline? timeline;
    static object? timelineVm;
    static FrameworkElement? timelineView, cursorSource;
    static int h;

    static IItem? activeItem;
    static bool observedBubbleMove;
    static int originalLayer, originalFrame;
    static readonly List<string> trace=[];
    static readonly List<int> nativeLayersBeforeCorrection=[];
    static readonly List<int> correctedLayers=[];

    public static void Schedule()
    {
        var dir=Environment.GetEnvironmentVariable("CNWL_YMM4_NO_HARMONY_NATIVE_DRAG_DIR");
        if(scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled=true; output=Path.GetFullPath(dir); Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap), DispatcherPriority.ApplicationIdle);
    }

    static void Bootstrap()
    {
        var ticks=0; var projectCreated=false;
        var timer=new DispatcherTimer(DispatcherPriority.ApplicationIdle){Interval=TimeSpan.FromMilliseconds(400)};
        timer.Tick+=(_,_)=>{
            try
            {
                ticks++;
                foreach(Window window in Application.Current.Windows)
                {
                    var main=window.DataContext;
                    if(main?.GetType().FullName!="YukkuriMovieMaker.ViewModels.MainViewModel") continue;
                    var active=main.GetType().GetProperty("ActiveTimelineViewModel",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(main);
                    if(active is null && !projectCreated)
                    {
                        projectCreated=true;
                        main.GetType().GetMethod("CreateProject",Type.EmptyTypes)?.Invoke(main,null);
                        return;
                    }
                    if(active is null || running) continue;
                    var t=active.GetType().GetProperty("Timeline",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(active) as Timeline
                        ?? active.GetType().GetField("timeline",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                    if(t is null) continue;
                    running=true; timer.Stop(); timeline=t; timelineVm=active;
                    _=RunAsync(window,t); return;
                }
                if(ticks>100){timer.Stop();WriteResult("FAIL_BOOTSTRAP_TIMEOUT",[]);}
            }catch(Exception ex){timer.Stop();Fail(ex);}
        };
        timer.Start();
    }

    static async Task RunAsync(Window mainWindow, Timeline t)
    {
        try
        {
            mainWindow.WindowState=System.Windows.WindowState.Maximized;
            mainWindow.Activate();
            Native.SetForegroundWindow(new WindowInteropHelper(mainWindow).Handle);
            await Task.Delay(900);

            var ch=new Character{Name="CNWL_NATIVE_DRAG"};
            var item=new VoiceItem(ch){Frame=80,Length=80,Layer=1,Serif="native drag",Remark="CNWL_NATIVE_DRAG_ITEM"};
            var anchor=new VoiceItem(ch){Frame=220,Length=50,Layer=3,Serif="anchor",Remark="CNWL_NATIVE_DRAG_ANCHOR"};
            if(!t.TryAddItems([item],item.Frame,item.Layer) || !t.TryAddItems([anchor],anchor.Frame,anchor.Layer))
                throw new InvalidOperationException("fixture insert failed");
            t.CurrentFrame=0; t.SelectedItems=ImmutableList<IItem>.Empty;
            await Task.Delay(1300);

            timelineView=FindLargest(mainWindow,x=>x.GetType().Name=="TimelineView") ?? throw new InvalidOperationException("TimelineView missing");
            var itemView=FindItemView(timelineView,item) ?? throw new InvalidOperationException("item view missing");
            var scroll=FindAncestor<ScrollViewer>(itemView) ?? throw new InvalidOperationException("scroll viewer missing");
            cursorSource=scroll.Content as FrameworkElement ?? throw new InvalidOperationException("cursor source missing");
            h=SettingsBase<YMMSettings>.Default.LayerHeight;
            if(h<=0) throw new InvalidOperationException("LayerHeight invalid");

            ApplyFold();
            await Task.Delay(350);
            itemView=FindItemView(timelineView,item) ?? itemView;
            var box=Box(itemView);
            if(!box.Valid) throw new InvalidOperationException("item geometry invalid");

            cursorSource.AddHandler(Mouse.PreviewMouseDownEvent,new MouseButtonEventHandler(OnPreviewDown),true);
            cursorSource.AddHandler(Mouse.MouseMoveEvent,new MouseEventHandler(OnBubbleMove),true);
            cursorSource.AddHandler(Mouse.MouseUpEvent,new MouseButtonEventHandler(OnBubbleUp),true);

            originalLayer=item.Layer; originalFrame=item.Frame;

            // Diagonal drag: standard YMM4 owns horizontal/frame behavior; probe only corrects Layer after native MouseMove.
            await Drag(box.Center,new Point(box.Center.X+64,box.Center.Y+h));
            await Task.Delay(900);

            var finalLayer=item.Layer;
            var finalFrame=item.Frame;
            var frameChanged=finalFrame!=originalFrame;
            var layerCorrect=finalLayer==3;

            var manager=FindByTypeName(timelineVm,"YukkuriMovieMaker.UndoRedo.UndoRedoManager");
            var managerFound=manager is not null;
            var undoLayer=-1; var undoFrame=-1; var redoLayer=-1; var redoFrame=-1;
            var undoOk=false; var redoOk=false;

            if(manager is not null)
            {
                await InvokeTask(manager,"UndoAsync");
                await Task.Delay(550);
                undoLayer=item.Layer; undoFrame=item.Frame;
                undoOk=undoLayer==originalLayer && undoFrame==originalFrame;

                await InvokeTask(manager,"RedoAsync");
                await Task.Delay(550);
                redoLayer=item.Layer; redoFrame=item.Frame;
                redoOk=redoLayer==finalLayer && redoFrame==finalFrame;
            }

            File.WriteAllLines(Path.Combine(output,"trace.txt"),trace,new UTF8Encoding(false));
            WriteResult("PASS_NO_HARMONY_NATIVE_DRAG_OBSERVATION",[
                "harmony_reference_present=False",
                $"bubble_move_observed={observedBubbleMove}",
                $"original_layer={originalLayer}",
                $"original_frame={originalFrame}",
                $"native_layers_before_correction={string.Join(",",nativeLayersBeforeCorrection)}",
                $"corrected_layers={string.Join(",",correctedLayers)}",
                $"final_layer={finalLayer}",
                $"final_frame={finalFrame}",
                $"layer_matches_fold={layerCorrect}",
                $"frame_changed_by_native_drag={frameChanged}",
                $"undo_manager_found={managerFound}",
                $"undo_layer={undoLayer}",
                $"undo_frame={undoFrame}",
                $"undo_restored_original={undoOk}",
                $"redo_layer={redoLayer}",
                $"redo_frame={redoFrame}",
                $"redo_restored_final={redoOk}"
            ]);
        }
        catch(Exception ex){Fail(ex);}
    }

    static void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Left || cursorSource is null) return;
        var view=FindAncestorByName(e.OriginalSource as DependencyObject,"TimelineItemView") as FrameworkElement;
        var item=view is null?null:ItemOf(view.DataContext);
        if(item is null) return;
        activeItem=item;
        trace.Add($"preview_down layer={item.Layer} frame={item.Frame} p={Fmt(Mouse.GetPosition(cursorSource))}");
        // Deliberately do NOT set Handled: native YMM4 drag remains owner.
    }

    static void OnBubbleMove(object sender, MouseEventArgs e)
    {
        if(activeItem is null || cursorSource is null || e.LeftButton!=MouseButtonState.Pressed) return;
        observedBubbleMove=true;

        // This ancestor bubbling handler runs after TimelineItemView.OnMouseMove.
        var nativeLayer=activeItem.Layer;
        nativeLayersBeforeCorrection.Add(nativeLayer);

        var p=Mouse.GetPosition(cursorSource);
        var desired=MapDisplayYToLogicalLayer(p.Y);
        if(desired>=0 && activeItem.Layer!=desired)
        {
            activeItem.Layer=desired;
            correctedLayers.Add(desired);
            ApplyFold();
        }
        trace.Add($"bubble_move native_layer={nativeLayer} desired={desired} final_layer={activeItem.Layer} frame={activeItem.Frame} p={Fmt(p)}");
    }

    static void OnBubbleUp(object sender, MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=MouseButton.Left || activeItem is null) return;
        trace.Add($"bubble_up layer={activeItem.Layer} frame={activeItem.Frame}");
        activeItem=null;
        ApplyFold();
    }

    static int MapDisplayYToLogicalLayer(double y)
    {
        var display=(int)Math.Floor(y/h);
        return display>=2?display+1:display;
    }

    static void ApplyFold()
    {
        if(timelineView is null) return;
        foreach(var fe in Elements(timelineView).Where(x=>x.GetType().Name=="TimelineItemView"))
        {
            var item=ItemOf(fe.DataContext);
            if(item is null) continue;
            fe.RenderTransform=item.Layer>=3?new TranslateTransform(0,-h):Transform.Identity;
        }
    }

    static object? FindByTypeName(object? root,string fullName)
    {
        if(root is null) return null;
        for(Type? t=root.GetType();t is not null;t=t.BaseType)
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
            foreach(var p in t.GetProperties(flags).Where(p=>p.GetIndexParameters().Length==0 && p.CanRead))
            {
                try{var v=p.GetValue(root);if(v?.GetType().FullName==fullName)return v;}catch{}
            }
            foreach(var f in t.GetFields(flags))
            {
                try{var v=f.GetValue(root);if(v?.GetType().FullName==fullName)return v;}catch{}
            }
        }
        return null;
    }

    static async Task InvokeTask(object instance,string name)
    {
        var m=instance.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,Type.EmptyTypes)
            ?? throw new MissingMethodException(instance.GetType().FullName,name);
        if(m.Invoke(instance,null) is Task task) await task;
    }

    static async Task Drag(Point a,Point b)
    {
        Native.SetCursorPos((int)Math.Round(a.X),(int)Math.Round(a.Y)); await Task.Delay(100);
        Native.mouse_event(Native.LD,0,0,0,0); await Task.Delay(120);
        for(var i=1;i<=10;i++)
        {
            Native.SetCursorPos((int)Math.Round(a.X+(b.X-a.X)*i/10.0),(int)Math.Round(a.Y+(b.Y-a.Y)*i/10.0));
            await Task.Delay(70);
        }
        Native.mouse_event(Native.LU,0,0,0,0);
    }

    static FrameworkElement? FindItemView(DependencyObject root,IItem item)=>
        Elements(root).Where(x=>x.IsVisible&&x.GetType().Name=="TimelineItemView"&&ReferenceEquals(ItemOf(x.DataContext),item))
        .OrderByDescending(x=>x.ActualWidth*x.ActualHeight).FirstOrDefault();

    static FrameworkElement? FindLargest(DependencyObject root,Func<FrameworkElement,bool> pred)=>
        Elements(root).Where(x=>x.IsVisible&&x.ActualWidth>5&&x.ActualHeight>5&&pred(x))
        .OrderByDescending(x=>x.ActualWidth*x.ActualHeight).FirstOrDefault();

    static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        if(root is FrameworkElement fe) yield return fe;
        int count;try{count=VisualTreeHelper.GetChildrenCount(root);}catch{yield break;}
        for(var i=0;i<count;i++)foreach(var x in Elements(VisualTreeHelper.GetChild(root,i)))yield return x;
    }

    static T? FindAncestor<T>(DependencyObject? origin) where T:DependencyObject
    {
        var c=origin;while(c is not null){if(c is T t)return t;try{c=VisualTreeHelper.GetParent(c);}catch{break;}}return null;
    }

    static DependencyObject? FindAncestorByName(DependencyObject? origin,string name)
    {
        var c=origin;while(c is not null){if(c.GetType().Name==name)return c;try{c=VisualTreeHelper.GetParent(c);}catch{break;}}return null;
    }

    static IItem? ItemOf(object? dc)
    {
        if(dc is IItem d)return d;if(dc is null)return null;
        foreach(var p in dc.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
            .Where(p=>p.GetIndexParameters().Length==0&&p.CanRead&&(typeof(IItem).IsAssignableFrom(p.PropertyType)||p.Name.Contains("Item",StringComparison.OrdinalIgnoreCase)||p.Name is "Model" or "Source" or "Value")).Take(40))
        {
            try{var v=p.GetValue(dc);if(v is IItem i)return i;if(v is IEnumerable en&&v is not string)foreach(var x in en)if(x is IItem ni)return ni;}catch{}
        }
        return null;
    }

    static ScreenBox Box(FrameworkElement fe){try{var p=fe.PointToScreen(new Point());return new(p.X,p.Y,fe.ActualWidth,fe.ActualHeight);}catch{return default;}}
    static string Fmt(Point p)=>$"{p.X:F2},{p.Y:F2}";
    static void WriteResult(string status,IEnumerable<string> details)=>File.WriteAllLines(Path.Combine(output,"result.txt"),new[]{"status="+status}.Concat(details),new UTF8Encoding(false));
    static void Fail(Exception ex){try{File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString(),new UTF8Encoding(false));WriteResult("FAIL_EXCEPTION",["message="+ex.GetBaseException().Message]);}catch{}}
}
