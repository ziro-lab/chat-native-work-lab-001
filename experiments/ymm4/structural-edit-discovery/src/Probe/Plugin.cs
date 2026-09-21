using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Settings;

namespace Ymm4NoHarmonyStructuralDiscoveryProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL YMM4 Structural Edit Discovery";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_YMM4_STRUCTURAL_DISCOVERY_DIR");
        if (scheduled || string.IsNullOrWhiteSpace(dir))
            return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Bootstrap), DispatcherPriority.ApplicationIdle);
    }

    static void Bootstrap()
    {
        var ticks = 0;
        var created = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };

        timer.Tick += (_, _) =>
        {
            try
            {
                ticks++;
                foreach (Window w in Application.Current.Windows)
                {
                    var main = w.DataContext;
                    if (main?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel")
                        continue;

                    var active = main.GetType()
                        .GetProperty("ActiveTimelineViewModel", All)
                        ?.GetValue(main);

                    if (active is null && !created)
                    {
                        created = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                        return;
                    }

                    if (active is null)
                        continue;

                    var timeline = active.GetType()
                        .GetProperty("Timeline", All)
                        ?.GetValue(active) as Timeline
                        ?? active.GetType().GetField("timeline", All)?.GetValue(active) as Timeline;

                    if (timeline is null)
                        continue;

                    timer.Stop();
                    Run(main, active, timeline);
                    return;
                }

                if (ticks > 100)
                {
                    timer.Stop();
                    Write("FAIL_BOOTSTRAP_TIMEOUT", []);
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString(), new UTF8Encoding(false));
                Write("FAIL_EXCEPTION", ["message=" + ex.GetBaseException().Message]);
            }
        };

        timer.Start();
    }

    static void Run(object main, object active, Timeline timeline)
    {
        var lines = new List<string>
        {
            "status=PASS_NO_HARMONY_STRUCTURAL_DISCOVERY",
            "ymm_version=" + typeof(Timeline).Assembly.GetName().Version,
            "timeline_type=" + timeline.GetType().FullName,
            "timeline_vm_type=" + active.GetType().FullName,
            "main_vm_type=" + main.GetType().FullName,
        };

        // 1. User-facing command enum / command objects.
        lines.Add("[CommandType candidates]");
        foreach (var name in Enum.GetNames<CommandType>()
                     .Where(IsStructuralName)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            var value = Enum.Parse<CommandType>(name);
            ICommand? command = null;
            string error = "";
            try { command = CommandSettings.Default[value]; }
            catch (Exception ex) { error = ex.GetBaseException().GetType().Name + ":" + ex.GetBaseException().Message; }

            string can = "<unknown>";
            if (command is not null)
            {
                try { can = command.CanExecute(null).ToString(); }
                catch (Exception ex) { can = "error:" + ex.GetBaseException().GetType().Name; }
            }

            lines.Add($"command|{name}|object={command?.GetType().FullName ?? "<null>"}|can_null={can}|error={error}");
        }

        // 2. Methods/properties on the key host types.
        AddTypeSurface(lines, "Timeline", timeline.GetType());
        AddTypeSurface(lines, "TimelineViewModel", active.GetType());
        AddTypeSurface(lines, "MainViewModel", main.GetType());
        AddTypeSurface(lines, "LayerSettings", timeline.LayerSettings.GetType());
        AddTypeSurface(lines, "LayerSelection", timeline.LayerSelection.GetType());

        var ymm = typeof(Timeline).Assembly;
        foreach (var typeName in new[]
        {
            "YukkuriMovieMaker.ViewModels.TimelineLayerLabelItemViewModel",
            "YukkuriMovieMaker.Views.TimelineView",
            "YukkuriMovieMaker.Views.TimelineLayerLabelItemView"
        })
        {
            if (ymm.GetType(typeName) is { } type)
                AddTypeSurface(lines, type.Name, type);
            else
                lines.Add($"type_missing|{typeName}");
        }

        // 3. Locate UndoRedoManager and inspect its event/method surface.
        var manager = FindMemberValueByTypeName(active, "UndoRedoManager")
            ?? FindMemberValueByTypeName(main, "UndoRedoManager");
        if (manager is null)
        {
            lines.Add("undo_redo_manager=<not_found>");
        }
        else
        {
            lines.Add("undo_redo_manager=" + manager.GetType().FullName);
            foreach (var evt in manager.GetType().GetEvents(All).OrderBy(e => e.Name, StringComparer.Ordinal))
                lines.Add($"undo_event|{evt.Name}|handler={evt.EventHandlerType?.FullName}");
            foreach (var method in manager.GetType().GetMethods(All)
                         .Where(m => IsStructuralName(m.Name) || m.Name is "Record" or "Undo" or "Redo" or "AddCommand")
                         .OrderBy(m => m.Name, StringComparer.Ordinal))
                lines.Add("undo_method|" + DescribeMethod(method));
        }

        // 4. Any command-like members on layer-related view models.
        foreach (var type in ymm.GetTypes()
                     .Where(t => t.FullName?.Contains("Timeline", StringComparison.Ordinal) == true &&
                                 t.FullName.Contains("Layer", StringComparison.Ordinal))
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            foreach (var member in type.GetMembers(All)
                         .Where(m => IsStructuralName(m.Name) ||
                                     m.Name.Contains("Command", StringComparison.OrdinalIgnoreCase))
                         .Take(80))
            {
                lines.Add($"layer_type_member|{type.FullName}|{member.MemberType}|{member.Name}");
            }
        }

        File.WriteAllLines(Path.Combine(output, "discovery.txt"), lines, new UTF8Encoding(false));
        Write("PASS_NO_HARMONY_STRUCTURAL_DISCOVERY", [
            "command_candidates=" + lines.Count(x => x.StartsWith("command|", StringComparison.Ordinal)),
            "surface_lines=" + lines.Count(x => x.StartsWith("method|", StringComparison.Ordinal) || x.StartsWith("property|", StringComparison.Ordinal)),
            "undo_manager_found=" + (manager is not null)
        ]);
    }

    static void AddTypeSurface(List<string> lines, string label, Type type)
    {
        lines.Add($"[Type {label}] {type.FullName}");

        foreach (var method in type.GetMethods(All)
                     .Where(m => IsStructuralName(m.Name))
                     .OrderBy(m => m.Name, StringComparer.Ordinal)
                     .ThenBy(m => m.GetParameters().Length))
        {
            lines.Add($"method|{label}|{DescribeMethod(method)}");
        }

        foreach (var property in type.GetProperties(All)
                     .Where(p => IsStructuralName(p.Name))
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var getter = property.GetGetMethod(true);
            var setter = property.GetSetMethod(true);
            lines.Add(
                $"property|{label}|{property.Name}|type={property.PropertyType.FullName}|get={Visibility(getter)}|set={Visibility(setter)}");
        }

        foreach (var field in type.GetFields(All)
                     .Where(f => IsStructuralName(f.Name))
                     .OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            lines.Add($"field|{label}|{field.Name}|type={field.FieldType.FullName}|vis={Visibility(field)}");
        }

        foreach (var evt in type.GetEvents(All)
                     .Where(e => IsStructuralName(e.Name))
                     .OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            lines.Add($"event|{label}|{evt.Name}|handler={evt.EventHandlerType?.FullName}");
        }
    }

    static string DescribeMethod(MethodInfo method)
    {
        var parameters = string.Join(",", method.GetParameters().Select(p => (p.ParameterType.FullName ?? p.ParameterType.Name) + " " + p.Name));
        return $"{method.Name}|vis={Visibility(method)}|static={method.IsStatic}|return={method.ReturnType.FullName}|params=({parameters})";
    }

    static string Visibility(MethodBase? method)
    {
        if (method is null) return "none";
        if (method.IsPublic) return "public";
        if (method.IsFamily) return "protected";
        if (method.IsAssembly) return "internal";
        if (method.IsFamilyOrAssembly) return "protected_internal";
        return "private";
    }

    static string Visibility(FieldInfo field)
    {
        if (field.IsPublic) return "public";
        if (field.IsFamily) return "protected";
        if (field.IsAssembly) return "internal";
        if (field.IsFamilyOrAssembly) return "protected_internal";
        return "private";
    }

    static bool IsStructuralName(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("layer") ||
               n.Contains("insert") ||
               n.Contains("delete") ||
               n.Contains("remove") ||
               n.Contains("move") ||
               n.Contains("shift") ||
               n.Contains("reorder") ||
               n.Contains("swap");
    }

    static object? FindMemberValueByTypeName(object instance, string typeNamePart)
    {
        for (Type? type = instance.GetType(); type is not null; type = type.BaseType)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var property in type.GetProperties(flags)
                         .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead))
            {
                try
                {
                    var value = property.GetValue(instance);
                    if (value?.GetType().Name.Contains(typeNamePart, StringComparison.OrdinalIgnoreCase) == true)
                        return value;
                }
                catch { }
            }

            foreach (var field in type.GetFields(flags))
            {
                try
                {
                    var value = field.GetValue(instance);
                    if (value?.GetType().Name.Contains(typeNamePart, StringComparison.OrdinalIgnoreCase) == true)
                        return value;
                }
                catch { }
            }
        }
        return null;
    }

    static void Write(string status, IEnumerable<string> details) =>
        File.WriteAllLines(
            Path.Combine(output, "result.txt"),
            new[] { "status=" + status }.Concat(details),
            new UTF8Encoding(false));
}
