using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Voice;

namespace Ymm4VoiceVoxFakeApiRegenerationProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VOICEVOX Fake API Regeneration Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_VOICEVOX_FAKE_API_OUTPUT");
        if (scheduled || string.IsNullOrWhiteSpace(dir)) return;
        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Application.Current.Dispatcher.BeginInvoke(new Action(() => _ = RunAsync()), DispatcherPriority.ApplicationIdle);
    }

    static async Task RunAsync()
    {
        FakeVoiceVoxServer? server = null;
        try
        {
            server = new FakeVoiceVoxServer();
            await server.StartAsync();

            var engine = new VOICEVOXEngine(new VOICEVOXEngineContext())
            {
                Name = "CNWL Fake VOICEVOX",
                URL = server.BaseUrl,
                IsKanjiToYomiEnabled = true,
                Timeout = 10
            };
            engine.EngineConfig.IsExecuteEngineEnabled = false;

            Check("engine_points_to_fake_api", engine.URL == server.BaseUrl);
            Check("engine_execution_disabled", !engine.EngineConfig.IsExecuteEngineEnabled);

            var speakerToken = JToken.Parse(FakeVoiceVoxServer.SpeakersJson).First
                ?? throw new InvalidOperationException("speaker token missing");
            var speakerInfo = new VOICEVOXSpeakerInfo("cnwl-speaker-uuid", "CNWL test policy");
            var character = new VOICEVOXCharacter(speakerToken, new[] { speakerInfo }, false);

            Check("character_constructed", character.Name == "CNWL Probe");
            Check("character_has_style", character.Styles.Any(x => x.ID == 1));

            var speakerType = typeof(VOICEVOXEngine).Assembly.GetType("YukkuriMovieMaker.Voice.VOICEVOXVoiceSpeaker")
                ?? throw new InvalidOperationException("VOICEVOXVoiceSpeaker not found");
            var speakerObject = Activator.CreateInstance(speakerType, engine, character)
                ?? throw new InvalidOperationException("VOICEVOXVoiceSpeaker construction failed");
            var speaker = (IVoiceSpeaker)speakerObject;
            Check("builtin_speaker_constructed", speaker is not null);

            var parameter = speaker!.CreateVoiceParameter()
                ?? throw new InvalidOperationException("CreateVoiceParameter returned null");
            Check("voice_parameter_constructed", true);
            var styleProp = parameter.GetType().GetProperty("StyleID", BindingFlags.Instance | BindingFlags.Public);
            var styleId = styleProp is null ? -1 : Convert.ToInt32(styleProp.GetValue(parameter));
            Check("voice_parameter_uses_style_1", styleId == 1);

            var firstPath = Path.Combine(output, "baseline.wav");
            var first = await speaker.CreateVoiceAsync("テストです", null, parameter, firstPath)
                ?? throw new InvalidOperationException("First CreateVoiceAsync returned null");
            Check("first_create_voice_returns_pronounce", true);
            Check("first_wave_written", File.Exists(firstPath) && new FileInfo(firstPath).Length > 44);

            var firstAudioQueryCount = server.Requests.Count(x => x.Path.Contains("audio_query", StringComparison.OrdinalIgnoreCase));
            var firstSynthesisCount = server.Requests.Count(x => x.Path.Contains("synthesis", StringComparison.OrdinalIgnoreCase));
            Check("first_call_hits_audio_query", firstAudioQueryCount >= 1);
            Check("first_call_hits_synthesis", firstSynthesisCount >= 1);

            var aqProp = first.GetType().GetProperty("AudioQuery", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new MissingMemberException(first.GetType().FullName, "AudioQuery");
            var aq = aqProp.GetValue(first) ?? throw new InvalidOperationException("AudioQuery is null");
            var phrasesProp = aq.GetType().GetProperty("AccentPhrases", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new MissingMemberException(aq.GetType().FullName, "AccentPhrases");
            var phrases = phrasesProp.GetValue(aq) as System.Collections.IEnumerable
                ?? throw new InvalidOperationException("AccentPhrases not enumerable");
            var phrase = phrases.Cast<object>().FirstOrDefault()
                ?? throw new InvalidOperationException("no AccentPhrase");
            var pauseProp = phrase.GetType().GetProperty("PauseMora", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new MissingMemberException(phrase.GetType().FullName, "PauseMora");
            var pause = pauseProp.GetValue(phrase)
                ?? throw new InvalidOperationException("PauseMora is null");
            var vowelLengthProp = pause.GetType().GetProperty("VowelLength", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new MissingMemberException(pause.GetType().FullName, "VowelLength");

            var before = Convert.ToDouble(vowelLengthProp.GetValue(pause));
            vowelLengthProp.SetValue(pause, 0d);
            var after = Convert.ToDouble(vowelLengthProp.GetValue(pause));
            Check("pause_mutated_to_zero", before > 0 && after == 0);

            var requestCountBeforeSecond = server.Requests.Count;
            var audioQueryCountBeforeSecond = server.Requests.Count(x => x.Path.Contains("audio_query", StringComparison.OrdinalIgnoreCase));
            var synthesisCountBeforeSecond = server.Requests.Count(x => x.Path.Contains("synthesis", StringComparison.OrdinalIgnoreCase));

            var secondPath = Path.Combine(output, "modified.wav");
            var second = await speaker.CreateVoiceAsync("テストです", first, parameter, secondPath)
                ?? throw new InvalidOperationException("Second CreateVoiceAsync returned null");
            Check("second_create_voice_returns_pronounce", true);
            Check("second_wave_written", File.Exists(secondPath) && new FileInfo(secondPath).Length > 44);

            var secondNewRequests = server.Requests.Skip(requestCountBeforeSecond).ToArray();
            var audioQueryCountAfterSecond = server.Requests.Count(x => x.Path.Contains("audio_query", StringComparison.OrdinalIgnoreCase));
            var synthesisCountAfterSecond = server.Requests.Count(x => x.Path.Contains("synthesis", StringComparison.OrdinalIgnoreCase));

            Check("second_call_reuses_pronounce_without_audio_query",
                audioQueryCountAfterSecond == audioQueryCountBeforeSecond);
            Check("second_call_hits_synthesis_once",
                synthesisCountAfterSecond == synthesisCountBeforeSecond + 1);

            var lastSynthesis = server.Requests.LastOrDefault(x => x.Path.Contains("synthesis", StringComparison.OrdinalIgnoreCase));
            var sentPauseLength = ReadPauseVowelLength(lastSynthesis?.Body);
            Check("modified_zero_reaches_synthesis", sentPauseLength is not null && Math.Abs(sentPauseLength.Value) < 0.000001);

            var secondAq = aqProp.GetValue(second);
            var persisted = ReadPauseFromAudioQuery(secondAq);
            Check("returned_pronounce_keeps_zero", persisted is not null && Math.Abs(persisted.Value) < 0.000001);

            File.WriteAllText(Path.Combine(output, "behavior.json"), JsonSerializer.Serialize(new
            {
                host = "4.56.1.0 Lite",
                engine = new { engine.Name, engine.URL, activeUrl = Safe(() => engine.GetActiveURL()) },
                character = new
                {
                    character.Name,
                    character.SpeakerUuid,
                    styles = character.Styles.Select(x => new { x.Name, x.ID }).ToArray()
                },
                speaker = new
                {
                    type = speakerType.FullName,
                    speaker.EngineName,
                    speaker.SpeakerName,
                    speaker.ID,
                    styleId
                },
                mutation = new
                {
                    before,
                    after,
                    sentPauseLength,
                    persisted
                },
                requests = server.Requests.Select(x => new { x.Method, x.Path, x.Query, x.Body }).ToArray(),
                secondNewRequests = secondNewRequests.Select(x => new { x.Method, x.Path, x.Query, x.Body }).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true }));

            Write("PASS_VOICEVOX_FAKE_API_REGENERATION", null);
        }
        catch (Exception ex)
        {
            Write("FAIL_VOICEVOX_FAKE_API_REGENERATION", ex.ToString());
        }
        finally
        {
            if (server is not null)
                await server.DisposeAsync();
        }
    }

    static double? ReadPauseFromAudioQuery(object? aq)
    {
        try
        {
            if (aq is null) return null;
            var pp = aq.GetType().GetProperty("AccentPhrases")?.GetValue(aq) as System.Collections.IEnumerable;
            var phrase = pp?.Cast<object>().FirstOrDefault();
            var pause = phrase?.GetType().GetProperty("PauseMora")?.GetValue(phrase);
            var value = pause?.GetType().GetProperty("VowelLength")?.GetValue(pause);
            return value is null ? null : Convert.ToDouble(value);
        }
        catch { return null; }
    }

    static double? ReadPauseVowelLength(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            var j = JToken.Parse(body);
            var token = j.SelectToken("$.accent_phrases[0].pause_mora.vowel_length")
                     ?? j.SelectToken("$.accentPhrases[0].pauseMora.vowelLength")
                     ?? j.SelectToken("$.AccentPhrases[0].PauseMora.VowelLength");
            return token?.Value<double>();
        }
        catch { return null; }
    }

    static T? Safe<T>(Func<T> action)
    {
        try { return action(); } catch { return default; }
    }

    static void Check(string id, bool passed) => requirements.Add(new { id, passed });

    static void Write(string status, string? error)
    {
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            schema = "cnwl.voicevox-fake-api-regeneration.v1",
            status,
            host = "4.56.1.0 Lite",
            sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            requirements,
            error
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed class FakeVoiceVoxServer : IAsyncDisposable
{
    public const string SpeakersJson = """
[
  {
    "name": "CNWL Probe",
    "speaker_uuid": "cnwl-speaker-uuid",
    "styles": [
      { "name": "Normal", "id": 1, "type": "talk" }
    ],
    "version": "0.0.0",
    "supported_features": {
      "permitted_synthesis_morphing": "NOTHING"
    }
  }
]
""";

    const string AudioQueryJson = """
{
  "accent_phrases": [
    {
      "moras": [
        {
          "text": "テ",
          "consonant": "t",
          "consonant_length": 0.05,
          "vowel": "e",
          "vowel_length": 0.10,
          "pitch": 5.5
        },
        {
          "text": "ス",
          "consonant": "s",
          "consonant_length": 0.05,
          "vowel": "u",
          "vowel_length": 0.10,
          "pitch": 5.4
        }
      ],
      "accent": 1,
      "pause_mora": {
        "text": "、",
        "consonant": null,
        "consonant_length": null,
        "vowel": "pau",
        "vowel_length": 0.20,
        "pitch": 0.0
      },
      "is_interrogative": false
    }
  ],
  "speedScale": 1.0,
  "pitchScale": 0.0,
  "intonationScale": 1.0,
  "volumeScale": 1.0,
  "prePhonemeLength": 0.10,
  "postPhonemeLength": 0.10,
  "outputSamplingRate": 24000,
  "outputStereo": false,
  "kana": "テ'ス、"
}
""";

    readonly HttpListener listener = new();
    readonly CancellationTokenSource cts = new();
    Task? loop;

    public sealed record RequestRecord(string Method, string Path, string Query, string Body);

    public List<RequestRecord> Requests { get; } = [];
    public string BaseUrl { get; }

    public FakeVoiceVoxServer()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();

        BaseUrl = $"http://127.0.0.1:{port}";
        listener.Prefixes.Add(BaseUrl + "/");
    }

    public Task StartAsync()
    {
        listener.Start();
        loop = Task.Run(LoopAsync);
        return Task.CompletedTask;
    }

    async Task LoopAsync()
    {
        while (!cts.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(cts.Token);
                _ = Task.Run(() => HandleAsync(context));
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) when (cts.IsCancellationRequested) { break; }
            catch { if (cts.IsCancellationRequested) break; }
        }
    }

    async Task HandleAsync(HttpListenerContext c)
    {
        try
        {
            string body = "";
            if (c.Request.HasEntityBody)
            {
                using var sr = new StreamReader(c.Request.InputStream, c.Request.ContentEncoding ?? Encoding.UTF8);
                body = await sr.ReadToEndAsync();
            }

            lock (Requests)
                Requests.Add(new(c.Request.HttpMethod, c.Request.Url?.AbsolutePath ?? "", c.Request.Url?.Query ?? "", body));

            var path = (c.Request.Url?.AbsolutePath ?? "").TrimEnd('/').ToLowerInvariant();
            switch (path)
            {
                case "/audio_query":
                    await Json(c, AudioQueryJson);
                    break;
                case "/accent_phrases":
                    {
                        var q = JObject.Parse(AudioQueryJson);
                        await Json(c, q["accent_phrases"]!.ToString(Newtonsoft.Json.Formatting.None));
                        break;
                    }
                case "/mora_data":
                case "/mora_length":
                case "/mora_pitch":
                    await Json(c, body);
                    break;
                case "/synthesis":
                case "/synthesis_morphing":
                    await Bytes(c, CreateSilentWave(), "audio/wav");
                    break;
                case "/speakers":
                    await Json(c, SpeakersJson);
                    break;
                case "/speaker_info":
                    await Json(c, """{"policy":"CNWL test policy","portrait":"","style_infos":[]}""");
                    break;
                case "/engine_manifest":
                    await Json(c, """{"manifest_version":"0.13.1","name":"CNWL Fake","brand_name":"CNWL","uuid":"cnwl-fake","url":"https://example.invalid","icon":"","default_sampling_rate":24000,"frame_rate":93.75,"terms_of_service":"","update_infos":[],"dependency_licenses":[],"supported_features":{"adjust_mora_pitch":true,"synthesis_morphing":false,"manage_library":false,"return_resource_url":false}}""");
                    break;
                case "/version":
                    await Json(c, "\"0.0.0\"");
                    break;
                case "/is_initialized_speaker":
                    await Json(c, "true");
                    break;
                case "/initialize_speaker":
                    c.Response.StatusCode = 204;
                    c.Response.Close();
                    break;
                default:
                    await Json(c, "{}");
                    break;
            }
        }
        catch
        {
            try { c.Response.StatusCode = 500; c.Response.Close(); } catch { }
        }
    }

    static async Task Json(HttpListenerContext c, string json)
    {
        await Bytes(c, Encoding.UTF8.GetBytes(json), "application/json");
    }

    static async Task Bytes(HttpListenerContext c, byte[] bytes, string contentType)
    {
        c.Response.StatusCode = 200;
        c.Response.ContentType = contentType;
        c.Response.ContentLength64 = bytes.Length;
        await c.Response.OutputStream.WriteAsync(bytes);
        c.Response.Close();
    }

    static byte[] CreateSilentWave()
    {
        const int rate = 24000;
        const short channels = 1;
        const short bits = 16;
        const int samples = 4800;
        var dataSize = samples * channels * (bits / 8);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write(channels);
        bw.Write(rate);
        bw.Write(rate * channels * (bits / 8));
        bw.Write((short)(channels * (bits / 8)));
        bw.Write(bits);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        bw.Write(new byte[dataSize]);
        bw.Flush();
        return ms.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        cts.Cancel();
        try { listener.Stop(); } catch { }
        if (loop is not null)
            try { await loop; } catch { }
        listener.Close();
        cts.Dispose();
    }
}
