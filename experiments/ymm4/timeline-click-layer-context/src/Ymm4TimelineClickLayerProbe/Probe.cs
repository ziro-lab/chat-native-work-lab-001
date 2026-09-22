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

namespace Ymm4TimelineClickLayerProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL Timeline Click Layer Probe";
    public void SetCulture(CultureInfo cultureInfo) => Bootstrap.Schedule();
}
public sealed class ProbeTool : IToolPlugin
{
    public string Name => "CNWL Timeline Click Layer Probe";
    public Type ViewModelType => typeof(ProbeVm);
    public Type ViewType => typeof(ProbeView);
    public bool AllowMultipleInstances => false;
}
public sealed class ProbeView : UserControl { public ProbeView()=>Content=new TextBlock{Text="CNWL layer-click probe"}; }
public sealed class ProbeVm : ITimelineToolViewModel, IToolViewModel
{
    public string Title=>"CNWL Timeline Click Layer Probe"; public bool CanSuspend=>false;
    public void SetTimelineToolInfo(TimelineToolInfo info)=>LayerProbe.Start(info.Timeline);
    public ToolState SaveState()=>new(){Title=Title}; public void LoadState(ToolState stateData){}
    public event PropertyChangedEventHandler? PropertyChanged { add{} remove{} }
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested { add{} remove{} }
}

internal static class Bootstrap
{
    static bool scheduled,created,started;
    public static void Schedule()
    {
        if(scheduled||string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CNWL_YMM4_LAYER_CLICK_DIR")))return;
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
                        if(active==null||started)continue;
                        var timeline=active.GetType().GetProperty("Timeline",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(active) as Timeline;
                        if(timeline==null)continue;
                        started=true;timer.Stop();LayerProbe.Start(timeline);return;
                    }
                    if(ticks>100){timer.Stop();LayerProbe.Fail("bootstrap timeout");}
                }catch(Exception ex){timer.Stop();LayerProbe.Fail(ex.ToString());}
            };
            timer.Start();
        }));
    }
}

internal static class Native
{
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] internal static extern void mouse_event(uint flags,uint dx,uint dy,uint data,nuint extra);
    internal const uint LD=0x0002,LU=0x0004;
}

internal readonly record struct Box(double Left,double Top,double Width,double Height)
{
    public double Right=>Left+Width; public double Bottom=>Top+Height; public double CenterY=>Top+Height/2;
    public bool Valid=>Width>5&&Height>5;
}
internal sealed record Sample(int expectedLayer,bool blankRouteVerified,string source,string[] route,Dictionary<string,int> semanticValues);
internal sealed record Result(string schema,string status,string host,bool usableClickLayerRoute,string[] usableCandidates,Sample[] samples,string[] publicLayerSurface,string? error);

internal static class LayerProbe
{
    static bool started;
    static Timeline? timeline;
    static string OutDir=>Path.GetFullPath(Environment.GetEnvironmentVariable("CNWL_YMM4_LAYER_CLICK_DIR")!);
    public static void Start(Timeline t){if(started)return;started=true;timeline=t;_=RunAsync();}
    public static void Fail(string error)
    {
        try{
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir,"error.txt"),error,new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(OutDir,"result.json"),JsonSerializer.Serialize(new Result("cnwl.timeline-click-layer.v1","FAIL_EXCEPTION","4.55.1.1 Lite",false,[],[],[],error),new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
        }catch{}
    }

    static async Task RunAsync()
    {
        try{
            Directory.CreateDirectory(OutDir);
            var t=timeline!;
            var main=Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w.DataContext?.GetType().FullName=="YukkuriMovieMaker.ViewModels.MainViewModel")
                ??throw new InvalidOperationException("Main window not found");
            main.WindowState=WindowState.Maximized;main.Activate();Native.SetForegroundWindow(new WindowInteropHelper(main).Handle);await Task.Delay(900);

            var character=new Character{Name="CNWL_Layer_Click"};
            var expected=new[]{1,3,5};
            var items=expected.Select(layer=>new VoiceItem(character){Frame=10,Length=60,Layer=layer,Serif="layer "+layer,Remark="CNWL_LAYER_CLICK_"+layer}).ToArray();
            foreach(var item in items)
                if(!t.Items.Any(x=>x.Remark==item.Remark)&&!t.TryAddItems([item],item.Frame,item.Layer))throw new InvalidOperationException("Could not insert layer fixture "+item.Layer);
            foreach(var item in items)
                if(t.Items.FirstOrDefault(x=>x.Remark==item.Remark) is VoiceItem actual && !ReferenceEquals(actual,item))
                    items[Array.IndexOf(items,item)]=actual;
            t.RefreshTimelineLengthAndMaxLayer();
            t.SelectedItems=ImmutableList.Create<IItem>(items[1]);
            var mainVm=main.DataContext!;
            var active=mainVm.GetType().GetProperty("ActiveTimelineViewModel",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(mainVm)
                ??throw new InvalidOperationException("Active TimelineViewModel missing");
            var scrollToItem=active.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public).FirstOrDefault(m=>m.Name=="ScrollToItem"&&m.GetParameters().Length==1);
            try{scrollToItem?.Invoke(active,[items[1]]);}catch{}
            await Task.Delay(1300);

            var timelineView=FindLargest(main,fe=>fe.GetType().FullName=="YukkuriMovieMaker.Views.TimelineView"||fe.DataContext?.GetType().FullName=="YukkuriMovieMaker.ViewModels.TimelineViewModel")
                ??throw new InvalidOperationException("TimelineView not found");
            var timelineBox=GetBox(timelineView);
            if(!timelineBox.Valid)throw new InvalidOperationException("Timeline geometry invalid");

            var samples=new List<Sample>();
            var surface=PublicLayerSurface(active.GetType()).ToList();
            for(var n=0;n<items.Length;n++){
                var item=items[n];
                try{scrollToItem?.Invoke(active,[item]);}catch{}
                await Task.Delay(500);
                var itemElement=FindItemElement(timelineView,item)??throw new InvalidOperationException("Rendered item missing after ScrollToItem for layer "+item.Layer);
                var ib=GetBox(itemElement);
                var point=FindBlankPoint(main,timelineView,timelineBox,ib.CenterY)??throw new InvalidOperationException("No blank Timeline hit on layer "+item.Layer);
                Native.SetCursorPos((int)Math.Round(point.X),(int)Math.Round(point.Y));await Task.Delay(120);
                var hit=Mouse.DirectlyOver as DependencyObject;
                var route=Route(hit).ToArray();var desc=route.Select(DescribeOne).ToArray();
                var blank=desc.Any(x=>x.Contains("TimelineViewModel",StringComparison.Ordinal))&&!desc.Any(x=>x.Contains("TimelineItemView",StringComparison.Ordinal));
                var values=CaptureSemanticLayerValues(route,active,timelineView,point,surface);
                Native.mouse_event(Native.LD,0,0,0,0);await Task.Delay(60);Native.mouse_event(Native.LU,0,0,0,0);await Task.Delay(350);
                samples.Add(new(item.Layer,blank,hit?.GetType().FullName??"<null>",desc,values));
            }

            var common=samples.SelectMany(s=>s.semanticValues.Keys).Distinct(StringComparer.Ordinal)
                .Where(k=>samples.All(s=>s.semanticValues.TryGetValue(k,out var v)&&v==s.expectedLayer)).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            File.WriteAllLines(Path.Combine(OutDir,"surface.txt"),surface.Distinct().OrderBy(x=>x),new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(OutDir,"routes.txt"),samples.SelectMany(s=>new[]{"=== expected layer "+s.expectedLayer+" ==="}.Concat(s.route).Concat(s.semanticValues.OrderBy(x=>x.Key).Select(x=>$"SEM {x.Key}={x.Value}"))),new UTF8Encoding(false));
            var result=new Result("cnwl.timeline-click-layer.v1","PASS_LAYER_CLICK_OBSERVATION","4.55.1.1 Lite",common.Length>0,common,samples.ToArray(),surface.Distinct().OrderBy(x=>x).ToArray(),null);
            File.WriteAllText(Path.Combine(OutDir,"result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
        }catch(Exception ex){Fail(ex.ToString());}
    }

    static Dictionary<string,int> CaptureSemanticLayerValues(IReadOnlyList<DependencyObject> route,object active,FrameworkElement timelineView,Point screen,List<string> surface)
    {
        var values=new Dictionary<string,int>(StringComparer.Ordinal);
        for(var i=0;i<route.Count;i++){
            CaptureObject("route["+i+"]:"+route[i].GetType().FullName,route[i],values,surface);
            if(route[i] is FrameworkElement fe&&fe.DataContext!=null)CaptureObject("dc["+i+"]:"+fe.DataContext.GetType().FullName,fe.DataContext,values,surface);
        }
        var local=timelineView.PointFromScreen(screen);
        foreach(var m in active.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public).Where(m=>m.Name.Contains("Layer",StringComparison.OrdinalIgnoreCase)&&m.ReturnType==typeof(int))){
            var ps=m.GetParameters();object?[]? args=null;string sig="";
            if(ps.Length==1&&ps[0].ParameterType==typeof(Point)){args=[local];sig="(Point)";}
            else if(ps.Length==1&&ps[0].ParameterType==typeof(double)){args=[local.Y];sig="(doubleY)";}
            else if(ps.Length==1&&ps[0].ParameterType==typeof(int)){args=[(int)Math.Round(local.Y)];sig="(intY)";}
            if(args==null)continue;
            var key="TimelineViewModel."+m.Name+sig;surface.Add("PUBLIC_METHOD "+key);
            try{if(m.Invoke(active,args) is int v)values[key]=v;}catch(Exception ex){surface.Add("METHOD_THROW "+key+" "+ex.GetBaseException().GetType().Name);}
        }
        return values;
    }
    static void CaptureObject(string prefix,object obj,Dictionary<string,int> values,List<string> surface)
    {
        foreach(var p in obj.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>p.CanRead&&p.GetIndexParameters().Length==0&&p.Name.Contains("Layer",StringComparison.OrdinalIgnoreCase))){
            var key=prefix+"."+p.Name;surface.Add("PUBLIC_PROPERTY "+key+":"+p.PropertyType.FullName);
            try{
                var v=p.GetValue(obj);
                if(v is int i)values[key]=i; else if(v is short s)values[key]=s; else if(v is long l&&l is >=int.MinValue and <=int.MaxValue)values[key]=(int)l;
            }catch(Exception ex){surface.Add("PROPERTY_THROW "+key+" "+ex.GetBaseException().GetType().Name);}
        }
    }
    static IEnumerable<string> PublicLayerSurface(Type t)
    {
        foreach(var p in t.GetProperties(BindingFlags.Instance|BindingFlags.Public).Where(p=>p.Name.Contains("Layer",StringComparison.OrdinalIgnoreCase)))
            yield return $"PUBLIC_PROPERTY {t.FullName}.{p.Name}:{p.PropertyType.FullName}";
        foreach(var m in t.GetMethods(BindingFlags.Instance|BindingFlags.Public).Where(m=>m.Name.Contains("Layer",StringComparison.OrdinalIgnoreCase)))
            yield return $"PUBLIC_METHOD {t.FullName}.{m.Name}({string.Join(",",m.GetParameters().Select(p=>p.ParameterType.FullName))}):{m.ReturnType.FullName}";
    }
    static Point? FindBlankPoint(Window main,FrameworkElement timelineView,Box tb,double y)
    {
        foreach(var xf in new[]{.92,.82,.72,.62,.52,.42}){
            var p=new Point(tb.Left+tb.Width*xf,y);
            var local=main.PointFromScreen(p);var hit=main.InputHitTest(local) as DependencyObject;
            var d=string.Join("<-",Route(hit).Select(DescribeOne));
            if(d.Contains("TimelineViewModel",StringComparison.Ordinal)&&!d.Contains("TimelineItemView",StringComparison.Ordinal))return p;
        }
        return null;
    }
    static IReadOnlyList<DependencyObject> Route(DependencyObject? source)
    {
        var list=new List<DependencyObject>();var cur=source;
        for(var i=0;i<32&&cur!=null;i++){
            list.Add(cur);
            if(cur.GetType().FullName=="YukkuriMovieMaker.Views.TimelineView")break;
            cur=Parent(cur);
        }
        return list;
    }
    static DependencyObject? Parent(DependencyObject d)=>d switch{
        Visual or System.Windows.Media.Media3D.Visual3D=>VisualTreeHelper.GetParent(d),
        FrameworkContentElement c=>c.Parent,
        _=>null
    };
    static string DescribeOne(DependencyObject d)=>d.GetType().FullName+"[dc="+((d as FrameworkElement)?.DataContext?.GetType().FullName??"<null>")+"]";
    static FrameworkElement? FindItemElement(DependencyObject root,IItem item)=>Elements(root).Where(x=>x.IsVisible&&ReferencesItem(x.DataContext,item)&&GetBox(x).Valid).OrderByDescending(x=>x.ActualWidth*x.ActualHeight).FirstOrDefault();
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
    static FrameworkElement? FindLargest(DependencyObject root,Func<FrameworkElement,bool> pred)=>Elements(root).Where(x=>x.IsVisible&&x.ActualWidth>5&&x.ActualHeight>5&&pred(x)).OrderByDescending(x=>x.ActualWidth*x.ActualHeight).FirstOrDefault();
    static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        if(root is FrameworkElement fe)yield return fe;int n=0;try{n=VisualTreeHelper.GetChildrenCount(root);}catch{}
        for(var i=0;i<n;i++)foreach(var x in Elements(VisualTreeHelper.GetChild(root,i)))yield return x;
    }
    static Box GetBox(FrameworkElement fe){try{var a=fe.PointToScreen(new Point(0,0));var b=fe.PointToScreen(new Point(fe.ActualWidth,fe.ActualHeight));return new(a.X,a.Y,b.X-a.X,b.Y-a.Y);}catch{return default;}}
}
