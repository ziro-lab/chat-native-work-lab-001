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

namespace Ymm4VoiceItemHelperMoraTransientProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VoiceItem Helper Mora Transient Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_HELPER_MORA_OUTPUT");
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
                Write("PASS_VOICEITEM_HELPER_MORA_TRANSIENT", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_VOICEITEM_HELPER_MORA_TRANSIENT", ex.ToString());
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
            Name = "CNWL Helper VOICEVOX",
            URL = url,
            Path = "",
            Timeout = 10_000
        };

        const string fakeSpeakerUuid = "11111111-1111-1111-1111-111111111111";
        engine.SpeakerInfos.Add(new VOICEVOXSpeakerInfo(fakeSpeakerUuid, ""));

        var speakerJson = JObject.Parse("""
        {
          "name": "CNWL Helper",
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
                Name = "CNWL Helper",
                Voice = new VoiceDescription(speaker),
                VoiceParameter = parameter
            };

            var vowel = await RunVowelHelperCase(
                timeline,
                character,
                speaker,
                parameter);

            var consonant = await RunConsonantHelperCase(
                timeline,
                character,
                speaker,
                parameter);

            File.WriteAllText(
                Path.Combine(output, "helper-mora-observation.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        host = "4.56.1.0 Lite",
                        speakerId = speaker.ID,
                        vowel,
                        consonant
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            registration.Restore();
        }
    }

    static async Task<object> RunVowelHelperCase(
        Timeline timeline,
        Character character,
        IVoiceSpeaker speaker,
        IVoiceParameter parameter)
    {
        const string serif = "えええ";
        const string hatsuon = "エエエ";
        const string augmented = "エウエウエ";

        var voice = CreateVoice(character, parameter, serif, hatsuon, "VOWEL_HELPER");
        Check("vowel_voice_added_to_timeline",
            timeline.TryAddItems([voice], 180, 5));

        await voice.CreateVoiceFileAsync();
        var path = RequireVoicePath(voice, "vowel");
        Check("vowel_baseline_file_exists", File.Exists(path));

        var persistedSerif = voice.Serif;
        var persistedHatsuon = voice.Hatsuon;

        var analysisPath = Path.Combine(output, "vowel-helper-analysis.wav");
        var pronounce = await speaker.CreateVoiceAsync(
            augmented,
            pronounce: null,
            parameter,
            analysisPath)
            ?? throw new InvalidOperationException(
                "Vowel helper analysis returned null Pronounce.");

        var moras = GetFirstPhraseMoras(pronounce);
        Check("vowel_helper_mora_shape",
            MoraTexts(moras).SequenceEqual(
                new[] { "エ", "ウ", "エ", "ウ", "エ" }));

        var originalLengths = moras
            .Select(x => GetDouble(x, "VowelLength"))
            .ToArray();

        foreach (var index in new[] { 1, 3 })
            SetDouble(moras[index], "VowelLength", 0.0);

        Check("vowel_helper_lengths_zero",
            GetDouble(moras[1], "VowelLength") == 0.0
            && GetDouble(moras[3], "VowelLength") == 0.0);

        Check("vowel_nonhelper_lengths_preserved",
            GetDouble(moras[0], "VowelLength") == originalLengths[0]
            && GetDouble(moras[2], "VowelLength") == originalLengths[2]
            && GetDouble(moras[4], "VowelLength") == originalLengths[4]);

        var regenerated = await speaker.CreateVoiceAsync(
            augmented,
            pronounce,
            parameter,
            path)
            ?? throw new InvalidOperationException(
                "Vowel helper synthesis returned null Pronounce.");

        voice.ClearVoiceCache();
        voice.Pronounce = regenerated;

        var finalMoras = GetFirstPhraseMoras(regenerated);
        Check("vowel_helper_zero_survives_synthesis",
            GetDouble(finalMoras[1], "VowelLength") == 0.0
            && GetDouble(finalMoras[3], "VowelLength") == 0.0);

        Check("vowel_persisted_serif_unchanged",
            voice.Serif == persistedSerif && voice.Serif == serif);
        Check("vowel_persisted_hatsuon_unchanged",
            voice.Hatsuon == persistedHatsuon && voice.Hatsuon == hatsuon);
        Check("vowel_corrected_real_file_exists",
            File.Exists(path) && new FileInfo(path).Length == 5444);

        return new
        {
            serif = voice.Serif,
            hatsuon = voice.Hatsuon,
            augmented,
            moraTexts = MoraTexts(finalMoras),
            helperIndexes = new[] { 1, 3 },
            helperVowelLengths = new[]
            {
                GetDouble(finalMoras[1], "VowelLength"),
                GetDouble(finalMoras[3], "VowelLength")
            },
            fileLength = new FileInfo(path).Length
        };
    }

    static async Task<object> RunConsonantHelperCase(
        Timeline timeline,
        Character character,
        IVoiceSpeaker speaker,
        IVoiceParameter parameter)
    {
        const string serif = "ええ";
        const string hatsuon = "エエ";
        const string augmented = "エセエ";

        var voice = CreateVoice(character, parameter, serif, hatsuon, "CONSONANT_HELPER");
        Check("consonant_voice_added_to_timeline",
            timeline.TryAddItems([voice], 360, 7));

        await voice.CreateVoiceFileAsync();
        var path = RequireVoicePath(voice, "consonant");
        Check("consonant_baseline_file_exists", File.Exists(path));

        var persistedSerif = voice.Serif;
        var persistedHatsuon = voice.Hatsuon;

        var analysisPath = Path.Combine(output, "consonant-helper-analysis.wav");
        var pronounce = await speaker.CreateVoiceAsync(
            augmented,
            pronounce: null,
            parameter,
            analysisPath)
            ?? throw new InvalidOperationException(
                "Consonant helper analysis returned null Pronounce.");

        var moras = GetFirstPhraseMoras(pronounce);
        Check("consonant_helper_mora_shape",
            MoraTexts(moras).SequenceEqual(
                new[] { "エ", "セ", "エ" }));

        var helper = moras[1];
        var originalVowel = GetDouble(helper, "VowelLength");
        var originalConsonant = GetNullableDouble(helper, "ConsonantLength");

        Check("consonant_helper_has_consonant",
            string.Equals(
                GetString(helper, "Consonant"),
                "s",
                StringComparison.Ordinal)
            && originalConsonant is > 0.0);

        SetDouble(helper, "ConsonantLength", 0.0);

        Check("consonant_helper_length_zero",
            GetNullableDouble(helper, "ConsonantLength") == 0.0);
        Check("consonant_helper_vowel_preserved",
            GetDouble(helper, "VowelLength") == originalVowel
            && originalVowel > 0.0);

        var regenerated = await speaker.CreateVoiceAsync(
            augmented,
            pronounce,
            parameter,
            path)
            ?? throw new InvalidOperationException(
                "Consonant helper synthesis returned null Pronounce.");

        voice.ClearVoiceCache();
        voice.Pronounce = regenerated;

        var finalMoras = GetFirstPhraseMoras(regenerated);
        var finalHelper = finalMoras[1];

        Check("consonant_helper_zero_survives_synthesis",
            GetNullableDouble(finalHelper, "ConsonantLength") == 0.0);
        Check("consonant_helper_vowel_survives_synthesis",
            GetDouble(finalHelper, "VowelLength") == originalVowel);

        Check("consonant_persisted_serif_unchanged",
            voice.Serif == persistedSerif && voice.Serif == serif);
        Check("consonant_persisted_hatsuon_unchanged",
            voice.Hatsuon == persistedHatsuon && voice.Hatsuon == hatsuon);
        Check("consonant_corrected_real_file_exists",
            File.Exists(path) && new FileInfo(path).Length == 5644);

        return new
        {
            serif = voice.Serif,
            hatsuon = voice.Hatsuon,
            augmented,
            moraTexts = MoraTexts(finalMoras),
            helperIndex = 1,
            helperConsonant = GetString(finalHelper, "Consonant"),
            helperConsonantLength =
                GetNullableDouble(finalHelper, "ConsonantLength"),
            helperVowelLength =
                GetDouble(finalHelper, "VowelLength"),
            fileLength = new FileInfo(path).Length
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
                    schema = "cnwl.voiceitem-helper-mora-transient.v1",
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
