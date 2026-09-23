using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Voice;

namespace Ymm4VoiceItemRegenerationLifecycleProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VoiceItem Regeneration Lifecycle Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_VOICEITEM_REGEN_OUTPUT");
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
                Write("PASS_VOICEITEM_REGENERATION_LIFECYCLE", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_VOICEITEM_REGENERATION_LIFECYCLE", ex.ToString());
            }
        };

        timer.Start();
    }

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
        Check("fake_engine_character_resolved",
            engine.Characters.Any(x => x.SpeakerUuid == fakeSpeakerUuid));

        var vvCharacter = new VOICEVOXCharacter(
            speakerJson,
            Array.Empty<VOICEVOXSpeakerInfo>(),
            false);

        var speakerType = typeof(VOICEVOXEngine).Assembly.GetType("YukkuriMovieMaker.Voice.VOICEVOXVoiceSpeaker")
            ?? throw new InvalidOperationException("VOICEVOXVoiceSpeaker type not found.");
        var speakerObject = Activator.CreateInstance(speakerType, engine, vvCharacter)
            ?? throw new InvalidOperationException("VOICEVOXVoiceSpeaker construction failed.");
        if (speakerObject is not IVoiceSpeaker speaker)
            throw new InvalidOperationException("Built-in VOICEVOX speaker does not implement IVoiceSpeaker.");
        Check("builtin_voicevox_speaker_constructed", true);

        var registration = RegisterEngineInYmmSettings(engine, speaker.ID);
        try
        {
            Check("fake_engine_registered",
                registration.ResolvedEngine is not null &&
                ReferenceEquals(registration.ResolvedEngine, engine));

            var parameter = speaker.CreateVoiceParameter();
            var styleProperty = parameter.GetType().GetProperty("StyleID", BindingFlags.Instance | BindingFlags.Public);
            styleProperty?.SetValue(parameter, 1);

            var voiceDescription = new VoiceDescription(speaker);
            Check("voice_description_binds_speaker",
                ReferenceEquals(voiceDescription.Speaker, speaker));

            var projectCharacter = new Character
            {
                Name = "CNWL Lifecycle",
                Voice = voiceDescription,
                VoiceParameter = parameter
            };

            var voice = new VoiceItem
            {
                Serif = "ア",
                Hatsuon = "ア",
                CharacterName = projectCharacter.Name,
                VoiceParameter = parameter
            };
            voice.Character = projectCharacter;
            voice.VoiceParameter = parameter;

            Check("voice_added_to_real_timeline", timeline.TryAddItems([voice], 240, 6));
            Check("voice_present_in_real_timeline", timeline.Items.Any(x => ReferenceEquals(x, voice)));
            Check("voice_uses_fake_character",
                ReferenceEquals(voice.Character, projectCharacter));

            Exception? initialError = null;
            try
            {
                await voice.CreateVoiceFileAsync();
            }
            catch (Exception ex)
            {
                initialError = ex;
            }
            Check("initial_voiceitem_generation_completed", initialError is null);
            if (initialError is not null)
                throw new InvalidOperationException("Initial VoiceItem generation failed.", initialError);

            var voicePath = voice.FilePath;
            Check("real_voiceitem_file_path_available",
                !string.IsNullOrWhiteSpace(voicePath) && File.Exists(voicePath));
            if (string.IsNullOrWhiteSpace(voicePath) || !File.Exists(voicePath))
                throw new InvalidOperationException("Real VoiceItem file path was unavailable after generation.");

            IVoicePronounce? baselinePronounce = null;
            Exception? analysisError = null;
            var analysisPath = Path.Combine(output, "baseline-analysis.wav");
            try
            {
                baselinePronounce = await speaker.CreateVoiceAsync(
                    voice.Hatsuon,
                    pronounce: null,
                    parameter,
                    analysisPath);
            }
            catch (Exception ex)
            {
                analysisError = ex;
            }
            Check("public_speaker_analysis_completed", analysisError is null);
            if (analysisError is not null)
                throw new InvalidOperationException("Public speaker baseline analysis failed.", analysisError);
            if (baselinePronounce is null)
                throw new InvalidOperationException("Public speaker returned null Pronounce.");

            voice.Pronounce = baselinePronounce;
            Check("pronounce_assigned_to_real_voiceitem",
                ReferenceEquals(voice.Pronounce, baselinePronounce));

            var initialQuery = GetAudioQuery(baselinePronounce);
            var initialPause = GetPauseVowelLength(initialQuery);
            Check("initial_pause_from_audio_query_nonzero", initialPause > 0.0);

            SetPauseVowelLength(initialQuery, 0.0);
            var patchedPause = GetPauseVowelLength(initialQuery);
            Check("patched_pause_is_zero", patchedPause == 0.0);

            IVoicePronounce? regeneratedPronounce = null;
            Exception? regenerationError = null;
            try
            {
                regeneratedPronounce = await speaker.CreateVoiceAsync(
                    voice.Hatsuon,
                    baselinePronounce,
                    parameter,
                    voicePath);
            }
            catch (Exception ex)
            {
                regenerationError = ex;
            }
            Check("public_speaker_regeneration_completed", regenerationError is null);
            if (regenerationError is not null)
                throw new InvalidOperationException("Public speaker regeneration failed.", regenerationError);
            if (regeneratedPronounce is null)
                throw new InvalidOperationException("Public speaker regeneration returned null Pronounce.");

            voice.ClearVoiceCache();
            voice.Pronounce = regeneratedPronounce;
            Check("regenerated_pronounce_assigned_to_real_voiceitem",
                ReferenceEquals(voice.Pronounce, regeneratedPronounce));

            var finalQuery = GetAudioQuery(regeneratedPronounce);
            var finalPause = GetPauseVowelLength(finalQuery);
            Check("patched_pause_survives_regeneration", finalPause == 0.0);
            Check("regenerated_real_voiceitem_file_exists",
                File.Exists(voicePath) && new FileInfo(voicePath).Length > 44);

            File.WriteAllText(
                Path.Combine(output, "lifecycle-observation.json"),
                JsonSerializer.Serialize(new
                {
                    host = "4.56.1.0 Lite",
                    speakerId = speaker.ID,
                    voiceDescription = new
                    {
                        voiceDescription.API,
                        voiceDescription.Arg,
                        voiceDescription.Display,
                        speakerType = voiceDescription.Speaker?.GetType().FullName
                    },
                    initial = new
                    {
                        pronounceType = baselinePronounce.GetType().FullName,
                        pauseVowelLength = initialPause,
                        voice.FilePath,
                        voiceFileLength = new FileInfo(voicePath).Length,
                        analysisFileLength = File.Exists(analysisPath) ? new FileInfo(analysisPath).Length : 0,
                        voiceCacheLength = voice.VoiceCache?.Length ?? 0
                    },
                    patched = new
                    {
                        pauseVowelLength = patchedPause
                    },
                    regenerated = new
                    {
                        pronounceType = regeneratedPronounce.GetType().FullName,
                        pauseVowelLength = finalPause,
                        samePronounceReference = ReferenceEquals(baselinePronounce, regeneratedPronounce),
                        voice.FilePath,
                        voiceFileLength = new FileInfo(voicePath).Length,
                        voiceCacheLength = voice.VoiceCache?.Length ?? 0
                    }
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            registration.Restore();
        }
    }

    static object GetAudioQuery(IVoicePronounce pronounce)
    {
        var p = pronounce.GetType().GetProperty("AudioQuery", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(pronounce.GetType().FullName, "AudioQuery");
        return p.GetValue(pronounce)
            ?? throw new InvalidOperationException("VOICEVOX AudioQuery was null.");
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
            throw new InvalidOperationException("VowelLength is not publicly writable.");
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
            ?? throw new InvalidOperationException("VOICEVOXSettings.Default returned null.");

        var enginesProperty = settingsType.GetProperty("Engines", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(settingsType.FullName, "Engines");
        if (enginesProperty.SetMethod?.IsPublic != true)
            throw new InvalidOperationException("VOICEVOXSettings.Engines is not publicly settable.");

        var original = enginesProperty.GetValue(settings)
            ?? throw new InvalidOperationException("VOICEVOXSettings.Engines returned null.");

        var add = original.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == "Add"
                              && m.GetParameters().Length == 1
                              && m.GetParameters()[0].ParameterType.IsAssignableFrom(typeof(VOICEVOXEngine)))
            ?? throw new MissingMethodException(original.GetType().FullName, "Add(VOICEVOXEngine)");

        var augmented = add.Invoke(original, [engine])
            ?? throw new InvalidOperationException("Immutable Engines.Add returned null.");
        enginesProperty.SetValue(settings, augmented);

        var findEngine = settingsType.GetMethod(
            "FindEngine",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(string)],
            modifiers: null)
            ?? throw new MissingMethodException(settingsType.FullName, "FindEngine(string)");

        var resolved = findEngine.Invoke(settings, [speakerId]);

        return new SettingsRegistration(
            settingsType.FullName ?? settingsType.Name,
            resolved,
            () =>
            {
                try { enginesProperty.SetValue(settings, original); }
                catch { }
            });
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

    static void Check(string id, bool passed) =>
        requirements.Add(new { id, passed });

    static void Write(string status, string? error)
    {
        File.WriteAllText(
            Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(new
            {
                schema = "cnwl.voiceitem-regeneration-lifecycle.v2",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
