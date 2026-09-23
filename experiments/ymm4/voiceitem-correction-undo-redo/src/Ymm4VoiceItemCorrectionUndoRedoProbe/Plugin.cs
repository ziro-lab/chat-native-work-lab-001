using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.UndoRedo;
using YukkuriMovieMaker.Voice;

namespace Ymm4VoiceItemCorrectionUndoRedoProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "CNWL VoiceItem Correction Undo Redo Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_VOICEITEM_UNDO_OUTPUT");
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
                var window = Application.Current.Windows.Cast<Window>()
                    .FirstOrDefault(w => w.DataContext?.GetType().FullName == "YukkuriMovieMaker.ViewModels.MainViewModel");
                var main = window?.DataContext;
                if (window is null || main is null)
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
                await RunAsync(window, main, active, url);
                Write("PASS_VOICEITEM_CORRECTION_UNDO_REDO", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_VOICEITEM_CORRECTION_UNDO_REDO", ex.ToString());
            }
        };

        timer.Start();
    }

    sealed record VoiceSnapshot(
        IVoicePronounce Pronounce,
        byte[] WavBytes,
        string WavSha256,
        double PauseVowelLength);

    static async Task RunAsync(Window window, object main, object active, string url)
    {
        window.WindowState = WindowState.Normal;
        window.Activate();
        Native.SetForegroundWindow(new WindowInteropHelper(window).Handle);
        await Task.Delay(300);

        var timeline = FindTimeline(active)
            ?? throw new InvalidOperationException("Timeline could not be resolved.");
        Check("timeline_resolved", true);

        var managerAcquisition = AcquireUndoManager(main, active, timeline);
        var manager = managerAcquisition.Manager
            ?? throw new InvalidOperationException("UndoRedoManager acquisition returned null.");
        Check("host_undo_manager_acquired", true);

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
        EventHandler? recordedHandler = null;
        EventHandler? undoHandler = null;
        EventHandler? redoHandler = null;

        try
        {
            Check("fake_engine_registered",
                registration.ResolvedEngine is not null &&
                ReferenceEquals(registration.ResolvedEngine, engine));

            var parameter = speaker.CreateVoiceParameter();
            parameter.GetType().GetProperty("StyleID", BindingFlags.Instance | BindingFlags.Public)
                ?.SetValue(parameter, 1);

            var voiceDescription = new VoiceDescription(speaker);
            var projectCharacter = new Character
            {
                Name = "CNWL Undo",
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
            voice.Serif = "ア";
            voice.Hatsuon = "ア";
            voice.CharacterName = projectCharacter.Name;

            Check("voice_added_to_real_timeline", timeline.TryAddItems([voice], 240, 6));
            Check("voice_present_in_real_timeline", timeline.Items.Any(x => ReferenceEquals(x, voice)));

            await voice.CreateVoiceFileAsync();
            var voicePath = voice.FilePath;
            Check("real_voiceitem_file_path_available",
                !string.IsNullOrWhiteSpace(voicePath) && File.Exists(voicePath));
            if (string.IsNullOrWhiteSpace(voicePath) || !File.Exists(voicePath))
                throw new InvalidOperationException("VoiceItem file path unavailable.");

            var baselinePath = Path.Combine(output, "baseline.wav");
            var baselinePronounce = await speaker.CreateVoiceAsync(
                voice.Hatsuon,
                pronounce: null,
                parameter,
                baselinePath)
                ?? throw new InvalidOperationException("Baseline Pronounce was null.");
            var baselinePause = GetPauseVowelLength(GetAudioQuery(baselinePronounce));
            Check("baseline_pause_nonzero", baselinePause > 0.0);

            var correctedSeedPath = Path.Combine(output, "corrected-seed.wav");
            var correctedPronounce = await speaker.CreateVoiceAsync(
                voice.Hatsuon,
                pronounce: null,
                parameter,
                correctedSeedPath)
                ?? throw new InvalidOperationException("Corrected seed Pronounce was null.");
            SetPauseVowelLength(GetAudioQuery(correctedPronounce), 0.0);
            Check("corrected_pause_zero_before_synthesis",
                GetPauseVowelLength(GetAudioQuery(correctedPronounce)) == 0.0);

            var correctedPath = Path.Combine(output, "corrected.wav");
            var correctedReturned = await speaker.CreateVoiceAsync(
                voice.Hatsuon,
                correctedPronounce,
                parameter,
                correctedPath)
                ?? throw new InvalidOperationException("Corrected synthesis returned null Pronounce.");
            Check("corrected_synthesis_kept_pause_zero",
                GetPauseVowelLength(GetAudioQuery(correctedReturned)) == 0.0);

            var baselineBytes = File.ReadAllBytes(baselinePath);
            var correctedBytes = File.ReadAllBytes(correctedPath);
            var baselineHash = Hash(baselineBytes);
            var correctedHash = Hash(correctedBytes);

            Check("baseline_and_corrected_wav_differ",
                !baselineHash.Equals(correctedHash, StringComparison.OrdinalIgnoreCase));

            var baseline = new VoiceSnapshot(
                baselinePronounce,
                baselineBytes,
                baselineHash,
                baselinePause);
            var corrected = new VoiceSnapshot(
                correctedReturned,
                correctedBytes,
                correctedHash,
                0.0);

            ApplySnapshot(voice, voicePath, baseline);
            Check("baseline_snapshot_attached",
                ReferenceEquals(voice.Pronounce, baseline.Pronounce)
                && GetPauseVowelLength(GetAudioQuery(voice.Pronounce!)) == baselinePause
                && HashFile(voicePath) == baselineHash);

            // Separate fixture creation/native setup from the plugin correction operation.
            manager.Record();

            int recorded = 0;
            int undoed = 0;
            int redoed = 0;
            int undoCallbacks = 0;
            int redoCallbacks = 0;

            recordedHandler = (_, _) => recorded++;
            undoHandler = (_, _) => undoed++;
            redoHandler = (_, _) => redoed++;
            manager.Recorded += recordedHandler;
            manager.Undoed += undoHandler;
            manager.Redoed += redoHandler;

            ApplySnapshot(voice, voicePath, corrected);
            manager.AddCommand(new UndoRedoActionCommand(
                () =>
                {
                    undoCallbacks++;
                    ApplySnapshot(voice, voicePath, baseline);
                },
                () =>
                {
                    redoCallbacks++;
                    ApplySnapshot(voice, voicePath, corrected);
                }));
            manager.Record();

            Check("correction_committed_as_single_record", recorded == 1);
            Check("corrected_state_after_apply",
                GetPauseVowelLength(GetAudioQuery(voice.Pronounce!)) == 0.0
                && HashFile(voicePath) == correctedHash);

            window.Activate();
            Native.SetForegroundWindow(new WindowInteropHelper(window).Handle);

            var undoEventBefore = undoed;
            await Native.Key(0x5A, ctrl: true);
            await WaitUntil(
                "correction undo stabilization",
                () => undoed > undoEventBefore
                   && undoCallbacks == 1
                   && ReferenceEquals(voice.Pronounce, baseline.Pronounce)
                   && GetPauseVowelLength(GetAudioQuery(voice.Pronounce!)) == baselinePause
                   && HashFile(voicePath) == baselineHash);

            Check("one_user_undo_event", undoed - undoEventBefore == 1);
            Check("one_plugin_undo_callback", undoCallbacks == 1);
            Check("undo_restores_baseline_pronounce",
                ReferenceEquals(voice.Pronounce, baseline.Pronounce)
                && GetPauseVowelLength(GetAudioQuery(voice.Pronounce!)) == baselinePause);
            Check("undo_restores_baseline_wav",
                HashFile(voicePath) == baselineHash);

            var redoEventBefore = redoed;
            await Native.Key(0x59, ctrl: true);
            await WaitUntil(
                "correction redo stabilization",
                () => redoed > redoEventBefore
                   && redoCallbacks == 1
                   && ReferenceEquals(voice.Pronounce, corrected.Pronounce)
                   && GetPauseVowelLength(GetAudioQuery(voice.Pronounce!)) == 0.0
                   && HashFile(voicePath) == correctedHash);

            Check("one_user_redo_event", redoed - redoEventBefore == 1);
            Check("one_plugin_redo_callback", redoCallbacks == 1);
            Check("redo_restores_corrected_pronounce",
                ReferenceEquals(voice.Pronounce, corrected.Pronounce)
                && GetPauseVowelLength(GetAudioQuery(voice.Pronounce!)) == 0.0);
            Check("redo_restores_corrected_wav",
                HashFile(voicePath) == correctedHash);

            undoEventBefore = undoed;
            await Native.Key(0x5A, ctrl: true);
            await WaitUntil(
                "final undo stabilization",
                () => undoed > undoEventBefore
                   && undoCallbacks == 2
                   && ReferenceEquals(voice.Pronounce, baseline.Pronounce)
                   && HashFile(voicePath) == baselineHash);

            Check("second_undo_restores_baseline",
                undoCallbacks == 2
                && HashFile(voicePath) == baselineHash
                && GetPauseVowelLength(GetAudioQuery(voice.Pronounce!)) == baselinePause);

            File.WriteAllText(
                Path.Combine(output, "undo-redo-observation.json"),
                JsonSerializer.Serialize(new
                {
                    host = "4.56.1.0 Lite",
                    manager = new
                    {
                        managerAcquisition.ModelFieldVisibility,
                        managerAcquisition.ManagerPropertyVisibility,
                        managerAcquisition.PublicReachableCandidateCount,
                        managerAcquisition.PublicReachableCandidates
                    },
                    baseline = new
                    {
                        pauseVowelLength = baselinePause,
                        wavLength = baselineBytes.Length,
                        wavSha256 = baselineHash,
                        pronounceType = baselinePronounce.GetType().FullName
                    },
                    corrected = new
                    {
                        pauseVowelLength = 0.0,
                        wavLength = correctedBytes.Length,
                        wavSha256 = correctedHash,
                        pronounceType = correctedReturned.GetType().FullName
                    },
                    history = new
                    {
                        recorded,
                        undoed,
                        redoed,
                        undoCallbacks,
                        redoCallbacks,
                        finalPause = GetPauseVowelLength(GetAudioQuery(voice.Pronounce!)),
                        finalWavSha256 = HashFile(voicePath)
                    }
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            if (recordedHandler is not null) manager.Recorded -= recordedHandler;
            if (undoHandler is not null) manager.Undoed -= undoHandler;
            if (redoHandler is not null) manager.Redoed -= redoHandler;
            registration.Restore();
        }
    }

    sealed record ManagerAcquisition(
        UndoRedoManager Manager,
        string ModelFieldVisibility,
        string ManagerPropertyVisibility,
        int PublicReachableCandidateCount,
        string[] PublicReachableCandidates);

    static ManagerAcquisition AcquireUndoManager(object main, object active, Timeline timeline)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var modelField = main.GetType().GetField("model", all)
            ?? throw new MissingMemberException(main.GetType().FullName, "model");
        var model = modelField.GetValue(main)
            ?? throw new InvalidOperationException("MainViewModel.model was null.");

        var managerProperty = model.GetType().GetProperty("UndoRedoManager", all)
            ?? throw new MissingMemberException(model.GetType().FullName, "UndoRedoManager");
        var manager = managerProperty.GetValue(model) as UndoRedoManager
            ?? throw new InvalidOperationException("MainModel.UndoRedoManager was unavailable.");

        var candidates = new List<string>();

        void Inspect(string label, object target)
        {
            foreach (var p in target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (p.GetIndexParameters().Length != 0 || !typeof(UndoRedoManager).IsAssignableFrom(p.PropertyType))
                    continue;
                candidates.Add($"{label}.property:{p.Name}");
            }

            foreach (var f in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (typeof(UndoRedoManager).IsAssignableFrom(f.FieldType))
                    candidates.Add($"{label}.field:{f.Name}");
            }
        }

        Inspect("main", main);
        Inspect("active", active);
        Inspect("timeline", timeline);
        Inspect("model", model);

        foreach (var type in AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeTypes).Distinct())
        {
            foreach (var p in type.GetProperties(BindingFlags.Static | BindingFlags.Public))
            {
                if (p.GetIndexParameters().Length == 0 && typeof(UndoRedoManager).IsAssignableFrom(p.PropertyType))
                    candidates.Add($"static-property:{type.FullName}.{p.Name}");
            }

            foreach (var f in type.GetFields(BindingFlags.Static | BindingFlags.Public))
            {
                if (typeof(UndoRedoManager).IsAssignableFrom(f.FieldType))
                    candidates.Add($"static-field:{type.FullName}.{f.Name}");
            }
        }

        return new ManagerAcquisition(
            manager,
            Visibility(modelField),
            managerProperty.GetMethod is null ? "no-getter" : Visibility(managerProperty.GetMethod),
            candidates.Distinct().OrderBy(x => x).Count(),
            candidates.Distinct().OrderBy(x => x).ToArray());
    }

    static string Visibility(FieldInfo f) =>
        f.IsPublic ? "public" :
        f.IsFamily ? "protected" :
        f.IsAssembly ? "internal" :
        f.IsPrivate ? "private" :
        "nonpublic";

    static string Visibility(MethodBase m) =>
        m.IsPublic ? "public" :
        m.IsFamily ? "protected" :
        m.IsAssembly ? "internal" :
        m.IsPrivate ? "private" :
        "nonpublic";

    static Type[] SafeTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(x => x is not null).Cast<Type>().ToArray(); }
        catch { return []; }
    }

    static void ApplySnapshot(VoiceItem voice, string path, VoiceSnapshot snapshot)
    {
        File.WriteAllBytes(path, snapshot.WavBytes);
        voice.ClearVoiceCache();
        voice.Pronounce = snapshot.Pronounce;
    }

    static async Task WaitUntil(string name, Func<bool> predicate, int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (predicate())
                return;
            await Task.Delay(50);
        }
        throw new TimeoutException(name);
    }

    static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    static string HashFile(string path) => Hash(File.ReadAllBytes(path));

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

    static class Native
    {
        const byte VK_CONTROL = 0x11;
        const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        internal static async Task Key(byte key, bool ctrl)
        {
            if (ctrl) keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (ctrl) keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            await Task.Delay(180);
        }
    }

    static void Check(string id, bool passed) =>
        requirements.Add(new { id, passed });

    static void Write(string status, string? error)
    {
        File.WriteAllText(
            Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(new
            {
                schema = "cnwl.voiceitem-correction-undo-redo.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
