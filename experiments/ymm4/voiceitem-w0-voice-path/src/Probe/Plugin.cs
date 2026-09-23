using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Voice;
using YmmTextDecoration = YukkuriMovieMaker.Commons.TextDecoration;

namespace Ymm4VoiceItemW0VoicePathProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VoiceItem w0 Voice Path Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_W0_VOICE_PATH_OUTPUT");
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
                Write("PASS_W0_VOICE_PATH_OBSERVATION", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_W0_VOICE_PATH_OBSERVATION", ex.ToString());
            }
        };

        timer.Start();
    }

    sealed record CaseObservation(
        string Name,
        string SerifBefore,
        string HatsuonBefore,
        string HatsuonAfter,
        int[] ZeroWaitPositions,
        bool GenerationSucceeded,
        string? Error,
        string? FilePath,
        long FileLength,
        string? PronounceType);

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
        var vvCharacter = new VOICEVOXCharacter(speakerJson, Array.Empty<VOICEVOXSpeakerInfo>(), false);
        var speakerType = typeof(VOICEVOXEngine).Assembly.GetType("YukkuriMovieMaker.Voice.VOICEVOXVoiceSpeaker")
            ?? throw new TypeLoadException("VOICEVOXVoiceSpeaker");
        var speakerObject = Activator.CreateInstance(speakerType, engine, vvCharacter)
            ?? throw new InvalidOperationException("VOICEVOXVoiceSpeaker construction failed.");
        if (speakerObject is not IVoiceSpeaker speaker)
            throw new InvalidOperationException("Built-in VOICEVOX speaker interface missing.");

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
                Name = "CNWL w0",
                Voice = new VoiceDescription(speaker),
                VoiceParameter = parameter
            };

            var baseline = await RunCaseAsync(
                timeline, character, parameter, url,
                "baseline", "アイ", "アイ", 100, 4);

            var serifTagged = await RunCaseAsync(
                timeline, character, parameter, url,
                "serif-tagged", "ア<w0>イ", "アイ", 220, 5);

            var hatsuonTagged = await RunCaseAsync(
                timeline, character, parameter, url,
                "hatsuon-tagged", "アイ", "ア<w0>イ", 340, 6);

            Check("baseline_generation_succeeded", baseline.GenerationSucceeded);
            Check("serif_tagged_generation_succeeded", serifTagged.GenerationSucceeded);
            Check("serif_tagged_boundary_position_1",
                serifTagged.ZeroWaitPositions.SequenceEqual(new[] { 1 }));
            Check("hatsuon_tagged_attempt_completed", true);
            Check("three_cases_recorded", true);

            File.WriteAllText(
                Path.Combine(output, "voice-path-observation.json"),
                JsonSerializer.Serialize(new
                {
                    host = "4.56.1.0 Lite",
                    speakerId = speaker.ID,
                    cases = new[] { baseline, serifTagged, hatsuonTagged }
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            registration.Restore();
        }
    }

    static async Task<CaseObservation> RunCaseAsync(
        Timeline timeline,
        Character character,
        IVoiceParameter parameter,
        string url,
        string name,
        string serif,
        string hatsuon,
        int frame,
        int layer)
    {
        using var client = new HttpClient();
        await MarkAsync(client, url, name + "-start");

        var voice = new VoiceItem
        {
            Serif = serif,
            Hatsuon = hatsuon,
            CharacterName = character.Name,
            VoiceParameter = parameter,
            Remark = "CNWL_W0_" + name
        };
        voice.Character = character;
        voice.VoiceParameter = parameter;
        voice.Serif = serif;
        voice.Hatsuon = hatsuon;
        voice.CharacterName = character.Name;

        if (!timeline.TryAddItems([voice], frame, layer))
            throw new InvalidOperationException("Could not add case VoiceItem: " + name);

        var positions = ParseZeroWaitPositions(voice.Serif);

        bool succeeded = false;
        string? error = null;
        try
        {
            await voice.CreateVoiceFileAsync();
            succeeded = true;
        }
        catch (Exception ex)
        {
            error = ex.ToString();
        }
        finally
        {
            await MarkAsync(client, url, name + "-end");
        }

        var path = voice.FilePath;
        return new CaseObservation(
            name,
            serif,
            hatsuon,
            voice.Hatsuon,
            positions,
            succeeded,
            error,
            path,
            !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? new FileInfo(path).Length : 0,
            voice.Pronounce?.GetType().FullName);
    }

    static async Task MarkAsync(HttpClient client, string url, string name)
    {
        var endpoint = url.TrimEnd('/') + "/cnwl_case?name=" + Uri.EscapeDataString(name);
        using var response = await client.GetAsync(endpoint);
        response.EnsureSuccessStatusCode();
    }

    static int[] ParseZeroWaitPositions(string text)
    {
        var parsed = ControlTagParser.Parse(
            text,
            ImmutableList<YmmTextDecoration>.Empty,
            32.0,
            "Yu Gothic UI",
            false,
            false);

        return parsed.Item3
            .Where(tag =>
                tag.Type.ToString() == "Wait"
                && Math.Abs(Convert.ToDouble(tag.Value, CultureInfo.InvariantCulture)) < 0.000001
                && tag.Operator.ToString() == "Set")
            .Select(tag => tag.Position)
            .ToArray();
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
                schema = "cnwl.voiceitem-w0-voice-path.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
