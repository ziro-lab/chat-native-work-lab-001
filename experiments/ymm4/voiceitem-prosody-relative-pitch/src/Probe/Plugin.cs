using System.Collections;
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

namespace Ymm4VoiceItemProsodyRelativePitchProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VoiceItem Prosody Relative Pitch Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_PROSODY_OUTPUT");
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

                var main = Application.Current.Windows
                    .Cast<Window>()
                    .Select(x => x.DataContext)
                    .FirstOrDefault(x =>
                        x?.GetType().FullName
                        == "YukkuriMovieMaker.ViewModels.MainViewModel");

                if (main is null)
                    return;

                var active = main.GetType().GetProperty(
                    "ActiveTimelineViewModel",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(main);

                if (active is null)
                {
                    if (!created)
                    {
                        created = true;
                        main.GetType().GetMethod("CreateProject", Type.EmptyTypes)
                            ?.Invoke(main, null);
                    }

                    if (ticks > 160)
                        throw new TimeoutException("ActiveTimelineViewModel was not created.");
                    return;
                }

                timer.Stop();
                await RunAsync(active, url);
                Write("PASS_VOICEITEM_PROSODY_RELATIVE_PITCH", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_VOICEITEM_PROSODY_RELATIVE_PITCH", ex.ToString());
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
            Name = "CNWL Prosody VOICEVOX",
            URL = url,
            Path = "",
            Timeout = 10_000
        };

        const string fakeSpeakerUuid = "11111111-1111-1111-1111-111111111111";
        engine.SpeakerInfos.Add(new VOICEVOXSpeakerInfo(fakeSpeakerUuid, ""));

        var speakerJson = JObject.Parse("""
        {
          "name": "CNWL Prosody",
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

        engine.SpeakersJsonCache = new JArray(speakerJson)
            .ToString(Newtonsoft.Json.Formatting.None);
        Check("fake_engine_character_resolved",
            engine.Characters.Any(x => x.SpeakerUuid == fakeSpeakerUuid));

        var vvCharacter = new VOICEVOXCharacter(
            speakerJson,
            Array.Empty<VOICEVOXSpeakerInfo>(),
            false);

        var speakerType = typeof(VOICEVOXEngine).Assembly.GetType(
            "YukkuriMovieMaker.Voice.VOICEVOXVoiceSpeaker")
            ?? throw new TypeLoadException("VOICEVOXVoiceSpeaker");

        var speakerObject = Activator.CreateInstance(
            speakerType,
            engine,
            vvCharacter)
            ?? throw new InvalidOperationException(
                "VOICEVOXVoiceSpeaker construction failed.");

        if (speakerObject is not IVoiceSpeaker speaker)
            throw new InvalidOperationException(
                "Built-in VOICEVOX speaker does not implement IVoiceSpeaker.");

        Check("builtin_voicevox_speaker_constructed", true);

        var registration = RegisterEngineInYmmSettings(engine, speaker.ID);
        try
        {
            Check("fake_engine_registered",
                registration.ResolvedEngine is not null
                && ReferenceEquals(registration.ResolvedEngine, engine));

            var parameter = speaker.CreateVoiceParameter();
            parameter.GetType().GetProperty(
                "StyleID",
                BindingFlags.Instance | BindingFlags.Public)
                ?.SetValue(parameter, 1);

            var character = new Character
            {
                Name = "CNWL Prosody",
                Voice = new VoiceDescription(speaker),
                VoiceParameter = parameter
            };

            var rise = await RunPitchCase(
                timeline,
                character,
                speaker,
                parameter,
                "RISE",
                startFrame: 180,
                layer: 5,
                offsets: [-0.08, -0.04, 0.0, 0.04, 0.08]);

            var fall = await RunPitchCase(
                timeline,
                character,
                speaker,
                parameter,
                "FALL",
                startFrame: 360,
                layer: 7,
                offsets: [0.08, 0.04, 0.0, -0.04, -0.08]);

            File.WriteAllText(
                Path.Combine(output, "prosody-observation.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        host = "4.56.1.0 Lite",
                        speakerId = speaker.ID,
                        rise,
                        fall
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            registration.Restore();
        }
    }

    static async Task<object> RunPitchCase(
        Timeline timeline,
        Character character,
        IVoiceSpeaker speaker,
        IVoiceParameter parameter,
        string name,
        int startFrame,
        int layer,
        IReadOnlyList<double> offsets)
    {
        const string serif = "えええええ";
        const string hatsuon = "エエエエエ";

        if (offsets.Count != 5)
            throw new ArgumentException("Expected five pitch offsets.", nameof(offsets));

        var voice = CreateVoice(
            character,
            parameter,
            serif,
            hatsuon,
            "PROSODY_" + name);

        Check(
            name.ToLowerInvariant() + "_voice_added_to_timeline",
            timeline.TryAddItems([voice], startFrame, layer));

        await voice.CreateVoiceFileAsync();
        var path = RequireVoicePath(voice, name.ToLowerInvariant());
        Check(
            name.ToLowerInvariant() + "_baseline_file_exists",
            File.Exists(path));

        var persistedSerif = voice.Serif;
        var persistedHatsuon = voice.Hatsuon;

        var analysisPath = Path.Combine(
            output,
            name.ToLowerInvariant() + "-analysis.wav");

        var pronounce = await speaker.CreateVoiceAsync(
            hatsuon,
            pronounce: null,
            parameter,
            analysisPath)
            ?? throw new InvalidOperationException(
                name + " analysis returned null Pronounce.");

        var moras = GetFirstPhraseMoras(pronounce);
        Check(
            name.ToLowerInvariant() + "_mora_shape",
            MoraTexts(moras).SequenceEqual(
                new[] { "エ", "エ", "エ", "エ", "エ" }));

        var baselinePitches = moras
            .Select(x => GetDouble(x, "Pitch"))
            .ToArray();

        var baselineVowelLengths = moras
            .Select(x => GetDouble(x, "VowelLength"))
            .ToArray();

        Check(
            name.ToLowerInvariant() + "_baseline_pitch_shape",
            baselinePitches.SequenceEqual(
                new[] { 5.00, 5.08, 5.16, 5.08, 5.00 }));

        var expected = baselinePitches
            .Select((value, index) => value + offsets[index])
            .ToArray();

        for (var i = 0; i < moras.Count; i++)
            SetDouble(moras[i], "Pitch", expected[i]);

        Check(
            name.ToLowerInvariant() + "_relative_pitch_applied",
            moras.Select(x => GetDouble(x, "Pitch"))
                .Zip(expected)
                .All(x => Math.Abs(x.First - x.Second) < 0.000001));

        Check(
            name.ToLowerInvariant() + "_durations_preserved",
            moras.Select(x => GetDouble(x, "VowelLength"))
                .Zip(baselineVowelLengths)
                .All(x => Math.Abs(x.First - x.Second) < 0.000001));

        var regenerated = await speaker.CreateVoiceAsync(
            hatsuon,
            pronounce,
            parameter,
            path)
            ?? throw new InvalidOperationException(
                name + " synthesis returned null Pronounce.");

        voice.ClearVoiceCache();
        voice.Pronounce = regenerated;

        var finalMoras = GetFirstPhraseMoras(regenerated);
        var finalPitches = finalMoras
            .Select(x => GetDouble(x, "Pitch"))
            .ToArray();

        Check(
            name.ToLowerInvariant() + "_pitch_survives_synthesis",
            finalPitches.Zip(expected)
                .All(x => Math.Abs(x.First - x.Second) < 0.000001));

        Check(
            name.ToLowerInvariant() + "_persisted_serif_unchanged",
            voice.Serif == persistedSerif && voice.Serif == serif);

        Check(
            name.ToLowerInvariant() + "_persisted_hatsuon_unchanged",
            voice.Hatsuon == persistedHatsuon && voice.Hatsuon == hatsuon);

        Check(
            name.ToLowerInvariant() + "_corrected_real_file_exists",
            File.Exists(path) && new FileInfo(path).Length > 44);

        return new
        {
            serif = voice.Serif,
            hatsuon = voice.Hatsuon,
            offsets,
            baselinePitches,
            finalPitches,
            vowelLengths = finalMoras
                .Select(x => GetDouble(x, "VowelLength"))
                .ToArray(),
            fileLength = new FileInfo(path).Length,
        };
    }

    static VoiceItem CreateVoice(
        Character character,
        IVoiceParameter parameter,
        string serif,
        string hatsuon,
        string remark)
    {
        var voice = new VoiceItem
        {
            Serif = serif,
            Hatsuon = hatsuon,
            CharacterName = character.Name,
            VoiceParameter = parameter,
            Remark = "CNWL_" + remark
        };

        voice.Character = character;
        voice.VoiceParameter = parameter;
        voice.Serif = serif;
        voice.Hatsuon = hatsuon;
        voice.CharacterName = character.Name;

        return voice;
    }

    static string RequireVoicePath(VoiceItem voice, string name)
    {
        var path = voice.FilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException(
                $"{name} VoiceItem FilePath was unavailable.");
        return path;
    }

    static List<object> GetFirstPhraseMoras(IVoicePronounce pronounce)
    {
        var query = pronounce.GetType().GetProperty(
            "AudioQuery",
            BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(pronounce)
            ?? throw new InvalidOperationException("AudioQuery unavailable.");

        var phrases = query.GetType().GetProperty(
            "AccentPhrases",
            BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(query) as IEnumerable
            ?? throw new InvalidOperationException("AccentPhrases unavailable.");

        var phrase = phrases.Cast<object>().FirstOrDefault()
            ?? throw new InvalidOperationException("AccentPhrases empty.");

        var moras = phrase.GetType().GetProperty(
            "Moras",
            BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(phrase) as IEnumerable
            ?? throw new InvalidOperationException("Moras unavailable.");

        return moras.Cast<object>().ToList();
    }

    static string[] MoraTexts(IEnumerable<object> moras) =>
        moras.Select(x => GetString(x, "Text") ?? string.Empty).ToArray();

    static string? GetString(object target, string propertyName) =>
        target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(target)?.ToString();

    static double GetDouble(object target, string propertyName)
    {
        var value = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(target)
            ?? throw new InvalidOperationException(
                $"{propertyName} was null.");

        return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    static double? GetNullableDouble(object target, string propertyName)
    {
        var value = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(target);

        return value is null
            ? null
            : Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    static void SetDouble(
        object target,
        string propertyName,
        double value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(
                target.GetType().FullName,
                propertyName);

        if (property.SetMethod?.IsPublic != true)
            throw new InvalidOperationException(
                $"{propertyName} is not publicly writable.");

        property.SetValue(target, value);
    }

    sealed record SettingsRegistration(
        object? ResolvedEngine,
        Action Restore);

    static SettingsRegistration RegisterEngineInYmmSettings(
        VOICEVOXEngine engine,
        string speakerId)
    {
        var settingsType = typeof(VOICEVOXEngine).Assembly.GetType(
            "YukkuriMovieMaker.Settings.VOICEVOXSettings")
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings type not found.");

        var defaultProperty = settingsType.GetProperty(
            "Default",
            BindingFlags.Static
            | BindingFlags.Public
            | BindingFlags.FlattenHierarchy)
            ?? settingsType.BaseType?.GetProperty(
                "Default",
                BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.FlattenHierarchy)
            ?? throw new MissingMemberException(
                settingsType.FullName,
                "Default");

        var settings = defaultProperty.GetValue(null)
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings.Default returned null.");

        var enginesProperty = settingsType.GetProperty(
            "Engines",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(
                settingsType.FullName,
                "Engines");

        var original = enginesProperty.GetValue(settings)
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings.Engines returned null.");

        var add = original.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(x =>
                x.Name == "Add"
                && x.GetParameters().Length == 1
                && x.GetParameters()[0].ParameterType
                    .IsAssignableFrom(typeof(VOICEVOXEngine)))
            ?? throw new MissingMethodException(
                original.GetType().FullName,
                "Add(VOICEVOXEngine)");

        var augmented = add.Invoke(original, [engine])
            ?? throw new InvalidOperationException(
                "Engines.Add returned null.");

        enginesProperty.SetValue(settings, augmented);

        var findEngine = settingsType.GetMethod(
            "FindEngine",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(string)],
            modifiers: null)
            ?? throw new MissingMethodException(
                settingsType.FullName,
                "FindEngine(string)");

        var resolved = findEngine.Invoke(settings, [speakerId]);

        return new SettingsRegistration(
            resolved,
            () =>
            {
                try
                {
                    enginesProperty.SetValue(settings, original);
                }
                catch
                {
                }
            });
    }

    static Timeline? FindTimeline(object active)
    {
        for (var type = active.GetType();
             type is not null;
             type = type.BaseType)
        {
            foreach (var field in type.GetFields(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic))
            {
                if (typeof(Timeline).IsAssignableFrom(field.FieldType)
                    && field.GetValue(active) is Timeline timeline)
                {
                    return timeline;
                }
            }

            foreach (var property in type.GetProperties(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length != 0
                    || !typeof(Timeline).IsAssignableFrom(
                        property.PropertyType))
                {
                    continue;
                }

                try
                {
                    if (property.GetValue(active) is Timeline timeline)
                        return timeline;
                }
                catch
                {
                }
            }
        }

        return null;
    }

    static void Check(string id, bool passed)
    {
        requirements.Add(new { id, passed });
        if (!passed)
            throw new InvalidOperationException(
                "Requirement failed: " + id);
    }

    static void Write(string status, string? error)
    {
        File.WriteAllText(
            Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(
                new
                {
                    schema = "cnwl.voiceitem-prosody-relative-pitch.v1",
                    status,
                    host = "4.56.1.0 Lite",
                    sourceHead =
                        Environment.GetEnvironmentVariable("GITHUB_SHA"),
                    requirements,
                    error
                },
                new JsonSerializerOptions { WriteIndented = true }));
    }
}
