using System.Collections;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;

namespace Ymm4ToolGroupProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL Tool Group Probe";
    public void SetCulture(CultureInfo cultureInfo) => GroupProbe.Schedule(cultureInfo.Name);
}

public sealed class EnglishUtilitiesTool : IToolPlugin
{
    public string Name => "CNWL Group English";
    public Type ViewModelType => typeof(EnglishProbeVm);
    public Type ViewType => typeof(ProbeView);
    public bool AllowMultipleInstances => false;
    public string DefaultGroupName => "Utilities";
}
public sealed class JapaneseUtilitiesTool : IToolPlugin
{
    public string Name => "CNWL Group Japanese";
    public Type ViewModelType => typeof(JapaneseProbeVm);
    public Type ViewType => typeof(ProbeView);
    public bool AllowMultipleInstances => false;
    public string DefaultGroupName => "ユーティリティ";
}
public sealed class DefaultGroupTool : IToolPlugin
{
    public string Name => "CNWL Group Default";
    public Type ViewModelType => typeof(DefaultProbeVm);
    public Type ViewType => typeof(ProbeView);
    public bool AllowMultipleInstances => false;
    public string DefaultGroupName => "";
}
public sealed class ProbeView : UserControl
{
    public ProbeView() => Content = new TextBlock { Text = "CNWL group probe" };
}
public abstract class ProbeVmBase : IToolViewModel
{
    public abstract string Title { get; }
    public bool CanSuspend => false;
    public ToolState SaveState() => new() { Title = Title };
    public void LoadState(ToolState stateData) { }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested { add { } remove { } }
}
public sealed class EnglishProbeVm : ProbeVmBase { public override string Title => "CNWL Group English"; }
public sealed class JapaneseProbeVm : ProbeVmBase { public override string Title => "CNWL Group Japanese"; }
public sealed class DefaultProbeVm : ProbeVmBase { public override string Title => "CNWL Group Default"; }

internal static class GroupProbe
{
    static bool scheduled;
    static string output = "";
    static string culture = "";

    public static void Schedule(string cultureName)
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_GROUP_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true; output = Path.GetFullPath(dir); culture = cultureName;
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start));
    }

    static void Start()
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(500) };
        var ticks = 0;
        var projectCreated = false;
        var lastPaths = new Dictionary<string,string>();
        timer.Tick += (_, _) =>
        {
            try
            {
                ticks++;
                foreach (Window w in Application.Current.Windows)
                {
                    var main = w.DataContext;
                    if (main?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel") continue;
                    var active = main.GetType().GetProperty("ActiveTimelineViewModel", BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(main);
                    if (active == null && !projectCreated)
                    {
                        projectCreated = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                        continue;
                    }
                    if (active == null) continue;

                    var prop = main.GetType().GetProperty("ToolMenuItems", BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                    if (prop?.GetValue(main) is not IEnumerable items) continue;

                    var lines = new List<string> { "culture=" + culture };
                    DumpInterface(lines);
                    var paths = new Dictionary<string,string>();
                    foreach (var item in items) Visit(item, "", lines, paths, 0);
                    lastPaths = paths;
                    File.WriteAllLines(Path.Combine(output,"menu-tree.txt"), lines, new UTF8Encoding(false));

                    var markers = new[]{"CNWL Group English","CNWL Group Japanese","CNWL Group Default"};
                    if (markers.All(paths.ContainsKey))
                    {
                        var result = new List<string> { "status=PASS_TOOL_GROUP_OBSERVED", "culture=" + culture };
                        foreach (var m in markers) result.Add(m.Replace(' ','_') + "_path=" + paths[m]);
                        var jp = paths["CNWL Group Japanese"];
                        var en = paths["CNWL Group English"];
                        result.Add("japanese_parent=" + Parent(jp));
                        result.Add("english_parent=" + Parent(en));
                        result.Add("japanese_joined_localized_utilities=" + Parent(jp).Contains("ユーティリティ", StringComparison.Ordinal));
                        result.Add("english_created_or_joined_parent=" + Parent(en));
                        File.WriteAllLines(Path.Combine(output,"result.txt"), result, new UTF8Encoding(false));
                        timer.Stop();
                        return;
                    }
                }
                if (ticks >= 100)
                {
                    timer.Stop();
                    var markers = new[]{"CNWL Group English","CNWL Group Japanese","CNWL Group Default"};
                    var result = new List<string> { "status=FAIL_TIMEOUT", "culture=" + culture, "observed_marker_count=" + lastPaths.Count };
                    foreach (var m in markers) result.Add(m.Replace(' ','_') + "_path=" + (lastPaths.TryGetValue(m, out var p) ? p : "<missing>"));
                    File.WriteAllLines(Path.Combine(output,"result.txt"), result, new UTF8Encoding(false));
                }
            }
            catch(Exception ex)
            {
                timer.Stop();
                File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString(),new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(output,"result.txt"),"status=FAIL_EXCEPTION\n",new UTF8Encoding(false));
            }
        };
        timer.Start();
    }

    static string Parent(string path)
    {
        var i=path.LastIndexOf(" > ",StringComparison.Ordinal);
        return i < 0 ? "" : path[..i];
    }

    static void DumpInterface(List<string> lines)
    {
        lines.Add("=== IToolPlugin public surface ===");
        foreach(var m in typeof(IToolPlugin).GetMembers().OrderBy(x=>x.Name))
            lines.Add(m.MemberType+" "+m.Name+" "+m);
        foreach(var asm in new[]{typeof(IToolPlugin).Assembly})
        {
            foreach(var t in SafeTypes(asm).Where(t=>t.Name.Contains("Tool",StringComparison.OrdinalIgnoreCase)||t.Name.Contains("Group",StringComparison.OrdinalIgnoreCase)||t.Name.Contains("Utilit",StringComparison.OrdinalIgnoreCase)))
            {
                foreach(var f in t.GetFields(BindingFlags.Public|BindingFlags.Static))
                {
                    if(f.FieldType==typeof(string)) { try { lines.Add($"STATIC_STRING {t.FullName}.{f.Name}={f.GetValue(null)}"); } catch {} }
                }
                foreach(var p in t.GetProperties(BindingFlags.Public|BindingFlags.Static))
                {
                    if(p.PropertyType==typeof(string) && p.GetIndexParameters().Length==0) { try { lines.Add($"STATIC_STRING {t.FullName}.{p.Name}={p.GetValue(null)}"); } catch {} }
                }
            }
        }
    }

    static Type[] SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch(ReflectionTypeLoadException ex) { return ex.Types.Where(x=>x!=null).Cast<Type>().ToArray(); }
    }

    static void Visit(object item, string parent, List<string> lines, Dictionary<string,string> paths, int depth)
    {
        if(depth>8) return;
        var t=item.GetType();
        string Label()
        {
            foreach(var n in new[]{"Header","Title","Name"})
            {
                try { var v=t.GetProperty(n)?.GetValue(item)?.ToString(); if(!string.IsNullOrWhiteSpace(v)) return v; } catch {}
            }
            return t.Name;
        }
        var label=Label();
        var path=string.IsNullOrEmpty(parent)?label:parent+" > "+label;
        lines.Add(new string(' ',depth*2)+path+" :: "+t.FullName);
        if(label.StartsWith("CNWL Group ",StringComparison.Ordinal)) paths[label]=path;

        object? children=null;
        foreach(var n in new[]{"Children","Items"})
        {
            try { children=t.GetProperty(n)?.GetValue(item); if(children is IEnumerable) break; } catch {}
        }
        if(children is IEnumerable e)
            foreach(var child in e) if(child!=null) Visit(child,path,lines,paths,depth+1);
    }
}
