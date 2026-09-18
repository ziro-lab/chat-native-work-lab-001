using System.Collections;
using System.IO;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;

namespace Ymm4PreviewRefreshProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL Preview Refresh Probe";
    public void SetCulture(CultureInfo cultureInfo) => Bootstrap.Schedule();
}
public sealed class ProbeTool : IToolPlugin
{
    public string Name => "CNWL Preview Refresh Probe";
    public Type ViewModelType => typeof(ProbeVm);
    public Type ViewType => typeof(ProbeView);
    public bool AllowMultipleInstances => false;
}
public sealed class ProbeView : UserControl { public ProbeView()=>Content=new TextBlock{Text="CNWL preview refresh probe"}; }
public sealed class ProbeVm : ITimelineToolViewModel, IToolViewModel
{
    public string Title=>"CNWL Preview Refresh Probe"; public bool CanSuspend=>false;
    public void SetTimelineToolInfo(TimelineToolInfo info)=>PreviewProbe.Start(info);
    public ToolState SaveState()=>new(){Title=Title}; public void LoadState(ToolState stateData){}
    public event PropertyChangedEventHandler? PropertyChanged { add{} remove{} }
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested { add{} remove{} }
}

internal static class Bootstrap
{
    static bool scheduled, created, opened;
    public static void Schedule()
    {
        if(scheduled || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CNWL_YMM4_PREVIEW_DIR"))) return;
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
                    if(active==null && !created){created=true; main.GetType().GetMethod("CreateProject",Type.EmptyTypes)?.Invoke(main,null); return;}
                    if(active!=null && !opened){opened=OpenTool(main);}
                    if(opened){timer.Stop();return;}
                }
                if(ticks>80) timer.Stop();
            };
            timer.Start();
        }));
    }
    static bool OpenTool(object main)
    {
        if(main.GetType().GetProperty("ToolMenuItems")?.GetValue(main) is not IEnumerable items) return false;
        bool Visit(object x,int d)
        {
            if(d>8)return false; var t=x.GetType();
            var label=t.GetProperty("Header")?.GetValue(x)?.ToString()??t.GetProperty("Title")?.GetValue(x)?.ToString()??t.GetProperty("Name")?.GetValue(x)?.ToString()??"";
            if(label.Contains("CNWL Preview Refresh Probe",StringComparison.Ordinal))
            {
                if(t.GetProperty("Command")?.GetValue(x) is ICommand c){var p=t.GetProperty("CommandParameter")?.GetValue(x);if(c.CanExecute(p)){c.Execute(p);return true;}}
                foreach(var n in new[]{"IsVisible","IsSelected","IsActive"}) try{t.GetProperty(n)?.SetValue(x,true);}catch{}
                return true;
            }
            var ch=(t.GetProperty("Children")?.GetValue(x)??t.GetProperty("Items")?.GetValue(x)) as IEnumerable;
            if(ch!=null) foreach(var y in ch) if(y!=null&&Visit(y,d+1)) return true;
            return false;
        }
        foreach(var x in items) if(x!=null&&Visit(x,0)) return true; return false;
    }
}

internal static class PreviewProbe
{
    static bool started;
    static readonly string[] Keys=["preview","refresh","render","redraw","invalidate","update","seek","frame"];
    static string OutDir=>Path.GetFullPath(Environment.GetEnvironmentVariable("CNWL_YMM4_PREVIEW_DIR")!);

    public static void Start(TimelineToolInfo info)
    {
        if(started)return; started=true;
        _=RunAsync(info);
    }

    static async Task RunAsync(TimelineToolInfo info)
    {
        try
        {
            Directory.CreateDirectory(OutDir);
            var timeline=info.Timeline;
            var surface=new List<string>();
            Dump("TimelineToolInfo",info.GetType(),surface);
            Dump("Timeline",timeline.GetType(),surface);
            var pluginMembers=RelevantPublic(info.GetType()).Concat(RelevantPublic(timeline.GetType())).ToArray();
            var pluginPreviewRefreshCandidates=pluginMembers.Where(IsDedicatedPreviewRefreshLike).ToArray();
            surface.Add("PLUGIN_DEDICATED_PREVIEW_REFRESH_CANDIDATE_COUNT="+pluginPreviewRefreshCandidates.Length);
            foreach(var m in pluginPreviewRefreshCandidates) surface.Add("PLUGIN_PREVIEW_REFRESH_CANDIDATE "+FormatMember(m));

            object? main=null, preview=null, active=null;
            foreach(Window w in Application.Current.Windows)
                if(w.DataContext?.GetType().FullName=="YukkuriMovieMaker.ViewModels.MainViewModel"){main=w.DataContext;break;}
            if(main!=null)
            {
                foreach(var p in main.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public))
                {
                    if(!p.Name.Contains("Preview",StringComparison.OrdinalIgnoreCase)&&!(p.PropertyType.FullName?.Contains("Preview",StringComparison.OrdinalIgnoreCase)??false)) continue;
                    try{var v=p.GetValue(main);surface.Add($"MAIN_PUBLIC_PREVIEW_PROPERTY {p.Name}:{p.PropertyType.FullName} value={v?.GetType().FullName??"<null>"}"); if(v!=null)preview??=v;}catch{}
                }
                active=main.GetType().GetProperty("ActiveTimelineViewModel",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(main);
                preview ??= FindPreviewDataContext();
                if(preview!=null)Dump("Live PreviewViewModel/DataContext",preview.GetType(),surface);
                if(active!=null)Dump("Live TimelineViewModel",active.GetType(),surface);
            }
            File.WriteAllLines(Path.Combine(OutDir,"surface.txt"),surface,new UTF8Encoding(false));
            var assemblyPublicCandidates=DumpAssemblyPublicSurface();

            var events=new List<string>(); int seq=0; string phase="baseline";
            void Log(string s)=>events.Add($"{++seq:D4} phase={phase} frame={timeline.CurrentFrame} {s}");
            if(timeline is INotifyPropertyChanged tn) tn.PropertyChanged+=(_,e)=>Log("TIMELINE PropertyChanged "+e.PropertyName);
            if(preview is INotifyPropertyChanged pn) pn.PropertyChanged+=(_,e)=>Log("PREVIEW PropertyChanged "+e.PropertyName);

            phase="direct-currentframe-11"; timeline.CurrentFrame=11; Log("ACTION set Timeline.CurrentFrame=11"); await Task.Delay(350);
            phase="direct-currentframe-22"; timeline.CurrentFrame=22; Log("ACTION set Timeline.CurrentFrame=22"); await Task.Delay(350);

            var scroll=active?.GetType().GetMethod("ScrollFrame",BindingFlags.Instance|BindingFlags.Public,null,[typeof(int)],null);
            var scrollAvailable=scroll!=null;
            var beforeScrollFrame=timeline.CurrentFrame;
            if(scroll!=null)
            {
                phase="timelinevm-scrollframe-33"; scroll.Invoke(active,[33]); Log("ACTION TimelineViewModel.ScrollFrame(33)"); await Task.Delay(350);
            }
            var afterScrollFrame=timeline.CurrentFrame;

            var previewEventCount=events.Count(x=>x.Contains("PREVIEW PropertyChanged",StringComparison.Ordinal));
            var directPreviewEvents=events.Count(x=>x.Contains("PREVIEW PropertyChanged",StringComparison.Ordinal)&&(x.Contains("phase=direct-currentframe-11")||x.Contains("phase=direct-currentframe-22")));
            File.WriteAllLines(Path.Combine(OutDir,"events.txt"),events,new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(OutDir,"result.txt"),
            [
                "status=PASS_PREVIEW_REFRESH_OBSERVATION",
                "plugin_dedicated_preview_refresh_candidate_count="+pluginPreviewRefreshCandidates.Length,
                "assembly_public_semantic_candidate_count="+assemblyPublicCandidates,
                "preview_vm_found="+(preview!=null),
                "timeline_vm_scrollframe_public="+scrollAvailable,
                "timeline_vm_scrollframe_moves_currentframe="+(afterScrollFrame!=beforeScrollFrame),
                "scrollframe_before_currentframe="+beforeScrollFrame,
                "scrollframe_after_currentframe="+afterScrollFrame,
                "preview_property_changed_total="+previewEventCount,
                "preview_property_changed_during_direct_currentframe="+directPreviewEvents,
                "final_current_frame="+timeline.CurrentFrame
            ],new UTF8Encoding(false));
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(OutDir,"error.txt"),ex.ToString(),new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(OutDir,"result.txt"),"status=FAIL_EXCEPTION\n",new UTF8Encoding(false));
        }
    }

    static IEnumerable<MemberInfo> RelevantPublic(Type t)=>t.GetMembers(BindingFlags.Instance|BindingFlags.Public).Where(m=>Keys.Any(k=>m.Name.Contains(k,StringComparison.OrdinalIgnoreCase)));
    static bool IsDedicatedPreviewRefreshLike(MemberInfo m)
    {
        var n=m.Name;
        if(n.Equals("RefreshTimelineLengthAndMaxLayer",StringComparison.OrdinalIgnoreCase)) return false;
        return new[]{"preview","render","redraw","invalidate","seek"}.Any(k=>n.Contains(k,StringComparison.OrdinalIgnoreCase))
            || (n.Contains("refresh",StringComparison.OrdinalIgnoreCase) && !n.Contains("TimelineLength",StringComparison.OrdinalIgnoreCase));
    }
    static string FormatMember(MemberInfo m)=>m switch
    {
        PropertyInfo p=>$"PROPERTY {p.DeclaringType?.FullName}.{p.Name}:{p.PropertyType.FullName}",
        MethodInfo mi=>$"METHOD {mi.DeclaringType?.FullName}.{mi.Name}({string.Join(",",mi.GetParameters().Select(p=>p.ParameterType.FullName))}):{mi.ReturnType.FullName}",
        EventInfo e=>$"EVENT {e.DeclaringType?.FullName}.{e.Name}:{e.EventHandlerType?.FullName}",
        _=>$"{m.MemberType} {m.DeclaringType?.FullName}.{m.Name}"
    };

    static object? FindPreviewDataContext()
    {
        var seen=new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach(Window w in Application.Current.Windows)
        {
            foreach(var fe in Elements(w))
            {
                var dc=fe.DataContext;
                if(dc==null || !seen.Add(dc)) continue;
                var name=dc.GetType().FullName??"";
                if(name.Contains("Preview",StringComparison.OrdinalIgnoreCase)) return dc;
            }
        }
        return null;
    }

    static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        if(root is FrameworkElement fe) yield return fe;
        int count=0; try{count=VisualTreeHelper.GetChildrenCount(root);}catch{}
        for(int i=0;i<count;i++) foreach(var child in Elements(VisualTreeHelper.GetChild(root,i))) yield return child;
    }

    static int DumpAssemblyPublicSurface()
    {
        var lines=new List<string>();
        foreach(var asm in AppDomain.CurrentDomain.GetAssemblies().Where(a=>(a.GetName().Name??"").StartsWith("YukkuriMovieMaker",StringComparison.Ordinal)).OrderBy(a=>a.GetName().Name))
        {
            foreach(var t in SafeTypes(asm).Where(t=>t.IsPublic || t.IsNestedPublic).OrderBy(t=>t.FullName))
            {
                var typeName=t.FullName??t.Name;
                foreach(var m in t.GetMembers(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.DeclaredOnly))
                {
                    var combined=typeName+"."+m.Name;
                    var semantic=combined.Contains("Preview",StringComparison.OrdinalIgnoreCase)
                        || combined.Contains("Render",StringComparison.OrdinalIgnoreCase)
                        || combined.Contains("Redraw",StringComparison.OrdinalIgnoreCase)
                        || combined.Contains("Invalidate",StringComparison.OrdinalIgnoreCase)
                        || combined.Contains("Seek",StringComparison.OrdinalIgnoreCase)
                        || combined.Contains("Refresh",StringComparison.OrdinalIgnoreCase)
                        || ((typeName.Contains("Player",StringComparison.OrdinalIgnoreCase)||typeName.Contains("Video",StringComparison.OrdinalIgnoreCase)) && m.Name.Contains("Update",StringComparison.OrdinalIgnoreCase));
                    if(!semantic) continue;
                    lines.Add($"{asm.GetName().Name} | {FormatMember(m)}");
                }
            }
        }
        var distinct=lines.Distinct().OrderBy(x=>x).ToArray();
        File.WriteAllLines(Path.Combine(OutDir,"assembly-public-surface.txt"),distinct,new UTF8Encoding(false));
        return distinct.Length;
    }

    static Type[] SafeTypes(Assembly a)
    {
        try{return a.GetTypes();}
        catch(ReflectionTypeLoadException ex){return ex.Types.Where(x=>x!=null).Cast<Type>().ToArray();}
    }

    static void Dump(string title,Type t,List<string> lines)
    {
        lines.Add("=== "+title+" :: "+t.FullName+" ===");
        foreach(var m in RelevantPublic(t).OrderBy(x=>x.MemberType).ThenBy(x=>x.Name))
        {
            lines.Add(m switch
            {
                PropertyInfo p=>$"PROPERTY public {p.PropertyType.FullName} {p.Name} read={p.CanRead} write={p.CanWrite}",
                MethodInfo mi=>$"METHOD public {mi.ReturnType.FullName} {mi.Name}({string.Join(",",mi.GetParameters().Select(p=>p.ParameterType.FullName+" "+p.Name))})",
                EventInfo e=>$"EVENT public {e.EventHandlerType?.FullName} {e.Name}",
                _=>m.MemberType+" "+m.Name
            });
        }
    }
}
