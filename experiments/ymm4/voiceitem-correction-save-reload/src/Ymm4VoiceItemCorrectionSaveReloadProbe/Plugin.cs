using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Voice;

namespace Ymm4VoiceItemCorrectionSaveReloadProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VoiceItem Correction Save Reload Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

[VideoEffect("CNWL 発音補助永続化", ["CNWL"], [])]
public sealed class PronunciationAssistPersistenceEffect : VideoEffectBase
{
    public override string Label => "CNWL 発音補助永続化";
    public bool AssistEnabled { get; set; } = true;
    public string ProbeTag { get; set; } = "CNWL-VQA-PERSIST-A";

    public override IEnumerable<string> CreateExoVideoFilters(
        int keyFrameIndex,
        ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(
        IGraphicsDevicesAndContext devices) => new PassThroughProcessor();

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

internal static class Probe
{
    const string MarkerSerifA = "前<w0>後";
    const string HatsuonA = "まえあと";
    const string Remark = "CNWL_VQA_SAVE_RELOAD";
    const string ProbeTagA = "CNWL-VQA-PERSIST-A";

    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_VOICEITEM_SAVE_RELOAD_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(dir))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(
            new Action(Start),
            DispatcherPriority.ApplicationIdle);
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

                var active = GetActive(main);
                if (active is null)
                {
                    if (!created)
                    {
                        created = true;
                        PublicMethod(main, "CreateProject", Type.EmptyTypes).Invoke(main, null);
                    }

                    if (ticks > 160)
                        throw new TimeoutException("ActiveTimelineViewModel was not created.");
                    return;
                }

                timer.Stop();
                await RunAsync(main);
                Write("PASS_VOICEITEM_CORRECTION_SAVE_RELOAD", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_VOICEITEM_CORRECTION_SAVE_RELOAD", ex.ToString());
            }
        };

        timer.Start();
    }

    static async Task RunAsync(object main)
    {
        var active = GetActive(main)
            ?? throw new InvalidOperationException("ActiveTimelineViewModel missing.");
        var timeline = FindTimeline(active)
            ?? throw new InvalidOperationException("Timeline could not be resolved.");
        Check("timeline_resolved", true);

        var saveProject = PublicMethod(main, "SaveProject", typeof(string));
        var openProject = PublicMethod(main, "OpenProject", typeof(string));
        Check("public_save_project_available", saveProject.IsPublic);
        Check("public_open_project_available", openProject.IsPublic);

        var character = new Character { Name = "CNWL VQA Persist" };
        var voice = new VoiceItem(character)
        {
            Serif = MarkerSerifA,
            Hatsuon = HatsuonA,
            Remark = Remark,
            Frame = 120,
            Layer = 5,
            Length = 90
        };

        var effect = new PronunciationAssistPersistenceEffect
        {
            AssistEnabled = true,
            ProbeTag = ProbeTagA,
            IsEnabled = true
        };
        AppendEffect(voice, effect);

        var pronounceA = CreatePronounce(0.0);
        voice.Pronounce = pronounceA;

        Check("voice_added_to_real_timeline",
            timeline.TryAddItems([voice], voice.Frame, voice.Layer));
        Check("voice_present_before_save",
            timeline.Items.Any(x => ReferenceEquals(x, voice)));
        Check("marker_present_before_save", voice.Serif == MarkerSerifA);
        Check("assist_effect_present_before_save",
            GetAssistEffect(voice) is { IsEnabled: true, AssistEnabled: true } beforeEffect
            && beforeEffect.ProbeTag == ProbeTagA);
        Check("pronounce_zero_before_save",
            GetPauseVowelLength(voice.Pronounce) == 0.0);

        var pathA = Path.Combine(output, "voice-quality-a.ymmp");
        saveProject.Invoke(main, [pathA]);
        await WaitUntil("project A save", () => File.Exists(pathA) && new FileInfo(pathA).Length > 0);
        Check("project_a_saved", File.Exists(pathA) && new FileInfo(pathA).Length > 0);

        var rawA = File.ReadAllText(pathA);
        Check("project_a_contains_marker_source", rawA.Contains("<w0>", StringComparison.Ordinal));
        Check("project_a_contains_assist_probe_tag", rawA.Contains(ProbeTagA, StringComparison.Ordinal));

        // Deliberately diverge the live object, then save a different project file.
        // Reopening A must restore A's values from the project file rather than
        // accidentally passing because the original object was never changed.
        voice.Serif = "壊したB";
        voice.Hatsuon = "こわしたびー";
        var liveEffect = GetAssistEffect(voice)
            ?? throw new InvalidOperationException("Assist effect disappeared before mutation.");
        liveEffect.AssistEnabled = false;
        liveEffect.IsEnabled = false;
        liveEffect.ProbeTag = "CNWL-VQA-PERSIST-B";
        voice.Pronounce = CreatePronounce(0.75);

        Check("live_state_mutated_away_from_a",
            voice.Serif != MarkerSerifA
            && voice.Hatsuon != HatsuonA
            && GetPauseVowelLength(voice.Pronounce) == 0.75
            && GetAssistEffect(voice) is { IsEnabled: false, AssistEnabled: false } bEffect
            && bEffect.ProbeTag == "CNWL-VQA-PERSIST-B");

        var pathB = Path.Combine(output, "voice-quality-b.ymmp");
        saveProject.Invoke(main, [pathB]);
        await WaitUntil("project B save", () => File.Exists(pathB) && new FileInfo(pathB).Length > 0);
        Check("project_b_saved", File.Exists(pathB) && new FileInfo(pathB).Length > 0);

        openProject.Invoke(main, [pathA]);
        await WaitUntil(
            "project A reopen",
            () => SamePath(GetProjectFilePath(main), pathA)
               && FindVoice(main, Remark) is not null,
            12000);

        var reloaded = FindVoice(main, Remark)
            ?? throw new InvalidOperationException("Reloaded VoiceItem was not found.");
        var reloadedEffect = GetAssistEffect(reloaded);

        Check("project_a_reopened", SamePath(GetProjectFilePath(main), pathA));
        Check("reloaded_voice_found", reloaded is not null);
        Check("reloaded_serif_marker_exact", reloaded.Serif == MarkerSerifA);
        Check("reloaded_hatsuon_exact", reloaded.Hatsuon == HatsuonA);
        Check("reloaded_assist_effect_found", reloadedEffect is not null);
        Check("reloaded_assist_enabled",
            reloadedEffect?.AssistEnabled == true);
        Check("reloaded_effect_enabled",
            reloadedEffect?.IsEnabled == true);
        Check("reloaded_probe_tag_exact",
            reloadedEffect?.ProbeTag == ProbeTagA);

        var pronouncePersisted = reloaded.Pronounce is not null;
        double? reloadedPause = pronouncePersisted
            ? GetPauseVowelLength(reloaded.Pronounce)
            : null;

        // Pronounce persistence is optional for the product design. If YMM4
        // persisted it, however, the corrected state must not silently degrade.
        Check("reloaded_pronounce_safe_if_present",
            !pronouncePersisted || reloadedPause == 0.0);

        var durableReapplySource =
            reloaded.Serif == MarkerSerifA
            && reloadedEffect is { IsEnabled: true, AssistEnabled: true }
            && reloadedEffect.ProbeTag == ProbeTagA;
        Check("durable_reapply_source_survives_reload", durableReapplySource);

        File.WriteAllText(
            Path.Combine(output, "save-reload-observation.json"),
            JsonSerializer.Serialize(new
            {
                host = "4.56.1.0 Lite",
                projectA = new
                {
                    path = pathA,
                    length = new FileInfo(pathA).Length,
                    containsMarker = rawA.Contains("<w0>", StringComparison.Ordinal),
                    containsProbeTag = rawA.Contains(ProbeTagA, StringComparison.Ordinal)
                },
                reloaded = new
                {
                    sameObjectReference = ReferenceEquals(voice, reloaded),
                    reloaded.Serif,
                    reloaded.Hatsuon,
                    reloaded.Remark,
                    assistEffect = reloadedEffect is null ? null : new
                    {
                        reloadedEffect.AssistEnabled,
                        reloadedEffect.IsEnabled,
                        reloadedEffect.ProbeTag,
                        type = reloadedEffect.GetType().FullName
                    },
                    pronouncePersisted,
                    pronounceType = reloaded.Pronounce?.GetType().FullName,
                    pauseVowelLength = reloadedPause,
                    durableReapplySource
                }
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    static VoiceItem? FindVoice(object main, string remark)
    {
        var active = GetActive(main);
        var timeline = active is null ? null : FindTimeline(active);
        return timeline?.Items.OfType<VoiceItem>()
            .FirstOrDefault(x => x.Remark == remark);
    }

    static object? GetActive(object main) =>
        main.GetType().GetProperty(
            "ActiveTimelineViewModel",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(main);

    static MethodInfo PublicMethod(object target, string name, params Type[] types) =>
        target.GetType().GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types,
            modifiers: null)
        ?? throw new MissingMethodException(target.GetType().FullName, name);

    static string? GetProjectFilePath(object main)
    {
        var p = main.GetType().GetProperty("ProjectFilePath", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(main.GetType().FullName, "ProjectFilePath");
        var reactive = p.GetValue(main);
        return reactive?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(reactive) as string;
    }

    static bool SamePath(string? actual, string expected) =>
        !string.IsNullOrWhiteSpace(actual)
        && string.Equals(
            Path.GetFullPath(actual),
            Path.GetFullPath(expected),
            StringComparison.OrdinalIgnoreCase);

    static IVoicePronounce CreatePronounce(double pause)
    {
        var query = new VOICEVOXAudioQuery
        {
            SpeedScale = 1.0,
            PitchScale = 0.0,
            IntonationScale = 1.0,
            VolumeScale = 1.0,
            PrePhonemeLength = 0.1,
            PostPhonemeLength = 0.1,
            OutputSamplingRate = 24000,
            OutputStereo = false,
            Kana = "マエ'、アト"
        };

        var phrase = new VOICEVOXAccentPhrase { Accent = 1 };
        phrase.Moras.Add(new VOICEVOXMora
        {
            Text = "前",
            Vowel = "a",
            VowelLength = 0.12,
            Pitch = 5.0
        });
        phrase.PauseMora = new VOICEVOXMora
        {
            Text = "、",
            Vowel = "pau",
            VowelLength = pause,
            Pitch = 0.0
        };
        query.AccentPhrases.Add(phrase);

        var type = typeof(VOICEVOXEngine).Assembly.GetType("YukkuriMovieMaker.Voice.VOICEVOXVoicePronounce")
            ?? throw new TypeLoadException("VOICEVOXVoicePronounce");
        return Activator.CreateInstance(type, query) as IVoicePronounce
            ?? throw new InvalidOperationException("VOICEVOXVoicePronounce construction failed.");
    }

    static double GetPauseVowelLength(IVoicePronounce? pronounce)
    {
        if (pronounce is null)
            throw new InvalidOperationException("Pronounce is null.");

        var qProp = pronounce.GetType().GetProperty("AudioQuery", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(pronounce.GetType().FullName, "AudioQuery");
        var query = qProp.GetValue(pronounce)
            ?? throw new InvalidOperationException("AudioQuery is null.");
        var phrasesProp = query.GetType().GetProperty("AccentPhrases", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(query.GetType().FullName, "AccentPhrases");
        var phrases = phrasesProp.GetValue(query) as IEnumerable
            ?? throw new InvalidOperationException("AccentPhrases is not enumerable.");
        var phrase = phrases.Cast<object>().FirstOrDefault()
            ?? throw new InvalidOperationException("AccentPhrases is empty.");
        var pauseProp = phrase.GetType().GetProperty("PauseMora", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(phrase.GetType().FullName, "PauseMora");
        var mora = pauseProp.GetValue(phrase)
            ?? throw new InvalidOperationException("PauseMora is null.");
        var valueProp = mora.GetType().GetProperty("VowelLength", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(mora.GetType().FullName, "VowelLength");
        return Convert.ToDouble(valueProp.GetValue(mora), CultureInfo.InvariantCulture);
    }

    static void AppendEffect(VoiceItem voice, PronunciationAssistPersistenceEffect effect)
    {
        var prop = typeof(VoiceItem).GetProperty("JimakuVideoEffects", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(typeof(VoiceItem).FullName, "JimakuVideoEffects");
        var current = prop.GetValue(voice)
            ?? throw new InvalidOperationException("JimakuVideoEffects returned null.");

        if (current is IList list && !list.IsReadOnly && !list.IsFixedSize)
        {
            list.Add(effect);
            return;
        }

        var add = current.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == "Add"
                              && m.GetParameters().Length == 1
                              && (m.GetParameters()[0].ParameterType.IsAssignableFrom(effect.GetType())
                                  || m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(IVideoEffect))))
            ?? throw new MissingMethodException(current.GetType().FullName, "Add");

        var updated = add.Invoke(current, [effect]);
        if (updated is null || ReferenceEquals(updated, current))
            return;

        if (prop.SetMethod?.IsPublic != true)
            throw new InvalidOperationException("JimakuVideoEffects is immutable but has no public setter.");
        prop.SetValue(voice, updated);
    }

    static PronunciationAssistPersistenceEffect? GetAssistEffect(VoiceItem voice)
    {
        var prop = typeof(VoiceItem).GetProperty("JimakuVideoEffects", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(typeof(VoiceItem).FullName, "JimakuVideoEffects");
        return (prop.GetValue(voice) as IEnumerable)?
            .Cast<object>()
            .OfType<PronunciationAssistPersistenceEffect>()
            .FirstOrDefault();
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

    static async Task WaitUntil(string name, Func<bool> condition, int timeoutMs = 10000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
                return;
            await Task.Delay(100);
        }
        throw new TimeoutException(name);
    }

    static void Check(string id, bool passed) =>
        requirements.Add(new { id, passed });

    static void Write(string status, string? error)
    {
        File.WriteAllText(
            Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(new
            {
                schema = "cnwl.voiceitem-correction-save-reload.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
