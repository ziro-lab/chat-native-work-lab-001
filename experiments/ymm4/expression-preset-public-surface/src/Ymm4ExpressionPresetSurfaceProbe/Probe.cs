using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Tachie;
using YukkuriMovieMaker.Plugin.Tachie.Psd;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4ExpressionPresetSurfaceProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL Expression Preset Surface Probe";
    public void SetCulture(CultureInfo cultureInfo) => Bootstrap.Schedule();
}

public sealed class ProbeTool : IToolPlugin
{
    public string Name => "CNWL Expression Preset Surface Probe";
    public Type ViewModelType => typeof(ProbeVm);
    public Type ViewType => typeof(ProbeView);
    public bool AllowMultipleInstances => false;
}
public sealed class ProbeView : UserControl
{
    internal static ProbeView? Current;
    public ProbeView()
    {
        Current = this;
        Content = new TextBlock { Text = "CNWL Expression Preset Surface Probe", Margin = new Thickness(8) };
    }
}
public sealed class ProbeVm : ITimelineToolViewModel, IToolViewModel
{
    public string Title => "CNWL Expression Preset Surface Probe";
    public bool CanSuspend => false;
    public void SetTimelineToolInfo(TimelineToolInfo info) => Probe.Run();
    public ToolState SaveState() => new() { Title = Title };
    public void LoadState(ToolState stateData) { }
    public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    public event EventHandler<CreateNewToolViewRequestedEventArgs>? CreateNewToolViewRequested { add { } remove { } }
}

internal static class Bootstrap
{
    private static bool scheduled, created, opened;
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
                    foreach (Window w in Application.Current.Windows)
                    {
                        var main = w.DataContext;
                        if (main?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel") continue;
                        var active = main.GetType().GetProperty("ActiveTimelineViewModel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(main);
                        if (active == null && !created)
                        {
                            created = true;
                            main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                            return;
                        }
                        if (active != null && !opened) opened = OpenTool(main);
                        if (opened && ProbeView.Current != null) { timer.Stop(); return; }
                    }
                    if (ticks > 120) { timer.Stop(); Probe.Fail("Tool open/bootstrap timeout"); }
                }
                catch (Exception ex) { timer.Stop(); Probe.Fail(ex.ToString()); }
            };
            timer.Start();
        }));
    }

    private static bool OpenTool(object main)
    {
        if (main.GetType().GetProperty("ToolMenuItems")?.GetValue(main) is not IEnumerable items) return false;
        bool Visit(object x, int depth)
        {
            if (depth > 8) return false;
            var t = x.GetType();
            var label = t.GetProperty("Header")?.GetValue(x)?.ToString()
                ?? t.GetProperty("Title")?.GetValue(x)?.ToString()
                ?? t.GetProperty("Name")?.GetValue(x)?.ToString()
                ?? "";
            if (label.Contains("CNWL Expression Preset Surface Probe", StringComparison.Ordinal))
            {
                if (t.GetProperty("Command")?.GetValue(x) is System.Windows.Input.ICommand c)
                {
                    var p = t.GetProperty("CommandParameter")?.GetValue(x);
                    if (c.CanExecute(p)) { c.Execute(p); return true; }
                }
                foreach (var n in new[] { "IsVisible", "IsSelected", "IsActive" })
                    try { t.GetProperty(n)?.SetValue(x, true); } catch { }
                return true;
            }
            var children = (t.GetProperty("Children")?.GetValue(x) ?? t.GetProperty("Items")?.GetValue(x)) as IEnumerable;
            if (children != null)
                foreach (var y in children)
                    if (y != null && Visit(y, depth + 1)) return true;
            return false;
        }
        foreach (var x in items)
            if (x != null && Visit(x, 0)) return true;
        return false;
    }
}

internal sealed record BoundaryResult(
    string schema,
    string status,
    string host,
    bool tachiePluginRegistryPublic,
    bool bareFaceItemPublicConstruction,
    bool publicPluginFaceParameterFactory,
    bool publicFaceCompositionSucceeded,
    bool psdPresetDataPublic,
    bool psdPresetFileSettingsPublic,
    bool psdPresetLoaderPublic,
    string psdFaceParameterRuntimeType,
    bool psdFaceParameterRuntimeTypePublic,
    bool publicPsdPresetToFaceParameterBridge,
    bool psdPresetEditorApplyPublic,
    bool animationPresetDataTypePublic,
    bool animationPresetParameterBindingPublic,
    bool safeGeneralExpressionPresetMode,
    string decision);

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

    private static bool started;
    public static void Run()
    {
        if (started) return;
        started = true;
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

            var targeted = new List<string>();
            foreach (var t in exported.Where(IsTargetedPresetType).OrderBy(t => t.FullName, StringComparer.Ordinal))
                DescribeType(t, targeted);
            targeted.Add("");
            targeted.Add("=== CHARACTER TACHIE/PRESET RELATED PUBLIC PROPERTIES ===");
            foreach (var p in typeof(Character).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => RelatedName(p.Name) || RelatedName(TypeName(p.PropertyType))).OrderBy(p => p.Name, StringComparer.Ordinal))
                targeted.Add($"PROPERTY {typeof(Character).FullName}.{p.Name}:{TypeName(p.PropertyType)} read={p.CanRead} write={p.CanWrite}");
            File.WriteAllLines(Path.Combine(OutDir, "targeted-preset-surface.txt"), targeted, new UTF8Encoding(false));

            var pluginSurface = new List<string>();
            DescribeContract(typeof(ITachiePlugin), pluginSurface);
            DescribeContract(typeof(ITachieFaceParameter), pluginSurface);
            DescribeContract(typeof(TachieFaceParameterBase), pluginSurface);

            var allTypes = assemblies.SelectMany(GetTypesSafe).Distinct().ToArray();
            var pluginTypes = allTypes.Where(t => typeof(ITachiePlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
            foreach (var pt in pluginTypes)
            {
                pluginSurface.Add($"=== IMPLEMENTATION {pt.FullName} public={pt.IsPublic || pt.IsNestedPublic} ===");
                var ctor = pt.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                pluginSurface.Add($"PUBLIC_PARAMETERLESS_CTOR={ctor != null}");
                if (ctor != null)
                {
                    try
                    {
                        var plugin = (ITachiePlugin)ctor.Invoke([]);
                        pluginSurface.Add($"PLUGIN_NAME={plugin.Name}");
                        var fp = plugin.CreateFaceParameter();
                        pluginSurface.Add($"FACE_PARAMETER_RUNTIME_TYPE={fp?.GetType().FullName ?? "<null>"} public={(fp?.GetType().IsPublic == true || fp?.GetType().IsNestedPublic == true)}");
                        if (fp != null) DescribeRuntimeObject(fp, pluginSurface);
                    }
                    catch (Exception ex) { pluginSurface.Add($"CREATE_FACE_PARAMETER_THROW={ex.GetType().Name}:{ex.Message}"); }
                }
                pluginSurface.Add("");
            }

            pluginSurface.Add("=== PUBLIC PLUGIN CONTAINER CANDIDATES ===");
            foreach (var t in exported.OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
                    if (ContainsTachiePlugin(p.PropertyType))
                        pluginSurface.Add($"PROPERTY {t.FullName}.{p.Name}:{TypeName(p.PropertyType)}");
                }
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => !m.IsSpecialName))
                {
                    if (ContainsTachiePlugin(m.ReturnType))
                        pluginSurface.Add($"METHOD {t.FullName}.{Signature(m)}");
                }
            }
            File.WriteAllLines(Path.Combine(OutDir, "tachie-plugin-surface.txt"), pluginSurface, new UTF8Encoding(false));

            var presetEditorSurface = new List<string>();
            foreach (var t in allTypes.Where(t =>
                (t.FullName ?? "").Contains("YukkuriMovieMaker.Plugin.Tachie.Psd", StringComparison.Ordinal) &&
                ((t.FullName ?? "").Contains("Preset", StringComparison.OrdinalIgnoreCase) ||
                 (t.FullName ?? "").Contains("FaceParameter", StringComparison.OrdinalIgnoreCase)) ||
                (t.FullName ?? "").Contains("YukkuriMovieMaker.Plugin.Tachie.AnimationTachie.Preset", StringComparison.Ordinal))
                .OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                DescribeDeclaredType(t, presetEditorSurface);
                foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(p => p.Name.Contains("Preset", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Layer", StringComparison.OrdinalIgnoreCase)))
                {
                    var attrs = p.GetCustomAttributesData().Select(a => a.AttributeType.FullName ?? a.AttributeType.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                    presetEditorSurface.Add($"ATTRIBUTES {t.FullName}.{p.Name}=[{string.Join(",", attrs)}]");
                }
            }
            File.WriteAllLines(Path.Combine(OutDir, "preset-editor-declared-surface.txt"), presetEditorSurface, new UTF8Encoding(false));

            var registryProperty = typeof(PluginLoader).GetProperty("TachiePlugins", BindingFlags.Public | BindingFlags.Static);
            var registryPublic = registryProperty?.CanRead == true;
            var psdPlugin = new PsdTachiePlugin();
            var psdFaceParameter = psdPlugin.CreateFaceParameter();
            var character = new Character { Name = "CNWL_Boundary", TachieType = psdPlugin.GetType(), TachieDefaultFaceParameter = psdFaceParameter };
            var bareFace = new TachieFaceItem(character);
            bareFace.TachieFaceParameter = psdFaceParameter;
            var faceComposition = ReferenceEquals(bareFace.Character, character) && ReferenceEquals(bareFace.TachieFaceParameter, psdFaceParameter);

            var psdPresetLoader = typeof(PsdFileSettings).GetMethod("LoadFromPsdFilePath",
                BindingFlags.Public | BindingFlags.Static, null, [typeof(string)], null);
            var psdPreset = new PsdPreset("CNWL", ImmutableList<string>.Empty, ImmutableList<string>.Empty);
            var psdSettings = new PsdFileSettings { Presets = [psdPreset] };
            var presetDataPublic = typeof(PsdPreset).IsPublic && psdSettings.Presets.Count == 1 && ReferenceEquals(psdSettings.Presets[0], psdPreset);

            var runtimeFaceType = psdFaceParameter.GetType();
            bool IsPsdPresetToFaceBridge(MethodInfo m)
            {
                var sigTypes = m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType).ToArray();
                var hasPreset = sigTypes.Any(t => t == typeof(PsdPreset) || (t.IsGenericType && t.GetGenericArguments().Contains(typeof(PsdPreset))));
                var hasFace = sigTypes.Any(t => t == typeof(ITachieFaceParameter) || typeof(ITachieFaceParameter).IsAssignableFrom(t));
                return hasPreset && hasFace;
            }
            var publicBridge = exported.SelectMany(t =>
            {
                try { return t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static); }
                catch { return []; }
            }).Any(IsPsdPresetToFaceBridge);

            var psdEditorApplyPublic = typeof(PsdPresetEditor)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(m => m.Name.Contains("Apply", StringComparison.OrdinalIgnoreCase) ||
                          m.Name.Contains("Set", StringComparison.OrdinalIgnoreCase) ||
                          IsPsdPresetToFaceBridge(m));

            var animationPresetType = allTypes.FirstOrDefault(t => t.FullName == "YukkuriMovieMaker.Plugin.Tachie.AnimationTachie.Preset");
            var animationCombo = allTypes.FirstOrDefault(t => t.FullName == "YukkuriMovieMaker.Plugin.Tachie.AnimationTachie.PresetComboBox");
            var animationParameter = animationCombo?.GetProperty("TachieParameter", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            var animationBindingPublic = animationParameter?.GetMethod?.IsPublic == true && animationParameter?.SetMethod?.IsPublic == true;
            var safeGeneral = registryPublic && faceComposition && presetDataPublic && publicBridge && psdEditorApplyPublic &&
                              animationPresetType?.IsPublic == true && animationBindingPublic;

            var boundary = new BoundaryResult(
                "cnwl.expression-preset-public-boundary.v1",
                "PASS_EXPRESSION_PRESET_PUBLIC_BOUNDARY",
                "4.55.1.1 Lite",
                registryPublic,
                true,
                psdFaceParameter != null,
                faceComposition,
                presetDataPublic,
                typeof(PsdFileSettings).IsPublic,
                psdPresetLoader != null,
                runtimeFaceType.FullName ?? runtimeFaceType.Name,
                runtimeFaceType.IsPublic || runtimeFaceType.IsNestedPublic,
                publicBridge,
                psdEditorApplyPublic,
                animationPresetType?.IsPublic == true || animationPresetType?.IsNestedPublic == true,
                animationBindingPublic,
                safeGeneral,
                safeGeneral
                    ? "A public end-to-end preset route is available."
                    : "Bare expression items and plugin face-parameter factories are public, but saved expression preset enumeration/application is not exposed as one public end-to-end contract. Do not implement general preset mode without widening the safety boundary.");

            File.WriteAllText(Path.Combine(OutDir, "boundary.json"),
                JsonSerializer.Serialize(boundary, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

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

    private static IEnumerable<Type> GetTypesSafe(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>(); }
        catch { return []; }
    }

    private static bool ContainsTachiePlugin(Type t)
    {
        if (t == typeof(ITachiePlugin)) return true;
        if (t.IsArray) return ContainsTachiePlugin(t.GetElementType()!);
        if (t.IsGenericType) return t.GetGenericArguments().Any(ContainsTachiePlugin);
        return false;
    }

    private static void DescribeContract(Type t, List<string> output)
    {
        output.Add($"=== CONTRACT {t.FullName} ===");
        foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public).OrderBy(p => p.Name, StringComparer.Ordinal))
            output.Add($"PROPERTY {p.Name}:{TypeName(p.PropertyType)} read={p.CanRead} write={p.CanWrite}");
        foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public).Where(m => !m.IsSpecialName).OrderBy(m => m.Name, StringComparer.Ordinal))
            output.Add("METHOD " + Signature(m));
        output.Add("");
    }

    private static void DescribeRuntimeObject(object value, List<string> output)
    {
        var t = value.GetType();
        foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            string rendered = "<unread>";
            if (p.CanRead && p.GetIndexParameters().Length == 0)
            {
                try
                {
                    var v = p.GetValue(value);
                    rendered = v switch
                    {
                        null => "<null>",
                        string s => s,
                        ICollection c => $"collection:{c.Count}",
                        IEnumerable => "enumerable",
                        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "<null>"
                    };
                }
                catch (Exception ex) { rendered = $"<throw:{ex.GetType().Name}>"; }
            }
            output.Add($"RUNTIME_PROPERTY {p.Name}:{TypeName(p.PropertyType)} read={p.CanRead} write={p.CanWrite} value={rendered}");
        }
        foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(m => !m.IsSpecialName).OrderBy(m => m.Name, StringComparer.Ordinal))
            output.Add("RUNTIME_METHOD " + Signature(m));
    }

    private static void DescribeDeclaredType(Type t, List<string> output)
    {
        output.Add($"=== DECLARED TYPE {t.FullName} public={t.IsPublic || t.IsNestedPublic} ===");
        foreach (var c in t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            output.Add($"CTOR {Signature(c)} public={c.IsPublic}");
        foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var gm = p.GetMethod; var sm = p.SetMethod;
            output.Add($"PROPERTY {p.Name}:{TypeName(p.PropertyType)} getPublic={gm?.IsPublic == true} setPublic={sm?.IsPublic == true}");
        }
        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).OrderBy(f => f.Name, StringComparer.Ordinal))
            output.Add($"FIELD {f.Name}:{TypeName(f.FieldType)} public={f.IsPublic} static={f.IsStatic}");
        foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName).OrderBy(m => m.Name, StringComparer.Ordinal))
            output.Add($"METHOD {Signature(m)} public={m.IsPublic}");
        output.Add("");
    }

    private static bool RelatedName(string value) =>
        value.Contains("Preset", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Psd", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Tachie", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Face", StringComparison.OrdinalIgnoreCase);

    private static bool IsTargetedPresetType(Type t)
    {
        var n = t.FullName ?? t.Name;
        return n.Contains("YukkuriMovieMaker.Plugin.Tachie.Psd", StringComparison.Ordinal) &&
               (n.Contains("Preset", StringComparison.OrdinalIgnoreCase) || n.Contains("FileSettings", StringComparison.OrdinalIgnoreCase) || n.Contains("FaceParameter", StringComparison.OrdinalIgnoreCase)) ||
               n.Contains("YukkuriMovieMaker.Plugin.Tachie.AnimationTachie.Preset", StringComparison.Ordinal);
    }

    private static void DescribeType(Type t, List<string> output)
    {
        output.Add($"=== TYPE {t.FullName} ===");
        foreach (var c in t.GetConstructors(BindingFlags.Instance | BindingFlags.Public).OrderBy(Signature, StringComparer.Ordinal))
            output.Add("CTOR " + Signature(c));
        foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public).OrderBy(p => p.Name, StringComparer.Ordinal))
            output.Add($"PROPERTY {p.Name}:{TypeName(p.PropertyType)} static={IsStatic(p)} read={p.CanRead} write={p.CanWrite}");
        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public).OrderBy(f => f.Name, StringComparer.Ordinal))
            output.Add($"FIELD {f.Name}:{TypeName(f.FieldType)} static={f.IsStatic}");
        foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public).Where(m => !m.IsSpecialName).OrderBy(m => m.Name, StringComparer.Ordinal))
            output.Add("METHOD " + Signature(m));
        output.Add("");
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
