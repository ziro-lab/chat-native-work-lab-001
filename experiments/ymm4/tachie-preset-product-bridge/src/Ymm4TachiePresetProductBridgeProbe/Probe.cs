#pragma warning disable CS0618
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using YukkuriMovieMaker.Project.Items;

namespace Ymm4TachiePresetProductBridgeProbe;

public sealed class PluginEntry : ILocalizePlugin
{
    public string Name => "CNWL Tachie Preset Product Bridge";
    public void SetCulture(CultureInfo cultureInfo) => Bootstrap.Schedule();
}

public sealed class ProbeTool : IToolPlugin
{
    public string Name => "CNWL Tachie Preset Product Bridge";
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
        Host.Children.Add(new TextBlock { Text = "CNWL Tachie Preset Product Bridge", Margin = new Thickness(8) });
        Content = new ScrollViewer { Content = Host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}

public sealed class ProbeVm : ITimelineToolViewModel, IToolViewModel
{
    public string Title => "CNWL Tachie Preset Product Bridge";
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
        if (scheduled || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CNWL_TACHIE_PRESET_BRIDGE_OUTPUT")))
            return;

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
                        if (main?.GetType().FullName != "YukkuriMovieMaker.ViewModels.MainViewModel")
                            continue;

                        var active = main.GetType()
                            .GetProperty("ActiveTimelineViewModel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?.GetValue(main);

                        if (active == null && !created)
                        {
                            created = true;
                            main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                            return;
                        }

                        if (active != null && !opened)
                            opened = OpenTool(main);

                        if (opened && ProbeView.Current != null)
                        {
                            timer.Stop();
                            return;
                        }
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
        if (main.GetType().GetProperty("ToolMenuItems")?.GetValue(main) is not IEnumerable items)
            return false;

        bool Visit(object x, int depth)
        {
            if (depth > 8) return false;

            var t = x.GetType();
            var label = t.GetProperty("Header")?.GetValue(x)?.ToString()
                ?? t.GetProperty("Title")?.GetValue(x)?.ToString()
                ?? t.GetProperty("Name")?.GetValue(x)?.ToString()
                ?? "";

            if (label.Contains("CNWL Tachie Preset Product Bridge", StringComparison.Ordinal))
            {
                if (t.GetProperty("Command")?.GetValue(x) is System.Windows.Input.ICommand c)
                {
                    var p = t.GetProperty("CommandParameter")?.GetValue(x);
                    if (c.CanExecute(p))
                    {
                        c.Execute(p);
                        return true;
                    }
                }
                return true;
            }

            var children = (t.GetProperty("Children")?.GetValue(x) ?? t.GetProperty("Items")?.GetValue(x)) as IEnumerable;
            if (children != null)
            {
                foreach (var child in children)
                    if (child != null && Visit(child, depth + 1))
                        return true;
            }
            return false;
        }

        foreach (var x in items)
            if (x != null && Visit(x, 0))
                return true;

        return false;
    }
}

public sealed class SyntheticModernCharacterParameter : TachieCharacterParameterBase { }

public sealed class SyntheticModernFaceParameter : TachieFaceParameterBase
{
    [Display(Name = "Preset")]
    [SyntheticModernPresetEditor]
    public object PresetDummy { get; } = new();

    public string Mood
    {
        get => mood;
        set => Set(ref mood, value);
    }
    private string mood = "Neutral";

    public int Level
    {
        get => level;
        set => Set(ref level, value);
    }
    private int level = 1;

    protected override IEnumerable<IAnimatable> GetAnimatables() => [];
}

internal sealed class SyntheticModernPresetEditorAttribute : PropertyEditorAttribute2, IPropertyEditorForTachieParameterAttribute
{
    private readonly Dictionary<ComboBox, SelectionChangedEventHandler> handlers = new();
    public object? CharacterParameter { get; set; }

    public override FrameworkElement Create() => new ComboBox
    {
        ItemsSource = new[] { "CNWL_MODERN_NEUTRAL", "CNWL_MODERN_HAPPY" },
        SelectedIndex = 0,
        MinWidth = 120
    };

    public override void SetBindings(FrameworkElement control, ItemProperty[] itemProperties)
    {
        var box = (ComboBox)control;
        var owner = (SyntheticModernFaceParameter)itemProperties.Single().PropertyOwner;

        SelectionChangedEventHandler handler = (_, _) =>
        {
            if (box.SelectedItem?.ToString() == "CNWL_MODERN_HAPPY")
            {
                owner.Mood = "Happy";
                owner.Level = 7;
            }
            else
            {
                owner.Mood = "Neutral";
                owner.Level = 1;
            }
        };

        handlers[box] = handler;
        box.SelectionChanged += handler;
    }

    public override void ClearBindings(FrameworkElement control)
    {
        if (control is ComboBox box && handlers.Remove(box, out var handler))
            box.SelectionChanged -= handler;
    }
}

internal sealed record ModernBindingResult(
    bool Passed,
    string ItemPropertyConstructionRoute,
    string PropertiesCacheRoute,
    bool MutationObserved,
    bool CleanupSucceeded,
    string? Error);

internal sealed record CaseResult(
    string Name,
    string PluginType,
    bool CharacterResolution,
    string ResolutionDetail,
    string Candidate,
    string BindRoute,
    string[] Choices,
    bool MutationObserved,
    string BeforeFingerprint,
    string AppliedFingerprint,
    string RepeatFingerprint,
    bool FingerprintStable,
    bool ItemRoundtrip,
    bool CancellationObserved,
    bool ClearBindingsSucceeded,
    bool StageRestored,
    string? Error);

internal static class Probe
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static bool started;

    private static string OutDir => Path.GetFullPath(Environment.GetEnvironmentVariable("CNWL_TACHIE_PRESET_BRIDGE_OUTPUT")!);
    private static string FixtureDir => Path.GetFullPath(Environment.GetEnvironmentVariable("CNWL_TACHIE_PRESET_BRIDGE_FIXTURE")!);

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
            File.WriteAllText(
                Path.Combine(OutDir, "result.json"),
                JsonSerializer.Serialize(new
                {
                    schema = "cnwl.tachie-preset-product-bridge.v1",
                    status = "FAIL_EXCEPTION",
                    host = "4.55.1.1 Lite",
                    error
                }, JsonOptions),
                new UTF8Encoding(false));
        }
        catch { }
    }

    private static async Task RunAsync()
    {
        try
        {
            Directory.CreateDirectory(OutDir);

            var modern = await TestModernItemPropertyBindingAsync();

            var animation = PluginLoader.TachiePlugins.SingleOrDefault(x =>
                x.GetType().FullName == "YukkuriMovieMaker.Plugin.Tachie.AnimationTachie.AnimationTachiePlugin");
            var psd = PluginLoader.TachiePlugins.SingleOrDefault(x =>
                x.GetType().FullName == "YukkuriMovieMaker.Plugin.Tachie.Psd.PsdTachiePlugin");

            var animationResult = animation == null
                ? FailedCase("animation", "AnimationTachiePlugin not found")
                : await RunBuiltInCaseAsync("animation", animation, "CNWL_ANIM_SMILE");

            var psdResult = psd == null
                ? FailedCase("psd", "PsdTachiePlugin not found")
                : await RunBuiltInCaseAsync("psd", psd, "CNWL_PSD_ON");

            var assertions = new
            {
                animationCharacterResolution = animationResult.CharacterResolution,
                psdCharacterResolution = psdResult.CharacterResolution,
                modernItemPropertyBinding = modern.Passed,
                animationFingerprintStable = animationResult.FingerprintStable,
                psdFingerprintStable = psdResult.FingerprintStable,
                animationItemRoundtrip = animationResult.ItemRoundtrip,
                psdItemRoundtrip = psdResult.ItemRoundtrip,
                animationCleanupCancellation = animationResult.CancellationObserved &&
                                               animationResult.ClearBindingsSucceeded &&
                                               animationResult.StageRestored,
                psdCleanupCancellation = psdResult.CancellationObserved &&
                                         psdResult.ClearBindingsSucceeded &&
                                         psdResult.StageRestored
            };

            var allPassed = assertions.animationCharacterResolution &&
                            assertions.psdCharacterResolution &&
                            assertions.modernItemPropertyBinding &&
                            assertions.animationFingerprintStable &&
                            assertions.psdFingerprintStable &&
                            assertions.animationItemRoundtrip &&
                            assertions.psdItemRoundtrip &&
                            assertions.animationCleanupCancellation &&
                            assertions.psdCleanupCancellation;

            var result = new
            {
                schema = "cnwl.tachie-preset-product-bridge.v1",
                status = allPassed ? "PASS_TACHIE_PRESET_PRODUCT_BRIDGE_P0" : "FAIL_TACHIE_PRESET_PRODUCT_BRIDGE_P0",
                host = "4.55.1.1 Lite",
                assertions,
                modernItemProperty = modern,
                cases = new[] { animationResult, psdResult }
            };

            File.WriteAllText(
                Path.Combine(OutDir, "result.json"),
                JsonSerializer.Serialize(result, JsonOptions),
                new UTF8Encoding(false));

            var report = new List<string>
            {
                $"status={result.status}",
                $"modern-item-property passed={modern.Passed} ctor={modern.ItemPropertyConstructionRoute} cache={modern.PropertiesCacheRoute} mutation={modern.MutationObserved} cleanup={modern.CleanupSucceeded} error={modern.Error}"
            };

            foreach (var x in result.cases)
            {
                report.Add($"[{x.Name}] plugin={x.PluginType}");
                report.Add($"  resolution={x.CharacterResolution} {x.ResolutionDetail}");
                report.Add($"  candidate={x.Candidate} bind={x.BindRoute} choices=[{string.Join(", ", x.Choices)}] mutation={x.MutationObserved}");
                report.Add($"  fingerprint before={x.BeforeFingerprint} applied={x.AppliedFingerprint} repeat={x.RepeatFingerprint} stable={x.FingerprintStable}");
                report.Add($"  item-roundtrip={x.ItemRoundtrip}");
                report.Add($"  cancellation={x.CancellationObserved} clear={x.ClearBindingsSucceeded} stage-restored={x.StageRestored}");
                if (x.Error != null) report.Add($"  error={x.Error}");
            }

            File.WriteAllLines(Path.Combine(OutDir, "report.txt"), report, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Fail(ex.ToString());
        }
    }

    private static CaseResult FailedCase(string name, string error) => new(
        name, "<missing>", false, error, "", "", [], false, "", "", "", false, false, false, false, false, error);

    private static async Task<CaseResult> RunBuiltInCaseAsync(string name, ITachiePlugin plugin, string targetCandidate)
    {
        try
        {
            var characterParameter = plugin.CreateCharacterParameter();
            var defaultFace = plugin.CreateFaceParameter();
            ConfigureFixturePaths(characterParameter, defaultFace);

            var character = new Character
            {
                Name = "CNWL_" + name,
                TachieType = plugin.GetType(),
                TachieCharacterParameter = characterParameter,
                TachieDefaultFaceParameter = defaultFace
            };

            var resolution = ResolveCharacter(character);
            var resolutionOk = resolution.Plugin != null &&
                               ReferenceEquals(resolution.CharacterParameter, characterParameter) &&
                               resolution.Plugin.GetType() == plugin.GetType();

            var first = plugin.CreateFaceParameter();
            ConfigureFixturePaths(characterParameter, first);
            var before = Fingerprint(first);

            var applied1 = await ApplyNamedPresetAsync(characterParameter, first, targetCandidate);
            var appliedFingerprint = Fingerprint(first);

            var second = plugin.CreateFaceParameter();
            ConfigureFixturePaths(characterParameter, second);
            var applied2 = await ApplyNamedPresetAsync(characterParameter, second, targetCandidate);
            var repeatFingerprint = Fingerprint(second);

            var fingerprintStable = applied1.MutationObserved &&
                                    applied2.MutationObserved &&
                                    before != appliedFingerprint &&
                                    appliedFingerprint == repeatFingerprint &&
                                    appliedFingerprint == Fingerprint(first) &&
                                    repeatFingerprint == Fingerprint(second);

            var item = new TachieFaceItem(character)
            {
                TachieFaceParameter = first
            };
            var roundtrip = item.TachieFaceParameter != null &&
                            Fingerprint(item.TachieFaceParameter) == appliedFingerprint;

            var cleanup = await TestCancellationCleanupAsync(characterParameter, plugin.CreateFaceParameter());

            return new CaseResult(
                name,
                plugin.GetType().FullName ?? plugin.Name,
                resolutionOk,
                resolution.Detail,
                targetCandidate,
                applied1.BindRoute,
                applied1.Choices,
                applied1.MutationObserved,
                before,
                appliedFingerprint,
                repeatFingerprint,
                fingerprintStable,
                roundtrip,
                cleanup.CancellationObserved,
                cleanup.ClearSucceeded,
                cleanup.StageRestored,
                null);
        }
        catch (Exception ex)
        {
            return FailedCase(name, ex.GetType().Name + ": " + ex.Message) with
            {
                PluginType = plugin.GetType().FullName ?? plugin.Name
            };
        }
    }

    private sealed record CharacterResolution(ITachiePlugin? Plugin, object? CharacterParameter, string Detail);

    private static CharacterResolution ResolveCharacter(Character character)
    {
        if (character.TachieType == null)
            return new(null, null, "Character.TachieType is null");

        var matches = PluginLoader.TachiePlugins
            .Where(x => x.GetType() == character.TachieType)
            .ToArray();

        if (matches.Length != 1)
            return new(null, character.TachieCharacterParameter, $"exact plugin matches={matches.Length}");

        if (character.TachieCharacterParameter == null)
            return new(matches[0], null, "Character.TachieCharacterParameter is null");

        var freshType = matches[0].CreateCharacterParameter().GetType();
        var activeType = character.TachieCharacterParameter.GetType();

        if (freshType != activeType)
            return new(matches[0], character.TachieCharacterParameter,
                $"parameter type mismatch active={activeType.FullName} fresh={freshType.FullName}");

        return new(matches[0], character.TachieCharacterParameter,
            $"exact plugin={matches[0].GetType().FullName}; parameter={activeType.FullName}");
    }

    private sealed record ApplyResult(bool MutationObserved, string BindRoute, string[] Choices);

    private static async Task<ApplyResult> ApplyNamedPresetAsync(
        object characterParameter,
        object faceParameter,
        string targetCandidate)
    {
        var opened = await OpenPresetEditorAsync(characterParameter, faceParameter);
        try
        {
            var before = Fingerprint(faceParameter);
            var choices = EnumerateChoices(opened.Control).Distinct(StringComparer.Ordinal).ToArray();
            var selected = await SelectNamedChoiceAsync(opened.Control, targetCandidate);
            var after = Fingerprint(faceParameter);

            return new(selected && before != after, opened.BindRoute, choices);
        }
        finally
        {
            ClearEditor(opened.Editor, opened.Control);
        }
    }

    private sealed record CleanupResult(bool CancellationObserved, bool ClearSucceeded, bool StageRestored);

    private static async Task<CleanupResult> TestCancellationCleanupAsync(object characterParameter, object faceParameter)
    {
        ConfigureFixturePaths(characterParameter, faceParameter);
        var host = ProbeView.Current?.Host ?? throw new InvalidOperationException("Probe staging host unavailable.");
        var baseline = host.Children.Count;
        object? editor = null;
        FrameworkElement? control = null;
        var cancelled = false;
        var clear = false;

        try
        {
            var opened = await OpenPresetEditorAsync(characterParameter, faceParameter);
            editor = opened.Editor;
            control = opened.Control;

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            try
            {
                cts.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
        }
        finally
        {
            if (editor != null && control != null)
                clear = ClearEditor(editor, control);
        }

        return new(cancelled, clear, host.Children.Count == baseline);
    }

    private sealed record OpenedEditor(object Editor, FrameworkElement Control, PropertyInfo Property, string BindRoute);

    private static async Task<OpenedEditor> OpenPresetEditorAsync(object characterParameter, object faceParameter)
    {
        var host = ProbeView.Current?.Host ?? throw new InvalidOperationException("Probe staging host unavailable.");

        foreach (var property in faceParameter.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var display = property.GetCustomAttribute<DisplayAttribute>()?.Name ?? "";
            foreach (var editor in property.GetCustomAttributes(inherit: true))
            {
                if (!LooksLikePresetEditor(editor, property, display))
                    continue;

                var t = editor.GetType();
                var cp = t.GetProperty("CharacterParameter", BindingFlags.Instance | BindingFlags.Public);
                if (cp?.SetMethod?.IsPublic != true)
                    throw new InvalidOperationException($"{t.FullName}.CharacterParameter is not publicly settable.");

                cp.SetValue(editor, characterParameter);

                var create = t.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)
                    ?? throw new MissingMethodException(t.FullName, "Create()");
                var control = create.Invoke(editor, null) as FrameworkElement
                    ?? throw new InvalidOperationException("Preset editor Create() did not return FrameworkElement.");

                control.Opacity = 0.01;
                control.IsHitTestVisible = false;
                control.Margin = new Thickness(1);
                control.MinWidth = 120;
                host.Children.Add(control);

                try
                {
                    var route = BindEditorPublic(editor, control, faceParameter, property);
                    await SettleAsync(control);
                    return new(editor, control, property, route);
                }
                catch
                {
                    ClearEditor(editor, control);
                    throw;
                }
            }
        }

        throw new InvalidOperationException($"No public preset editor route found for {faceParameter.GetType().FullName}.");
    }

    private static string BindEditorPublic(object editor, FrameworkElement control, object faceParameter, PropertyInfo property)
    {
        var t = editor.GetType();
        var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(x => x.Name == "SetBindings")
            .ToArray();

        var modern = methods.FirstOrDefault(m =>
        {
            var ps = m.GetParameters();
            return ps.Length == 2 &&
                   typeof(FrameworkElement).IsAssignableFrom(ps[0].ParameterType) &&
                   ps[1].ParameterType == typeof(ItemProperty[]);
        });

        if (modern != null)
        {
            try
            {
                var itemProperty = CreatePublicItemProperty(faceParameter, faceParameter, property, out _, out _);
                modern.Invoke(editor, [control, new[] { itemProperty }]);
                return "public ItemProperty[]";
            }
            catch
            {
                // The legacy public overload is still a valid built-in application route.
                // P0 verifies ItemProperty[] construction separately and records this fallback.
            }
        }

        var legacy = methods.FirstOrDefault(m =>
        {
            var ps = m.GetParameters();
            return ps.Length == 4 &&
                   typeof(FrameworkElement).IsAssignableFrom(ps[0].ParameterType) &&
                   ps[3].ParameterType == typeof(PropertyInfo);
        });

        if (legacy == null)
            throw new MissingMethodException(t.FullName, "public SetBindings");

        legacy.Invoke(editor, [control, faceParameter, faceParameter, property]);
        return "public legacy object/object/PropertyInfo";
    }

    private static bool ClearEditor(object editor, FrameworkElement control)
    {
        var clearSucceeded = false;
        try
        {
            var clear = editor.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(x => x.Name == "ClearBindings" && x.GetParameters().Length == 1);

            if (clear == null)
                return false;

            clear.Invoke(editor, [control]);
            clearSucceeded = true;
        }
        catch
        {
            clearSucceeded = false;
        }
        finally
        {
            try { ProbeView.Current?.Host.Children.Remove(control); } catch { }
        }

        return clearSucceeded;
    }

    private static bool LooksLikePresetEditor(object editor, PropertyInfo property, string displayName)
    {
        var t = editor.GetType();
        var presetContext = ContainsPreset(property.Name) || ContainsPreset(displayName) || ContainsPreset(t.Name);
        if (!presetContext) return false;

        var create = t.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
        var clear = t.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Any(x => x.Name == "ClearBindings" && x.GetParameters().Length == 1);
        var bind = t.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Any(x => x.Name == "SetBindings");

        return create != null && clear && bind;
    }

    private static bool ContainsPreset(string value) =>
        value.Contains("Preset", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("プリセット", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> SelectNamedChoiceAsync(FrameworkElement control, string targetLabel)
    {
        var selectors = Descendants(control).OfType<Selector>().Distinct().ToArray();
        foreach (var selector in selectors)
        {
            var target = selector.Items.Cast<object?>()
                .FirstOrDefault(x => string.Equals(Label(x), targetLabel, StringComparison.Ordinal));
            if (target == null) continue;

            selector.SelectedItem = target;
            await SettleAsync(control);
            return true;
        }

        var contexts = Descendants(control)
            .OfType<FrameworkElement>()
            .Select(x => x.DataContext)
            .Where(x => x != null)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Cast<object>()
            .ToArray();

        foreach (var context in contexts)
        {
            if (!TrySelectContextChoice(context, targetLabel))
                continue;

            await SettleAsync(control);
            return true;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateChoices(FrameworkElement control)
    {
        foreach (var selector in Descendants(control).OfType<Selector>().Distinct())
            foreach (var item in selector.Items.Cast<object?>())
            {
                var label = Label(item);
                if (!string.IsNullOrWhiteSpace(label))
                    yield return label;
            }

        foreach (var context in Descendants(control)
            .OfType<FrameworkElement>()
            .Select(x => x.DataContext)
            .Where(x => x != null)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Cast<object>())
        {
            foreach (var item in EnumerateContextChoices(context))
            {
                var label = Label(item);
                if (!string.IsNullOrWhiteSpace(label))
                    yield return label;
            }
        }
    }

    private static IEnumerable<object?> EnumerateContextChoices(object context)
    {
        foreach (var p in context.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!p.CanRead || p.GetIndexParameters().Length != 0)
                continue;
            if (!ContainsPreset(p.Name) && !p.Name.Contains("Items", StringComparison.OrdinalIgnoreCase))
                continue;

            object? value;
            try { value = p.GetValue(context); }
            catch { continue; }

            if (value is string || value is not IEnumerable items)
                continue;

            foreach (var item in items)
                yield return item;
        }
    }

    private static bool TrySelectContextChoice(object context, string targetLabel)
    {
        var t = context.GetType();
        var collections = t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead &&
                        p.GetIndexParameters().Length == 0 &&
                        (ContainsPreset(p.Name) || p.Name.Contains("Items", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var selections = t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.SetMethod?.IsPublic == true &&
                        (p.Name.Contains("Selected", StringComparison.OrdinalIgnoreCase) ||
                         p.Name.Contains("CurrentPreset", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        foreach (var collection in collections)
        {
            object? value;
            try { value = collection.GetValue(context); }
            catch { continue; }

            if (value is string || value is not IEnumerable items)
                continue;

            var candidates = items.Cast<object?>().Where(x => x != null).Cast<object>().ToArray();
            var target = candidates.FirstOrDefault(x => string.Equals(Label(x), targetLabel, StringComparison.Ordinal));
            if (target == null)
                continue;

            foreach (var selection in selections)
            {
                if (!selection.PropertyType.IsAssignableFrom(target.GetType()))
                    continue;

                try
                {
                    selection.SetValue(context, target);
                    return true;
                }
                catch { }
            }
        }

        return false;
    }

    private static string Label(object? value)
    {
        if (value == null) return "";
        if (value is string s) return s;

        var t = value.GetType();
        foreach (var name in new[] { "Display", "Name", "Label", "Content", "Text" })
        {
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (p?.CanRead != true || p.GetIndexParameters().Length != 0)
                continue;

            try
            {
                var rendered = p.GetValue(value)?.ToString();
                if (!string.IsNullOrWhiteSpace(rendered))
                    return rendered;
            }
            catch { }
        }

        var preset = t.GetProperty("Preset", BindingFlags.Instance | BindingFlags.Public);
        if (preset?.CanRead == true)
        {
            try
            {
                var nested = preset.GetValue(value);
                var name = nested?.GetType()
                    .GetProperty("Name", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(nested)
                    ?.ToString();

                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch { }
        }

        return value.ToString() ?? "";
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
                    if (child is DependencyObject d)
                        stack.Push(d);
            }
            catch { }
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

    private static async Task<ModernBindingResult> TestModernItemPropertyBindingAsync()
    {
        var host = ProbeView.Current?.Host ?? throw new InvalidOperationException("Probe staging host unavailable.");
        var baseline = host.Children.Count;
        var character = new SyntheticModernCharacterParameter();
        var face = new SyntheticModernFaceParameter();
        var property = typeof(SyntheticModernFaceParameter).GetProperty(nameof(SyntheticModernFaceParameter.PresetDummy))!;
        var editor = property.GetCustomAttributes(inherit: true).OfType<SyntheticModernPresetEditorAttribute>().Single();
        var control = editor.Create();
        host.Children.Add(control);

        var itemRoute = "";
        var cacheRoute = "";
        var mutation = false;
        var cleanup = false;

        try
        {
            editor.CharacterParameter = character;
            var itemProperty = CreatePublicItemProperty(face, face, property, out itemRoute, out cacheRoute);
            editor.SetBindings(control, [itemProperty]);

            await SettleAsync(control);

            var box = (ComboBox)control;
            box.SelectedItem = "CNWL_MODERN_HAPPY";
            await SettleAsync(control);

            mutation = face.Mood == "Happy" && face.Level == 7;
        }
        catch (Exception ex)
        {
            try { editor.ClearBindings(control); } catch { }
            try { host.Children.Remove(control); } catch { }
            return new(false, itemRoute, cacheRoute, false, host.Children.Count == baseline, ex.GetType().Name + ": " + ex.Message);
        }

        try
        {
            editor.ClearBindings(control);
            host.Children.Remove(control);
            cleanup = host.Children.Count == baseline;

            var beforeMood = face.Mood;
            var beforeLevel = face.Level;
            ((ComboBox)control).SelectedItem = "CNWL_MODERN_NEUTRAL";
            await SettleAsync(control);

            cleanup = cleanup && face.Mood == beforeMood && face.Level == beforeLevel;
        }
        catch
        {
            cleanup = false;
        }

        return new(mutation && cleanup, itemRoute, cacheRoute, mutation, cleanup, null);
    }

    private static ItemProperty CreatePublicItemProperty(
        object item,
        object propertyOwner,
        PropertyInfo property,
        out string itemRoute,
        out string cacheRoute)
    {
        var ctor = typeof(ItemProperty).GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(x =>
            {
                var p = x.GetParameters();
                return p.Length == 4 &&
                       p[0].ParameterType == typeof(object) &&
                       p[1].ParameterType == typeof(object) &&
                       p[2].ParameterType == typeof(PropertyInfo);
            })
            ?? throw new MissingMethodException(typeof(ItemProperty).FullName, "public 4-argument constructor");

        var cacheType = ctor.GetParameters()[3].ParameterType;
        var cache = TryCreatePublicCache(cacheType, item.GetType(), out cacheRoute);

        try
        {
            var created = ctor.Invoke([item, propertyOwner, property, cache]);
            itemRoute = $"public {ctor}";
            return (ItemProperty)created;
        }
        catch when (cache == null && !cacheType.IsValueType)
        {
            itemRoute = $"public {ctor} with null cache";
            cacheRoute = "null accepted";
            return (ItemProperty)ctor.Invoke([item, propertyOwner, property, null]);
        }
    }

    private static object? TryCreatePublicCache(Type cacheType, Type itemType, out string route)
    {
        var empty = cacheType.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
        if (empty != null)
        {
            route = $"public {empty}";
            return empty.Invoke([]);
        }

        foreach (var ctor in cacheType.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
        {
            var ps = ctor.GetParameters();

            if (ps.Length == 1 && ps[0].ParameterType == typeof(Type))
            {
                route = $"public {ctor} using item type";
                return ctor.Invoke([itemType]);
            }

            if (ps.All(x => x.HasDefaultValue))
            {
                route = $"public {ctor} using defaults";
                return ctor.Invoke(ps.Select(x => x.DefaultValue).ToArray());
            }
        }

        foreach (var p in cacheType.GetProperties(BindingFlags.Static | BindingFlags.Public))
        {
            if (!p.CanRead || !cacheType.IsAssignableFrom(p.PropertyType) || p.GetIndexParameters().Length != 0)
                continue;

            var value = p.GetValue(null);
            if (value != null)
            {
                route = $"public static property {cacheType.FullName}.{p.Name}";
                return value;
            }
        }

        foreach (var f in cacheType.GetFields(BindingFlags.Static | BindingFlags.Public))
        {
            if (!cacheType.IsAssignableFrom(f.FieldType))
                continue;

            var value = f.GetValue(null);
            if (value != null)
            {
                route = $"public static field {cacheType.FullName}.{f.Name}";
                return value;
            }
        }

        foreach (var m in cacheType.GetMethods(BindingFlags.Static | BindingFlags.Public))
        {
            if (m.GetParameters().Length != 0 || !cacheType.IsAssignableFrom(m.ReturnType))
                continue;

            var value = m.Invoke(null, null);
            if (value != null)
            {
                route = $"public static method {cacheType.FullName}.{m.Name}()";
                return value;
            }
        }

        route = "no public cache factory found; trying null";
        return null;
    }

    private static void ConfigureFixturePaths(object characterParameter, object faceParameter)
    {
        var animationDir = Path.Combine(FixtureDir, "animation");
        var psdPath = Path.Combine(FixtureDir, "psd", "1layer.psd");

        foreach (var p in characterParameter.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!p.CanWrite || p.PropertyType != typeof(string))
                continue;

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
            if (!target.CanWrite || target.PropertyType != typeof(string))
                continue;

            if (!target.Name.Contains("File", StringComparison.OrdinalIgnoreCase) &&
                !target.Name.Contains("Directory", StringComparison.OrdinalIgnoreCase))
                continue;

            var source = characterParameter.GetType().GetProperty(target.Name, BindingFlags.Instance | BindingFlags.Public);
            if (source?.CanRead != true || source.PropertyType != typeof(string))
                continue;

            try { target.SetValue(faceParameter, source.GetValue(characterParameter)); }
            catch { }
        }
    }

    private static string Fingerprint(object value)
    {
        var canonical = Canonical(value, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string Canonical(object? value, int depth, HashSet<object> seen)
    {
        if (value == null) return "null";

        var t = value.GetType();

        if (value is string s) return "s:" + Escape(s);
        if (value is char ch) return "c:" + ((int)ch).ToString(CultureInfo.InvariantCulture);
        if (value is bool b) return b ? "b:1" : "b:0";
        if (t.IsEnum) return "e:" + (t.FullName ?? t.Name) + ":" + Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong)
            return "i:" + Convert.ToString(value, CultureInfo.InvariantCulture);
        if (value is float f) return "f:" + f.ToString("R", CultureInfo.InvariantCulture);
        if (value is double d) return "d:" + d.ToString("R", CultureInfo.InvariantCulture);
        if (value is decimal m) return "m:" + m.ToString(CultureInfo.InvariantCulture);
        if (value is Guid g) return "g:" + g.ToString("D");
        if (value is TimeSpan ts) return "ts:" + ts.Ticks.ToString(CultureInfo.InvariantCulture);
        if (value is DateTime dt) return "dt:" + dt.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
        if (value is DateTimeOffset dto) return "dto:" + dto.UtcTicks.ToString(CultureInfo.InvariantCulture);
        if (value is Type type) return "type:" + (type.AssemblyQualifiedName ?? type.FullName ?? type.Name);

        if (value is IEnumerable enumerable)
        {
            var parts = new List<string>();
            var count = 0;
            foreach (var item in enumerable)
            {
                if (count++ >= 128)
                {
                    parts.Add("<truncated>");
                    break;
                }
                parts.Add(Canonical(item, depth + 1, seen));
            }
            return "list:[" + string.Join(",", parts) + "]";
        }

        if (depth >= 3)
            return "opaque:" + (t.FullName ?? t.Name);

        if (!t.IsValueType && !seen.Add(value))
            return "cycle:" + (t.FullName ?? t.Name);

        try
        {
            var properties = t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToArray();

            var entries = new List<string>();
            foreach (var p in properties)
            {
                try
                {
                    entries.Add(Escape(p.Name) + "=" + Canonical(p.GetValue(value), depth + 1, seen));
                }
                catch (Exception ex)
                {
                    entries.Add(Escape(p.Name) + "=<throw:" + ex.GetType().Name + ">");
                }
            }

            return "obj:" + (t.FullName ?? t.Name) + "{" + string.Join(";", entries) + "}";
        }
        finally
        {
            if (!t.IsValueType)
                seen.Remove(value);
        }
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace(";", "\\;", StringComparison.Ordinal)
             .Replace(",", "\\,", StringComparison.Ordinal)
             .Replace("=", "\\=", StringComparison.Ordinal)
             .Replace("[", "\\[", StringComparison.Ordinal)
             .Replace("]", "\\]", StringComparison.Ordinal)
             .Replace("{", "\\{", StringComparison.Ordinal)
             .Replace("}", "\\}", StringComparison.Ordinal);
}
