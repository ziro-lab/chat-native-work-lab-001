using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Voice;

namespace Ymm4VoiceItemAudioRefreshSurfaceProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VoiceItem Audio Refresh Surface Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_VOICEITEM_AUDIO_REFRESH_OUTPUT");
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
                Write("PASS_VOICEITEM_AUDIO_REFRESH_SURFACE", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_VOICEITEM_AUDIO_REFRESH_SURFACE", ex.ToString());
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
            ?? throw new TypeLoadException("VOICEVOXVoiceSpeaker");
        var speakerObject = Activator.CreateInstance(speakerType, engine, vvCharacter)
            ?? throw new InvalidOperationException("VOICEVOXVoiceSpeaker construction failed.");
        if (speakerObject is not IVoiceSpeaker speaker)
            throw new InvalidOperationException("Built-in VOICEVOX speaker interface missing.");
        Check("builtin_voicevox_speaker_constructed", true);

        var registration = RegisterEngineInYmmSettings(engine, speaker.ID);
        try
        {
            Check("fake_engine_registered",
                registration.ResolvedEngine is not null
                && ReferenceEquals(registration.ResolvedEngine, engine));

            var parameter = speaker.CreateVoiceParameter();
            parameter.GetType().GetProperty("StyleID", BindingFlags.Instance | BindingFlags.Public)
                ?.SetValue(parameter, 1);

            var character = new Character
            {
                Name = "CNWL Refresh",
                Voice = new VoiceDescription(speaker),
                VoiceParameter = parameter
            };

            var voice = new VoiceItem
            {
                Serif = "ア",
                Hatsuon = "ア",
                CharacterName = character.Name,
                VoiceParameter = parameter,
                Remark = "CNWL_AUDIO_REFRESH"
            };
            voice.Character = character;
            voice.VoiceParameter = parameter;
            voice.Serif = "ア";
            voice.Hatsuon = "ア";
            voice.CharacterName = character.Name;

            Check("voice_added_to_real_timeline", timeline.TryAddItems([voice], 240, 6));
            Check("voice_present_in_real_timeline", timeline.Items.Any(x => ReferenceEquals(x, voice)));

            var voiceChanged = new List<string>();
            if (voice is INotifyPropertyChanged voiceNpc)
                voiceNpc.PropertyChanged += (_, e) => voiceChanged.Add(e.PropertyName ?? "<null>");

            var timelineChanged = new List<string>();
            if (timeline is INotifyPropertyChanged timelineNpc)
                timelineNpc.PropertyChanged += (_, e) => timelineChanged.Add(e.PropertyName ?? "<null>");

            await voice.CreateVoiceFileAsync();
            var voicePath = voice.FilePath;
            Check("real_voiceitem_file_path_available",
                !string.IsNullOrWhiteSpace(voicePath) && File.Exists(voicePath));
            if (string.IsNullOrWhiteSpace(voicePath) || !File.Exists(voicePath))
                throw new InvalidOperationException("VoiceItem FilePath unavailable.");

            var baselineHash = HashFile(voicePath);
            var baselineLength = new FileInfo(voicePath).Length;

            var analysisPath = Path.Combine(output, "analysis.wav");
            var corrected = await speaker.CreateVoiceAsync(
                voice.Hatsuon,
                pronounce: null,
                parameter,
                analysisPath)
                ?? throw new InvalidOperationException("Fresh Pronounce was null.");

            var query = GetAudioQuery(corrected);
            var baselinePause = GetPauseVowelLength(query);
            Check("fresh_analysis_pause_nonzero", baselinePause > 0.0);

            SetPauseVowelLength(query, 0.0);
            var regenerated = await speaker.CreateVoiceAsync(
                voice.Hatsuon,
                corrected,
                parameter,
                voicePath)
                ?? throw new InvalidOperationException("Corrected Pronounce was null.");

            var correctedHash = HashFile(voicePath);
            var correctedLength = new FileInfo(voicePath).Length;
            Check("corrected_wav_replaced_real_file",
                baselineHash != correctedHash && correctedLength > 44);

            var clearMethod = typeof(VoiceItem).GetMethod(
                "ClearVoiceCache",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            Check("clear_voice_cache_public", clearMethod is not null);

            var cacheProperty = typeof(VoiceItem).GetProperty(
                "VoiceCache",
                BindingFlags.Instance | BindingFlags.Public);
            Check("voice_cache_property_public",
                cacheProperty?.GetMethod?.IsPublic == true);

            bool sentinelSeeded = false;
            object? cacheBeforeClear = null;
            object? cacheAfterClear = null;
            if (cacheProperty is not null)
            {
                sentinelSeeded = TrySeedCacheSentinel(cacheProperty, voice);
                cacheBeforeClear = SafeGet(cacheProperty, voice);
            }

            Check("voice_cache_sentinel_seeded", sentinelSeeded);
            Check("voice_cache_nonempty_before_clear", !IsCacheEmpty(cacheBeforeClear));

            voice.ClearVoiceCache();
            if (cacheProperty is not null)
                cacheAfterClear = SafeGet(cacheProperty, voice);

            Check("voice_cache_empty_after_clear", IsCacheEmpty(cacheAfterClear));
            Check("clear_cache_preserves_corrected_wav",
                HashFile(voicePath) == correctedHash);

            voice.Pronounce = regenerated;
            Check("corrected_pronounce_attached",
                ReferenceEquals(voice.Pronounce, regenerated)
                && GetPauseVowelLength(GetAudioQuery(regenerated)) == 0.0);

            var beforeFrame = timeline.CurrentFrame;
            var frameA = Math.Max(0, voice.Frame);
            var frameB = frameA + 1;
            timeline.CurrentFrame = frameA;
            timeline.CurrentFrame = frameB;
            timeline.CurrentFrame = frameA;

            var currentFrameEvents = timelineChanged.Count(x => x == nameof(Timeline.CurrentFrame));
            Check("timeline_currentframe_propertychanged_observed",
                currentFrameEvents >= 2);

            var refreshCandidates = PublicRefreshCandidates().ToArray();
            Check("plugin_refresh_surface_inventoried", true);

            File.WriteAllText(
                Path.Combine(output, "audio-refresh-observation.json"),
                JsonSerializer.Serialize(new
                {
                    host = "4.56.1.0 Lite",
                    audio = new
                    {
                        baselineLength,
                        correctedLength,
                        baselineSha256 = baselineHash,
                        correctedSha256 = correctedHash,
                        baselinePause,
                        correctedPause = GetPauseVowelLength(GetAudioQuery(regenerated))
                    },
                    cache = new
                    {
                        propertyType = cacheProperty?.PropertyType.FullName,
                        publicGet = cacheProperty?.GetMethod?.IsPublic == true,
                        publicSet = cacheProperty?.SetMethod?.IsPublic == true,
                        sentinelSeeded,
                        beforeClear = DescribeCache(cacheBeforeClear),
                        afterClear = DescribeCache(cacheAfterClear),
                        clearMethodPublic = clearMethod is not null
                    },
                    notifications = new
                    {
                        voicePropertyChanged = voiceChanged,
                        timelinePropertyChanged = timelineChanged,
                        currentFrameEventCount = currentFrameEvents,
                        beforeFrame,
                        finalFrame = timeline.CurrentFrame
                    },
                    publicRefreshCandidates = refreshCandidates
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            registration.Restore();
        }
    }

    static IEnumerable<object> PublicRefreshCandidates()
    {
        var types = new[]
        {
            typeof(Timeline),
            typeof(TimelineToolInfo)
        };

        foreach (var type in types)
        {
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (method.IsSpecialName)
                    continue;

                var n = method.Name;
                if (n.Contains("Refresh", StringComparison.OrdinalIgnoreCase)
                    || n.Contains("Redraw", StringComparison.OrdinalIgnoreCase)
                    || n.Contains("Invalidate", StringComparison.OrdinalIgnoreCase)
                    || n.Contains("Preview", StringComparison.OrdinalIgnoreCase)
                    || n.Contains("Render", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new
                    {
                        declaringType = type.FullName,
                        method = n,
                        returnType = method.ReturnType.FullName,
                        parameters = method.GetParameters().Select(p => p.ParameterType.FullName).ToArray()
                    };
                }
            }
        }
    }

    static bool TrySeedCacheSentinel(PropertyInfo property, VoiceItem voice)
    {
        if (property.SetMethod?.IsPublic != true)
            return false;

        object? value = property.PropertyType == typeof(string)
            ? "CNWL_STALE_CACHE"
            : property.PropertyType == typeof(byte[])
                ? new byte[] { 1, 2, 3, 4, 5 }
                : property.PropertyType.IsArray
                    ? Array.CreateInstance(property.PropertyType.GetElementType()!, 1)
                    : null;

        if (value is null)
            return false;

        property.SetValue(voice, value);
        return !IsCacheEmpty(SafeGet(property, voice));
    }

    static object? SafeGet(PropertyInfo property, object target)
    {
        try { return property.GetValue(target); }
        catch { return null; }
    }

    static bool IsCacheEmpty(object? value) => value switch
    {
        null => true,
        string s => s.Length == 0,
        Array a => a.Length == 0,
        ICollection c => c.Count == 0,
        _ => false
    };

    sealed record CacheDescription(string Type, int Length);

    static CacheDescription DescribeCache(object? value) => value switch
    {
        null => new CacheDescription("<null>", 0),
        string s => new CacheDescription(typeof(string).FullName ?? nameof(String), s.Length),
        Array a => new CacheDescription(a.GetType().FullName ?? a.GetType().Name, a.Length),
        ICollection collection => new CacheDescription(
            collection.GetType().FullName ?? collection.GetType().Name,
            collection.Count),
        _ => new CacheDescription(value.GetType().FullName ?? value.GetType().Name, -1)
    };

    static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

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
            .FirstOrDefault(m => m.Name == "Add"
                && m.GetParameters().Length == 1
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
                try { if (p.GetValue(active) is Timeline pt) return pt; }
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
                schema = "cnwl.voiceitem-audio-refresh-surface.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
