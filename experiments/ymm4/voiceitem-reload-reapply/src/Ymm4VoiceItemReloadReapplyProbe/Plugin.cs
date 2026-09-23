using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
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

namespace Ymm4VoiceItemReloadReapplyProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VoiceItem Reload Reapply Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

[VideoEffect("CNWL reload reapply assist", ["CNWL"], [])]
public sealed class ReloadReapplyAssistEffect : VideoEffectBase
{
    public override string Label => "CNWL reload reapply assist";
    public bool AssistEnabled { get; set; } = true;
    public string ProbeTag { get; set; } = "CNWL-RELOAD-REAPPLY-A";

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
    const string SerifA = "前<w0>後";
    const string HatsuonA = "ア";
    const string Remark = "CNWL_RELOAD_REAPPLY";
    const string ProbeTagA = "CNWL-RELOAD-REAPPLY-A";

    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    static void Log(string message) =>
        File.AppendAllText(
            Path.Combine(output, "progress.txt"),
            DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_RELOAD_REAPPLY_OUTPUT");
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

                var active = GetActive(main);
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
                await RunAsync(main, url);
                Write("PASS_VOICEITEM_RELOAD_REAPPLY", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_VOICEITEM_RELOAD_REAPPLY", ex.ToString());
            }
        };

        timer.Start();
    }

    static async Task RunAsync(object main, string url)
    {
        Log("run-start");
        var active = GetActive(main)
            ?? throw new InvalidOperationException("ActiveTimelineViewModel missing.");
        var timeline = FindTimeline(active)
            ?? throw new InvalidOperationException("Timeline missing.");
        Check("timeline_resolved", true);

        var engine = new VOICEVOXEngine(new VOICEVOXEngineContext())
        {
            Name = "CNWL Fake VOICEVOX",
            URL = url,
            Path = "",
            Timeout = 10_000
        };

        engine.EngineManifestJsonCache = new JObject
        {
            ["manifest_version"] = "0.13.1",
            ["name"] = "CNWL Fake",
            ["brand_name"] = "CNWL",
            ["uuid"] = "00000000-0000-0000-0000-000000000001",
            ["version"] = "0.0.0",
            ["url"] = "https://example.invalid",
            ["command"] = "",
            ["port"] = 50126,
            ["icon"] = "",
            ["default_sampling_rate"] = 24000,
            ["frame_rate"] = 93.75,
            ["terms_of_service"] = "",
            ["update_infos"] = new JArray(),
            ["dependency_licenses"] = new JArray(),
            ["supported_features"] = new JObject()
        }.ToString(Newtonsoft.Json.Formatting.None);

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
        Check("fake_engine_character_resolved",
            engine.Characters.Any(x => x.SpeakerUuid == fakeSpeakerUuid));
        var vvCharacter = new VOICEVOXCharacter(
            speakerJson,
            Array.Empty<VOICEVOXSpeakerInfo>(),
            false);
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
                registration.ResolvedEngine is not null
                && ReferenceEquals(registration.ResolvedEngine, engine));
            Log("engine-registered");

            var parameter = speaker.CreateVoiceParameter();
            parameter.GetType().GetProperty("StyleID", BindingFlags.Instance | BindingFlags.Public)
                ?.SetValue(parameter, 1);

            var voiceDescription = new VoiceDescription(speaker);
            var character = new Character
            {
                Name = "CNWL Reload",
                Voice = voiceDescription,
                VoiceParameter = parameter
            };

            var voice = new VoiceItem
            {
                Serif = SerifA,
                Hatsuon = HatsuonA,
                CharacterName = character.Name,
                VoiceParameter = parameter,
                Remark = Remark
            };
            voice.Character = character;
            voice.VoiceParameter = parameter;
            voice.Serif = SerifA;
            voice.Hatsuon = HatsuonA;
            voice.CharacterName = character.Name;

            var effect = new ReloadReapplyAssistEffect
            {
                AssistEnabled = true,
                IsEnabled = true,
                ProbeTag = ProbeTagA
            };
            AppendEffect(voice, effect);

            Check("voice_added_to_real_timeline", timeline.TryAddItems([voice], 240, 6));
            Check("durable_source_present_before_save",
                IsDurableSourceReady(voice));

            Log("initial-generate-start");
            await voice.CreateVoiceFileAsync();
            Log("initial-generate-done");
            Check("initial_voice_file_generated",
                !string.IsNullOrWhiteSpace(voice.FilePath)
                && File.Exists(voice.FilePath)
                && new FileInfo(voice.FilePath).Length > 44);

            // Persistence fixture boundary:
            // the fake VOICEVOX provider is not an installed persistent engine.
            // Save/reload should exercise only the durable correction source.
            var persistenceCharacter = new Character { Name = character.Name };
            voice.Character = persistenceCharacter;
            voice.CharacterName = persistenceCharacter.Name;
            voice.Serif = SerifA;
            voice.Hatsuon = HatsuonA;
            Check("synthetic_speaker_detached_before_save",
                voice.Character?.Voice?.Speaker is null);
            Log("synthetic-speaker-detached-before-save");

            var saveProject = PublicMethod(main, "SaveProject", typeof(string));
            var openProject = PublicMethod(main, "OpenProject", typeof(string));

            var pathA = Path.Combine(output, "reapply-a.ymmp");
            Log("save-a-start");
            saveProject.Invoke(main, [pathA]);
            await WaitUntil("save A", () => File.Exists(pathA) && new FileInfo(pathA).Length > 0);
            Log("save-a-done");
            Check("project_a_saved", true);

            voice.Serif = "壊したB";
            var liveEffect = GetAssistEffect(voice)
                ?? throw new InvalidOperationException("Assist effect missing before B mutation.");
            liveEffect.AssistEnabled = false;
            liveEffect.IsEnabled = false;
            liveEffect.ProbeTag = "CNWL-RELOAD-REAPPLY-B";
            var pathB = Path.Combine(output, "reapply-b.ymmp");
            Log("save-b-start");
            saveProject.Invoke(main, [pathB]);
            await WaitUntil("save B", () => File.Exists(pathB) && new FileInfo(pathB).Length > 0);
            Log("save-b-done");
            Check("project_b_saved", true);

            Log($"open-a-start return={openProject.ReturnType.FullName}");
            openProject.Invoke(main, [pathA]);
            Log("open-a-invoke-returned");
            await WaitUntil(
                "open A",
                () => SamePath(GetProjectFilePath(main), pathA)
                   && FindVoice(main, Remark) is not null,
                12000);
            Log("open-a-done");
            VoiceItem reloaded = FindVoice(main, Remark)
                ?? throw new InvalidOperationException("Reloaded VoiceItem missing.");
            Check("reloaded_is_new_object", !ReferenceEquals(voice, reloaded));
            Check("durable_source_survives_reload", IsDurableSourceReady(reloaded));
            Check("pronounce_is_transient_after_reload", reloaded.Pronounce is null);

            Log("reload-state-validated");
            Check("reloaded_project_has_no_synthetic_speaker",
                reloaded.Character?.Voice?.Speaker is null);

            // Rebind only the synthetic test provider after native project reload.
            // Real installed YMM4 voice providers own their own persistence/resolution.
            reloaded.Character = character;
            reloaded.CharacterName = character.Name;
            reloaded.VoiceParameter = parameter;
            reloaded.Hatsuon = HatsuonA;
            reloaded.Serif = SerifA;

            var reloadedSpeaker = reloaded.Character?.Voice?.Speaker;
            Check("fixture_speaker_rebound_for_reapply",
                reloadedSpeaker is not null && reloadedSpeaker.ID == speaker.ID);
            if (reloadedSpeaker is null)
                throw new InvalidOperationException("Fixture speaker rebind failed.");
            Log("fixture-speaker-rebound");

            var reloadedParameter = reloaded.VoiceParameter
                ?? reloaded.Character?.VoiceParameter
                ?? parameter;
            Check("reloaded_voice_parameter_available", reloadedParameter is not null);

            if (string.IsNullOrWhiteSpace(reloaded.FilePath) || !File.Exists(reloaded.FilePath))
                await reloaded.CreateVoiceFileAsync();

            var voicePath = reloaded.FilePath;
            Check("reloaded_voice_file_available",
                !string.IsNullOrWhiteSpace(voicePath) && File.Exists(voicePath));
            if (string.IsNullOrWhiteSpace(voicePath) || !File.Exists(voicePath))
                throw new InvalidOperationException("Reloaded VoiceItem file unavailable.");

            // Controller-style reapply: the durable marker/effect source decides
            // whether the transient pronunciation should be rebuilt.
            Check("controller_reapply_condition_true", IsDurableSourceReady(reloaded));

            var analysisPath = Path.Combine(output, "reload-analysis.wav");
            Log("fresh-analysis-start");
            var freshPronounce = await reloadedSpeaker.CreateVoiceAsync(
                reloaded.Hatsuon,
                pronounce: null,
                reloadedParameter,
                analysisPath)
                ?? throw new InvalidOperationException("Fresh Pronounce was null.");
            Log("fresh-analysis-done");
            var freshQuery = GetAudioQuery(freshPronounce);
            var freshPause = GetPauseVowelLength(freshQuery);
            Check("fresh_pronounce_reanalyzed_after_reload", freshPause > 0.0);

            ResolvePersistedCorrectionAndApply(reloaded, freshQuery);
            Check("persisted_correction_resolved_to_zero",
                GetPauseVowelLength(freshQuery) == 0.0);

            Log("corrected-synthesis-start");
            var regenerated = await reloadedSpeaker.CreateVoiceAsync(
                reloaded.Hatsuon,
                freshPronounce,
                reloadedParameter,
                voicePath)
                ?? throw new InvalidOperationException("Regenerated Pronounce was null.");

            Log("corrected-synthesis-done");
            reloaded.ClearVoiceCache();
            reloaded.Pronounce = regenerated;

            var finalPause = GetPauseVowelLength(GetAudioQuery(regenerated));
            Check("reapplied_pronounce_attached", ReferenceEquals(reloaded.Pronounce, regenerated));
            Check("reapplied_pause_zero", finalPause == 0.0);
            Check("reapplied_voice_file_exists",
                File.Exists(voicePath) && new FileInfo(voicePath).Length > 44);

            File.WriteAllText(
                Path.Combine(output, "reload-reapply-observation.json"),
                JsonSerializer.Serialize(new
                {
                    host = "4.56.1.0 Lite",
                    speakerId = speaker.ID,
                    reloaded = new
                    {
                        sameObject = ReferenceEquals(voice, reloaded),
                        reloaded.Serif,
                        reloaded.Hatsuon,
                        pronounceBeforeReapplyWasNull = true,
                        speakerId = reloadedSpeaker.ID,
                        effect = GetAssistEffect(reloaded) is { } e ? new
                        {
                            e.AssistEnabled,
                            e.IsEnabled,
                            e.ProbeTag
                        } : null
                    },
                    reapply = new
                    {
                        freshPause,
                        finalPause,
                        voicePath,
                        voiceFileLength = new FileInfo(voicePath).Length,
                        pronounceType = regenerated.GetType().FullName
                    }
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            registration.Restore();
        }
    }

    static bool IsDurableSourceReady(VoiceItem voice) =>
        voice.Serif == SerifA
        && voice.Serif.Contains("<w0>", StringComparison.Ordinal)
        && voice.Hatsuon == HatsuonA
        && GetAssistEffect(voice) is { AssistEnabled: true, IsEnabled: true } effect
        && effect.ProbeTag == ProbeTagA;

    static void ResolvePersistedCorrectionAndApply(VoiceItem voice, object query)
    {
        if (!IsDurableSourceReady(voice))
            throw new InvalidOperationException("Durable correction source not ready.");
        SetPauseVowelLength(query, 0.0);
    }

    static VoiceItem? FindVoice(object main, string remark)
    {
        var active = GetActive(main);
        var timeline = active is null ? null : FindTimeline(active);
        return timeline?.Items.OfType<VoiceItem>().FirstOrDefault(x => x.Remark == remark);
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
        && string.Equals(Path.GetFullPath(actual), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);

    static void AppendEffect(VoiceItem voice, ReloadReapplyAssistEffect effect)
    {
        var prop = typeof(VoiceItem).GetProperty("JimakuVideoEffects", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(typeof(VoiceItem).FullName, "JimakuVideoEffects");
        var current = prop.GetValue(voice)
            ?? throw new InvalidOperationException("JimakuVideoEffects null.");

        if (current is IList list && !list.IsReadOnly && !list.IsFixedSize)
        {
            list.Add(effect);
            return;
        }

        var add = current.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 1
                && (m.GetParameters()[0].ParameterType.IsAssignableFrom(effect.GetType())
                    || m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(IVideoEffect))))
            ?? throw new MissingMethodException(current.GetType().FullName, "Add");
        var updated = add.Invoke(current, [effect]);
        if (updated is not null && !ReferenceEquals(updated, current))
        {
            if (prop.SetMethod?.IsPublic != true)
                throw new InvalidOperationException("JimakuVideoEffects setter unavailable.");
            prop.SetValue(voice, updated);
        }
    }

    static ReloadReapplyAssistEffect? GetAssistEffect(VoiceItem voice)
    {
        var prop = typeof(VoiceItem).GetProperty("JimakuVideoEffects", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(typeof(VoiceItem).FullName, "JimakuVideoEffects");
        return (prop.GetValue(voice) as IEnumerable)?
            .Cast<object>()
            .OfType<ReloadReapplyAssistEffect>()
            .FirstOrDefault();
    }

    static object GetAudioQuery(IVoicePronounce pronounce)
    {
        var p = pronounce.GetType().GetProperty("AudioQuery", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(pronounce.GetType().FullName, "AudioQuery");
        return p.GetValue(pronounce)
            ?? throw new InvalidOperationException("AudioQuery null.");
    }

    static double GetPauseVowelLength(object query)
    {
        var phrasesProperty = query.GetType().GetProperty("AccentPhrases", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(query.GetType().FullName, "AccentPhrases");
        var phrases = phrasesProperty.GetValue(query) as IEnumerable
            ?? throw new InvalidOperationException("AccentPhrases not enumerable.");
        var phrase = phrases.Cast<object>().FirstOrDefault()
            ?? throw new InvalidOperationException("AccentPhrases empty.");
        var pauseProperty = phrase.GetType().GetProperty("PauseMora", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(phrase.GetType().FullName, "PauseMora");
        var pause = pauseProperty.GetValue(phrase)
            ?? throw new InvalidOperationException("PauseMora null.");
        var valueProperty = pause.GetType().GetProperty("VowelLength", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(pause.GetType().FullName, "VowelLength");
        return Convert.ToDouble(valueProperty.GetValue(pause), CultureInfo.InvariantCulture);
    }

    static void SetPauseVowelLength(object query, double value)
    {
        var phrasesProperty = query.GetType().GetProperty("AccentPhrases", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(query.GetType().FullName, "AccentPhrases");
        var phrases = phrasesProperty.GetValue(query) as IEnumerable
            ?? throw new InvalidOperationException("AccentPhrases not enumerable.");
        var phrase = phrases.Cast<object>().FirstOrDefault()
            ?? throw new InvalidOperationException("AccentPhrases empty.");
        var pauseProperty = phrase.GetType().GetProperty("PauseMora", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(phrase.GetType().FullName, "PauseMora");
        var pause = pauseProperty.GetValue(phrase)
            ?? throw new InvalidOperationException("PauseMora null.");
        var valueProperty = pause.GetType().GetProperty("VowelLength", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(pause.GetType().FullName, "VowelLength");
        if (valueProperty.SetMethod?.IsPublic != true)
            throw new InvalidOperationException("VowelLength not writable.");
        valueProperty.SetValue(pause, value);
    }

    sealed record SettingsRegistration(string SettingsType, object? ResolvedEngine, Action Restore);

    static SettingsRegistration RegisterEngineInYmmSettings(VOICEVOXEngine engine, string speakerId)
    {
        var settingsType = typeof(VOICEVOXEngine).Assembly.GetType("YukkuriMovieMaker.Settings.VOICEVOXSettings")
            ?? throw new InvalidOperationException("VOICEVOXSettings type not found.");
        var defaultProperty = settingsType.GetProperty(
            "Default", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?? settingsType.BaseType?.GetProperty(
                "Default", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)
            ?? throw new MissingMemberException(settingsType.FullName, "Default");
        var settings = defaultProperty.GetValue(null)
            ?? throw new InvalidOperationException("VOICEVOXSettings.Default null.");
        var enginesProperty = settingsType.GetProperty("Engines", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(settingsType.FullName, "Engines");
        var original = enginesProperty.GetValue(settings)
            ?? throw new InvalidOperationException("Engines null.");
        var add = original.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(VOICEVOXEngine)))
            ?? throw new MissingMethodException(original.GetType().FullName, "Add");
        var augmented = add.Invoke(original, [engine])
            ?? throw new InvalidOperationException("Engines.Add null.");
        enginesProperty.SetValue(settings, augmented);
        var findEngine = settingsType.GetMethod(
            "FindEngine", BindingFlags.Instance | BindingFlags.Public,
            binder: null, types: [typeof(string)], modifiers: null)
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

    static async Task WaitUntil(string name, Func<bool> condition, int timeoutMs = 10000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return;
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
                schema = "cnwl.voiceitem-reload-reapply.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
