#pragma warning disable CS0618
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Tachie;
using YukkuriMovieMaker.Project;

namespace Ymm4GenericExpressionPresetCapabilityProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL Generic Expression Preset Capability";
    public void SetCulture(CultureInfo cultureInfo) => Bootstrap.Schedule();
}

public sealed class ProbeTool : IToolPlugin
{
    public string Name => "CNWL Generic Expression Preset Capability";
    public Type ViewModelType => typeof(ProbeVm);
    public Type ViewType => typeof(ProbeView);
    public bool AllowMultipleInstances => false;
}

public sealed class ProbeView : UserControl
{
    internal static ProbeView? Current;
    internal StackPanel Host { get; } = new();
    public ProbeView()
    {
        Current = this;
        Host.Children.Add(new TextBlock { Text = "CNWL Generic Expression Preset Capability", Margin = new Thickness(8) });
        Content = new ScrollViewer { Content = Host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}

public sealed class ProbeVm : ITimelineToolViewModel, IToolViewModel
{
    public string Title => "CNWL Generic Expression Preset Capability";
    public bool CanSuspend => false;
    public void SetTimelineToolInfo(TimelineToolInfo info) => Probe.Start();
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
        if (scheduled || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CNWL_GENERIC_PRESET_OUTPUT"))) return;
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
                    if (ticks > 120)
                    {
                        timer.Stop();
                        Probe.Fail("Tool bootstrap timeout");
                    }
                }
                catch (Exception ex)
                {
                    timer.Stop();
                    Probe.Fail(ex.ToString());
                }
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
            if (label.Contains("CNWL Generic Expression Preset Capability", StringComparison.Ordinal))
            {
                if (t.GetProperty("Command")?.GetValue(x) is System.Windows.Input.ICommand c)
                {
                    var p = t.GetProperty("CommandParameter")?.GetValue(x);
                    if (c.CanExecute(p)) { c.Execute(p); return true; }
                }
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

public sealed class SyntheticDirectCharacterParameter : TachieCharacterParameterBase
{
    public string Preset
    {
        get => preset;
        set => Set(ref preset, value);
    }
    private string preset = "//Neutral\npart=a\n//Smile\npart=b\n//Angry\npart=c\n";
}

public sealed class SyntheticDirectFaceParameter : TachieFaceParameterBase
{
    public string Preset
    {
        get => preset;
        set => Set(ref preset, value);
    }
    private string preset = "";
    protected override IEnumerable<IAnimatable> GetAnimatables() => [];
}

public sealed class SyntheticResolvedCharacterParameter : TachieCharacterParameterBase { }

public sealed class SyntheticResolvedFaceParameter : TachieFaceParameterBase
{
    [Display(Name = "Preset")]
    [SyntheticResolvedPresetEditor]
    public object PresetDummy { get; } = new();

    public string Tone
    {
        get => tone;
        set => Set(ref tone, value);
    }
    private string tone = "Calm";

    public int Strength
    {
        get => strength;
        set => Set(ref strength, value);
    }
    private int strength = 1;

    protected override IEnumerable<IAnimatable> GetAnimatables() => [];
}

internal sealed class SyntheticResolvedPresetEditorAttribute : PropertyEditorForTachieParameterAttribute
{
    public override FrameworkElement Create() => new ComboBox
    {
        ItemsSource = new[] { "CNWL_SYNTH_CALM", "CNWL_SYNTH_BOLD" },
        SelectedIndex = 0,
        MinWidth = 120
    };

    public override void SetBindings(FrameworkElement control, object item, object propertyOwner, PropertyInfo propertyInfo)
    {
        var box = (ComboBox)control;
        var owner = (SyntheticResolvedFaceParameter)propertyOwner;
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem?.ToString() == "CNWL_SYNTH_BOLD")
            {
                owner.Tone = "Bold";
                owner.Strength = 9;
            }
            else
            {
                owner.Tone = "Calm";
                owner.Strength = 1;
            }
        };
    }

    public override void ClearBindings(FrameworkElement control) { }
}

public sealed class SyntheticNoiseFaceParameter : TachieFaceParameterBase
{
    public string CompressionPreset
    {
        get => value;
        set => Set(ref this.value, value);
    }
    private string value = "Fast";
    protected override IEnumerable<IAnimatable> GetAnimatables() => [];
}

public sealed class SyntheticBrokenFaceParameter : TachieFaceParameterBase
{
    [Display(Name = "Preset")]
    [SyntheticBrokenPresetEditor]
    public object PresetDummy { get; } = new();
    protected override IEnumerable<IAnimatable> GetAnimatables() => [];
}

internal sealed class SyntheticBrokenPresetEditorAttribute : PropertyEditorForTachieParameterAttribute
{
    public override FrameworkElement Create() => throw new InvalidOperationException("CNWL synthetic broken preset editor");
    public override void SetBindings(FrameworkElement control, object item, object propertyOwner, PropertyInfo propertyInfo) { }
    public override void ClearBindings(FrameworkElement control) { }
}

internal sealed record EditorAttempt(
    string Property,
    string AttributeType,
    bool CharacterParameterAssigned,
    bool CreateSucceeded,
    bool BindSucceeded,
    string ControlType,
    string[] Choices,
    bool SelectionAttempted,
    bool MutationObserved,
    string[] ChangedProperties,
    string? Error);

internal sealed record CapabilityResult(
    string Origin,
    string Name,
    string CharacterParameterType,
    string FaceParameterType,
    string ModuleMvid,
    string[] DirectPresetNames,
    bool DirectApplyAttempted,
    bool DirectMutationObserved,
    string[] DirectChangedProperties,
    int PresetEditorCandidateCount,
    EditorAttempt[] Editors,
    int Score,
    string Grade,
    string[] Errors);

internal static class Probe
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static bool started;
    private static string OutDir => Path.GetFullPath(Environment.GetEnvironmentVariable("CNWL_GENERIC_PRESET_OUTPUT")!);
    private static string FixtureDir => Path.GetFullPath(Environment.GetEnvironmentVariable("CNWL_GENERIC_PRESET_FIXTURE")!);

    public static void Start()
    {
        if (started) return;
        started = true;
        Application.Current.Dispatcher.BeginInvoke(new Action(async () => await RunAsync()));
    }

    public static void Fail(string error)
    {
        try
        {
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "result.json"),
                JsonSerializer.Serialize(new
                {
                    schema = "cnwl.generic-expression-preset-capability.v1",
                    status = "FAIL_EXCEPTION",
                    host = "4.55.1.1 Lite",
                    error
                }, JsonOptions), new UTF8Encoding(false));
        }
        catch { }
    }

    private static async Task RunAsync()
    {
        try
        {
            Directory.CreateDirectory(OutDir);
            var results = new List<CapabilityResult>();

            foreach (var plugin in PluginLoader.TachiePlugins.OrderBy(x => x.GetType().FullName, StringComparer.Ordinal))
            {
                try
                {
                    var cp = plugin.CreateCharacterParameter();
                    var fp = plugin.CreateFaceParameter();
                    ConfigureFixturePaths(cp, fp);
                    results.Add(await ProbeCapabilityAsync("host", plugin.GetType().FullName ?? plugin.Name, cp, fp));
                }
                catch (Exception ex)
                {
                    results.Add(new CapabilityResult(
                        "host", plugin.GetType().FullName ?? plugin.Name, "<create-failed>", "<create-failed>",
                        plugin.GetType().Module.ModuleVersionId.ToString(), [], false, false, [], 0, [], 0, "None",
                        [ex.GetType().Name + ": " + ex.Message]));
                }
            }

            results.Add(await ProbeCapabilityAsync(
                "synthetic", "direct-named",
                new SyntheticDirectCharacterParameter(), new SyntheticDirectFaceParameter()));
            results.Add(await ProbeCapabilityAsync(
                "synthetic", "editor-expanded",
                new SyntheticResolvedCharacterParameter(), new SyntheticResolvedFaceParameter()));
            results.Add(await ProbeCapabilityAsync(
                "synthetic", "noise",
                new SyntheticResolvedCharacterParameter(), new SyntheticNoiseFaceParameter()));
            results.Add(await ProbeCapabilityAsync(
                "synthetic", "broken-editor",
                new SyntheticResolvedCharacterParameter(), new SyntheticBrokenFaceParameter()));

            var direct = results.Single(x => x.Origin == "synthetic" && x.Name == "direct-named");
            var editor = results.Single(x => x.Origin == "synthetic" && x.Name == "editor-expanded");
            var noise = results.Single(x => x.Origin == "synthetic" && x.Name == "noise");
            var broken = results.Single(x => x.Origin == "synthetic" && x.Name == "broken-editor");
            var hostWithEditors = results.Where(x => x.Origin == "host" && x.PresetEditorCandidateCount > 0).ToArray();

            var baseType = typeof(PropertyEditorForTachieParameterAttribute);
            var cpProperty = baseType.GetProperty("CharacterParameter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var loadedTypes = AppDomain.CurrentDomain.GetAssemblies().SelectMany(GetTypesSafe).Distinct().ToArray();
            var modernTachieEditorInterface = loadedTypes.FirstOrDefault(t =>
                t.FullName == "YukkuriMovieMaker.Commons.IPropertyEditorForTachieParameterAttribute");
            var itemPropertyType = loadedTypes.FirstOrDefault(t => t.FullName == "YukkuriMovieMaker.Commons.ItemProperty");
            var propertyEditor2Type = loadedTypes.FirstOrDefault(t => t.FullName == "YukkuriMovieMaker.Commons.PropertyEditorAttribute2");

            var modernContractLines = new List<string>();
            DescribeReflectionType(modernTachieEditorInterface, modernContractLines);
            DescribeReflectionType(propertyEditor2Type, modernContractLines);
            DescribeReflectionType(itemPropertyType, modernContractLines);
            File.WriteAllLines(Path.Combine(OutDir, "modern-editor-contract.txt"), modernContractLines, new UTF8Encoding(false));

            var assertions = new
            {
                propertyEditorBasePublic = baseType.IsPublic || baseType.IsNestedPublic,
                propertyEditorCharacterParameterSettable = cpProperty?.SetMethod != null,
                syntheticDirectStrong = direct.Grade == "Strong" && direct.DirectMutationObserved,
                syntheticEditorStrong = editor.Grade == "Strong" && editor.Editors.Any(x => x.MutationObserved),
                syntheticNoiseRejected = noise.Grade == "None",
                brokenEditorContained = broken.Errors.Length > 0 && results.Count >= 4,
                hostPresetEditorsDiscovered = hostWithEditors.Length,
                hostPresetEditorNames = hostWithEditors.Select(x => x.Name).ToArray()
            };

            var status = assertions.syntheticDirectStrong && assertions.syntheticEditorStrong &&
                         assertions.syntheticNoiseRejected && assertions.brokenEditorContained &&
                         assertions.hostPresetEditorsDiscovered >= 2
                ? "PASS_GENERIC_EXPRESSION_PRESET_CAPABILITY_SURVEY"
                : "FAIL_GENERIC_EXPRESSION_PRESET_CAPABILITY_SURVEY";

            var manifest = new
            {
                schema = "cnwl.generic-expression-preset-capability.v1",
                status,
                host = "4.55.1.1 Lite",
                fixture = new
                {
                    root = FixtureDir,
                    movablePresetIni = Path.Combine(FixtureDir, "animation", "preset.ini"),
                    psd = Path.Combine(FixtureDir, "psd", "1layer.psd"),
                    psdSettings = Path.Combine(FixtureDir, "psd", "1layer-ymm.json")
                },
                editorContract = new
                {
                    legacy = new
                    {
                        type = baseType.FullName,
                        isPublic = baseType.IsPublic || baseType.IsNestedPublic,
                        characterParameterProperty = cpProperty?.ToString(),
                        create = baseType.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public)?.ToString(),
                        setBindings = baseType.GetMethod("SetBindings", BindingFlags.Instance | BindingFlags.Public)?.ToString(),
                        clearBindings = baseType.GetMethod("ClearBindings", BindingFlags.Instance | BindingFlags.Public)?.ToString()
                    },
                    modern = new
                    {
                        interfaceType = modernTachieEditorInterface?.FullName,
                        interfacePublic = modernTachieEditorInterface?.IsPublic == true || modernTachieEditorInterface?.IsNestedPublic == true,
                        itemPropertyType = itemPropertyType?.FullName,
                        itemPropertyPublic = itemPropertyType?.IsPublic == true || itemPropertyType?.IsNestedPublic == true,
                        propertyEditor2Type = propertyEditor2Type?.FullName,
                        propertyEditor2Public = propertyEditor2Type?.IsPublic == true || propertyEditor2Type?.IsNestedPublic == true
                    }
                },
                assertions,
                results
            };

            File.WriteAllText(Path.Combine(OutDir, "result.json"),
                JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));

            var report = new List<string>
            {
                $"status={status}",
                $"host-editor-candidates={hostWithEditors.Length}",
                ""
            };
            foreach (var x in results)
            {
                report.Add($"[{x.Origin}] {x.Name}");
                report.Add($"  character={x.CharacterParameterType}");
                report.Add($"  face={x.FaceParameterType}");
                report.Add($"  mvid={x.ModuleMvid}");
                report.Add($"  direct=[{string.Join(", ", x.DirectPresetNames)}] direct-mutated={x.DirectMutationObserved}");
                report.Add($"  editor-candidates={x.PresetEditorCandidateCount} score={x.Score} grade={x.Grade}");
                foreach (var e in x.Editors)
                {
                    report.Add($"    {e.Property} / {e.AttributeType}");
                    report.Add($"      create={e.CreateSucceeded} bind={e.BindSucceeded} control={e.ControlType}");
                    report.Add($"      choices=[{string.Join(", ", e.Choices)}]");
                    report.Add($"      select={e.SelectionAttempted} mutated={e.MutationObserved} changed=[{string.Join(", ", e.ChangedProperties)}]");
                    if (e.Error != null) report.Add($"      error={e.Error}");
                }
                foreach (var error in x.Errors) report.Add($"  error={error}");
                report.Add("");
            }
            File.WriteAllLines(Path.Combine(OutDir, "report.txt"), report, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Fail(ex.ToString());
        }
    }

    private static void ConfigureFixturePaths(object characterParameter, object faceParameter)
    {
        var animationDir = Path.Combine(FixtureDir, "animation");
        var psdPath = Path.Combine(FixtureDir, "psd", "1layer.psd");

        foreach (var p in characterParameter.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!p.CanWrite || p.PropertyType != typeof(string)) continue;
            try
            {
                if (p.Name.Contains("Directory", StringComparison.OrdinalIgnoreCase))
                    p.SetValue(characterParameter, animationDir);
                else if (p.Name.Contains("FilePath", StringComparison.OrdinalIgnoreCase))
                    p.SetValue(characterParameter, psdPath);
            }
            catch { }
        }

        foreach (var target in faceParameter.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!target.CanWrite || target.PropertyType != typeof(string)) continue;
            if (!target.Name.Contains("File", StringComparison.OrdinalIgnoreCase) &&
                !target.Name.Contains("Directory", StringComparison.OrdinalIgnoreCase)) continue;
            var source = characterParameter.GetType().GetProperty(target.Name, BindingFlags.Instance | BindingFlags.Public);
            if (source?.CanRead != true || source.PropertyType != typeof(string)) continue;
            try { target.SetValue(faceParameter, source.GetValue(characterParameter)); } catch { }
        }
    }

    private static async Task<CapabilityResult> ProbeCapabilityAsync(string origin, string name, object characterParameter, object faceParameter)
    {
        var errors = new List<string>();
        var directNames = DiscoverDirectPresetNames(characterParameter, faceParameter);
        var directAttempted = false;
        var directMutated = false;
        string[] directChanged = [];

        var directProperty = faceParameter.GetType().GetProperty("Preset", BindingFlags.Instance | BindingFlags.Public);
        if (directProperty?.CanWrite == true && directProperty.PropertyType == typeof(string) && directNames.Count > 0)
        {
            directAttempted = true;
            try
            {
                var before = Snapshot(faceParameter);
                var target = directNames.Count > 1 ? directNames[1] : directNames[0];
                directProperty.SetValue(faceParameter, target);
                var after = Snapshot(faceParameter);
                directChanged = Diff(before, after);
                directMutated = directChanged.Length > 0 && Equals(directProperty.GetValue(faceParameter), target);
            }
            catch (Exception ex) { errors.Add("direct: " + ex.GetType().Name + ": " + ex.Message); }
        }

        var editorAttempts = new List<EditorAttempt>();
        foreach (var p in faceParameter.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            object[] attrs;
            try { attrs = p.GetCustomAttributes(true); }
            catch (Exception ex)
            {
                errors.Add($"attributes {p.Name}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            var display = p.GetCustomAttributes(typeof(DisplayAttribute), true).OfType<DisplayAttribute>().FirstOrDefault()?.Name ?? "";
            foreach (var editor in attrs)
            {
                if (!LooksLikePresetEditor(editor, p, display)) continue;
                try
                {
                    editorAttempts.Add(await TryEditorAsync(characterParameter, faceParameter, p, editor));
                }
                catch (Exception ex)
                {
                    editorAttempts.Add(new EditorAttempt(
                        p.Name, editor.GetType().FullName ?? editor.GetType().Name, false, false, false, "", [],
                        false, false, [], ex.GetType().Name + ": " + ex.Message));
                    errors.Add($"editor {p.Name}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            if ((ContainsPresetToken(p.Name) || ContainsPresetToken(display)) && editorAttempts.Count == 0)
            {
                var attrNames = attrs.Select(x => x.GetType().FullName ?? x.GetType().Name).ToArray();
                if (attrNames.Length > 0)
                    errors.Add($"preset-property-attributes {p.Name}: [{string.Join(",", attrNames)}]");
            }
        }

        foreach (var e in editorAttempts.Where(x => x.Error != null))
            errors.Add($"editor-contained {e.Property}: {e.Error}");

        var score = 0;
        if (directProperty?.CanWrite == true && directProperty.PropertyType == typeof(string)) score += 4;
        if (directNames.Count > 0) score += 2;
        if (directMutated) score += 4;
        if (editorAttempts.Count > 0) score += 3;
        if (editorAttempts.Any(x => x.Choices.Length > 0)) score += 2;
        if (editorAttempts.Any(x => x.MutationObserved)) score += 4;

        var grade = score >= 8 ? "Strong" : score >= 3 ? "Medium" : "None";

        return new CapabilityResult(
            origin,
            name,
            characterParameter.GetType().FullName ?? characterParameter.GetType().Name,
            faceParameter.GetType().FullName ?? faceParameter.GetType().Name,
            faceParameter.GetType().Module.ModuleVersionId.ToString(),
            directNames.ToArray(),
            directAttempted,
            directMutated,
            directChanged,
            editorAttempts.Count,
            editorAttempts.ToArray(),
            score,
            grade,
            errors.Distinct().ToArray());
    }

    private static List<string> DiscoverDirectPresetNames(object characterParameter, object faceParameter)
    {
        var result = new List<string>();
        var facePreset = faceParameter.GetType().GetProperty("Preset", BindingFlags.Instance | BindingFlags.Public);
        if (facePreset?.CanWrite != true || facePreset.PropertyType != typeof(string)) return result;

        foreach (var p in characterParameter.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!p.CanRead || p.PropertyType != typeof(string) || !ContainsPresetToken(p.Name)) continue;
            string? text;
            try { text = p.GetValue(characterParameter) as string; } catch { continue; }
            if (string.IsNullOrWhiteSpace(text)) continue;

            foreach (Match m in Regex.Matches(text, @"(?m)^\s*//\s*(?<name>[^\r\n]+?)\s*$"))
                AddName(m.Groups["name"].Value);
            foreach (Match m in Regex.Matches(text, @"(?m)^\s*\[(?<name>[^\]\r\n]+)\]\s*$"))
                AddName(m.Groups["name"].Value);
        }
        return result;

        void AddName(string value)
        {
            value = value.Trim();
            if (value.Length > 0 && !result.Contains(value, StringComparer.Ordinal)) result.Add(value);
        }
    }

    private static async Task<EditorAttempt> TryEditorAsync(
        object characterParameter,
        object faceParameter,
        PropertyInfo property,
        object editor)
    {
        var attrType = editor.GetType();
        var cpAssigned = false;
        var created = false;
        var bound = false;
        FrameworkElement? control = null;
        string? error = null;
        var choices = new List<string>();
        var attempted = false;
        var mutated = false;
        string[] changed = [];

        try
        {
            var cpProp = attrType.GetProperty("CharacterParameter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (cpProp?.SetMethod != null)
            {
                cpProp.SetValue(editor, characterParameter);
                cpAssigned = true;
            }

            var create = attrType.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                ?? throw new MissingMethodException(attrType.FullName, "Create()");
            control = create.Invoke(editor, null) as FrameworkElement
                ?? throw new InvalidOperationException("Preset editor Create() did not return FrameworkElement.");
            created = true;
            control.Opacity = 0.01;
            control.IsHitTestVisible = false;
            control.Margin = new Thickness(1);
            control.MinWidth = 120;
            ProbeView.Current?.Host.Children.Add(control);

            var setBindings = attrType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == "SetBindings").ToArray();
            var legacy = setBindings.FirstOrDefault(m =>
            {
                var ps = m.GetParameters();
                return ps.Length == 4 && typeof(FrameworkElement).IsAssignableFrom(ps[0].ParameterType) &&
                       ps[3].ParameterType == typeof(PropertyInfo);
            });
            if (legacy != null)
            {
                legacy.Invoke(editor, [control, faceParameter, faceParameter, property]);
                bound = true;
            }
            else
            {
                var modern = setBindings.FirstOrDefault(m =>
                {
                    var ps = m.GetParameters();
                    return ps.Length == 2 && typeof(FrameworkElement).IsAssignableFrom(ps[0].ParameterType) && ps[1].ParameterType.IsArray;
                });
                if (modern == null)
                    throw new MissingMethodException(attrType.FullName, "SetBindings");

                throw new NotSupportedException("Modern ItemProperty[] binding contract discovered; object construction is a separate compatibility route.");
            }

            await SettleAsync(control);

            var selectors = Descendants(control).OfType<Selector>().Distinct().ToArray();
            var contexts = Descendants(control).OfType<FrameworkElement>()
                .Select(x => x.DataContext).Where(x => x != null).Distinct(ReferenceEqualityComparer.Instance).Cast<object>().ToArray();

            foreach (var selector in selectors)
                foreach (var item in selector.Items.Cast<object?>())
                    AddChoice(Label(item));

            foreach (var ctx in contexts)
                foreach (var item in EnumerateContextChoices(ctx))
                    AddChoice(Label(item));

            var before = Snapshot(faceParameter);

            foreach (var selector in selectors)
            {
                var target = selector.Items.Cast<object?>()
                    .FirstOrDefault(x => !ReferenceEquals(x, selector.SelectedItem) && Label(x).StartsWith("CNWL_", StringComparison.Ordinal));
                if (target == null && selector.Items.Count > 1)
                    target = selector.Items.Cast<object?>().FirstOrDefault(x => !ReferenceEquals(x, selector.SelectedItem));
                if (target == null) continue;

                attempted = true;
                selector.SelectedItem = target;
                await SettleAsync(control);
                changed = Diff(before, Snapshot(faceParameter));
                if (changed.Length > 0) { mutated = true; break; }
            }

            if (!mutated)
            {
                foreach (var ctx in contexts)
                {
                    var selected = TrySelectContextChoice(ctx);
                    if (!selected) continue;
                    attempted = true;
                    await SettleAsync(control);
                    changed = Diff(before, Snapshot(faceParameter));
                    if (changed.Length > 0) { mutated = true; break; }
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
        }
        finally
        {
            if (control != null)
            {
                try
                {
                    var clear = attrType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m => m.Name == "ClearBindings" && m.GetParameters().Length == 1);
                    clear?.Invoke(editor, [control]);
                }
                catch { }
                try { ProbeView.Current?.Host.Children.Remove(control); } catch { }
            }
        }

        return new EditorAttempt(
            property.Name,
            attrType.FullName ?? attrType.Name,
            cpAssigned,
            created,
            bound,
            control?.GetType().FullName ?? "",
            choices.ToArray(),
            attempted,
            mutated,
            changed,
            error);

        void AddChoice(string value)
        {
            value = value.Trim();
            if (value.Length > 0 && !choices.Contains(value, StringComparer.Ordinal)) choices.Add(value);
        }
    }

    private static async Task SettleAsync(FrameworkElement control)
    {
        try
        {
            control.ApplyTemplate();
            control.Measure(new Size(600, 400));
            control.Arrange(new Rect(0, 0, 600, 400));
            control.UpdateLayout();
        }
        catch { }
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        await Task.Delay(150);
        await Dispatcher.Yield(DispatcherPriority.Background);
    }

    private static IEnumerable<object?> EnumerateContextChoices(object context)
    {
        foreach (var p in context.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
            if (!ContainsPresetToken(p.Name) && !p.Name.Contains("Items", StringComparison.OrdinalIgnoreCase)) continue;
            object? value;
            try { value = p.GetValue(context); } catch { continue; }
            if (value is string || value is not IEnumerable items) continue;
            foreach (var item in items) yield return item;
        }
    }

    private static bool TrySelectContextChoice(object context)
    {
        var t = context.GetType();
        var collectionProps = t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 &&
                        (ContainsPresetToken(p.Name) || p.Name.Contains("Items", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var selectionProps = t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanWrite && (p.Name.Contains("Selected", StringComparison.OrdinalIgnoreCase) ||
                                      p.Name.Contains("CurrentPreset", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        foreach (var cp in collectionProps)
        {
            object? value;
            try { value = cp.GetValue(context); } catch { continue; }
            if (value is string || value is not IEnumerable items) continue;
            var list = items.Cast<object?>().Where(x => x != null).Cast<object>().ToArray();
            foreach (var sp in selectionProps)
            {
                object? current = null;
                try { if (sp.CanRead) current = sp.GetValue(context); } catch { }
                var target = list.FirstOrDefault(x => !ReferenceEquals(x, current) && Label(x).StartsWith("CNWL_", StringComparison.Ordinal))
                    ?? list.FirstOrDefault(x => !ReferenceEquals(x, current))
                    ?? list.FirstOrDefault();
                if (target == null || !sp.PropertyType.IsAssignableFrom(target.GetType())) continue;
                try
                {
                    sp.SetValue(context, target);
                    return true;
                }
                catch { }
            }
        }
        return false;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current)) continue;
            yield return current;

            try
            {
                var count = VisualTreeHelper.GetChildrenCount(current);
                for (var i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(current, i);
                    if (child != null) stack.Push(child);
                }
            }
            catch { }

            try
            {
                foreach (var child in LogicalTreeHelper.GetChildren(current))
                    if (child is DependencyObject d) stack.Push(d);
            }
            catch { }
        }
    }

    private static string Label(object? value)
    {
        if (value == null) return "";
        if (value is string s) return s;
        var t = value.GetType();
        foreach (var name in new[] { "Display", "Name", "Label", "Content", "Text" })
        {
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (p?.CanRead != true || p.GetIndexParameters().Length != 0) continue;
            try
            {
                var v = p.GetValue(value)?.ToString();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            catch { }
        }
        var preset = t.GetProperty("Preset", BindingFlags.Instance | BindingFlags.Public);
        if (preset?.CanRead == true)
        {
            try
            {
                var p = preset.GetValue(value);
                if (p != null)
                {
                    var n = p.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public)?.GetValue(p)?.ToString();
                    if (!string.IsNullOrWhiteSpace(n)) return n;
                }
            }
            catch { }
        }
        return value.ToString() ?? "";
    }

    private static Dictionary<string, string> Snapshot(object value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
            try { result[p.Name] = Render(p.GetValue(value)); }
            catch (Exception ex) { result[p.Name] = "<throw:" + ex.GetType().Name + ">"; }
        }
        return result;
    }

    private static string Render(object? value)
    {
        if (value == null) return "<null>";
        if (value is string s) return s;
        var t = value.GetType();
        if (t.IsPrimitive || t.IsEnum || value is decimal || value is Guid) return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (value is IEnumerable<string> strings) return "[" + string.Join("|", strings.Take(64)) + "]";
        if (value is IEnumerable items)
        {
            var labels = new List<string>();
            foreach (var item in items)
            {
                labels.Add(Label(item));
                if (labels.Count >= 64) break;
            }
            return "[" + string.Join("|", labels) + "]";
        }
        return value.ToString() ?? t.FullName ?? t.Name;
    }

    private static string[] Diff(Dictionary<string, string> before, Dictionary<string, string> after) =>
        before.Keys.Union(after.Keys, StringComparer.Ordinal)
            .Where(k => !before.TryGetValue(k, out var a) || !after.TryGetValue(k, out var b) || a != b)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<Type> GetTypesSafe(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>(); }
        catch { return []; }
    }

    private static void DescribeReflectionType(Type? type, List<string> output)
    {
        if (type == null)
        {
            output.Add("<missing>");
            output.Add("");
            return;
        }

        output.Add($"=== {type.FullName} public={type.IsPublic || type.IsNestedPublic} interface={type.IsInterface} ===");
        foreach (var c in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderBy(x => x.ToString(), StringComparer.Ordinal))
            output.Add($"CTOR public={c.IsPublic} {c}");
        foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderBy(x => x.Name, StringComparer.Ordinal))
            output.Add($"PROPERTY {p.Name}:{p.PropertyType.FullName} getPublic={p.GetMethod?.IsPublic == true} setPublic={p.SetMethod?.IsPublic == true}");
        foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(x => !x.IsSpecialName).OrderBy(x => x.Name, StringComparer.Ordinal))
            output.Add($"METHOD public={m.IsPublic} {m}");
        output.Add("");
    }

    private static bool LooksLikePresetEditor(object attribute, PropertyInfo property, string displayName)
    {
        var t = attribute.GetType();
        if (!ContainsPresetToken(property.Name) && !ContainsPresetToken(displayName) && !ContainsPresetToken(t.Name))
            return false;

        var create = t.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        var sets = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(m => m.Name == "SetBindings").ToArray();
        var clear = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Any(m => m.Name == "ClearBindings");
        var tachieInterface = t.GetInterfaces().Any(i =>
            i.FullName == "YukkuriMovieMaker.Commons.IPropertyEditorForTachieParameterAttribute");
        var legacy = typeof(PropertyEditorForTachieParameterAttribute).IsAssignableFrom(t);

        return create != null && sets.Length > 0 && clear && (tachieInterface || legacy || t.Name.Contains("Preset", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsPresetToken(string value) =>
        value.Contains("Preset", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("プリセット", StringComparison.OrdinalIgnoreCase);
}
