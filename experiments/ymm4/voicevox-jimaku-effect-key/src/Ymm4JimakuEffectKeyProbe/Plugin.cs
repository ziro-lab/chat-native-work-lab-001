using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4JimakuEffectKeyProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL Jimaku Effect Key Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

[VideoEffect("CNWL 発音補助キー", ["CNWL"], [])]
public sealed class PronunciationAssistKeyEffect : VideoEffectBase
{
    public override string Label => "CNWL 発音補助キー";

    [Display(Name = "発音補助キー")]
    [PronunciationAssistProbeEditor]
    public bool AssistEnabled { get; set; } = true;

    public string ProbeTag { get; set; } = "CNWL-JIMAKU-KEY";

    public override IEnumerable<string> CreateExoVideoFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    public override IVideoEffectProcessor CreateVideoEffect(IGraphicsDevicesAndContext devices) => new PassThroughProcessor();
    protected override IEnumerable<IAnimatable> GetAnimatables() => [];
}

internal sealed class PassThroughProcessor : IVideoEffectProcessor
{
    ID2D1Image? input;
    public ID2D1Image Output => input ?? throw new InvalidOperationException("input is null");
    public DrawDescription Update(EffectDescription effectDescription) => effectDescription.DrawDescription;
    public void SetInput(ID2D1Image? value) => input = value;
    public void ClearInput() => input = null;
    public void Dispose() { }
}

internal sealed class PronunciationAssistProbeEditorAttribute : PropertyEditorAttribute2
{
    public override FrameworkElement Create() => new PronunciationAssistProbeEditor();

    public override void SetBindings(FrameworkElement control, ItemProperty[] itemProperties)
    {
        if (control is not PronunciationAssistProbeEditor editor)
            return;
        editor.ItemProperties = itemProperties;
        ProbeState.PropertyEditorBound = true;
        ProbeState.PropertyOwnerWasKeyEffect =
            itemProperties.Length > 0 && itemProperties.All(x => x.PropertyOwner is PronunciationAssistKeyEffect);
    }

    public override void ClearBindings(FrameworkElement control)
    {
        if (control is PronunciationAssistProbeEditor editor)
            editor.ItemProperties = null;
    }
}

internal sealed class PronunciationAssistProbeEditor : Button, IPropertyEditorControl2
{
    public event EventHandler? BeginEdit;
    public event EventHandler? EndEdit;
    public ItemProperty[]? ItemProperties { get; set; }

    public PronunciationAssistProbeEditor() => Content = "CNWL 発音補助";

    public void SetEditorInfo(IEditorInfo? info)
    {
        if (info is null)
            return;

        ProbeState.EditorInfoSeen = true;
        ProbeState.VoiceItemEditSeen = info.VoiceItemEdit is not null;

        if (info.VoiceItemEdit is not null && Interlocked.Exchange(ref ProbeState.RegenerationStartedFlag, 1) == 0)
            _ = RegenerateAsync(info);
    }

    static async Task RegenerateAsync(IEditorInfo info)
    {
        try
        {
            ProbeState.RegenerationStarted = true;
            ProbeState.EditorHatsuon = info.VoiceItemEdit?.Hatsuon;
            await (info.VoiceItemEdit?.CreateVoiceFileAsync(force: true) ?? Task.CompletedTask);
            ProbeState.RegenerationCompleted = true;
        }
        catch (Exception ex)
        {
            ProbeState.RegenerationError = ex.ToString();
        }
    }
}

internal static class ProbeState
{
    internal static volatile bool PropertyEditorBound;
    internal static volatile bool PropertyOwnerWasKeyEffect;
    internal static volatile bool EditorInfoSeen;
    internal static volatile bool VoiceItemEditSeen;
    internal static volatile bool RegenerationStarted;
    internal static volatile bool RegenerationCompleted;
    internal static int RegenerationStartedFlag;
    internal static string? RegenerationError;
    internal static string? EditorHatsuon;
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_JIMAKU_EFFECT_KEY_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(dir))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(Start), DispatcherPriority.ApplicationIdle);
    }

    static void Start()
    {
        int ticks = 0;
        bool created = false;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };

        timer.Tick += async (_, _) =>
        {
            try
            {
                ticks++;
                var main = Application.Current.Windows.Cast<Window>()
                    .Select(w => w.DataContext)
                    .FirstOrDefault(x => x?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                if (main is null)
                    return;

                var active = main.GetType().GetProperty("ActiveTimelineViewModel",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(main);
                if (active is null)
                {
                    if (!created)
                    {
                        created = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)?.Invoke(main, null);
                    }
                    if (ticks > 160)
                        throw new TimeoutException("ActiveTimelineViewModel was not created.");
                    return;
                }

                timer.Stop();
                await RunAsync(main, active);
                Write("PASS_JIMAKU_EFFECT_KEY", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_JIMAKU_EFFECT_KEY", ex.ToString());
            }
        };
        timer.Start();
    }

    static async Task RunAsync(object main, object active)
    {
        var timeline = FindTimeline(active)
            ?? throw new InvalidOperationException("Timeline could not be resolved.");
        Check("timeline_resolved", true);

        var characters = ReadCharacters(active).ToArray();
        Check("characters_available", characters.Length > 0);
        if (characters.Length == 0)
            throw new InvalidOperationException("No characters available.");

        var mainModel = FindMainModel(main)
            ?? throw new InvalidOperationException("MainModel could not be resolved.");

        VoiceItem? voice = null;
        string? characterName = null;
        Exception? lastVoiceError = null;

        foreach (var candidate in characters.Take(6))
        {
            try
            {
                voice = await AddVoiceAsync(mainModel, candidate.Model, "CNWL 発音補助テスト");
                if (voice is not null)
                {
                    characterName = candidate.Name;
                    break;
                }
            }
            catch (Exception ex)
            {
                lastVoiceError = ex;
            }
        }

        Check("voice_created_by_host", voice is not null);
        if (voice is null)
            throw new InvalidOperationException("YMM4 could not create a synthetic VoiceItem.", lastVoiceError);

        var jimakuProp = voice.GetType().GetProperty("JimakuVideoEffects",
            BindingFlags.Instance | BindingFlags.Public);
        Check("jimaku_effects_public_readable", jimakuProp?.GetMethod?.IsPublic == true);
        if (jimakuProp is null)
            throw new MissingMemberException("VoiceItem.JimakuVideoEffects");

        var effect = new PronunciationAssistKeyEffect();
        AppendEffect(voice, jimakuProp, effect);

        var effectsAfter = Enumerate(jimakuProp.GetValue(voice)).ToArray();
        Check("custom_effect_stored_in_jimaku", effectsAfter.Any(x => ReferenceEquals(x, effect)));
        Check("custom_effect_detectable_by_type", effectsAfter.OfType<PronunciationAssistKeyEffect>().Any());

        Check("enabled_effect_is_active_key", HasActiveKey(voice, jimakuProp));
        effect.IsEnabled = false;
        Check("disabled_effect_is_not_active_key", !HasActiveKey(voice, jimakuProp));
        effect.IsEnabled = true;
        Check("reenabled_effect_is_active_key", HasActiveKey(voice, jimakuProp));

        timeline.SelectedItems = ImmutableList.Create<IItem>(voice);
        timeline.CurrentFrame = voice.Frame;
        Check("voice_selected_in_timeline",
            timeline.SelectedItems.Count == 1 && ReferenceEquals(timeline.SelectedItems[0], voice));

        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline &&
               (!ProbeState.PropertyEditorBound ||
                !ProbeState.EditorInfoSeen ||
                !ProbeState.VoiceItemEditSeen ||
                (!ProbeState.RegenerationCompleted && ProbeState.RegenerationError is null)))
        {
            await Task.Delay(200);
        }

        Check("effect_property_editor_bound", ProbeState.PropertyEditorBound);
        Check("effect_editor_owner_is_custom_effect", ProbeState.PropertyOwnerWasKeyEffect);
        Check("effect_editor_received_editor_info", ProbeState.EditorInfoSeen);
        Check("effect_editor_received_voice_edit_service", ProbeState.VoiceItemEditSeen);
        Check("standard_voice_regeneration_started", ProbeState.RegenerationStarted);
        Check("standard_voice_regeneration_completed",
            ProbeState.RegenerationCompleted && ProbeState.RegenerationError is null);

        var evidence = new
        {
            host = "4.56.1.0 Lite",
            characterName,
            voice = new
            {
                voice.CharacterName,
                voice.Serif,
                voice.Hatsuon,
                voice.Frame,
                voice.Layer,
                voice.Length,
                voice.VoiceLength,
                pronounceType = voice.Pronounce?.GetType().FullName
            },
            jimakuEffectsProperty = new
            {
                type = jimakuProp.PropertyType.FullName,
                publicGet = jimakuProp.GetMethod?.IsPublic == true,
                publicSet = jimakuProp.SetMethod?.IsPublic == true,
                effects = effectsAfter.Select(x => new
                {
                    type = x.GetType().FullName,
                    enabled = (x as IVideoEffect)?.IsEnabled
                }).ToArray()
            },
            editorBridge = new
            {
                ProbeState.PropertyEditorBound,
                ProbeState.PropertyOwnerWasKeyEffect,
                ProbeState.EditorInfoSeen,
                ProbeState.VoiceItemEditSeen,
                ProbeState.RegenerationStarted,
                ProbeState.RegenerationCompleted,
                ProbeState.RegenerationError,
                ProbeState.EditorHatsuon
            }
        };

        File.WriteAllText(Path.Combine(output, "behavior.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    static bool HasActiveKey(VoiceItem voice, PropertyInfo prop)
        => Enumerate(prop.GetValue(voice))
            .OfType<PronunciationAssistKeyEffect>()
            .Any(x => x.IsEnabled);

    static void AppendEffect(VoiceItem voice, PropertyInfo prop, PronunciationAssistKeyEffect effect)
    {
        var current = prop.GetValue(voice)
            ?? throw new InvalidOperationException("JimakuVideoEffects returned null.");

        if (current is IList list)
        {
            list.Add(effect);
            return;
        }

        var add = current.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == "Add" && m.GetParameters().Length == 1)
            .FirstOrDefault(m => m.GetParameters()[0].ParameterType.IsAssignableFrom(effect.GetType())
                || m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(IVideoEffect)));

        if (add is null)
            throw new MissingMethodException(current.GetType().FullName, "Add");

        var updated = add.Invoke(current, [effect]);
        if (updated is null || ReferenceEquals(updated, current))
            return;

        if (prop.SetMethod?.IsPublic != true)
            throw new InvalidOperationException("JimakuVideoEffects is immutable but has no public setter.");

        prop.SetValue(voice, updated);
    }

    static IEnumerable<object> Enumerate(object? value)
    {
        if (value is not IEnumerable enumerable)
            yield break;
        foreach (var item in enumerable)
            if (item is not null)
                yield return item;
    }

    static Timeline? FindTimeline(object active)
    {
        for (var t = active.GetType(); t is not null; t = t.BaseType)
        {
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (typeof(Timeline).IsAssignableFrom(f.FieldType) && f.GetValue(active) is Timeline ft)
                    return ft;

            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (p.GetIndexParameters().Length != 0 || !typeof(Timeline).IsAssignableFrom(p.PropertyType))
                    continue;
                try
                {
                    if (p.GetValue(active) is Timeline pt)
                        return pt;
                }
                catch { }
            }
        }
        return null;
    }

    static object? FindMainModel(object main)
    {
        const string target = "YukkuriMovieMaker.Project.MainModel";
        for (var t = main.GetType(); t is not null; t = t.BaseType)
        {
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (f.FieldType.FullName == target && f.GetValue(main) is { } fv)
                    return fv;

            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (p.PropertyType.FullName != target || p.GetIndexParameters().Length != 0)
                    continue;
                try
                {
                    if (p.GetValue(main) is { } pv)
                        return pv;
                }
                catch { }
            }
        }
        return null;
    }

    sealed record CharacterCandidate(string Name, object Model);

    static IEnumerable<CharacterCandidate> ReadCharacters(object active)
    {
        var p = active.GetType().GetProperty("Characters",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p?.GetValue(active) is not IEnumerable values)
            yield break;

        foreach (var entry in values)
        {
            if (entry is null)
                continue;

            object model = entry;
            var nameProp = entry.GetType().GetProperty("Name");
            if (nameProp is null)
            {
                var inner = entry.GetType().GetProperty("Character");
                if (inner?.GetValue(entry) is { } innerValue)
                {
                    model = innerValue;
                    nameProp = innerValue.GetType().GetProperty("Name");
                }
            }

            var name = nameProp?.GetValue(model) as string;
            if (!string.IsNullOrWhiteSpace(name))
                yield return new CharacterCandidate(name, model);
        }
    }

    static async Task<VoiceItem?> AddVoiceAsync(object mainModel, object character, string serif)
    {
        var method = mainModel.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(x => x.Name == "AddVoiceItemAsync" && x.GetParameters().Length == 5)
            ?? throw new MissingMethodException(mainModel.GetType().FullName, "AddVoiceItemAsync");

        var taskObject = method.Invoke(mainModel, [120, 8, character, serif, null]);
        if (taskObject is not Task task)
            return taskObject as VoiceItem;

        await task;
        return taskObject.GetType().GetProperty("Result")?.GetValue(taskObject) as VoiceItem;
    }

    static void Check(string id, bool passed) => requirements.Add(new { id, passed });

    static void Write(string status, string? error)
    {
        var result = new
        {
            schema = "cnwl.jimaku-effect-key.v1",
            status,
            host = "4.56.1.0 Lite",
            sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            requirements,
            error
        };

        File.WriteAllText(Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
}
