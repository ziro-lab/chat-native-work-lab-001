using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
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

namespace Ymm4VoiceItemAssistDisableRemoveProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VoiceItem Assist Disable Remove Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

[VideoEffect("CNWL assist lifecycle key", ["CNWL"], [])]
public sealed class AssistLifecycleEffect : VideoEffectBase
{
    public override string Label => "CNWL assist lifecycle key";
    public bool AssistEnabled { get; set; } = true;
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

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_ASSIST_LIFECYCLE_OUTPUT");
        var url = Environment.GetEnvironmentVariable("CNWL_FAKE_VOICEVOX_URL");
        if (scheduled || string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(url))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => Start(url)),
            DispatcherPriority.ApplicationIdle);
    }

    static void Start(string url)
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

                var active = main.GetType().GetProperty(
                    "ActiveTimelineViewModel",
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
                await RunAsync(active, url);
                Write("PASS_VOICEITEM_ASSIST_DISABLE_REMOVE", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_VOICEITEM_ASSIST_DISABLE_REMOVE", ex.ToString());
            }
        };

        timer.Start();
    }

    sealed record State(string Label, bool Active, double Pause, string WavSha256, long WavLength);

    static async Task RunAsync(object active, string url)
    {
        var timeline = FindTimeline(active)
            ?? throw new InvalidOperationException("Timeline could not be resolved.");
        Check("timeline_resolved", true);

        var engine = new VOICEVOXEngine(new VOICEVOXEngineContext())
        {
            Name = "CNWL Fake VOICEVOX",
            URL = url,
            Path = "",
            Timeout = 10_000
        };

        const string fakeSpeakerUuid = "11111111-1111-1111-1111-111111111111";
        engine.SpeakerInfos.Add(new VOICEVOXSpeakerInfo(fakeSpeakerUuid, ""));

        var speakerJson = JObject.Parse("""
        {
          "name": "CNWL Speaker",
          "speaker_uuid": "11111111-1111-1111-1111-111111111111",
          "styles": [
            { "name": "Normal", "id": 1, "type": "talk" }
          ],
          "version": "0.0.0",
          "supported_features": {
            "permitted_synthesis_morphing": "SELF_ONLY"
          }
        }
        """);
        engine.SpeakersJsonCache = new JArray(speakerJson).ToString(Newtonsoft.Json.Formatting.None);
        Check("fake_engine_character_resolved", engine.Characters.Any(x => x.SpeakerUuid == fakeSpeakerUuid));

        var vvCharacter = new VOICEVOXCharacter(speakerJson, Array.Empty<VOICEVOXSpeakerInfo>(), false);
        var speakerType = typeof(VOICEVOXEngine).Assembly.GetType("YukkuriMovieMaker.Voice.VOICEVOXVoiceSpeaker")
            ?? throw new TypeLoadException("VOICEVOXVoiceSpeaker");
        var speakerObject = Activator.CreateInstance(speakerType, engine, vvCharacter)
            ?? throw new InvalidOperationException("VOICEVOXVoiceSpeaker construction failed.");
        if (speakerObject is not IVoiceSpeaker speaker)
            throw new InvalidOperationException("VOICEVOX speaker interface missing.");
        Check("builtin_voicevox_speaker_constructed", true);

        var registration = RegisterEngineInYmmSettings(engine, speaker.ID);
        try
        {
            Check("fake_engine_registered",
                registration.ResolvedEngine is not null && ReferenceEquals(registration.ResolvedEngine, engine));

            var parameter = speaker.CreateVoiceParameter();
            parameter.GetType().GetProperty("StyleID", BindingFlags.Instance | BindingFlags.Public)?.SetValue(parameter, 1);

            var character = new Character
            {
                Name = "CNWL Assist Lifecycle",
                Voice = new VoiceDescription(speaker),
                VoiceParameter = parameter
            };

            var voice = new VoiceItem
            {
                Serif = "ア",
                Hatsuon = "ア",
                CharacterName = character.Name,
                VoiceParameter = parameter,
                Remark = "CNWL_ASSIST_LIFECYCLE"
            };
            voice.Character = character;
            voice.VoiceParameter = parameter;
            voice.Serif = "ア";
            voice.Hatsuon = "ア";
            voice.CharacterName = character.Name;

            var effect = new AssistLifecycleEffect
            {
                AssistEnabled = true,
                IsEnabled = true
            };
            voice.JimakuVideoEffects = voice.JimakuVideoEffects.Add(effect);

            var voiceChanged = new List<string>();
            var effectChanged = new List<string>();
            if (voice is INotifyPropertyChanged vn)
                vn.PropertyChanged += (_, e) => voiceChanged.Add(e.PropertyName ?? "<null>");
            if (effect is INotifyPropertyChanged en)
                en.PropertyChanged += (_, e) => effectChanged.Add(e.PropertyName ?? "<null>");

            Check("voice_added_to_real_timeline", timeline.TryAddItems([voice], 240, 6));
            Check("voice_present_in_real_timeline", timeline.Items.Any(x => ReferenceEquals(x, voice)));
            Check("assist_initially_active", IsAssistActive(voice));

            await voice.CreateVoiceFileAsync();
            var voicePath = voice.FilePath;
            Check("real_voiceitem_file_path_available",
                !string.IsNullOrWhiteSpace(voicePath) && File.Exists(voicePath));
            if (string.IsNullOrWhiteSpace(voicePath) || !File.Exists(voicePath))
                throw new InvalidOperationException("VoiceItem file path unavailable.");

            var corrected1 = await ReconcileAsync("corrected-1", voice, speaker, parameter, voicePath);
            Check("enabled_applies_correction", corrected1.Active && corrected1.Pause == 0.0);
            Check("enabled_corrected_wav", corrected1.WavLength > 44);

            effect.IsEnabled = false;
            Check("disable_notification_observed",
                effectChanged.Contains(nameof(VideoEffectBase.IsEnabled)));
            Check("assist_inactive_when_disabled", !IsAssistActive(voice));

            var baseline1 = await ReconcileAsync("baseline-disabled", voice, speaker, parameter, voicePath);
            Check("disable_restores_baseline_pronounce", !baseline1.Active && baseline1.Pause > 0.0);
            Check("disable_restores_different_wav", baseline1.WavSha256 != corrected1.WavSha256);

            effect.IsEnabled = true;
            Check("reenable_notification_observed",
                effectChanged.Count(x => x == nameof(VideoEffectBase.IsEnabled)) >= 2);
            Check("assist_active_after_reenable", IsAssistActive(voice));

            var corrected2 = await ReconcileAsync("corrected-2", voice, speaker, parameter, voicePath);
            Check("reenable_reapplies_correction", corrected2.Active && corrected2.Pause == 0.0);
            Check("reenable_restores_same_corrected_wav", corrected2.WavSha256 == corrected1.WavSha256);

            voiceChanged.Clear();
            voice.JimakuVideoEffects = voice.JimakuVideoEffects.Remove(effect);
            Check("remove_notification_observed",
                voiceChanged.Contains(nameof(VoiceItem.JimakuVideoEffects)));
            Check("assist_inactive_when_removed", !IsAssistActive(voice));

            var baseline2 = await ReconcileAsync("baseline-removed", voice, speaker, parameter, voicePath);
            Check("remove_restores_baseline_pronounce", !baseline2.Active && baseline2.Pause > 0.0);
            Check("remove_restores_same_baseline_wav", baseline2.WavSha256 == baseline1.WavSha256);
            Check("corrected_and_baseline_hashes_distinct", corrected1.WavSha256 != baseline1.WavSha256);

            File.WriteAllText(
                Path.Combine(output, "assist-lifecycle-observation.json"),
                JsonSerializer.Serialize(new
                {
                    host = "4.56.1.0 Lite",
                    voicePath,
                    notifications = new
                    {
                        voice = voiceChanged,
                        effect = effectChanged
                    },
                    states = new[] { corrected1, baseline1, corrected2, baseline2 }
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            registration.Restore();
        }
    }

    static bool IsAssistActive(VoiceItem voice) =>
        voice.JimakuVideoEffects
            .OfType<AssistLifecycleEffect>()
            .Any(x => x.IsEnabled && x.AssistEnabled);

    static async Task<State> ReconcileAsync(
        string label,
        VoiceItem voice,
        IVoiceSpeaker speaker,
        IVoiceParameter parameter,
        string voicePath)
    {
        var active = IsAssistActive(voice);
        IVoicePronounce result;

        if (active)
        {
            var analysisPath = Path.Combine(output, label + "-analysis.wav");
            var fresh = await speaker.CreateVoiceAsync(
                voice.Hatsuon,
                pronounce: null,
                parameter,
                analysisPath)
                ?? throw new InvalidOperationException(label + ": fresh Pronounce was null.");
            SetPauseVowelLength(GetAudioQuery(fresh), 0.0);
            result = await speaker.CreateVoiceAsync(
                voice.Hatsuon,
                fresh,
                parameter,
                voicePath)
                ?? throw new InvalidOperationException(label + ": corrected Pronounce was null.");
        }
        else
        {
            result = await speaker.CreateVoiceAsync(
                voice.Hatsuon,
                pronounce: null,
                parameter,
                voicePath)
                ?? throw new InvalidOperationException(label + ": baseline Pronounce was null.");
        }

        voice.ClearVoiceCache();
        voice.Pronounce = result;

        return new State(
            label,
            active,
            GetPauseVowelLength(GetAudioQuery(result)),
            HashFile(voicePath),
            new FileInfo(voicePath).Length);
    }

    static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    static object GetAudioQuery(IVoicePronounce pronounce)
    {
        var p = pronounce.GetType().GetProperty("AudioQuery", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(pronounce.GetType().FullName, "AudioQuery");
        return p.GetValue(pronounce)
            ?? throw new InvalidOperationException("AudioQuery was null.");
    }

    static double GetPauseVowelLength(object query)
    {
        var phrasesProperty = query.GetType().GetProperty("AccentPhrases", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(query.GetType().FullName, "AccentPhrases");
        var phrases = phrasesProperty.GetValue(query) as System.Collections.IEnumerable
            ?? throw new InvalidOperationException("AccentPhrases was not enumerable.");
        var phrase = phrases.Cast<object>().FirstOrDefault()
            ?? throw new InvalidOperationException("AccentPhrases was empty.");
        var pauseProperty = phrase.GetType().GetProperty("PauseMora", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(phrase.GetType().FullName, "PauseMora");
        var pause = pauseProperty.GetValue(phrase)
            ?? throw new InvalidOperationException("PauseMora was null.");
        var valueProperty = pause.GetType().GetProperty("VowelLength", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(pause.GetType().FullName, "VowelLength");
        return Convert.ToDouble(valueProperty.GetValue(pause), CultureInfo.InvariantCulture);
    }

    static void SetPauseVowelLength(object query, double value)
    {
        var phrasesProperty = query.GetType().GetProperty("AccentPhrases", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(query.GetType().FullName, "AccentPhrases");
        var phrases = phrasesProperty.GetValue(query) as System.Collections.IEnumerable
            ?? throw new InvalidOperationException("AccentPhrases was not enumerable.");
        var phrase = phrases.Cast<object>().FirstOrDefault()
            ?? throw new InvalidOperationException("AccentPhrases was empty.");
        var pauseProperty = phrase.GetType().GetProperty("PauseMora", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(phrase.GetType().FullName, "PauseMora");
        var pause = pauseProperty.GetValue(phrase)
            ?? throw new InvalidOperationException("PauseMora was null.");
        var valueProperty = pause.GetType().GetProperty("VowelLength", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(pause.GetType().FullName, "VowelLength");
        if (valueProperty.SetMethod?.IsPublic != true)
            throw new InvalidOperationException("VowelLength was not publicly writable.");
        valueProperty.SetValue(pause, value);
    }

    sealed record SettingsRegistration(string SettingsType, object? ResolvedEngine, Action Restore);

    static SettingsRegistration RegisterEngineInYmmSettings(VOICEVOXEngine engine, string speakerId)
    {
        var settingsType = typeof(VOICEVOXEngine).Assembly.GetType("YukkuriMovieMaker.Settings.VOICEVOXSettings")
            ?? throw new InvalidOperationException("VOICEVOXSettings type not found.");

        var defaultProperty = settingsType.GetProperty(
            "Default",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?? settingsType.BaseType?.GetProperty(
                "Default",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?? throw new MissingMemberException(settingsType.FullName, "Default");

        var settings = defaultProperty.GetValue(null)
            ?? throw new InvalidOperationException("VOICEVOXSettings.Default null.");

        var enginesProperty = settingsType.GetProperty("Engines", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(settingsType.FullName, "Engines");
        var original = enginesProperty.GetValue(settings)
            ?? throw new InvalidOperationException("Engines null.");

        var add = original.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == "Add"
                              && m.GetParameters().Length == 1
                              && m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(VOICEVOXEngine)))
            ?? throw new MissingMethodException(original.GetType().FullName, "Add");

        var augmented = add.Invoke(original, [engine])
            ?? throw new InvalidOperationException("Engines.Add null.");
        enginesProperty.SetValue(settings, augmented);

        var findEngine = settingsType.GetMethod(
            "FindEngine",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(string)],
            modifiers: null)
            ?? throw new MissingMethodException(settingsType.FullName, "FindEngine");

        var resolved = findEngine.Invoke(settings, [speakerId]);

        return new SettingsRegistration(
            settingsType.FullName ?? settingsType.Name,
            resolved,
            () => { try { enginesProperty.SetValue(settings, original); } catch { } });
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
                try { if (p.GetValue(active) is Timeline pt) return pt; } catch { }
            }
        }
        return null;
    }

    static void Check(string id, bool passed) =>
        requirements.Add(new { id, passed });

    static void Write(string status, string? error)
    {
        File.WriteAllText(
            Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(new
            {
                schema = "cnwl.voiceitem-assist-disable-remove.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
