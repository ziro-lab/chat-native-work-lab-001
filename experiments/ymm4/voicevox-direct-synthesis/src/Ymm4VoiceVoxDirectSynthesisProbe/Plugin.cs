using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Voice;

namespace Ymm4VoiceVoxDirectSynthesisProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VOICEVOX Direct Synthesis Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_VOICEVOX_DIRECT_OUTPUT");
        var url = Environment.GetEnvironmentVariable("CNWL_FAKE_VOICEVOX_URL");
        if (scheduled || string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(url))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(() => _ = RunAsync(url)), DispatcherPriority.ApplicationIdle);
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

            var activeUrl = engine.GetActiveURL();
            Check("engine_points_to_fake_backend",
                !string.IsNullOrWhiteSpace(activeUrl) &&
                activeUrl.StartsWith(url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));

            var speakerJson = JObject.Parse("""
            {
              "name": "CNWL Speaker",
              "speaker_uuid": "cnwl-speaker",
              "styles": [
                { "name": "Normal", "id": 1, "type": "talk" }
              ],
              "version": "0.0.0",
              "supported_features": {
                "permitted_synthesis_morphing": "SELF_ONLY"
              }
            }
            """);

            var character = new VOICEVOXCharacter(
                speakerJson,
                Array.Empty<VOICEVOXSpeakerInfo>(),
                false);

            var speakerType = typeof(VOICEVOXEngine).Assembly.GetType("YukkuriMovieMaker.Voice.VOICEVOXVoiceSpeaker")
                ?? throw new InvalidOperationException("VOICEVOXVoiceSpeaker type not found.");

            var speakerObject = Activator.CreateInstance(speakerType, engine, character)
                ?? throw new InvalidOperationException("VOICEVOXVoiceSpeaker construction failed.");

            Check("builtin_speaker_constructed", speakerObject is IVoiceSpeaker);
            if (speakerObject is not IVoiceSpeaker speaker)
                throw new InvalidOperationException("Built-in speaker does not implement IVoiceSpeaker.");

            var parameter = speaker.CreateVoiceParameter();
            var styleProperty = parameter.GetType().GetProperty("StyleID", BindingFlags.Instance | BindingFlags.Public);
            styleProperty?.SetValue(parameter, 1);

            var query = new VOICEVOXAudioQuery();
            SetIfPresent(query, "SpeedScale", 1.0);
            SetIfPresent(query, "PitchScale", 0.0);
            SetIfPresent(query, "IntonationScale", 1.0);
            SetIfPresent(query, "VolumeScale", 1.0);
            SetIfPresent(query, "PrePhonemeLength", 0.1);
            SetIfPresent(query, "PostPhonemeLength", 0.1);
            SetIfPresent(query, "PauseLengthScale", 1.0);
            SetIfPresent(query, "OutputSamplingRate", 24000);
            SetIfPresent(query, "OutputStereo", false);
            SetIfPresent(query, "Kana", "ア'、");

            var phrase = new VOICEVOXAccentPhrase { Accent = 1 };
            phrase.Moras.Add(new VOICEVOXMora
            {
                Text = "ア",
                Vowel = "a",
                VowelLength = 0.12,
                Pitch = 5.0
            });
            phrase.PauseMora = new VOICEVOXMora
            {
                Text = "、",
                Vowel = "pau",
                VowelLength = 0.0,
                Pitch = 0.0
            };
            query.AccentPhrases.Add(phrase);

            Check("supplied_pause_is_zero", phrase.PauseMora?.VowelLength == 0.0);

            var pronounceType = typeof(VOICEVOXEngine).Assembly.GetType("YukkuriMovieMaker.Voice.VOICEVOXVoicePronounce")
                ?? throw new InvalidOperationException("VOICEVOXVoicePronounce type not found.");
            var pronounceObject = Activator.CreateInstance(pronounceType, query)
                ?? throw new InvalidOperationException("VOICEVOXVoicePronounce construction failed.");
            if (pronounceObject is not IVoicePronounce pronounce)
                throw new InvalidOperationException("Pronounce object does not implement IVoicePronounce.");

            var queryHasErrors = ReadBoolProperty(query, "HasErrors");
            var pronounceHasErrors = ReadBoolProperty(pronounceObject, "HasErrors");
            var parameterHasErrors = ReadBoolProperty(parameter, "HasErrors");
            Check("query_has_no_errors", queryHasErrors != true);
            Check("pronounce_has_no_errors", pronounceHasErrors != true);
            Check("parameter_has_no_errors", parameterHasErrors != true);

            var wav = Path.Combine(output, "direct.wav");
            IVoicePronounce? returned = null;
            Exception? synthesisError = null;
            try
            {
                returned = await speaker.CreateVoiceAsync(
                    "AB",
                    pronounce,
                    parameter,
                    wav);
            }
            catch (Exception ex)
            {
                synthesisError = ex;
            }

            Check("create_voice_async_completed", synthesisError is null);
            Check("wav_written", File.Exists(wav) && new FileInfo(wav).Length > 44);
            Check("returned_voicevox_pronounce",
                returned is not null &&
                returned.GetType().FullName?.Contains("VOICEVOXVoicePronounce", StringComparison.Ordinal) == true);

            var observation = new
            {
                host = "4.56.1.0 Lite",
                requestedUrl = url,
                activeUrl,
                speakerType = speakerObject.GetType().FullName,
                parameterType = parameter.GetType().FullName,
                pronounceType = pronounceObject.GetType().FullName,
                returnedPronounceType = returned?.GetType().FullName,
                suppliedPauseVowelLength = phrase.PauseMora?.VowelLength,
                queryHasErrors,
                pronounceHasErrors,
                parameterHasErrors,
                queryProperties = query.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Where(x => x.GetIndexParameters().Length == 0)
                    .ToDictionary(
                        x => x.Name,
                        x =>
                        {
                            try { return x.GetValue(query)?.ToString(); }
                            catch { return "<error>"; }
                        }),
                wavExists = File.Exists(wav),
                wavLength = File.Exists(wav) ? new FileInfo(wav).Length : 0,
                synthesisError = synthesisError?.ToString()
            };

            File.WriteAllText(Path.Combine(output, "plugin-observation.json"),
                JsonSerializer.Serialize(observation, new JsonSerializerOptions { WriteIndented = true }));

            Write("PASS_VOICEVOX_DIRECT_SYNTHESIS_PLUGIN", null);
        }
        catch (Exception ex)
        {
            Write("FAIL_VOICEVOX_DIRECT_SYNTHESIS_PLUGIN", ex.ToString());
        }
    }

    static void SetIfPresent(object target, string name, object? value)
    {
        var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property?.SetMethod?.IsPublic != true)
            return;

        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        object? converted = value;
        if (value is not null && !targetType.IsInstanceOfType(value))
            converted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        property.SetValue(target, converted);
    }

    static bool? ReadBoolProperty(object target, string name)
    {
        try
        {
            var p = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            return p?.GetValue(target) is bool value ? value : null;
        }
        catch { return null; }
    }

    static void Check(string id, bool passed) => requirements.Add(new { id, passed });

    static void Write(string status, string? error)
    {
        File.WriteAllText(Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(new
            {
                schema = "cnwl.voicevox-direct-synthesis.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
