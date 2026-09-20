using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4ExpressionPresetSurfaceProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL Expression Preset Surface Probe";
    public void SetCulture(CultureInfo cultureInfo) => Bootstrap.Schedule();
}

internal static class Bootstrap
{
    private static bool scheduled;
    public static void Schedule()
    {
        if (scheduled || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CNWL_YMM4_EXPRESSION_PRESET_DIR"))) return;
        scheduled = true;
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(400) };
            var ticks = 0;
            timer.Tick += (_, _) =>
            {
                ticks++;
                try
                {
                    var main = Application.Current.Windows.Cast<Window>()
                        .FirstOrDefault(w => w.DataContext?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                    if (main == null)
                    {
                        if (ticks > 120) { timer.Stop(); Probe.Fail("Main window timeout"); }
                        return;
                    }
                    timer.Stop();
                    Probe.Run();
                }
                catch (Exception ex) { timer.Stop(); Probe.Fail(ex.ToString()); }
            };
            timer.Start();
        }));
    }
}

internal sealed record Result(
    string schema,
    string status,
    string host,
    string[] assemblies,
    string[] faceConstructors,
    string[] facePublicProperties,
    bool bareFaceConstructed,
    string? bareFaceRoute,
    string[] presetTypes,
    string[] presetMembers,
    string[] presetContainers,
    string[] presetApplicationCandidates,
    bool completeCandidateRoute,
    string? error);

internal static class Probe
{
    private static string OutDir => Path.GetFullPath(Environment.GetEnvironmentVariable("CNWL_YMM4_EXPRESSION_PRESET_DIR")!);

    public static void Fail(string error)
    {
        try
        {
            Directory.CreateDirectory(OutDir);
            var r = new Result("cnwl.expression-preset-surface.v1", "FAIL_EXCEPTION", "4.55.1.1 Lite", [], [], [], false, null, [], [], [], [], false, error);
            File.WriteAllText(Path.Combine(OutDir, "result.json"), JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }
        catch { }
    }

    public static void Run()
    {
        try
        {
            Directory.CreateDirectory(OutDir);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => (a.GetName().Name ?? "").StartsWith("YukkuriMovieMaker", StringComparison.Ordinal))
                .OrderBy(a => a.GetName().Name, StringComparer.Ordinal)
                .ToArray();
            var exported = assemblies.SelectMany(GetExportedTypesSafe).Distinct().ToArray();

            var face = typeof(TachieFaceItem);
            var faceCtors = face.GetConstructors(BindingFlags.Instance | BindingFlags.Public)
                .Select(Signature).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var faceProps = face.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(p => $"{p.Name}:{TypeName(p.PropertyType)} read={p.CanRead} write={p.CanWrite}")
                .OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var constructed = TryConstructBareFace(face);

            var presetTypes = exported
                .Where(t => ContainsPreset(t.Name) || ContainsPreset(t.FullName ?? ""))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Select(t => $"{t.FullName} public={t.IsPublic || t.IsNestedPublic} abstract={t.IsAbstract}")
                .ToArray();

            var presetMembers = new List<string>();
            var containers = new List<string>();
            var apply = new List<string>();

            foreach (var t in exported.OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (ContainsPreset(p.Name) || ContainsPreset(TypeName(p.PropertyType)))
                        presetMembers.Add($"PROPERTY {t.FullName}.{p.Name}:{TypeName(p.PropertyType)} static={IsStatic(p)}");
                }
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (ContainsPreset(f.Name) || ContainsPreset(TypeName(f.FieldType)))
                        presetMembers.Add($"FIELD {t.FullName}.{f.Name}:{TypeName(f.FieldType)} static={f.IsStatic}");
                }
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Where(m => !m.IsSpecialName))
                {
                    var sig = Signature(m);
                    if (ContainsPreset(m.Name) || ContainsPreset(TypeName(m.ReturnType)) || m.GetParameters().Any(p => ContainsPreset(TypeName(p.ParameterType))))
                        presetMembers.Add($"METHOD {t.FullName}.{sig}");
                    if ((ContainsPreset(sig) || ContainsPreset(t.Name)) && MethodLooksApplicable(m))
                        apply.Add($"{t.FullName}.{sig}");
                }
                InspectPublicContainers(t, containers);
            }

            presetMembers = presetMembers.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            containers = containers.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            apply = apply.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            var complete = constructed.Ok && containers.Count > 0 && apply.Count > 0;

            var result = new Result(
                "cnwl.expression-preset-surface.v1",
                "PASS_EXPRESSION_PRESET_PUBLIC_SURFACE_DISCOVERY",
                "4.55.1.1 Lite",
                assemblies.Select(a => $"{a.GetName().Name} {a.GetName().Version}").ToArray(),
                faceCtors, faceProps, constructed.Ok, constructed.Route,
                presetTypes, presetMembers.ToArray(), containers.ToArray(), apply.ToArray(), complete, null);

            File.WriteAllText(Path.Combine(OutDir, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(OutDir, "preset-surface.txt"),
                presetMembers.Concat(["", "=== CONTAINERS ==="]).Concat(containers).Concat(["", "=== APPLICATION CANDIDATES ==="]).Concat(apply),
                new UTF8Encoding(false));
        }
        catch (Exception ex) { Fail(ex.ToString()); }
    }

    private static IEnumerable<Type> GetExportedTypesSafe(Assembly a)
    {
        try { return a.GetExportedTypes(); }
        catch { return []; }
    }

    private static bool ContainsPreset(string value) => value.Contains("Preset", StringComparison.OrdinalIgnoreCase);

    private static string TypeName(Type t)
    {
        if (!t.IsGenericType) return t.FullName ?? t.Name;
        var raw = t.GetGenericTypeDefinition().FullName ?? t.Name;
        var tick = raw.IndexOf((char)96);
        var root = tick >= 0 ? raw[..tick] : raw;
        return root + "<" + string.Join(",", t.GetGenericArguments().Select(TypeName)) + ">";
    }

    private static string Signature(ConstructorInfo c) => $".ctor({string.Join(",", c.GetParameters().Select(p => TypeName(p.ParameterType)))})";
    private static string Signature(MethodInfo m) => $"{m.Name}({string.Join(",", m.GetParameters().Select(p => TypeName(p.ParameterType)))}):{TypeName(m.ReturnType)} static={m.IsStatic}";
    private static bool IsStatic(PropertyInfo p) => (p.GetMethod ?? p.SetMethod)?.IsStatic == true;

    private static bool MethodLooksApplicable(MethodInfo m)
    {
        var types = m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType).ToArray();
        return types.Any(t => t == typeof(TachieFaceItem) || typeof(IItem).IsAssignableFrom(t) || t == typeof(Character)) ||
               m.Name.Contains("Apply", StringComparison.OrdinalIgnoreCase) ||
               m.Name.Contains("Create", StringComparison.OrdinalIgnoreCase) ||
               m.Name.Contains("Add", StringComparison.OrdinalIgnoreCase) ||
               m.Name.Contains("Load", StringComparison.OrdinalIgnoreCase);
    }

    private static (bool Ok, string? Route) TryConstructBareFace(Type face)
    {
        var character = new Character { Name = "CNWL_PresetProbe" };
        foreach (var ctor in face.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
        {
            var ps = ctor.GetParameters();
            try
            {
                if (ps.Length == 1 && ps[0].ParameterType == typeof(Character))
                {
                    var value = ctor.Invoke([character]);
                    return (value is TachieFaceItem tf && tf.CharacterName == character.Name, Signature(ctor));
                }
                if (ps.Length == 0)
                {
                    var value = ctor.Invoke([]);
                    var cp = face.GetProperty("Character", BindingFlags.Instance | BindingFlags.Public);
                    if (cp?.CanWrite == true) cp.SetValue(value, character);
                    return (value is TachieFaceItem tf && tf.CharacterName == character.Name, Signature(ctor) + " + public Character setter");
                }
            }
            catch { }
        }
        return (false, null);
    }

    private static void InspectPublicContainers(Type t, List<string> output)
    {
        object? singleton = null;
        string singletonName = "";
        foreach (var sp in t.GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (!sp.CanRead || sp.GetIndexParameters().Length != 0) continue;
            if (sp.Name is not ("Default" or "Instance" or "Current") && !ContainsPreset(sp.Name)) continue;
            try
            {
                singleton = sp.GetValue(null);
                if (singleton != null) { singletonName = $"{t.FullName}.{sp.Name}"; break; }
            }
            catch { }
        }
        if (singleton == null) return;

        foreach (var p in singleton.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
            if (!ContainsPreset(p.Name) && !ContainsPreset(TypeName(p.PropertyType))) continue;
            try
            {
                var v = p.GetValue(singleton);
                var count = v is ICollection c ? c.Count.ToString(CultureInfo.InvariantCulture) : v is IEnumerable ? "enumerable" : "scalar";
                output.Add($"{singletonName}.{p.Name}:{TypeName(p.PropertyType)} value={count}");
            }
            catch (Exception ex)
            {
                output.Add($"{singletonName}.{p.Name}:{TypeName(p.PropertyType)} value=<throw:{ex.GetType().Name}>");
            }
        }
    }
}
