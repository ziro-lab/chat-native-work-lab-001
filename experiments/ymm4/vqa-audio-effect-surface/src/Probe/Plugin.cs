using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Audio;
using YukkuriMovieMaker.Player.Audio.Effects;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.UndoRedo;

namespace Ymm4VqaAudioEffectSurfaceProbe;

public enum ProbeMode
{
    [Display(Name = "None")]
    None,
    [Display(Name = "Hold")]
    Hold,
}

[AudioEffect("CNWL VQA 発音補助 Audio", ["CNWL", "音声"], ["VQA"])]
public sealed class ProbeAudioEffect : AudioEffectBase
{
    public const string DisplayLabel = "CNWL VQA 発音補助 Audio";

    public override string Label => DisplayLabel;

    [Display(GroupName = "発音補助", Name = "入力記号", Description = "強制区切り入力用の記号")]
    [TextEditor]
    public string Token
    {
        get => field;
        set => Set(ref field, value);
    } = "|";

    [Display(GroupName = "発音補助", Name = "モード", Description = "試験用設定")]
    [EnumComboBox]
    public ProbeMode Mode
    {
        get => field;
        set => Set(ref field, value);
    } = ProbeMode.None;

    public override IAudioEffectProcessor CreateAudioEffect(TimeSpan duration) =>
        new PassThroughAudioProcessor(duration);

    public override IEnumerable<string> CreateExoAudioFilters(
        int keyFrameIndex,
        ExoOutputDescription exoOutputDescription) => [];

    protected override IEnumerable<IAnimatable> GetAnimatables() => [];
}

internal sealed class PassThroughAudioProcessor : AudioEffectProcessorBase
{
    readonly TimeSpan duration;

    public PassThroughAudioProcessor(TimeSpan duration) =>
        this.duration = duration;

    public override int Hz => Input?.Hz ?? 0;

    public override long Duration =>
        Input is not null
            ? Input.Duration
            : (long)(duration.TotalSeconds * Hz) * 2;

    protected override void seek(long position) =>
        Input?.Seek(position);

    protected override int read(
        float[] destBuffer,
        int offset,
        int count) =>
        Input?.Read(destBuffer, offset, count) ?? 0;
}

internal sealed class FixtureAudioStream : IAudioStream
{
    readonly float[] data;
    long position;

    public FixtureAudioStream(float[] data, int hz = 48000)
    {
        this.data = data;
        Hz = hz;
    }

    public int Hz { get; }
    public long Duration => data.LongLength;
    public long Position => position;

    public int Read(float[] destBuffer, int offset, int count)
    {
        var remaining = Math.Max(0, data.LongLength - position);
        var copied = (int)Math.Min(count, remaining);
        Array.Copy(data, position, destBuffer, offset, copied);
        position += copied;
        return copied;
    }

    public void Seek(long position) =>
        this.position = Math.Clamp(position, 0, data.LongLength);

    public void Seek(TimeSpan time) =>
        Seek((long)(time.TotalSeconds * Hz) * 2);

    public void Dispose() { }
}

public sealed class TimelineTool : IToolPlugin
{
    public string Name => "CNWL VQA Audio Effect Gate";
    public Type ViewModelType => typeof(TimelineVm);
    public Type ViewType => typeof(ProbeView);
    public bool AllowMultipleInstances => false;
    public string DefaultGroupName =>
        YukkuriMovieMaker.Resources.Localization.Texts.ToolGroupUtilityName;
}

public sealed class ProbeView : UserControl
{
    public ProbeView() =>
        Content = new TextBlock { Text = "CNWL VQA Audio Effect Gate" };
}

public sealed class TimelineVm : ITimelineToolViewModel
{
    public void SetTimelineToolInfo(TimelineToolInfo info) =>
        Probe.Accept(info);
}

public sealed class Bootstrap : ILocalizePlugin
{
    public string Name => "CNWL VQA Audio Effect Bootstrap";
    public void SetCulture(CultureInfo cultureInfo) =>
        Probe.ScheduleFallback();
}

internal static class Probe
{
    const string Remark = "CNWL_VQA_AUDIO_EFFECT_SURFACE";
    const string Serif = "音声エフェクト試験です。";
    static bool scheduled;
    static bool running;
    static string output = "";
    static TimelineToolInfo? latestInfo;
    static readonly List<object> requirements = [];
    static readonly List<string> progress = [];

    internal static void ScheduleFallback()
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_VQA_AUDIO_EFFECT_OUTPUT");
        if (string.IsNullOrWhiteSpace(dir))
            return;

        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        if (scheduled)
            return;

        scheduled = true;
        Application.Current.Dispatcher.BeginInvoke(
            new Action(Start),
            DispatcherPriority.ApplicationIdle);
    }

    static void Start()
    {
        Log("fallback bootstrap active");

        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };

        int ticks = 0;
        bool created = false;
        bool openAttempted = false;

        timer.Tick += (_, _) =>
        {
            try
            {
                if (File.Exists(Path.Combine(output, "result.json")))
                {
                    timer.Stop();
                    return;
                }

                ticks++;

                foreach (Window window in Application.Current.Windows)
                {
                    var main = window.DataContext;
                    if (main?.GetType().FullName
                        != "YukkuriMovieMaker.ViewModels.MainViewModel")
                    {
                        continue;
                    }

                    var active = main.GetType().GetProperty(
                        "ActiveTimelineViewModel",
                        BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.NonPublic)
                        ?.GetValue(main);

                    if (active is null && !created)
                    {
                        created = true;
                        main.GetType().GetMethod(
                            "CreateProject",
                            Type.EmptyTypes)
                            ?.Invoke(main, null);
                        continue;
                    }

                    if (active is null)
                        continue;

                    var prop = main.GetType().GetProperty(
                        "ToolMenuItems",
                        BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.NonPublic);

                    if (prop?.GetValue(main) is not IEnumerable items)
                        continue;

                    var found = new Dictionary<string, object>();
                    foreach (var item in items.Cast<object>())
                        VisitToolMenu(item, found, 0);

                    if (!found.TryGetValue(
                        "CNWL VQA Audio Effect Gate",
                        out var target))
                    {
                        continue;
                    }

                    Log("target tool menu item observed");

                    if (!openAttempted)
                    {
                        openAttempted = true;
                        Log("target invoke=" + TryInvokeTool(target));
                    }
                }

                if (ticks >= 100)
                {
                    timer.Stop();
                    Write(
                        "FAIL_VQA_AUDIO_EFFECT_SURFACE",
                        "Host did not deliver TimelineToolInfo before timeout.");
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write(
                    "FAIL_VQA_AUDIO_EFFECT_SURFACE",
                    ex.ToString());
            }
        };

        timer.Start();
    }

    static void VisitToolMenu(
        object item,
        Dictionary<string, object> found,
        int depth)
    {
        if (depth > 8)
            return;

        var type = item.GetType();
        string? label = null;

        foreach (var name in new[] { "Header", "Title", "Name" })
        {
            try
            {
                var value = type.GetProperty(name)
                    ?.GetValue(item)
                    ?.ToString();

                if (!string.IsNullOrWhiteSpace(value))
                {
                    label = value;
                    break;
                }
            }
            catch
            {
            }
        }

        if (label == "CNWL VQA Audio Effect Gate")
            found[label] = item;

        foreach (var childName in new[] { "Children", "Items" })
        {
            try
            {
                if (type.GetProperty(childName)?.GetValue(item)
                    is IEnumerable children)
                {
                    foreach (var child in children.Cast<object>())
                        VisitToolMenu(child, found, depth + 1);
                }
            }
            catch
            {
            }
        }
    }

    static bool TryInvokeTool(object item)
    {
        if (item is ICommand direct
            && direct.CanExecute(null))
        {
            direct.Execute(null);
            return true;
        }

        foreach (var property in item.GetType()
            .GetProperties(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic))
        {
            try
            {
                if (property.GetIndexParameters().Length == 0
                    && property.GetValue(item) is ICommand command)
                {
                    foreach (var parameter in new object?[] { null, item })
                    {
                        if (command.CanExecute(parameter))
                        {
                            command.Execute(parameter);
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        return false;
    }

    internal static void Accept(TimelineToolInfo info)
    {
        latestInfo = info;
        var dir = Environment.GetEnvironmentVariable("CNWL_VQA_AUDIO_EFFECT_OUTPUT");
        if (string.IsNullOrWhiteSpace(dir))
            return;

        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);
        Log("SetTimelineToolInfo received");

        if (running)
            return;

        running = true;
        Application.Current.Dispatcher.BeginInvoke(
            new Action(async () => await RunAsync(info)),
            DispatcherPriority.ApplicationIdle);
    }

    static async Task RunAsync(TimelineToolInfo initialInfo)
    {
        try
        {
            Check("custom_audio_effect_host_loaded",
                typeof(ProbeAudioEffect).Assembly == typeof(TimelineTool).Assembly
                && typeof(ProbeAudioEffect).GetCustomAttributes(
                    typeof(AudioEffectAttribute), inherit: false).Length == 1);

            var timeline = initialInfo.Timeline
                ?? throw new InvalidOperationException("TimelineToolInfo.Timeline is null.");
            var manager = initialInfo.UndoRedoManager
                ?? throw new InvalidOperationException("TimelineToolInfo.UndoRedoManager is null.");

            var character = new Character { Name = "CNWL Audio Surface" };
            var voice = new VoiceItem(character)
            {
                Serif = Serif,
                Hatsuon = Serif,
                Remark = Remark,
                Frame = 120,
                Layer = 4,
                Length = 180
            };

            Require(timeline.TryAddItems([voice], voice.Frame, voice.Layer),
                "voice add");

            await Task.Delay(300);

            var publicAudioProperties = DescribeAudioProperties(voice);
            var collectionProperty = FindAudioEffectCollectionProperty(voice);
            Check("voice_audio_collection_public_enumerable",
                collectionProperty is not null
                && collectionProperty.GetMethod?.IsPublic == true
                && collectionProperty.GetValue(voice) is IEnumerable);

            if (collectionProperty is null)
                throw new InvalidOperationException(
                    "No public VoiceItem audio-effect collection was found.");

            var effect = new ProbeAudioEffect
            {
                Token = "BOUNDARY",
                Mode = ProbeMode.Hold,
                IsEnabled = true,
                Remark = "CNWL audio settings"
            };

            var effectEvents = new List<string>();
            effect.PropertyChanged += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.PropertyName))
                    effectEvents.Add(e.PropertyName!);
            };

            string? addError = null;
            var attached = TryMutateCollection(
                voice, collectionProperty, effect, add: true, out addError);

            Check("audio_effect_public_attach",
                attached && ContainsEffect(voice, collectionProperty, effect));

            Check("audio_effect_not_in_subtitle_collection",
                !voice.JimakuVideoEffects.Cast<object>()
                    .Any(x => ReferenceEquals(x, effect)));

            effect.IsEnabled = false;
            effect.Token = "||";
            effect.Mode = ProbeMode.None;

            Check("effect_enabled_notification",
                effectEvents.Contains(nameof(ProbeAudioEffect.IsEnabled)));
            Check("effect_custom_property_notification",
                effectEvents.Contains(nameof(ProbeAudioEffect.Token))
                && effectEvents.Contains(nameof(ProbeAudioEffect.Mode)));

            var input = new float[]
            {
                -1.0f, -0.75f, -0.5f, -0.25f,
                0.0f, 0.25f, 0.5f, 0.75f, 1.0f, 0.125f
            };
            var outputSamples = new float[input.Length];
            using (var source = new FixtureAudioStream(input))
            using (var processor = effect.CreateAudioEffect(TimeSpan.FromSeconds(1)))
            {
                processor.Input = source;
                var count = processor.Read(outputSamples, 0, outputSamples.Length);
                Check("pass_through_sample_identity",
                    count == input.Length
                    && input.SequenceEqual(outputSamples));
            }

            var beforeSerif = voice.Serif;
            var beforeHatsuon = voice.Hatsuon;
            var beforeFile = voice.FilePath;

            manager.Record();
            manager.AddCommand(new UndoRedoActionCommand(
                () => MutateOrThrow(
                    voice, collectionProperty, effect, add: false),
                () => MutateOrThrow(
                    voice, collectionProperty, effect, add: true)));
            manager.Record();

            await manager.UndoAsync();
            var removedByUndo =
                !ContainsEffect(voice, collectionProperty, effect);

            await manager.RedoAsync();
            var restoredByRedo =
                ContainsEffect(voice, collectionProperty, effect);

            Check("audio_effect_membership_undo_redo",
                removedByUndo && restoredByRedo);

            var uiObservation = await ObserveEditorUiAsync(voice);
            Check("audio_effect_visible_in_item_editor",
                uiObservation.EffectLabelVisible
                && uiObservation.AudioSectionVisible
                && !uiObservation.SubtitleCollectionContainsEffect);
            Check("audio_effect_property_editors_visible",
                uiObservation.TokenLabelVisible
                && uiObservation.ModeLabelVisible);

            effect.IsEnabled = false;
            effect.Token = "SAVE_RELOAD_TOKEN";
            effect.Mode = ProbeMode.Hold;

            var main = FindMainViewModel()
                ?? throw new InvalidOperationException("MainViewModel not found.");
            var save = PublicMethod(main, "SaveProject", typeof(string));
            var open = PublicMethod(main, "OpenProject", typeof(string));

            var projectA = Path.Combine(output, "audio-effect-a.ymmp");
            var projectB = Path.Combine(output, "audio-effect-b.ymmp");
            save.Invoke(main, [projectA]);
            await WaitUntil("project A save",
                () => File.Exists(projectA)
                    && new FileInfo(projectA).Length > 0);

            effect.Token = "MUTATED_B";
            MutateOrThrow(voice, collectionProperty, effect, add: false);
            save.Invoke(main, [projectB]);
            await WaitUntil("project B save",
                () => File.Exists(projectB)
                    && new FileInfo(projectB).Length > 0);

            open.Invoke(main, [projectA]);
            await WaitUntil("project A reopen",
                () => FindVoiceFromActiveTimeline(Remark) is { } loaded
                    && !ReferenceEquals(loaded, voice),
                15000);

            var reloaded = FindVoiceFromActiveTimeline(Remark)
                ?? throw new InvalidOperationException("Reloaded VoiceItem not found.");
            var reloadProperty = FindAudioEffectCollectionProperty(reloaded);
            var restoredEffects = reloadProperty is null
                ? []
                : EnumerateEffects(reloaded, reloadProperty)
                    .OfType<ProbeAudioEffect>()
                    .ToArray();

            var restored = restoredEffects.SingleOrDefault();
            Check("audio_effect_settings_save_reload",
                restored is not null
                && restored.Token == "SAVE_RELOAD_TOKEN"
                && restored.Mode == ProbeMode.Hold
                && restored.IsEnabled == false
                && !ReferenceEquals(reloaded, voice));

            var reloadPublicEnumerable =
                reloadProperty?.GetMethod?.IsPublic == true
                && reloadProperty.GetValue(reloaded) is IEnumerable;
            Check("audio_effect_public_enumeration_after_reload",
                reloadPublicEnumerable
                && restoredEffects.Length == 1);

            var reloadedSerifBeforeRemove = reloaded.Serif;
            var reloadedHatsuonBeforeRemove = reloaded.Hatsuon;
            var reloadedFileBeforeRemove = reloaded.FilePath;

            string? removeError = null;
            var removed = restored is not null
                && reloadProperty is not null
                && TryMutateCollection(
                    reloaded, reloadProperty, restored, add: false, out removeError);

            Check("audio_effect_public_remove",
                removed
                && reloadProperty is not null
                && !EnumerateEffects(reloaded, reloadProperty)
                    .OfType<ProbeAudioEffect>().Any());

            Check("remove_preserves_ordinary_voice_source",
                reloaded.Serif == reloadedSerifBeforeRemove
                && reloaded.Hatsuon == reloadedHatsuonBeforeRemove
                && reloaded.FilePath == reloadedFileBeforeRemove);

            var legacy = DescribeLegacySubtitleSurface(reloaded);

            File.WriteAllText(
                Path.Combine(output, "observation.json"),
                JsonSerializer.Serialize(new
                {
                    host = "4.56.1.0 Lite",
                    sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                    collection = new
                    {
                        collectionProperty.Name,
                        type = collectionProperty.PropertyType.FullName,
                        publicGet = collectionProperty.GetMethod?.IsPublic == true,
                        publicSet = collectionProperty.SetMethod?.IsPublic == true,
                        addError,
                        removeError
                    },
                    publicAudioProperties,
                    effectEvents,
                    uiObservation,
                    saveReload = new
                    {
                        projectA,
                        projectB,
                        restoredCount = restoredEffects.Length,
                        restored?.Token,
                        restored?.Mode,
                        restored?.IsEnabled
                    },
                    legacySubtitleSurface = legacy,
                    officialShape = new
                    {
                        baseType = typeof(ProbeAudioEffect).BaseType?.FullName,
                        attribute = typeof(AudioEffectAttribute).FullName,
                        processorBase = typeof(AudioEffectProcessorBase).FullName,
                        tokenEditors = typeof(ProbeAudioEffect)
                            .GetProperty(nameof(ProbeAudioEffect.Token))?
                            .GetCustomAttributes(false)
                            .Select(x => x.GetType().FullName)
                            .ToArray(),
                        modeEditors = typeof(ProbeAudioEffect)
                            .GetProperty(nameof(ProbeAudioEffect.Mode))?
                            .GetCustomAttributes(false)
                            .Select(x => x.GetType().FullName)
                            .ToArray()
                    }
                }, new JsonSerializerOptions { WriteIndented = true }));

            var allPassed = requirements.All(x =>
            {
                var prop = x.GetType().GetProperty("passed");
                return prop?.GetValue(x) is true;
            });

            Write(
                allPassed
                    ? "PASS_VQA_AUDIO_EFFECT_SURFACE"
                    : "FAIL_VQA_AUDIO_EFFECT_SURFACE",
                allPassed ? null : "One or more required assertions failed.");
        }
        catch (Exception ex)
        {
            Write("FAIL_VQA_AUDIO_EFFECT_SURFACE", ex.ToString());
        }
    }

    static object[] DescribeAudioProperties(VoiceItem voice) =>
        voice.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(x =>
                x.Name.Contains("Audio", StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains("Effect", StringComparison.OrdinalIgnoreCase))
            .Select(x => (object)new
            {
                x.Name,
                type = x.PropertyType.FullName,
                publicGet = x.GetMethod?.IsPublic == true,
                publicSet = x.SetMethod?.IsPublic == true,
                currentType = SafeGet(x, voice)?.GetType().FullName
            })
            .ToArray();

    static PropertyInfo? FindAudioEffectCollectionProperty(VoiceItem voice)
    {
        var properties = voice.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(x => x.GetMethod?.IsPublic == true)
            .OrderByDescending(x =>
                string.Equals(x.Name, "AudioEffects", StringComparison.Ordinal))
            .ThenByDescending(x =>
                x.Name.Contains("AudioEffect", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var property in properties)
        {
            object? value;
            try { value = property.GetValue(voice); }
            catch { continue; }

            if (value is not IEnumerable)
                continue;

            var hasAudioName =
                property.Name.Contains("Audio", StringComparison.OrdinalIgnoreCase)
                && property.Name.Contains("Effect", StringComparison.OrdinalIgnoreCase);
            var typeLooksAudio =
                property.PropertyType.FullName?.Contains(
                    "AudioEffect", StringComparison.OrdinalIgnoreCase) == true
                || property.PropertyType.GetGenericArguments()
                    .Any(x => typeof(AudioEffectBase).IsAssignableFrom(x)
                        || x.FullName?.Contains(
                            "AudioEffect", StringComparison.OrdinalIgnoreCase) == true);

            if (hasAudioName || typeLooksAudio)
                return property;
        }

        return null;
    }

    static bool TryMutateCollection(
        VoiceItem voice,
        PropertyInfo property,
        ProbeAudioEffect effect,
        bool add,
        out string? error)
    {
        error = null;
        var current = property.GetValue(voice);
        if (current is null)
        {
            error = "Collection is null.";
            return false;
        }

        if (current is IList list
            && !list.IsReadOnly
            && !list.IsFixedSize)
        {
            try
            {
                if (add) list.Add(effect);
                else list.Remove(effect);
                return add == ContainsEffect(voice, property, effect);
            }
            catch (Exception ex)
            {
                error = ex.GetBaseException().Message;
                return false;
            }
        }

        var methodName = add ? "Add" : "Remove";
        var method = current.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(x =>
                x.Name == methodName
                && x.GetParameters().Length == 1
                && x.GetParameters()[0].ParameterType
                    .IsAssignableFrom(effect.GetType()));

        if (method is null)
        {
            error = $"No public {methodName}(effect) route.";
            return false;
        }

        object? returned;
        try
        {
            returned = method.Invoke(current, [effect]);
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            return false;
        }

        if (returned is not null
            && property.PropertyType.IsInstanceOfType(returned)
            && !ReferenceEquals(returned, current))
        {
            if (property.SetMethod?.IsPublic != true)
            {
                error =
                    "Collection returned an updated value but property has no public setter.";
                return false;
            }

            property.SetValue(voice, returned);
        }

        if (add == ContainsEffect(voice, property, effect))
            return true;

        error = $"Public {methodName} route did not change membership.";
        return false;
    }

    static void MutateOrThrow(
        VoiceItem voice,
        PropertyInfo property,
        ProbeAudioEffect effect,
        bool add)
    {
        if (!TryMutateCollection(
            voice, property, effect, add, out var error))
        {
            throw new InvalidOperationException(error);
        }
    }

    static bool ContainsEffect(
        VoiceItem voice,
        PropertyInfo property,
        ProbeAudioEffect effect) =>
        EnumerateEffects(voice, property)
            .Any(x => ReferenceEquals(x, effect));

    static IEnumerable<object> EnumerateEffects(
        VoiceItem voice,
        PropertyInfo property) =>
        property.GetValue(voice) is IEnumerable enumerable
            ? enumerable.Cast<object>()
            : [];

    static async Task<UiObservation> ObserveEditorUiAsync(VoiceItem voice)
    {
        var main = FindMainViewModel();
        var selectionMembers = new List<object>();
        bool selectionAttempted = false;

        if (main is not null)
        {
            var active = main.GetType().GetProperty(
                "ActiveTimelineViewModel",
                BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(main);

            if (active is not null)
            {
                var itemsProperty = active.GetType().GetProperty(
                    "Items",
                    BindingFlags.Instance | BindingFlags.Public);

                if (itemsProperty?.GetValue(active) is IEnumerable vms)
                {
                    var vm = vms.Cast<object>().FirstOrDefault(x =>
                        ReferenceEquals(
                            x.GetType().GetProperty(
                                "Item",
                                BindingFlags.Instance | BindingFlags.Public)
                                ?.GetValue(x),
                            voice));

                    if (vm is not null)
                    {
                        foreach (var property in vm.GetType()
                            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                            .Where(x =>
                                x.Name.Contains("Select", StringComparison.OrdinalIgnoreCase)))
                        {
                            selectionMembers.Add(new
                            {
                                kind = "property",
                                property.Name,
                                type = property.PropertyType.FullName,
                                publicSet = property.SetMethod?.IsPublic == true
                            });

                            if (property.PropertyType == typeof(bool)
                                && property.SetMethod?.IsPublic == true)
                            {
                                try
                                {
                                    property.SetValue(vm, true);
                                    selectionAttempted = true;
                                }
                                catch { }
                            }

                            if (typeof(ICommand).IsAssignableFrom(property.PropertyType))
                            {
                                try
                                {
                                    if (property.GetValue(vm) is ICommand command)
                                    {
                                        foreach (var parameter in new object?[] { null, vm, voice })
                                        {
                                            if (command.CanExecute(parameter))
                                            {
                                                command.Execute(parameter);
                                                selectionAttempted = true;
                                                break;
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }

                        foreach (var method in active.GetType()
                            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                            .Where(x =>
                                x.Name.Contains("Select", StringComparison.OrdinalIgnoreCase)
                                && x.GetParameters().Length == 1))
                        {
                            var parameter = method.GetParameters()[0].ParameterType;
                            selectionMembers.Add(new
                            {
                                kind = "method",
                                method.Name,
                                parameter = parameter.FullName
                            });

                            try
                            {
                                if (parameter.IsInstanceOfType(vm))
                                {
                                    method.Invoke(active, [vm]);
                                    selectionAttempted = true;
                                }
                                else if (parameter.IsInstanceOfType(voice))
                                {
                                    method.Invoke(active, [voice]);
                                    selectionAttempted = true;
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        await Task.Delay(500);

        // The Item Editor is scrollable and lower groups can remain unrealized
        // until they enter the viewport. Use only public WPF surfaces here:
        // force visible ScrollViewers to their end, then expand audio/effect groups.
        foreach (Window window in Application.Current.Windows)
        {
            foreach (var viewer in EnumerateVisual(window).OfType<ScrollViewer>())
            {
                try
                {
                    if (viewer.IsVisible && viewer.ScrollableHeight > 0)
                    {
                        viewer.ScrollToVerticalOffset(viewer.ScrollableHeight);
                        viewer.UpdateLayout();
                    }
                }
                catch { }
            }
        }

        await Task.Delay(250);

        foreach (Window window in Application.Current.Windows)
        {
            foreach (var expander in EnumerateVisual(window).OfType<Expander>())
            {
                var header = expander.Header?.ToString() ?? "";
                if (header.Contains(ProbeAudioEffect.DisplayLabel, StringComparison.Ordinal)
                    || header.Contains("音声", StringComparison.Ordinal)
                    || header.Contains("Audio", StringComparison.OrdinalIgnoreCase)
                    || header.Contains("エフェクト", StringComparison.Ordinal)
                    || header.Contains("Effect", StringComparison.OrdinalIgnoreCase))
                {
                    expander.IsExpanded = true;
                    try { expander.UpdateLayout(); } catch { }
                }
            }
        }

        await Task.Delay(250);

        foreach (Window window in Application.Current.Windows)
        {
            foreach (var viewer in EnumerateVisual(window).OfType<ScrollViewer>())
            {
                try
                {
                    if (viewer.IsVisible && viewer.ScrollableHeight > 0)
                    {
                        viewer.ScrollToVerticalOffset(viewer.ScrollableHeight);
                        viewer.UpdateLayout();
                    }
                }
                catch { }
            }
        }

        await Task.Delay(300);

        var texts = Application.Current.Windows
            .Cast<Window>()
            .SelectMany(EnumerateVisual)
            .Where(x => x is FrameworkElement fe && fe.IsVisible)
            .Select(GetText)
            .OfType<string>()
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        File.WriteAllText(
            Path.Combine(output, "ui-text.txt"),
            string.Join(Environment.NewLine, texts));

        var effectVisible = texts.Any(x =>
            x.Contains(ProbeAudioEffect.DisplayLabel, StringComparison.Ordinal));
        var audioSection = texts.Any(x =>
            x.Contains("音声エフェクト", StringComparison.Ordinal)
            || x.Contains("オーディオエフェクト", StringComparison.OrdinalIgnoreCase)
            || x.Contains("Audio Effect", StringComparison.OrdinalIgnoreCase));
        var token = texts.Any(x =>
            x.Contains("入力記号", StringComparison.Ordinal));
        var mode = texts.Any(x =>
            x.Contains("モード", StringComparison.Ordinal));

        return new UiObservation(
            selectionAttempted,
            selectionMembers.ToArray(),
            effectVisible,
            audioSection,
            token,
            mode,
            voice.JimakuVideoEffects.Cast<object>()
                .Any(x => x is ProbeAudioEffect),
            texts.Where(x =>
                    x.Contains("CNWL", StringComparison.OrdinalIgnoreCase)
                    || x.Contains("音声", StringComparison.Ordinal)
                    || x.Contains("入力記号", StringComparison.Ordinal)
                    || x.Contains("モード", StringComparison.Ordinal))
                .Take(100)
                .ToArray());
    }

    static IEnumerable<DependencyObject> EnumerateVisual(DependencyObject root)
    {
        yield return root;
        int count;
        try { count = VisualTreeHelper.GetChildrenCount(root); }
        catch { yield break; }

        for (int i = 0; i < count; i++)
        {
            DependencyObject child;
            try { child = VisualTreeHelper.GetChild(root, i); }
            catch { continue; }

            foreach (var descendant in EnumerateVisual(child))
                yield return descendant;
        }
    }

    static string? GetText(DependencyObject value) =>
        value switch
        {
            TextBlock text => text.Text,
            Label label => label.Content?.ToString(),
            GroupBox group => group.Header?.ToString(),
            Expander expander => expander.Header?.ToString(),
            ContentControl content when content.Content is string text => text,
            _ => null,
        };

    static object DescribeLegacySubtitleSurface(VoiceItem voice)
    {
        var property = voice.GetType().GetProperty(
            nameof(VoiceItem.JimakuVideoEffects),
            BindingFlags.Instance | BindingFlags.Public);

        var current = property?.GetValue(voice);
        return new
        {
            property = property?.Name,
            type = property?.PropertyType.FullName,
            publicGet = property?.GetMethod?.IsPublic == true,
            publicSet = property?.SetMethod?.IsPublic == true,
            currentType = current?.GetType().FullName,
            publicAdd = current?.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Any(x => x.Name == "Add" && x.GetParameters().Length == 1) == true,
            publicRemove = current?.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Any(x => x.Name == "Remove" && x.GetParameters().Length == 1) == true,
        };
    }

    static object? SafeGet(PropertyInfo property, object target)
    {
        try { return property.GetValue(target); }
        catch { return null; }
    }

    static object? FindMainViewModel() =>
        Application.Current.Windows
            .Cast<Window>()
            .Select(x => x.DataContext)
            .FirstOrDefault(x =>
                x?.GetType().FullName
                == "YukkuriMovieMaker.ViewModels.MainViewModel");

    static MethodInfo PublicMethod(
        object target,
        string name,
        params Type[] parameterTypes) =>
        target.GetType().GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            parameterTypes,
            modifiers: null)
        ?? throw new MissingMethodException(target.GetType().FullName, name);

    static VoiceItem? FindCurrentVoice(string remark)
    {
        var timeline = latestInfo?.Timeline;
        return timeline?.Items.OfType<VoiceItem>()
            .FirstOrDefault(x => x.Remark == remark);
    }

    static VoiceItem? FindVoiceFromActiveTimeline(string remark)
    {
        var main = FindMainViewModel();
        if (main is null)
            return null;

        var active = main.GetType().GetProperty(
            "ActiveTimelineViewModel",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(main);

        var timeline = active is null ? null : FindTimeline(active);
        return timeline?.Items.OfType<VoiceItem>()
            .FirstOrDefault(x => x.Remark == remark);
    }

    static Timeline? FindTimeline(object active)
    {
        for (var type = active.GetType();
             type is not null;
             type = type.BaseType)
        {
            foreach (var field in type.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (typeof(Timeline).IsAssignableFrom(field.FieldType)
                    && field.GetValue(active) is Timeline timeline)
                {
                    return timeline;
                }
            }

            foreach (var property in type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length != 0
                    || !typeof(Timeline).IsAssignableFrom(property.PropertyType))
                {
                    continue;
                }

                try
                {
                    if (property.GetValue(active) is Timeline timeline)
                        return timeline;
                }
                catch { }
            }
        }

        return null;
    }

    static async Task WaitUntil(
        string name,
        Func<bool> condition,
        int timeoutMs = 10000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
                return;
            await Task.Delay(100);
        }

        throw new TimeoutException(name);
    }

    static void Require(bool value, string name)
    {
        if (!value)
            throw new InvalidOperationException(
                "Required setup failed: " + name);
    }

    static void Check(string id, bool passed)
    {
        requirements.Add(new { id, passed });
        Log($"{id}={passed}");
    }

    static void Log(string value)
    {
        progress.Add(value);
        if (!string.IsNullOrWhiteSpace(output))
        {
            File.AppendAllText(
                Path.Combine(output, "progress.txt"),
                DateTime.UtcNow.ToString("O")
                + " "
                + value
                + Environment.NewLine);
        }
    }

    static void Write(string status, string? error)
    {
        if (string.IsNullOrWhiteSpace(output))
            return;

        File.WriteAllText(
            Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(new
            {
                schema = "cnwl.vqa-audio-effect-surface.v1",
                status,
                host = "4.56.1.0 Lite",
                sourceHead = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                requirements,
                progress,
                error
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    sealed record UiObservation(
        bool SelectionAttempted,
        object[] SelectionMembers,
        bool EffectLabelVisible,
        bool AudioSectionVisible,
        bool TokenLabelVisible,
        bool ModeLabelVisible,
        bool SubtitleCollectionContainsEffect,
        string[] RelevantVisibleText);
}
