using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Voice;

namespace Ymm4VoiceSpeakerReadingPrefixMapProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL Voice Speaker Reading Prefix Map Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_READING_PREFIX_OUTPUT");
        var url = Environment.GetEnvironmentVariable("CNWL_FAKE_VOICEVOX_URL");
        if (scheduled || string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(url))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunAsync(url)),
            DispatcherPriority.ApplicationIdle);
    }

    static async Task RunAsync(string url)
    {
        try
        {
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

            engine.SpeakersJsonCache = new JArray(speakerJson)
                .ToString(Newtonsoft.Json.Formatting.None);

            var vvCharacter = new VOICEVOXCharacter(
                speakerJson,
                Array.Empty<VOICEVOXSpeakerInfo>(),
                false);

            var speakerType = typeof(VOICEVOXEngine).Assembly
                .GetType("YukkuriMovieMaker.Voice.VOICEVOXVoiceSpeaker")
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
                parameter.GetType()
                    .GetProperty("StyleID", BindingFlags.Instance | BindingFlags.Public)
                    ?.SetValue(parameter, 1);

                var publicConvert = typeof(IVoiceSpeaker).GetMethod(
                    "ConvertKanjiToYomiAsync",
                    BindingFlags.Instance | BindingFlags.Public,
                    binder: null,
                    types: [typeof(string), typeof(IVoiceParameter)],
                    modifiers: null);
                Check("convert_kanji_to_yomi_public", publicConvert is not null);

                const string fullText = "東京大学";
                const string prefixText = "東京";
                const string kanaText = "トウキョウダイガク";

                string? fullYomi = null;
                string? prefixYomi = null;
                string? kanaYomi = null;
                Exception? fullError = null;
                Exception? prefixError = null;
                Exception? kanaError = null;

                try { fullYomi = await speaker.ConvertKanjiToYomiAsync(fullText, parameter); }
                catch (Exception ex) { fullError = ex; }

                try { prefixYomi = await speaker.ConvertKanjiToYomiAsync(prefixText, parameter); }
                catch (Exception ex) { prefixError = ex; }

                try { kanaYomi = await speaker.ConvertKanjiToYomiAsync(kanaText, parameter); }
                catch (Exception ex) { kanaError = ex; }

                Check("full_conversion_completed", fullError is null);
                Check("prefix_conversion_completed", prefixError is null);
                Check("full_reading_nonempty", !string.IsNullOrWhiteSpace(fullYomi));
                Check("prefix_reading_nonempty", !string.IsNullOrWhiteSpace(prefixYomi));

                if (fullError is not null)
                    throw new InvalidOperationException(
                        "Full ConvertKanjiToYomiAsync failed.",
                        fullError);
                if (prefixError is not null)
                    throw new InvalidOperationException(
                        "Prefix ConvertKanjiToYomiAsync failed.",
                        prefixError);
                if (string.IsNullOrWhiteSpace(fullYomi)
                    || string.IsNullOrWhiteSpace(prefixYomi))
                    throw new InvalidOperationException("Reading result was empty.");

                var normalizedFull = NormalizeReading(fullYomi);
                var normalizedPrefix = NormalizeReading(prefixYomi);
                var normalizedKana = NormalizeReading(kanaYomi ?? "");

                Check("normalized_prefix_is_full_prefix",
                    normalizedFull.StartsWith(normalizedPrefix, StringComparison.Ordinal)
                    && normalizedPrefix.Length < normalizedFull.Length);

                var wav = Path.Combine(output, "full-reading.wav");
                var pronounce = await speaker.CreateVoiceAsync(
                    fullYomi,
                    pronounce: null,
                    parameter,
                    wav)
                    ?? throw new InvalidOperationException(
                        "Full-reading CreateVoiceAsync returned null Pronounce.");

                Check("full_reading_pronounce_created",
                    File.Exists(wav) && new FileInfo(wav).Length > 44);

                var query = GetAudioQuery(pronounce);
                var phraseFacts = DescribePhrases(query);
                var flatMora = string.Concat(
                    phraseFacts.SelectMany(x => x.Moras));

                var normalizedMora = NormalizeReading(flatMora);
                Check("full_reading_matches_mora_stream",
                    normalizedMora == normalizedFull);

                var cumulative = "";
                var candidates = new List<object>();
                int cumulativeMoraCount = 0;

                foreach (var phrase in phraseFacts)
                {
                    cumulative += string.Concat(phrase.Moras);
                    cumulativeMoraCount += phrase.Moras.Length;
                    var normalizedCumulative = NormalizeReading(cumulative);
                    if (normalizedCumulative == normalizedPrefix)
                    {
                        candidates.Add(new
                        {
                            phraseIndex = phrase.Index,
                            cumulativeMoraCount,
                            normalizedCumulative,
                            hasPauseMora = phrase.HasPauseMora,
                            pauseVowelLength = phrase.PauseVowelLength
                        });
                    }
                }

                Check("prefix_maps_to_unique_phrase_end", candidates.Count == 1);

                var candidateHasPause = candidates.Count == 1
                    && ReadBool(candidates[0], "hasPauseMora");
                Check("mapped_phrase_has_pause_mora", candidateHasPause);

                File.WriteAllText(
                    Path.Combine(output, "reading-prefix-observation.json"),
                    JsonSerializer.Serialize(new
                    {
                        host = "4.56.1.0 Lite",
                        speakerId = speaker.ID,
                        convertMethod = publicConvert?.ToString(),
                        inputs = new
                        {
                            fullText,
                            prefixText,
                            kanaText
                        },
                        returned = new
                        {
                            fullYomi,
                            prefixYomi,
                            kanaYomi,
                            normalizedFull,
                            normalizedPrefix,
                            normalizedKana,
                            fullError = fullError?.ToString(),
                            prefixError = prefixError?.ToString(),
                            kanaError = kanaError?.ToString()
                        },
                        pronounce = new
                        {
                            type = pronounce.GetType().FullName,
                            wavLength = new FileInfo(wav).Length,
                            flatMora,
                            normalizedMora,
                            phrases = phraseFacts,
                            prefixBoundaryCandidates = candidates
                        }
                    }, new JsonSerializerOptions { WriteIndented = true }));

                Write("PASS_VOICE_SPEAKER_READING_PREFIX_MAP", null);
            }
            finally
            {
                registration.Restore();
            }
        }
        catch (Exception ex)
        {
            Write("FAIL_VOICE_SPEAKER_READING_PREFIX_MAP", ex.ToString());
        }
    }

    sealed record PhraseFact(
        int Index,
        string[] Moras,
        bool HasPauseMora,
        double? PauseVowelLength);

    static PhraseFact[] DescribePhrases(object query)
    {
        var phrasesProperty = query.GetType().GetProperty(
            "AccentPhrases",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(
                query.GetType().FullName,
                "AccentPhrases");

        var phrases = phrasesProperty.GetValue(query) as IEnumerable
            ?? throw new InvalidOperationException(
                "AccentPhrases is not enumerable.");

        var result = new List<PhraseFact>();
        int index = 0;
        foreach (var phrase in phrases.Cast<object>())
        {
            var morasProperty = phrase.GetType().GetProperty(
                "Moras",
                BindingFlags.Instance | BindingFlags.Public)
                ?? throw new MissingMemberException(
                    phrase.GetType().FullName,
                    "Moras");

            var moras = morasProperty.GetValue(phrase) as IEnumerable
                ?? throw new InvalidOperationException("Moras not enumerable.");

            var texts = moras.Cast<object>()
                .Select(m =>
                    m.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public)
                        ?.GetValue(m)?.ToString() ?? "")
                .ToArray();

            var pauseProperty = phrase.GetType().GetProperty(
                "PauseMora",
                BindingFlags.Instance | BindingFlags.Public)
                ?? throw new MissingMemberException(
                    phrase.GetType().FullName,
                    "PauseMora");

            var pause = pauseProperty.GetValue(phrase);
            double? pauseLength = null;
            if (pause is not null)
            {
                var p = pause.GetType().GetProperty(
                    "VowelLength",
                    BindingFlags.Instance | BindingFlags.Public);
                if (p?.GetValue(pause) is { } value)
                    pauseLength = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }

            result.Add(new PhraseFact(
                index++,
                texts,
                pause is not null,
                pauseLength));
        }

        return result.ToArray();
    }

    static object GetAudioQuery(IVoicePronounce pronounce)
    {
        var p = pronounce.GetType().GetProperty(
            "AudioQuery",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(
                pronounce.GetType().FullName,
                "AudioQuery");

        return p.GetValue(pronounce)
            ?? throw new InvalidOperationException("AudioQuery was null.");
    }

    static string NormalizeReading(string value)
    {
        var sb = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            var text = rune.ToString();
            if (text is "'" or "’" or "、" or "。" or "，" or "," or " "
                or "　" or "/" or "？" or "?" or "！" or "!")
                continue;
            sb.Append(text);
        }
        return sb.ToString();
    }

    static bool ReadBool(object value, string propertyName)
    {
        var p = value.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        return p?.GetValue(value) is bool b && b;
    }

    sealed record SettingsRegistration(
        string SettingsType,
        object? ResolvedEngine,
        Action Restore);

    static SettingsRegistration RegisterEngineInYmmSettings(
        VOICEVOXEngine engine,
        string speakerId)
    {
        var settingsType = typeof(VOICEVOXEngine).Assembly
            .GetType("YukkuriMovieMaker.Settings.VOICEVOXSettings")
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

        var enginesProperty = settingsType.GetProperty(
            "Engines",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(settingsType.FullName, "Engines");

        var original = enginesProperty.GetValue(settings)
            ?? throw new InvalidOperationException("Engines null.");

        var add = original.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m =>
                m.Name == "Add"
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType
                    .IsAssignableFrom(typeof(VOICEVOXEngine)))
            ?? throw new MissingMethodException(
                original.GetType().FullName,
                "Add");

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
            () =>
            {
                try { enginesProperty.SetValue(settings, original); }
                catch { }
            });
    }

    static void Check(string id, bool passed) =>
        requirements.Add(new { id, passed });

    static void Write(string status, string? error)
    {
        File.WriteAllText(
            Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(new
            {
                schema = "cnwl.voice-speaker-reading-prefix-map.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
