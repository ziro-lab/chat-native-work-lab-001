using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ymm4NoHarmonyPanel;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyProductState;
using Ymm4NoHarmonyStructuralConvenience;
using Ymm4NoHarmonyUx;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YmmGroupItem = YukkuriMovieMaker.Project.Items.GroupItem;
using YukkuriMovieMaker.UndoRedo;

namespace Ymm4NoHarmonyFolderLayoutProbe;

internal sealed partial class HandsOnController : IDisposable
{
    private const string MenuTag = "CNWL.P4.HandsOn";

    private readonly Window window;
    private readonly FrameworkElement labels;
    private readonly Host host;
    private readonly Timeline timeline;
    private readonly UndoRedoManager undo;
    private readonly FolderStateStore state;
    private readonly DirectDisplay display;
    private readonly StructuralFolderBridge structural;
    private readonly FolderVisibilityCoordinator visibility;
    private readonly FolderCommands commands;
    private readonly InputMapAdapter input;
    private readonly FileDropMapAdapter fileDrop;
    private readonly TimelineVisualSummaryOverlay? visualSummary;
    private readonly AdornerLayer adornerLayer;
    private readonly MouseButtonEventHandler mouseHandler;

    private FolderOverlayAdorner? adorner;
    private bool overlayRefreshQueued;
    private bool disposed;

    internal HandsOnController(
        Window window,
        FrameworkElement labels,
        Host host,
        UndoRedoManager undo,
        FolderStateStore state)
    {
        this.window = window;
        this.labels = labels;
        this.host = host;
        this.undo = undo;
        this.state = state;
        timeline = host.Timeline;

        display = new DirectDisplay(host, HandsOnRuntime.Diagnostic);
        display.RegisterRefreshTarget(labels);
        display.Applied += OnDisplayApplied;
        structural = new StructuralFolderBridge(
            window,
            timeline,
            undo,
            state,
            HandsOnRuntime.Diagnostic);
        visibility = new FolderVisibilityCoordinator(
            timeline,
            state,
            HandsOnRuntime.Diagnostic);
        commands = new FolderCommands(
            window,
            host,
            undo,
            state,
            structural,
            visibility,
            HandsOnRuntime.Diagnostic);
        input = new InputMapAdapter(host, display, HandsOnRuntime.Diagnostic);
        fileDrop = new FileDropMapAdapter(host, display, HandsOnRuntime.Diagnostic);
        visualSummary = TimelineVisualSummaryOverlay.TryCreate(
            host,
            display,
            HandsOnRuntime.Diagnostic);
        adornerLayer = AdornerLayer.GetAdornerLayer(labels)
            ?? throw new InvalidOperationException("LayerLabels has no AdornerLayer.");

        mouseHandler = OnPreviewMouseDown;
        window.AddHandler(Mouse.PreviewMouseDownEvent, mouseHandler, true);
        RefreshFromDocument();
        ScheduleS0IntegrationSmoke();
        ScheduleS1IntegrationSmoke();
        ScheduleS2IntegrationSmoke();
        ScheduleS3IntegrationSmoke();
        ScheduleS4IntegrationSmoke();
        ScheduleS5CoordinateSmoke();
        ScheduleS5IntegrationSmoke();
        ScheduleP5CompatibilitySmoke();
        ScheduleP5MediaCompatibilitySmoke();
        ScheduleP5ThirdPartyCompatibilitySmoke();
        ScheduleP5ConfiguredVoiceSmoke();
        ScheduleP6Probes();
    }

    partial void ScheduleP6Probes();

    private void ScheduleP5ConfiguredVoiceSmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P5_CONFIGURED_VOICE_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunP5ConfiguredVoiceSmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunP5ConfiguredVoiceSmokeAsync()
    {
        try
        {
            await Task.Delay(700);

            if (timeline.Items.Any())
                throw new InvalidOperationException(
                    "P5 configured Voice smoke must start from an empty Timeline.");

            var wav = Environment.GetEnvironmentVariable(
                "CNWL_P5_VOICE_WAV");
            if (string.IsNullOrWhiteSpace(wav))
                throw new InvalidOperationException(
                    "CNWL_P5_VOICE_WAV is missing.");

            wav = Path.GetFullPath(wav);
            if (!File.Exists(wav))
                throw new FileNotFoundException(
                    "Configured Voice WAV fixture missing.",
                    wav);

            for (var layer = 0; layer <= 6; layer++)
            {
                ExecuteHostCommand(CommandType.AddLayer, layer);
                await Task.Delay(80);
            }

            static Type LoadedType(string fullName) =>
                AppDomain.CurrentDomain
                    .GetAssemblies()
                    .SelectMany(assembly =>
                    {
                        try { return assembly.GetTypes(); }
                        catch { return Type.EmptyTypes; }
                    })
                    .SingleOrDefault(type =>
                        string.Equals(
                            type.FullName,
                            fullName,
                            StringComparison.Ordinal))
                ?? throw new TypeLoadException(fullName);

            var speakerType = LoadedType(
                "YukkuriMovieMaker.Plugin.Community.Voice.Recording.RecordedVoiceSpeaker");
            var parameterType = LoadedType(
                "YukkuriMovieMaker.Plugin.Community.Voice.Recording.RecordedVoiceParameter");
            var descriptionType = LoadedType(
                "YukkuriMovieMaker.Plugin.Voice.VoiceDescription");

            var speaker =
                speakerType.GetProperty(
                    "Instance",
                    System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Public)
                ?.GetValue(null)
                ?? Activator.CreateInstance(speakerType)
                ?? throw new InvalidOperationException(
                    "RecordedVoiceSpeaker instance unavailable.");

            var parameter = Activator.CreateInstance(parameterType)
                ?? throw new InvalidOperationException(
                    "RecordedVoiceParameter instance unavailable.");

            parameterType.GetProperty("AudioFilePath")
                ?.SetValue(parameter, wav);
            parameterType.GetProperty("RecordsDirectory")
                ?.SetValue(parameter, Path.GetDirectoryName(wav) ?? "");
            parameterType.GetProperty("Text")
                ?.SetValue(parameter, "CNWL configured voice");

            var description = Activator.CreateInstance(
                    descriptionType,
                    [speaker])
                ?? throw new InvalidOperationException(
                    "VoiceDescription could not be created.");

            var character = new Character
            {
                Name = "CNWL_P5_CONFIGURED_VOICE"
            };

            character.GetType()
                .GetProperty("Voice")
                ?.SetValue(character, description);
            character.GetType()
                .GetProperty("VoiceParameter")
                ?.SetValue(character, parameter);

            var voice = new VoiceItem(character)
            {
                Frame = 100,
                Layer = 2,
                Length = 30,
                Serif = "CNWL configured voice",
                Remark = "CNWL_P5_CONFIGURED_VOICE"
            };

            voice.GetType()
                .GetProperty("VoiceParameter")
                ?.SetValue(voice, parameter);

            if (!timeline.TryAddItems(
                    [voice],
                    voice.Frame,
                    voice.Layer,
                    isItemSelectionEnabled: false))
            {
                throw new InvalidOperationException(
                    "P5 configured Voice TryAddItems returned false.");
            }

            await Task.Delay(1000);

            if (!timeline.Items.Any(item =>
                    ReferenceEquals(item, voice)))
            {
                throw new InvalidOperationException(
                    "P5 configured Voice did not remain live.");
            }

            var geometry =
                HandsOnHostAccess.ReadTimelineItemGeometry(host.Vm)
                    .SingleOrDefault(current =>
                        ReferenceEquals(current.Item, voice));

            if (geometry.Item is null
                || !double.IsFinite(geometry.Left)
                || !double.IsFinite(geometry.Width)
                || geometry.Width <= 0)
            {
                throw new InvalidOperationException(
                    "P5 configured Voice public geometry missing.");
            }

            var folderId = Guid.Parse(
                "dddddddd-eeee-ffff-0000-000000000001");
            var key = timeline.ID.ToString("D");

            state.ReplaceProductState(
                FolderProductStateRules.ReplaceCore(
                    FolderProductState.Empty,
                    FolderDocumentRules.NormalizeAndValidate(
                        new FolderDocument
                        {
                            Timelines =
                            [
                                new TimelineFolderState
                                {
                                    TimelineKey = key,
                                    Folders =
                                    [
                                        new PersistedFolder
                                        {
                                            Id = folderId,
                                            Start = 1,
                                            End = 2,
                                            Name = "P5 Voice",
                                            IsCollapsed = true
                                        }
                                    ]
                                }
                            ]
                        })));

            await Task.Delay(700);
            display.ThrowIfFailed();

            if (!display.Layout.IsHidden(2)
                || display.Layout.OwnerLogical(2) != 1)
            {
                throw new InvalidOperationException(
                    "P5 configured Voice fold owner mapping failed.");
            }

            if (visualSummary is null)
                throw new InvalidOperationException(
                    "P5 configured Voice visual summary unavailable.");

            visualSummary.Refresh();

            if (visualSummary.TimingBandCount != 1)
                throw new InvalidOperationException(
                    $"P5 expected 1 configured Voice timing band, got {visualSummary.TimingBandCount}.");

            commands.SelectItems(folderId);
            await Task.Delay(150);

            if (!timeline.SelectedItems.Any(item =>
                    ReferenceEquals(item, voice)))
            {
                throw new InvalidOperationException(
                    "P5 configured Voice selection failed.");
            }

            // This is the history boundary that the synthetic unconfigured
            // Voice fixture could not cross.
            undo.Record();
            await Task.Delay(1000);

            var folderRow = PanelProjection.Build(
                    state.ProductState,
                    key,
                    Math.Max(
                        timeline.MaxLayer,
                        timeline.LayerSettings.MaxLayer),
                    timeline.Items
                        .GroupBy(item => item.Layer)
                        .ToDictionary(
                            group => group.Key,
                            group => group.Count()),
                    [])
                .Single(row =>
                    row.FolderId == folderId);

            var dragBlock = PanelMoveRules.TryGetDragBlock([folderRow])
                ?? throw new InvalidOperationException(
                    "P5 configured Voice folder did not resolve to a drag block.");

            var drop = new PanelDropTarget(
                OriginalInsertionBoundary: 5,
                IntoFolderId: null);

            if (!commands.CanMovePanelRows(dragBlock, drop))
                throw new InvalidOperationException(
                    "P5 configured Voice block move was rejected.");

            commands.MovePanelRows(dragBlock, drop);
            await Task.Delay(550);

            if (voice.Layer != 4)
                throw new InvalidOperationException(
                    $"P5 configured Voice moved layer={voice.Layer}, expected 4.");
            AssertP5Folder(folderId, 3, 4);

            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(500);

            if (voice.Layer != 2)
                throw new InvalidOperationException(
                    $"P5 configured Voice Undo layer={voice.Layer}, expected 2.");
            AssertP5Folder(folderId, 1, 2);

            ExecuteHostCommand(CommandType.Redo, null);
            await Task.Delay(500);

            if (voice.Layer != 4)
                throw new InvalidOperationException(
                    $"P5 configured Voice Redo layer={voice.Layer}, expected 4.");
            AssertP5Folder(folderId, 3, 4);

            WriteP5ConfiguredVoiceResult(
                string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        "PASS_P5_CONFIGURED_VOICE",
                        $"timeline={key}",
                        "voice_backend=RecordedVoice",
                        "voice_fixture=local_wav",
                        "construct_add_live=1",
                        "common_geometry=1",
                        "fold_owner_mapping=1",
                        "timing_summary=1",
                        "folder_selection=1",
                        "history_record=1",
                        "block_move_undo_redo=1",
                        "no_voice_specific_folder_adapter=true"
                    })
                + Environment.NewLine);
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic(
                "p5_configured_voice_smoke_error=" + ex);

            WriteP5ConfiguredVoiceResult(
                "FAIL_P5_CONFIGURED_VOICE\n"
                + ex
                + "\n");
        }
    }

    private static void WriteP5ConfiguredVoiceResult(string text)
    {
        var dir = Environment.GetEnvironmentVariable(
            "CNWL_P4_HANDS_ON_DIAG_DIR");

        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "p5-configured-voice-result.txt"),
            text);
    }

    private void ScheduleP5ThirdPartyCompatibilitySmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P5_THIRD_PARTY_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunP5ThirdPartyCompatibilitySmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunP5ThirdPartyCompatibilitySmokeAsync()
    {
        try
        {
            await Task.Delay(700);

            if (timeline.Items.Any())
                throw new InvalidOperationException(
                    "P5 third-party smoke must start from an empty Timeline.");

            for (var layer = 0; layer <= 6; layer++)
            {
                ExecuteHostCommand(CommandType.AddLayer, layer);
                await Task.Delay(80);
            }

            const string typeName = "YMM43D.Project.Items.LightItem";

            var type = AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(assembly =>
                {
                    try { return assembly.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .SingleOrDefault(candidate =>
                    string.Equals(
                        candidate.FullName,
                        typeName,
                        StringComparison.Ordinal))
                ?? throw new TypeLoadException(
                    $"P5 third-party type not loaded: {typeName}.");

            var raw = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException(
                    "P5 third-party Activator returned null.");

            if (raw is not IItem item)
                throw new InvalidCastException(
                    "P5 third-party LightItem is not IItem.");

            SetP5Int(raw, "Frame", 80);
            SetP5Int(raw, "Layer", 2);
            SetP5Int(raw, "Length", 40);
            SetP5StringIfPossible(
                raw,
                "Remark",
                "CNWL_P5_THIRD_PARTY_LIGHT");

            if (!timeline.TryAddItems(
                    [item],
                    item.Frame,
                    item.Layer,
                    isItemSelectionEnabled: false))
            {
                throw new InvalidOperationException(
                    "P5 third-party TryAddItems returned false.");
            }

            await Task.Delay(900);

            if (!timeline.Items.Any(current =>
                    ReferenceEquals(current, item)))
            {
                throw new InvalidOperationException(
                    "P5 third-party item did not remain live.");
            }

            var beforeGeometry =
                HandsOnHostAccess.ReadTimelineItemGeometry(host.Vm)
                    .SingleOrDefault(current =>
                        ReferenceEquals(current.Item, item));

            if (beforeGeometry.Item is null
                || !double.IsFinite(beforeGeometry.Left)
                || !double.IsFinite(beforeGeometry.Width)
                || beforeGeometry.Width <= 0)
            {
                throw new InvalidOperationException(
                    "P5 third-party public geometry missing.");
            }

            var folderId = Guid.Parse(
                "cccccccc-dddd-eeee-ffff-000000000001");
            var key = timeline.ID.ToString("D");

            state.ReplaceProductState(
                FolderProductStateRules.ReplaceCore(
                    FolderProductState.Empty,
                    FolderDocumentRules.NormalizeAndValidate(
                        new FolderDocument
                        {
                            Timelines =
                            [
                                new TimelineFolderState
                                {
                                    TimelineKey = key,
                                    Folders =
                                    [
                                        new PersistedFolder
                                        {
                                            Id = folderId,
                                            Start = 1,
                                            End = 2,
                                            Name = "P5 Third Party",
                                            IsCollapsed = true
                                        }
                                    ]
                                }
                            ]
                        })));

            await Task.Delay(750);
            display.ThrowIfFailed();

            if (!display.Layout.IsHidden(2)
                || display.Layout.OwnerLogical(2) != 1)
            {
                throw new InvalidOperationException(
                    "P5 third-party fold owner mapping failed.");
            }

            if (visualSummary is null)
                throw new InvalidOperationException(
                    "P5 third-party visual summary unavailable.");

            visualSummary.Refresh();

            if (visualSummary.TimingBandCount != 1)
                throw new InvalidOperationException(
                    $"P5 expected 1 third-party timing band, got {visualSummary.TimingBandCount}.");

            commands.SelectItems(folderId);
            await Task.Delay(150);

            if (!timeline.SelectedItems.Any(selected =>
                    ReferenceEquals(selected, item)))
            {
                throw new InvalidOperationException(
                    "P5 third-party folder selection failed.");
            }

            undo.Record();
            await Task.Delay(250);

            var folderRow = PanelProjection.Build(
                    state.ProductState,
                    key,
                    Math.Max(
                        timeline.MaxLayer,
                        timeline.LayerSettings.MaxLayer),
                    timeline.Items
                        .GroupBy(current => current.Layer)
                        .ToDictionary(
                            group => group.Key,
                            group => group.Count()),
                    [])
                .Single(row =>
                    row.FolderId == folderId);

            var dragBlock = PanelMoveRules.TryGetDragBlock([folderRow])
                ?? throw new InvalidOperationException(
                    "P5 third-party folder did not resolve to a drag block.");

            var drop = new PanelDropTarget(
                OriginalInsertionBoundary: 5,
                IntoFolderId: null);

            if (!commands.CanMovePanelRows(dragBlock, drop))
                throw new InvalidOperationException(
                    "P5 third-party block move was rejected.");

            commands.MovePanelRows(dragBlock, drop);
            await Task.Delay(500);

            if (item.Layer != 4)
                throw new InvalidOperationException(
                    $"P5 third-party moved layer={item.Layer}, expected 4.");

            AssertP5Folder(folderId, 3, 4);

            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(450);

            if (item.Layer != 2)
                throw new InvalidOperationException(
                    $"P5 third-party Undo layer={item.Layer}, expected 2.");

            AssertP5Folder(folderId, 1, 2);

            ExecuteHostCommand(CommandType.Redo, null);
            await Task.Delay(450);

            if (item.Layer != 4)
                throw new InvalidOperationException(
                    $"P5 third-party Redo layer={item.Layer}, expected 4.");

            AssertP5Folder(folderId, 3, 4);

            var afterGeometry =
                HandsOnHostAccess.ReadTimelineItemGeometry(host.Vm)
                    .SingleOrDefault(current =>
                        ReferenceEquals(current.Item, item));

            if (afterGeometry.Item is null
                || !double.IsFinite(afterGeometry.Left)
                || !double.IsFinite(afterGeometry.Width)
                || afterGeometry.Width <= 0)
            {
                throw new InvalidOperationException(
                    "P5 third-party geometry missing after move.");
            }

            WriteP5ThirdPartyResult(
                string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        "PASS_P5_THIRD_PARTY",
                        $"timeline={key}",
                        "source_repo=Dolphin-kun/YMM43D",
                        "source_commit=a5fe44443d9b62912dec6861edf68cbdff9e7810",
                        "source_license=MIT",
                        "item_type=YMM43D.Project.Items.LightItem",
                        "construct_add_live=1",
                        "common_geometry=1",
                        "fold_owner_mapping=1",
                        "timing_summary=1",
                        "folder_selection=1",
                        "block_move_undo_redo=1",
                        "no_type_specific_folder_adapter=true"
                    })
                + Environment.NewLine);
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic(
                "p5_third_party_smoke_error=" + ex);

            WriteP5ThirdPartyResult(
                "FAIL_P5_THIRD_PARTY\n"
                + ex
                + "\n");
        }
    }

    private static void WriteP5ThirdPartyResult(string text)
    {
        var dir = Environment.GetEnvironmentVariable(
            "CNWL_P4_HANDS_ON_DIAG_DIR");

        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "p5-third-party-result.txt"),
            text);
    }

    private void ScheduleP5MediaCompatibilitySmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P5_MEDIA_COMPATIBILITY_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunP5MediaCompatibilitySmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunP5MediaCompatibilitySmokeAsync()
    {
        try
        {
            await Task.Delay(500);

            if (timeline.Items.Any())
                throw new InvalidOperationException(
                    "P5 media compatibility smoke must start from an empty Timeline.");

            string RequiredPath(string variable)
            {
                var value = Environment.GetEnvironmentVariable(variable);
                if (string.IsNullOrWhiteSpace(value))
                    throw new InvalidOperationException(
                        $"P5 media fixture variable missing: {variable}.");

                var full = Path.GetFullPath(value);
                if (!File.Exists(full))
                    throw new FileNotFoundException(
                        $"P5 media fixture does not exist: {full}.",
                        full);
                return full;
            }

            var imagePath = RequiredPath("CNWL_P5_IMAGE_FILE");
            var audioPath = RequiredPath("CNWL_P5_AUDIO_FILE");
            var videoPath = RequiredPath("CNWL_P5_VIDEO_FILE");

            for (var layer = 0; layer <= 6; layer++)
            {
                ExecuteHostCommand(CommandType.AddLayer, layer);
                await Task.Delay(80);
            }

            var image = new ImageItem(imagePath)
            {
                Frame = 40,
                Layer = 2,
                Remark = "CNWL_P5_REAL_IMAGE"
            };
            var audio = new AudioItem(audioPath)
            {
                Frame = 140,
                Layer = 2,
                Remark = "CNWL_P5_REAL_AUDIO"
            };
            var video = new VideoItem(videoPath)
            {
                Frame = 260,
                Layer = 2,
                Remark = "CNWL_P5_REAL_VIDEO"
            };

            IItem[] fixtures = [image, audio, video];

            foreach (var item in fixtures)
            {
                if (item.Length <= 0)
                {
                    throw new InvalidOperationException(
                        $"P5 {item.GetType().Name} did not derive a positive media length.");
                }

                if (!timeline.TryAddItems(
                        [item],
                        item.Frame,
                        item.Layer,
                        isItemSelectionEnabled: false))
                {
                    throw new InvalidOperationException(
                        $"P5 TryAddItems returned false for media-backed {item.GetType().Name}.");
                }
            }

            await Task.Delay(1000);

            foreach (var item in fixtures)
            {
                if (!timeline.Items.Any(current =>
                        ReferenceEquals(current, item)))
                {
                    throw new InvalidOperationException(
                        $"P5 media-backed {item.GetType().Name} did not remain live.");
                }
            }

            var geometry =
                HandsOnHostAccess.ReadTimelineItemGeometry(host.Vm);

            foreach (var item in fixtures)
            {
                var current = geometry.SingleOrDefault(candidate =>
                    ReferenceEquals(candidate.Item, item));

                if (current.Item is null
                    || !double.IsFinite(current.Left)
                    || !double.IsFinite(current.Width)
                    || current.Width <= 0)
                {
                    throw new InvalidOperationException(
                        $"P5 media-backed public geometry missing for {item.GetType().Name}.");
                }
            }

            var folderId = Guid.Parse(
                "bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
            var key = timeline.ID.ToString("D");

            state.ReplaceProductState(
                FolderProductStateRules.ReplaceCore(
                    FolderProductState.Empty,
                    FolderDocumentRules.NormalizeAndValidate(
                        new FolderDocument
                        {
                            Timelines =
                            [
                                new TimelineFolderState
                                {
                                    TimelineKey = key,
                                    Folders =
                                    [
                                        new PersistedFolder
                                        {
                                            Id = folderId,
                                            Start = 1,
                                            End = 2,
                                            Name = "P5 Media",
                                            IsCollapsed = true
                                        }
                                    ]
                                }
                            ]
                        })));

            await Task.Delay(750);
            display.ThrowIfFailed();

            if (!display.Layout.IsHidden(2)
                || display.Layout.OwnerLogical(2) != 1)
            {
                throw new InvalidOperationException(
                    "P5 media fold mapping did not map L2 to owner L1.");
            }

            if (visualSummary is null)
                throw new InvalidOperationException(
                    "P5 media visual summary overlay is unavailable.");

            visualSummary.Refresh();

            if (visualSummary.TimingBandCount != 3)
            {
                throw new InvalidOperationException(
                    $"P5 expected 3 media timing bands, got {visualSummary.TimingBandCount}.");
            }

            commands.SelectItems(folderId);
            await Task.Delay(150);

            if (timeline.SelectedItems.Count(selected =>
                    fixtures.Any(fixture =>
                        ReferenceEquals(selected, fixture)))
                != 3)
            {
                throw new InvalidOperationException(
                    "P5 media folder selection did not select all three fixtures.");
            }

            undo.Record();
            await Task.Delay(300);

            var folderRow = PanelProjection.Build(
                    state.ProductState,
                    key,
                    Math.Max(
                        timeline.MaxLayer,
                        timeline.LayerSettings.MaxLayer),
                    timeline.Items
                        .GroupBy(item => item.Layer)
                        .ToDictionary(
                            group => group.Key,
                            group => group.Count()),
                    [])
                .Single(row =>
                    row.FolderId == folderId);

            var block = PanelMoveRules.TryGetDragBlock([folderRow])
                ?? throw new InvalidOperationException(
                    "P5 media folder did not resolve to a drag block.");

            var drop = new PanelDropTarget(
                OriginalInsertionBoundary: 5,
                IntoFolderId: null);

            if (!commands.CanMovePanelRows(block, drop))
                throw new InvalidOperationException(
                    "P5 media block move was rejected.");

            commands.MovePanelRows(block, drop);
            await Task.Delay(550);

            AssertP5FixtureLayers(fixtures, expectedLayer: 4);
            AssertP5Folder(folderId, 3, 4);

            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(500);

            AssertP5FixtureLayers(fixtures, expectedLayer: 2);
            AssertP5Folder(folderId, 1, 2);

            ExecuteHostCommand(CommandType.Redo, null);
            await Task.Delay(500);

            AssertP5FixtureLayers(fixtures, expectedLayer: 4);
            AssertP5Folder(folderId, 3, 4);

            var afterGeometry =
                HandsOnHostAccess.ReadTimelineItemGeometry(host.Vm);

            foreach (var item in fixtures)
            {
                if (!afterGeometry.Any(current =>
                        ReferenceEquals(current.Item, item)
                        && double.IsFinite(current.Left)
                        && double.IsFinite(current.Width)
                        && current.Width > 0))
                {
                    throw new InvalidOperationException(
                        $"P5 media geometry missing after move for {item.GetType().Name}.");
                }
            }

            WriteP5MediaResult(
                string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        "PASS_P5_MEDIA_REALISM",
                        $"timeline={key}",
                        "media_types=ImageItem,AudioItem,VideoItem",
                        $"image_length={image.Length}",
                        $"audio_length={audio.Length}",
                        $"video_length={video.Length}",
                        "construct_from_real_file=3",
                        "add_live_geometry=3",
                        "fold_owner_mapping=3",
                        "timing_summary=3",
                        "folder_selection=3",
                        "block_move_undo_redo=3",
                        "no_type_specific_folder_adapter=true"
                    })
                + Environment.NewLine);
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic(
                "p5_media_compatibility_smoke_error=" + ex);

            WriteP5MediaResult(
                "FAIL_P5_MEDIA_REALISM\n"
                + ex
                + "\n");
        }
    }

    private static void WriteP5MediaResult(string text)
    {
        var dir = Environment.GetEnvironmentVariable(
            "CNWL_P4_HANDS_ON_DIAG_DIR");

        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "p5-media-result.txt"),
            text);
    }

    private void ScheduleP5CompatibilitySmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P5_COMPATIBILITY_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunP5CompatibilitySmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunP5CompatibilitySmokeAsync()
    {
        try
        {
            await Task.Delay(500);

            if (timeline.Items.Any())
                throw new InvalidOperationException(
                    "P5 compatibility smoke must start from a resource-free Timeline.");

            for (var layer = 0; layer <= 6; layer++)
            {
                ExecuteHostCommand(CommandType.AddLayer, layer);
                await Task.Delay(80);
            }

            var concreteTypes = typeof(IItem).Assembly
                .GetTypes()
                .Where(type =>
                    type != typeof(IItem)
                    && typeof(IItem).IsAssignableFrom(type)
                    && !type.IsAbstract
                    && type.GetConstructor(Type.EmptyTypes) is not null)
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();

            if (concreteTypes.Length != 13)
                throw new InvalidOperationException(
                    $"P5 expected 13 concrete parameterless IItem types, got {concreteTypes.Length}.");

            var fixtures = new List<IItem>();

            for (var index = 0; index < concreteTypes.Length; index++)
            {
                var type = concreteTypes[index];
                var raw = Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException(
                        $"P5 Activator returned null for {type.FullName}.");

                if (raw is not IItem item)
                    throw new InvalidCastException(
                        $"P5 {type.FullName} is not IItem.");

                var frame = 20 + index * 34;

                SetP5Int(raw, "Frame", frame);
                SetP5Int(raw, "Layer", 2);
                SetP5Int(raw, "Length", 20);
                SetP5StringIfPossible(
                    raw,
                    "Remark",
                    "CNWL_P5_" + type.Name);

                if (raw is YmmGroupItem group)
                    group.GroupRange = 1;

                if (!timeline.TryAddItems(
                        [item],
                        frame,
                        2,
                        isItemSelectionEnabled: false))
                {
                    throw new InvalidOperationException(
                        $"P5 TryAddItems returned false for {type.FullName}.");
                }

                fixtures.Add(item);
            }

            await Task.Delay(900);

            if (timeline.Items.Count(item =>
                    fixtures.Any(fixture =>
                        ReferenceEquals(item, fixture)))
                != 13)
            {
                throw new InvalidOperationException(
                    "P5 not all 13 built-in fixtures remained live.");
            }

            var initialGeometry =
                HandsOnHostAccess.ReadTimelineItemGeometry(host.Vm);

            foreach (var fixture in fixtures)
            {
                var geometry = initialGeometry.SingleOrDefault(
                    current => ReferenceEquals(current.Item, fixture));

                if (geometry.Item is null
                    || !double.IsFinite(geometry.Left)
                    || !double.IsFinite(geometry.Width)
                    || geometry.Width < 0)
                {
                    throw new InvalidOperationException(
                        $"P5 common public geometry missing for {fixture.GetType().FullName}.");
                }
            }

            var folderId = Guid.Parse(
                "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            var key = timeline.ID.ToString("D");

            state.ReplaceProductState(
                FolderProductStateRules.ReplaceCore(
                    FolderProductState.Empty,
                    FolderDocumentRules.NormalizeAndValidate(
                        new FolderDocument
                        {
                            Timelines =
                            [
                                new TimelineFolderState
                                {
                                    TimelineKey = key,
                                    Folders =
                                    [
                                        new PersistedFolder
                                        {
                                            Id = folderId,
                                            Start = 1,
                                            End = 2,
                                            Name = "P5 All Built-ins",
                                            IsCollapsed = true
                                        }
                                    ]
                                }
                            ]
                        })));

            await Task.Delay(750);
            display.ThrowIfFailed();

            if (!display.Layout.IsHidden(2)
                || display.Layout.OwnerLogical(2) != 1)
            {
                throw new InvalidOperationException(
                    "P5 fold mapping did not map fixture layer L2 to owner L1.");
            }

            if (visualSummary is null)
                throw new InvalidOperationException(
                    "P5 visual summary overlay is unavailable.");

            visualSummary.Refresh();

            if (visualSummary.TimingBandCount != 13)
            {
                throw new InvalidOperationException(
                    $"P5 expected 13 hidden timing bands, got {visualSummary.TimingBandCount}.");
            }

            commands.SelectItems(folderId);
            await Task.Delay(150);

            var selectedFixtureCount = timeline.SelectedItems.Count(
                selected => fixtures.Any(fixture =>
                    ReferenceEquals(selected, fixture)));

            if (selectedFixtureCount != 13)
            {
                throw new InvalidOperationException(
                    $"P5 folder selection selected {selectedFixtureCount}/13 fixture items.");
            }

            // VoiceItem without a configured speaker is intentionally treated
            // as setup-required for native history recording. P4 already
            // documented that YMM4 resource refresh can reject such a synthetic
            // Voice fixture at Record(). Verify common fold/geometry/selection
            // above, then remove only that zero-resource fixture before the
            // history-bearing block-move check.
            var voice = fixtures.Single(item =>
                item.GetType() == typeof(VoiceItem));

            timeline.DeleteItems([voice]);
            fixtures.Remove(voice);
            await Task.Delay(250);

            undo.Record();
            await Task.Delay(250);

            var folderRow = PanelProjection.Build(
                    state.ProductState,
                    key,
                    Math.Max(timeline.MaxLayer, timeline.LayerSettings.MaxLayer),
                    timeline.Items
                        .GroupBy(item => item.Layer)
                        .ToDictionary(group => group.Key, group => group.Count()),
                    timeline.Items
                        .OfType<YmmGroupItem>()
                        .OrderBy(group => group.Layer)
                        .ThenBy(group => group.Frame)
                        .Select(group => new GroupSpan(
                            group.Layer,
                            group.GroupRange))
                        .ToArray())
                .Single(row => row.FolderId == folderId);

            var block = PanelMoveRules.TryGetDragBlock([folderRow])
                ?? throw new InvalidOperationException(
                    "P5 folder did not resolve to a drag block.");

            var drop = new PanelDropTarget(
                OriginalInsertionBoundary: 5,
                IntoFolderId: null);

            if (!commands.CanMovePanelRows(block, drop))
                throw new InvalidOperationException(
                    "P5 representative block move was rejected.");

            commands.MovePanelRows(block, drop);
            await Task.Delay(500);

            AssertP5FixtureLayers(fixtures, expectedLayer: 4);
            AssertP5Folder(folderId, 3, 4);

            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(450);

            AssertP5FixtureLayers(fixtures, expectedLayer: 2);
            AssertP5Folder(folderId, 1, 2);

            ExecuteHostCommand(CommandType.Redo, null);
            await Task.Delay(450);

            AssertP5FixtureLayers(fixtures, expectedLayer: 4);
            AssertP5Folder(folderId, 3, 4);

            var afterGeometry =
                HandsOnHostAccess.ReadTimelineItemGeometry(host.Vm);

            foreach (var fixture in fixtures)
            {
                if (!afterGeometry.Any(current =>
                        ReferenceEquals(current.Item, fixture)
                        && double.IsFinite(current.Left)
                        && double.IsFinite(current.Width)
                        && current.Width >= 0))
                {
                    throw new InvalidOperationException(
                        $"P5 geometry missing after move for {fixture.GetType().FullName}.");
                }
            }

            WriteP5CompatibilityResult(
                string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        "PASS_P5_COMMON_BUILTINS",
                        $"timeline={key}",
                        "concrete_types=13",
                        "construct_add_live=13",
                        "common_geometry=13",
                        "fold_owner_mapping=13",
                        "timing_summary=13",
                        "folder_selection=13",
                        "history_move_types=12",
                        "voice_history=SETUP_REQUIRED_CONFIGURED_SPEAKER",
                        "block_move_undo_redo=12",
                        "no_type_specific_folder_adapter=true",
                        "types=" + string.Join(
                            ",",
                            concreteTypes.Select(type => type.Name))
                    })
                + Environment.NewLine);
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic(
                "p5_compatibility_smoke_error=" + ex);

            WriteP5CompatibilityResult(
                "FAIL_P5_COMMON_BUILTINS\n"
                + ex
                + "\n");
        }
    }

    private static void SetP5Int(
        object target,
        string propertyName,
        int value)
    {
        var property = target.GetType()
            .GetProperty(
                propertyName,
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public)
            ?? throw new MissingMemberException(
                target.GetType().FullName,
                propertyName);

        if (property.SetMethod?.IsPublic != true)
            throw new MissingMemberException(
                target.GetType().FullName,
                "public " + propertyName + " setter");

        property.SetValue(target, value);
    }

    private static void SetP5StringIfPossible(
        object target,
        string propertyName,
        string value)
    {
        var property = target.GetType()
            .GetProperty(
                propertyName,
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public);

        if (property?.SetMethod?.IsPublic == true
            && property.PropertyType == typeof(string))
        {
            property.SetValue(target, value);
        }
    }

    private void AssertP5FixtureLayers(
        IEnumerable<IItem> fixtures,
        int expectedLayer)
    {
        foreach (var fixture in fixtures)
        {
            if (fixture.Layer != expectedLayer)
            {
                throw new InvalidOperationException(
                    $"P5 {fixture.GetType().Name} layer={fixture.Layer}, expected {expectedLayer}.");
            }
        }
    }

    private void AssertP5Folder(
        Guid folderId,
        int expectedStart,
        int expectedEnd)
    {
        var folder = commands.FindFolder(folderId)
            ?? throw new InvalidOperationException(
                "P5 fixture folder is missing.");

        if (folder.Start != expectedStart
            || folder.End != expectedEnd)
        {
            throw new InvalidOperationException(
                $"P5 folder range {folder.Start}-{folder.End}, expected {expectedStart}-{expectedEnd}.");
        }

        display.ThrowIfFailed();
    }

    private static void WriteP5CompatibilityResult(
        string text)
    {
        var dir = Environment.GetEnvironmentVariable(
            "CNWL_P4_HANDS_ON_DIAG_DIR");

        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "p5-result.txt"),
            text);
    }

    private void ScheduleS5IntegrationSmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P4_S5_INTEGRATION_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunS5IntegrationSmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunS5IntegrationSmokeAsync()
    {
        try
        {
            await Task.Delay(500);

            if (timeline.Items.Any())
                throw new InvalidOperationException(
                    "S5 integration smoke must start from a resource-free Timeline.");

            for (var layer = 0; layer <= 6; layer++)
            {
                ExecuteHostCommand(CommandType.AddLayer, layer);
                await Task.Delay(90);
            }

            var owner = new YmmGroupItem
            {
                Frame = 40,
                Length = 80,
                Layer = 1,
                GroupRange = 3
            };
            var hiddenA = new YmmGroupItem
            {
                Frame = 150,
                Length = 60,
                Layer = 2,
                GroupRange = 1
            };
            var hiddenB = new YmmGroupItem
            {
                Frame = 300,
                Length = 90,
                Layer = 4,
                GroupRange = 1
            };
            var visible = new YmmGroupItem
            {
                Frame = 430,
                Length = 50,
                Layer = 5,
                GroupRange = 1
            };

            foreach (var item in new[]
            {
                owner,
                hiddenA,
                hiddenB,
                visible
            })
            {
                if (!timeline.TryAddItems(
                        [item],
                        item.Frame,
                        item.Layer,
                        isItemSelectionEnabled: false))
                {
                    throw new InvalidOperationException(
                        $"S5 visual fixture add failed at F{item.Frame}/L{item.Layer}.");
                }
            }

            undo.Record();
            await Task.Delay(650);

            var folderId = Guid.Parse(
                "99999999-9999-9999-9999-999999999999");
            var key = timeline.ID.ToString("D");

            state.ReplaceProductState(
                FolderProductStateRules.ReplaceCore(
                    FolderProductState.Empty,
                    FolderDocumentRules.NormalizeAndValidate(
                        new FolderDocument
                        {
                            Timelines =
                            [
                                new TimelineFolderState
                                {
                                    TimelineKey = key,
                                    Folders =
                                    [
                                        new PersistedFolder
                                        {
                                            Id = folderId,
                                            Start = 1,
                                            End = 4,
                                            Name = "Visual",
                                            IsCollapsed = true
                                        }
                                    ]
                                }
                            ]
                        })));

            await Task.Delay(700);
            display.ThrowIfFailed();

            void AssertFolderRowVisualSync(
                bool collapsed,
                string phase)
            {
                if (adorner is null)
                    throw new InvalidOperationException(
                        $"S5 folder overlay missing at {phase}.");

                if (!adorner.TryGetRenderedRowRect(
                        1,
                        out var ownerRowRect))
                {
                    throw new InvalidOperationException(
                        $"S5 owner layer row is not rendered at {phase}.");
                }

                if (!adorner.TryGetFolderRect(
                        folderId,
                        out var folderRect))
                {
                    throw new InvalidOperationException(
                        $"S5 folder tag rect missing at {phase}.");
                }

                var ownerCenter =
                    ownerRowRect.Top
                    + ownerRowRect.Height / 2.0;
                var folderCenter =
                    folderRect.Top
                    + folderRect.Height / 2.0;

                if (Math.Abs(
                        ownerCenter
                        - folderCenter)
                    > 0.75)
                {
                    throw new InvalidOperationException(
                        $"S5 folder tag center is offset from the rendered owner row at {phase}: " +
                        $"owner={ownerCenter:R}, folder={folderCenter:R}.");
                }

                var realizedChildren =
                    new[] { 2, 3, 4 }
                        .Count(layer =>
                            adorner.TryGetRenderedRowRect(
                                layer,
                                out _));

                var expectedChildren =
                    collapsed
                        ? 0
                        : 3;

                if (realizedChildren
                    != expectedChildren)
                {
                    throw new InvalidOperationException(
                        $"S5 LayerLabels did not refresh without scrolling at {phase}: " +
                        $"realized_children={realizedChildren}, expected={expectedChildren}.");
                }
            }

            AssertFolderRowVisualSync(
                collapsed: true,
                phase: "initial-collapse");

            commands.ToggleCollapsed(folderId);
            await Task.Delay(550);
            display.ThrowIfFailed();

            AssertFolderRowVisualSync(
                collapsed: false,
                phase: "expand-no-scroll");

            commands.ToggleCollapsed(folderId);
            await Task.Delay(550);
            display.ThrowIfFailed();

            AssertFolderRowVisualSync(
                collapsed: true,
                phase: "collapse-no-scroll");

            if (visualSummary is null)
                throw new InvalidOperationException(
                    "S5 item-area visual summary overlay is not attached.");

            visualSummary.Refresh();

            if (visualSummary.TimingBandCount != 2)
            {
                throw new InvalidOperationException(
                    $"Expected 2 hidden timing bands, got {visualSummary.TimingBandCount}.");
            }

            var geometry =
                HandsOnHostAccess.ReadTimelineItemGeometry(
                    host.Vm);
            TimelineItemGeometry Geometry(IItem item) =>
                geometry.Single(current =>
                    ReferenceEquals(current.Item, item));

            var hiddenAGeometry = Geometry(hiddenA);
            var hiddenBGeometry = Geometry(hiddenB);
            var visibleGeometry = Geometry(visible);

            bool HasTiming(
                TimelineItemGeometry expected) =>
                visualSummary.TimingRects.Any(rect =>
                    Math.Abs(rect.X - expected.Left) < 0.01
                    && Math.Abs(rect.Width - expected.Width) < 0.01);

            if (!HasTiming(hiddenAGeometry)
                || !HasTiming(hiddenBGeometry)
                || HasTiming(visibleGeometry))
            {
                throw new InvalidOperationException(
                    "S5 timing band X/Width projection does not match hidden-item geometry.");
            }

            var ownerRow =
                display.Layout.VisualRowOfLogical(1);
            var ownerTop =
                ownerRow * (double)display.Height;
            var ownerBottom =
                ownerTop + display.Height;

            if (visualSummary.TimingRects.Any(rect =>
                    rect.Y < ownerTop - 0.01
                    || rect.Bottom > ownerBottom + 0.01))
            {
                throw new InvalidOperationException(
                    "S5 timing bands escaped the collapsed owner row.");
            }

            if (visualSummary.GroupSegmentCount < 1)
                throw new InvalidOperationException(
                    "S5 Group visual projection produced no segments.");

            var ownerGeometry = Geometry(owner);
            if (!visualSummary.GroupRects.Any(rect =>
                    Math.Abs(rect.X - ownerGeometry.Left) < 0.01
                    && Math.Abs(rect.Width - ownerGeometry.Width) < 0.01
                    && rect.Y <= ownerTop + 0.01
                    && ownerTop < rect.Bottom))
            {
                throw new InvalidOperationException(
                    "S5 Group visual segment is not aligned to the owner GroupItem.");
            }

            var zoomHolder = Host.Get(
                host.Vm,
                "TimelineZoom");
            var zoomValue = zoomHolder?.GetType()
                .GetProperty(
                    "Value",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public);
            var zoomBefore = zoomValue?.GetValue(
                zoomHolder);

            if (zoomHolder is null
                || zoomValue?.SetMethod?.IsPublic != true
                || zoomBefore is null)
            {
                throw new InvalidOperationException(
                    "S5 requires the observed public TimelineZoom.Value surface.");
            }

            var beforeZoomRect =
                visualSummary.TimingRects
                    .Single(rect =>
                        Math.Abs(
                            rect.X - hiddenAGeometry.Left)
                        < 0.01);

            zoomValue.SetValue(
                zoomHolder,
                150.0);
            await Task.Delay(700);

            var zoomGeometry =
                HandsOnHostAccess.ReadTimelineItemGeometry(
                    host.Vm);
            var hiddenAZoom = zoomGeometry.Single(
                current =>
                    ReferenceEquals(
                        current.Item,
                        hiddenA));

            if (Math.Abs(
                    hiddenAZoom.Left
                    - hiddenAGeometry.Left)
                < 0.01
                || Math.Abs(
                    hiddenAZoom.Width
                    - hiddenAGeometry.Width)
                < 0.01)
            {
                throw new InvalidOperationException(
                    "S5 zoom did not update YMM4 item geometry.");
            }

            if (!visualSummary.TimingRects.Any(rect =>
                    Math.Abs(
                        rect.X - hiddenAZoom.Left)
                    < 0.01
                    && Math.Abs(
                        rect.Width - hiddenAZoom.Width)
                    < 0.01))
            {
                throw new InvalidOperationException(
                    "S5 overlay did not follow YMM4 zoom geometry.");
            }

            zoomValue.SetValue(
                zoomHolder,
                zoomBefore);
            await Task.Delay(600);

            var restoredGeometry =
                HandsOnHostAccess.ReadTimelineItemGeometry(
                    host.Vm)
                .Single(current =>
                    ReferenceEquals(
                        current.Item,
                        hiddenA));

            if (Math.Abs(
                    restoredGeometry.Left
                    - hiddenAGeometry.Left)
                > 0.01
                || Math.Abs(
                    restoredGeometry.Width
                    - hiddenAGeometry.Width)
                > 0.01)
            {
                throw new InvalidOperationException(
                    "S5 zoom reset did not restore item geometry.");
            }

            var rectBeforeScroll =
                visualSummary.TimingRects
                    .Single(rect =>
                        Math.Abs(
                            rect.X - restoredGeometry.Left)
                        < 0.01);
            var maxScroll = Math.Max(
                0,
                host.Scroll.ExtentWidth
                    - host.Scroll.ViewportWidth);
            var targetScroll = Math.Min(
                120,
                maxScroll);
            host.Scroll.ScrollToHorizontalOffset(
                targetScroll);
            await Task.Delay(350);

            var rectAfterScroll =
                visualSummary.TimingRects
                    .Single(rect =>
                        Math.Abs(
                            rect.X - restoredGeometry.Left)
                        < 0.01);

            if (Math.Abs(
                    rectAfterScroll.X
                    - rectBeforeScroll.X)
                > 0.01)
            {
                throw new InvalidOperationException(
                    "S5 content-coordinate overlay moved under horizontal scroll.");
            }

            host.Scroll.ScrollToHorizontalOffset(0);
            await Task.Delay(250);

            var ownerView = host.ItemViews()
                .FirstOrDefault(view =>
                    ReferenceEquals(
                        Host.Item(view.DataContext),
                        owner));

            var nativeVisuals =
                ownerView is null
                    ? "<owner-view-unrealized>"
                    : string.Join(
                        "|",
                        Host.Elements(ownerView)
                            .Take(80)
                            .Select(element =>
                            {
                                double y;
                                try
                                {
                                    y = element.TranslatePoint(
                                        new Point(),
                                        host.Source).Y;
                                }
                                catch
                                {
                                    y = double.NaN;
                                }

                                return element.GetType().Name
                                    + ":H="
                                    + element.ActualHeight.ToString("R")
                                    + ":Y="
                                    + y.ToString("R");
                            }));

            WriteS5Screenshot();

            WriteS5Result(
                string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        "PASS_S5_INTEGRATION",
                        $"timeline={key}",
                        "overlay_attached=true",
                        $"timing_bands={visualSummary.TimingBandCount}",
                        $"group_segments={visualSummary.GroupSegmentCount}",
                        "timing_x_width_y=true",
                        "fold_expand_no_scroll_refresh=true",
                        "folder_tag_rendered_row_alignment=true",
                        "zoom_follow=true",
                        "horizontal_scroll_content_alignment=true",
                        $"owner_native_visuals={nativeVisuals}"
                    })
                + Environment.NewLine);
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic(
                "s5_integration_smoke_error=" + ex);
            WriteS5Result(
                "FAIL_S5_INTEGRATION\n"
                + ex
                + "\n");
        }
    }

    private void WriteS5Screenshot()
    {
        var dir = Environment.GetEnvironmentVariable(
            "CNWL_P4_HANDS_ON_DIAG_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            return;

        var width = Math.Max(
            1,
            (int)Math.Ceiling(host.View.ActualWidth));
        var height = Math.Max(
            1,
            (int)Math.Ceiling(host.View.ActualHeight));

        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(host.View);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(
            BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(dir);
        using var stream = File.Create(
            Path.Combine(dir, "s5-view.png"));
        encoder.Save(stream);
    }

    private static void WriteS5Result(string text)
    {
        var dir = Environment.GetEnvironmentVariable(
            "CNWL_P4_HANDS_ON_DIAG_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "s5-result.txt"),
            text);
    }

    private void ScheduleS5CoordinateSmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P4_S5_COORDINATE_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunS5CoordinateSmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunS5CoordinateSmokeAsync()
    {
        try
        {
            await Task.Delay(500);

            if (timeline.Items.Any())
                throw new InvalidOperationException(
                    "S5 coordinate smoke must start from a resource-free Timeline.");

            for (var layer = 0; layer <= 3; layer++)
            {
                ExecuteHostCommand(CommandType.AddLayer, layer);
                await Task.Delay(90);
            }

            var a = new YmmGroupItem
            {
                Frame = 120,
                Length = 80,
                Layer = 1,
                GroupRange = 1
            };
            var b = new YmmGroupItem
            {
                Frame = 360,
                Length = 120,
                Layer = 2,
                GroupRange = 1
            };

            foreach (var item in new[] { a, b })
            {
                if (!timeline.TryAddItems(
                        [item],
                        item.Frame,
                        item.Layer,
                        isItemSelectionEnabled: false))
                {
                    throw new InvalidOperationException(
                        $"S5 fixture item add failed at F{item.Frame}/L{item.Layer}.");
                }
            }

            undo.Record();
            await Task.Delay(700);

            var timelineVm = host.Vm;

            var vmItems = Host.Get(
                    timelineVm,
                    "Items")
                as System.Collections.IEnumerable
                ?? throw new InvalidOperationException(
                    "TimelineViewModel.Items is not enumerable.");

            object FindItemVm(IItem target) =>
                vmItems.Cast<object>()
                    .FirstOrDefault(candidate =>
                        ReferenceEquals(
                            Host.Item(candidate),
                            target))
                ?? throw new InvalidOperationException(
                    $"Timeline item VM missing for F{target.Frame}/L{target.Layer}.");

            var itemVmA = FindItemVm(a);
            var itemVmB = FindItemVm(b);

            var leftProperty = itemVmA.GetType()
                .GetProperty("Left", Host.Flags)
                ?? throw new MissingMemberException(
                    itemVmA.GetType().FullName,
                    "Left");
            var widthProperty = itemVmA.GetType()
                .GetProperty("Width", Host.Flags)
                ?? throw new MissingMemberException(
                    itemVmA.GetType().FullName,
                    "Width");


            string DescribeItem(
                string key,
                YmmGroupItem item)
            {
                var vm = item == a
                    ? itemVmA
                    : itemVmB;

                var view = host.ItemViews()
                    .FirstOrDefault(candidate =>
                        ReferenceEquals(
                            Host.Item(candidate.DataContext),
                            item));

                var local = view is null
                    ? (Point?)null
                    : view.TranslatePoint(
                        new Point(),
                        host.Source);

                var candidates = vm.GetType()
                    .GetProperties(Host.Flags)
                    .Where(property =>
                        property.GetIndexParameters().Length == 0
                        && (property.Name.Contains(
                                "Left",
                                StringComparison.OrdinalIgnoreCase)
                            || property.Name.Contains(
                                "Width",
                                StringComparison.OrdinalIgnoreCase)
                            || property.Name.Contains(
                                "Frame",
                                StringComparison.OrdinalIgnoreCase)
                            || property.Name.Contains(
                                "X",
                                StringComparison.OrdinalIgnoreCase)))
                    .Select(property =>
                    {
                        object? value;
                        try
                        {
                            value = property.GetValue(vm);
                        }
                        catch (Exception ex)
                        {
                            value = "<error:"
                                + ex.GetBaseException().Message
                                + ">";
                        }

                        return property.Name
                            + "="
                            + (value ?? "<null>");
                    })
                    .Take(80)
                    .ToArray();

                return string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        $"{key}_frame={item.Frame}",
                        $"{key}_length={item.Length}",
                        $"{key}_layer={item.Layer}",
                        $"{key}_view_realized={view is not null}",
                        $"{key}_view_x={(local?.X.ToString("R") ?? "<unrealized>")}",
                        $"{key}_view_y={(local?.Y.ToString("R") ?? "<unrealized>")}",
                        $"{key}_actual_width={(view?.ActualWidth.ToString("R") ?? "<unrealized>")}",
                        $"{key}_actual_height={(view?.ActualHeight.ToString("R") ?? "<unrealized>")}",
                        $"{key}_vm_type={vm.GetType().FullName}",
                        $"{key}_vm_candidates={string.Join("|", candidates)}"
                    });
            }

            var zoomHolder = Host.Get(
                timelineVm,
                "TimelineZoom");
            var zoomValueProperty = zoomHolder?.GetType()
                .GetProperty(
                    "Value",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public);
            var zoomBefore = zoomValueProperty?.GetValue(
                zoomHolder);

            var adornerAvailable =
                AdornerLayer.GetAdornerLayer(host.Source)
                is not null;

            string ZoomSnapshot(string prefix)
            {
                var aVm = itemVmA;
                var bVm = itemVmB;

                FrameworkElement? ViewFor(IItem item) =>
                    host.ItemViews()
                        .FirstOrDefault(candidate =>
                            ReferenceEquals(
                                Host.Item(candidate.DataContext),
                                item));

                var aView = ViewFor(a);
                var bView = ViewFor(b);
                var aPoint = aView is null
                    ? (Point?)null
                    : aView.TranslatePoint(
                        new Point(),
                        host.Source);
                var bPoint = bView is null
                    ? (Point?)null
                    : bView.TranslatePoint(
                        new Point(),
                        host.Source);

                return string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        $"{prefix}_zoom={zoomValueProperty?.GetValue(zoomHolder) ?? "<null>"}",
                        $"{prefix}_a_left={leftProperty.GetValue(aVm)}",
                        $"{prefix}_a_width={widthProperty.GetValue(aVm)}",
                        $"{prefix}_a_view_x={(aPoint?.X.ToString("R") ?? "<unrealized>")}",
                        $"{prefix}_a_actual_width={(aView?.ActualWidth.ToString("R") ?? "<unrealized>")}",
                        $"{prefix}_b_left={leftProperty.GetValue(bVm)}",
                        $"{prefix}_b_width={widthProperty.GetValue(bVm)}",
                        $"{prefix}_b_view_x={(bPoint?.X.ToString("R") ?? "<unrealized>")}",
                        $"{prefix}_b_actual_width={(bView?.ActualWidth.ToString("R") ?? "<unrealized>")}",
                        $"{prefix}_scroll_h_offset={host.Scroll.HorizontalOffset:R}",
                        $"{prefix}_viewport={Host.Reactive(timelineVm, "Viewport")}"
                    });
            }

            string? zoom150 = null;
            if (zoomHolder is not null
                && zoomValueProperty?.SetMethod?.IsPublic == true)
            {
                zoomValueProperty.SetValue(
                    zoomHolder,
                    150.0);
                await Task.Delay(500);
                zoom150 = ZoomSnapshot("zoom150");

                if (zoomBefore is not null)
                {
                    zoomValueProperty.SetValue(
                        zoomHolder,
                        zoomBefore);
                    await Task.Delay(400);
                }
            }

            var scrollBefore = host.Scroll.HorizontalOffset;
            host.Scroll.ScrollToHorizontalOffset(
                Math.Min(
                    120,
                    Math.Max(
                        0,
                        host.Scroll.ExtentWidth
                        - host.Scroll.ViewportWidth)));
            await Task.Delay(300);
            var scrollSnapshot = ZoomSnapshot("scroll");
            host.Scroll.ScrollToHorizontalOffset(scrollBefore);
            await Task.Delay(250);

            var vmCandidates = timelineVm.GetType()
                .GetProperties(Host.Flags)
                .Where(property =>
                    property.GetIndexParameters().Length == 0
                    && (property.Name.Contains(
                            "Viewport",
                            StringComparison.OrdinalIgnoreCase)
                        || property.Name.Contains(
                            "Scale",
                            StringComparison.OrdinalIgnoreCase)
                        || property.Name.Contains(
                            "Zoom",
                            StringComparison.OrdinalIgnoreCase)
                        || property.Name.Contains(
                            "Frame",
                            StringComparison.OrdinalIgnoreCase)))
                .Select(property =>
                {
                    object? value;
                    try
                    {
                        value = property.GetValue(timelineVm);
                    }
                    catch (Exception ex)
                    {
                        value = "<error:"
                            + ex.GetBaseException().Message
                            + ">";
                    }

                    return property.Name
                        + "="
                        + (value ?? "<null>");
                })
                .Take(100)
                .ToArray();

            var result = string.Join(
                Environment.NewLine,
                new[]
                {
                    "PASS_S5_COORDINATE_DISCOVERY",
                    $"host_version={typeof(Timeline).Assembly.GetName().Version}",
                    $"source_type={host.Source.GetType().FullName}",
                    $"source_width={host.Source.ActualWidth:R}",
                    $"scroll_h_offset={host.Scroll.HorizontalOffset:R}",
                    $"scroll_h_extent={host.Scroll.ExtentWidth:R}",
                    $"scroll_h_viewport={host.Scroll.ViewportWidth:R}",
                    $"timeline_vm_type={timelineVm.GetType().FullName}",
                    $"timeline_vm_candidates={string.Join("|", vmCandidates)}",
                    $"item_left_type={leftProperty.PropertyType.FullName}",
                    $"item_left_get_public={leftProperty.GetMethod?.IsPublic}",
                    $"item_width_type={widthProperty.PropertyType.FullName}",
                    $"item_width_get_public={widthProperty.GetMethod?.IsPublic}",
                    $"timeline_zoom_holder_type={zoomHolder?.GetType().FullName ?? "<null>"}",
                    $"timeline_zoom_value_get_public={zoomValueProperty?.GetMethod?.IsPublic}",
                    $"timeline_zoom_value_set_public={zoomValueProperty?.SetMethod?.IsPublic}",
                    $"source_adorner_layer={adornerAvailable}",
                    DescribeItem("a", a),
                    DescribeItem("b", b),
                    zoom150 ?? "zoom150=UNAVAILABLE",
                    scrollSnapshot
                });

            WriteS5CoordinateResult(
                result + Environment.NewLine);
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic(
                "s5_coordinate_smoke_error=" + ex);
            WriteS5CoordinateResult(
                "FAIL_S5_COORDINATE_DISCOVERY\n"
                + ex
                + "\n");
        }
    }

    private static void WriteS5CoordinateResult(
        string text)
    {
        var dir = Environment.GetEnvironmentVariable(
            "CNWL_P4_HANDS_ON_DIAG_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(
                dir,
                "s5-coordinate-result.txt"),
            text);
    }

    private void ScheduleS4IntegrationSmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P4_S4_INTEGRATION_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunS4IntegrationSmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunS4IntegrationSmokeAsync()
    {
        try
        {
            await Task.Delay(500);

            if (timeline.Items.Any())
                throw new InvalidOperationException(
                    "S4 integration smoke must start from a resource-free Timeline.");

            for (var layer = 0; layer <= 10; layer++)
            {
                ExecuteHostCommand(
                    CommandType.AddLayer,
                    layer);
                await Task.Delay(90);
            }

            var key = timeline.ID.ToString("D");
            var outerId = Guid.Parse(
                "66666666-6666-6666-6666-666666666666");
            var innerId = Guid.Parse(
                "77777777-7777-7777-7777-777777777777");
            var laterId = Guid.Parse(
                "88888888-8888-8888-8888-888888888888");

            state.ReplaceProductState(
                FolderProductStateRules.ReplaceCore(
                    FolderProductState.Empty,
                    FolderDocumentRules.NormalizeAndValidate(
                        new FolderDocument
                        {
                            Timelines =
                            [
                                new TimelineFolderState
                                {
                                    TimelineKey = key,
                                    Folders =
                                    [
                                        new PersistedFolder
                                        {
                                            Id = outerId,
                                            Start = 1,
                                            End = 5,
                                            Name = "Outer"
                                        },
                                        new PersistedFolder
                                        {
                                            Id = innerId,
                                            Start = 2,
                                            End = 3,
                                            Name = "Inner"
                                        },
                                        new PersistedFolder
                                        {
                                            Id = laterId,
                                            Start = 7,
                                            End = 9,
                                            Name = "Later"
                                        }
                                    ]
                                }
                            ]
                        })));

            var movingMarker = new YmmGroupItem
            {
                Frame = 0,
                Length = Math.Max(1, timeline.Length),
                Layer = 2,
                GroupRange = 1
            };
            var stationaryMarker = new YmmGroupItem
            {
                Frame = 0,
                Length = Math.Max(1, timeline.Length),
                Layer = 4,
                GroupRange = 1
            };
            var laterMarker = new YmmGroupItem
            {
                Frame = 0,
                Length = Math.Max(1, timeline.Length),
                Layer = 7,
                GroupRange = 1
            };

            foreach (var marker in new[]
            {
                movingMarker,
                stationaryMarker,
                laterMarker
            })
            {
                if (!timeline.TryAddItems(
                        [marker],
                        marker.Frame,
                        marker.Layer,
                        isItemSelectionEnabled: false))
                {
                    throw new InvalidOperationException(
                        $"S4 marker GroupItem could not be added at L{marker.Layer}.");
                }
            }

            if (movingMarker.Layer != 2
                || stationaryMarker.Layer != 4
                || laterMarker.Layer != 7)
            {
                throw new InvalidOperationException(
                    "S4 marker fixture changed layer placement: "
                    + $"moving={movingMarker.Layer}, "
                    + $"stationary={stationaryMarker.Layer}, "
                    + $"later={laterMarker.Layer}.");
            }

            var color2 = Color.FromRgb(0x31, 0x42, 0x53);
            var color3 = Color.FromRgb(0x64, 0x75, 0x86);
            var color4 = Color.FromRgb(0x97, 0xA8, 0xB9);
            timeline.LayerSettings.Colors[2] = color2;
            timeline.LayerSettings.Colors[3] = color3;
            timeline.LayerSettings.Colors[4] = color4;
            undo.Record();
            await Task.Delay(200);

            commands.SetHidden(innerId, true);
            await Task.Delay(250);

            var beforeRestore = FolderProductStateRules.RestoreMap(
                state.ProductState,
                key);
            if (!beforeRestore.ContainsKey(2)
                || !beforeRestore.ContainsKey(3))
            {
                throw new InvalidOperationException(
                    "S4 hidden fixture did not establish restore ownership.");
            }

            var itemCounts = timeline.Items
                .GroupBy(item => item.Layer)
                .ToDictionary(
                    group => group.Key,
                    group => group.Count());
            var spans = timeline.Items
                .OfType<YmmGroupItem>()
                .OrderBy(item => item.Layer)
                .ThenBy(item => item.Frame)
                .Select(item => new GroupSpan(
                    item.Layer,
                    item.GroupRange))
                .ToArray();
            var rows = PanelProjection.Build(
                state.ProductState,
                key,
                Math.Max(
                    timeline.MaxLayer,
                    timeline.LayerSettings.MaxLayer),
                itemCounts,
                spans);

            var innerRow = rows.Single(
                row => row.FolderId == innerId);
            var laterRow = rows.Single(
                row => row.FolderId == laterId);

            var block = PanelMoveRules.TryGetDragBlock(
                [innerRow])
                ?? throw new InvalidOperationException(
                    "S4 inner folder did not resolve to a drag block.");
            var drop = PanelMoveRules.ResolveDrop(
                laterRow,
                PanelDropZone.Before);

            if (!commands.CanMovePanelRows(
                    block,
                    drop))
            {
                throw new InvalidOperationException(
                    "S4 representative panel move was rejected.");
            }

            var beforeSettingsMax =
                timeline.LayerSettings.MaxLayer;

            commands.MovePanelRows(
                block,
                drop);
            await Task.Delay(400);

            AssertS4MoveState(
                outerId,
                innerId,
                laterId,
                expectedOuter: (1, 3),
                expectedInner: (5, 6),
                expectedLater: (7, 9),
                movingMarker,
                expectedMovingLayer: 5,
                stationaryMarker,
                expectedStationaryLayer: 2,
                laterMarker,
                expectedLaterLayer: 7,
                colorAtMovedStart: color2,
                colorAtMovedEnd: color3,
                expectedRestoreLayers: [5, 6]);

            if (timeline.LayerSettings.MaxLayer
                != beforeSettingsMax)
            {
                throw new InvalidOperationException(
                    "Panel block move changed the explicit layer count.");
            }

            var movedParents = PanelProjection.ParentMap(
                FolderDocumentRules.FindTimeline(
                    state.Document,
                    key)!.Folders);
            if (movedParents[innerId] is not null)
            {
                throw new InvalidOperationException(
                    "Moved inner folder still belongs to Outer.");
            }

            ExecuteHostCommand(
                CommandType.Undo,
                null);
            await Task.Delay(450);

            AssertS4MoveState(
                outerId,
                innerId,
                laterId,
                expectedOuter: (1, 5),
                expectedInner: (2, 3),
                expectedLater: (7, 9),
                movingMarker,
                expectedMovingLayer: 2,
                stationaryMarker,
                expectedStationaryLayer: 4,
                laterMarker,
                expectedLaterLayer: 7,
                colorAtMovedStart: color2,
                colorAtMovedEnd: color3,
                expectedRestoreLayers: [2, 3]);

            var undoParents = PanelProjection.ParentMap(
                FolderDocumentRules.FindTimeline(
                    state.Document,
                    key)!.Folders);
            if (undoParents[innerId] != outerId)
            {
                throw new InvalidOperationException(
                    "Panel move Undo did not restore folder parentage.");
            }

            ExecuteHostCommand(
                CommandType.Redo,
                null);
            await Task.Delay(450);

            AssertS4MoveState(
                outerId,
                innerId,
                laterId,
                expectedOuter: (1, 3),
                expectedInner: (5, 6),
                expectedLater: (7, 9),
                movingMarker,
                expectedMovingLayer: 5,
                stationaryMarker,
                expectedStationaryLayer: 2,
                laterMarker,
                expectedLaterLayer: 7,
                colorAtMovedStart: color2,
                colorAtMovedEnd: color3,
                expectedRestoreLayers: [5, 6]);

            // The management panel itself must be constructible on the exact
            // host without optional XAML/resources or another state service.
            var panel = new FolderToolView();
            if (panel.Content is not Grid panelRoot
                || !panelRoot.Children
                    .OfType<ListBox>()
                    .Any())
            {
                throw new InvalidOperationException(
                    "S4 management panel did not build its ListBox surface.");
            }

            ScrollPanelToLayer(7);
            display.ThrowIfFailed();

            WriteS4Result(
                "PASS_S4_INTEGRATION\n" +
                $"timeline={key}\n" +
                "flat_panel_surface=true\n" +
                "block_move=true\n" +
                "folder_parentage=true\n" +
                "item_layer_map=true\n" +
                "layer_settings_map=true\n" +
                "visibility_restore_map=true\n" +
                "move_undo_redo=true\n" +
                "fold_aware_follow=true\n");
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic(
                "s4_integration_smoke_error=" + ex);
            WriteS4Result(
                "FAIL_S4_INTEGRATION\n"
                + ex
                + "\n");
        }
    }

    private void AssertS4MoveState(
        Guid outerId,
        Guid innerId,
        Guid laterId,
        (int Start, int End) expectedOuter,
        (int Start, int End) expectedInner,
        (int Start, int End) expectedLater,
        YmmGroupItem movingMarker,
        int expectedMovingLayer,
        YmmGroupItem stationaryMarker,
        int expectedStationaryLayer,
        YmmGroupItem laterMarker,
        int expectedLaterLayer,
        Color colorAtMovedStart,
        Color colorAtMovedEnd,
        IReadOnlyCollection<int> expectedRestoreLayers)
    {
        var key = timeline.ID.ToString("D");
        var folders = FolderDocumentRules.FindTimeline(
                state.Document,
                key)
            ?.Folders
            ?? throw new InvalidOperationException(
                "S4 folder state is missing.");

        void Folder(
            Guid id,
            (int Start, int End) expected)
        {
            var folder = folders.Single(
                candidate => candidate.Id == id);

            if (folder.Start != expected.Start
                || folder.End != expected.End)
            {
                throw new InvalidOperationException(
                    $"S4 folder {id} range " +
                    $"{folder.Start}-{folder.End}, expected " +
                    $"{expected.Start}-{expected.End}.");
            }
        }

        Folder(outerId, expectedOuter);
        Folder(innerId, expectedInner);
        Folder(laterId, expectedLater);

        if (movingMarker.Layer != expectedMovingLayer
            || stationaryMarker.Layer
                != expectedStationaryLayer
            || laterMarker.Layer != expectedLaterLayer)
        {
            throw new InvalidOperationException(
                "S4 marker item layer mapping is wrong.");
        }

        if (timeline.LayerSettings.Colors[
                expectedMovingLayer]
            != colorAtMovedStart
            || timeline.LayerSettings.Colors[
                expectedMovingLayer + 1]
            != colorAtMovedEnd)
        {
            throw new InvalidOperationException(
                "S4 LayerSettings color mapping is wrong.");
        }

        var restore = FolderProductStateRules.RestoreMap(
            state.ProductState,
            key);

        if (!restore.Keys
            .OrderBy(x => x)
            .SequenceEqual(
                expectedRestoreLayers.OrderBy(x => x)))
        {
            throw new InvalidOperationException(
                "S4 visibility restore keys are wrong: "
                + string.Join(",", restore.Keys.OrderBy(x => x)));
        }

        foreach (var layer in expectedRestoreLayers)
        {
            if (timeline.LayerSettings.IsVisibles[layer])
            {
                throw new InvalidOperationException(
                    $"S4 moved hidden layer L{layer} became visible.");
            }
        }

        display.ThrowIfFailed();
    }

    private static void WriteS4Result(
        string text)
    {
        var dir = Environment.GetEnvironmentVariable(
            "CNWL_P4_HANDS_ON_DIAG_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "s4-result.txt"),
            text);
    }

    private void ScheduleS3IntegrationSmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P4_S3_INTEGRATION_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunS3IntegrationSmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunS3IntegrationSmokeAsync()
    {
        try
        {
            await Task.Delay(500);

            if (timeline.Items.Any())
                throw new InvalidOperationException(
                    "S3 integration smoke must start from a resource-free Timeline.");

            // Establish enough real native layer structure that direct insert /
            // delete can prove row mapping, not only FolderDocument changes.
            for (var layer = 0; layer <= 8; layer++)
            {
                ExecuteHostCommand(CommandType.AddLayer, layer);
                await Task.Delay(120);
            }

            var key = timeline.ID.ToString("D");
            var folderA = Guid.Parse(
                "44444444-4444-4444-4444-444444444444");
            var folderB = Guid.Parse(
                "55555555-5555-5555-5555-555555555555");

            state.ReplaceProductState(
                FolderProductStateRules.ReplaceCore(
                    FolderProductState.Empty,
                    FolderDocumentRules.NormalizeAndValidate(
                        new FolderDocument
                        {
                            Timelines =
                            [
                                new TimelineFolderState
                                {
                                    TimelineKey = key,
                                    Folders =
                                    [
                                        new PersistedFolder
                                        {
                                            Id = folderA,
                                            Start = 1,
                                            End = 3,
                                            Name = "A"
                                        },
                                        new PersistedFolder
                                        {
                                            Id = folderB,
                                            Start = 5,
                                            End = 6,
                                            Name = "B"
                                        }
                                    ]
                                }
                            ]
                        })));

            var groupA = new YmmGroupItem
            {
                Frame = 0,
                Length = Math.Max(1, timeline.Length),
                Layer = 1,
                GroupRange = 2
            };
            if (!timeline.TryAddItems(
                    [groupA],
                    groupA.Frame,
                    groupA.Layer,
                    isItemSelectionEnabled: false))
            {
                throw new InvalidOperationException(
                    "S3 baseline YmmGroupItem could not be added.");
            }

            undo.Record();
            await Task.Delay(250);

            var baselineItemMaxLayer = timeline.MaxLayer;
            var baselineSettingsMaxLayer = timeline.LayerSettings.MaxLayer;

            // S2 + S3 composition: a row inserted into a hidden folder must
            // become hidden and get one restore entry before the same Record().
            commands.SetHidden(folderA, true);
            await Task.Delay(250);

            commands.AddLayerAtFolderEnd(folderA);
            await Task.Delay(350);

            AssertS3Folder(folderA, 1, 4, "tail_add_A");
            AssertS3Folder(folderB, 6, 7, "tail_shift_B");

            if (groupA.Layer != 1 || groupA.GroupRange != 3)
                throw new InvalidOperationException(
                    $"Tail insert GroupRange mismatch: L{groupA.Layer} R{groupA.GroupRange}.");

            var hiddenAfterTail = FolderProductStateRules.HiddenLayers(
                state.ProductState,
                key);
            var restoreAfterTail = FolderProductStateRules.RestoreMap(
                state.ProductState,
                key);

            if (!hiddenAfterTail.Contains(4)
                || timeline.LayerSettings.IsVisibles[4]
                || !restoreAfterTail.ContainsKey(4))
            {
                throw new InvalidOperationException(
                    "Inserted tail row did not join hidden/restore ownership.");
            }

            if (timeline.LayerSettings.MaxLayer
                    != baselineSettingsMaxLayer + 1)
            {
                throw new InvalidOperationException(
                    "Tail insert LayerSettings.MaxLayer mismatch: " +
                    $"{timeline.LayerSettings.MaxLayer} != {baselineSettingsMaxLayer + 1}.");
            }

            if (timeline.MaxLayer != baselineItemMaxLayer)
            {
                throw new InvalidOperationException(
                    "Tail insert unexpectedly changed item-derived Timeline.MaxLayer: " +
                    $"{timeline.MaxLayer} != {baselineItemMaxLayer}.");
            }

            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(350);
            AssertS3Folder(folderA, 1, 3, "tail_undo_A");
            AssertS3Folder(folderB, 5, 6, "tail_undo_B");
            if (groupA.GroupRange != 2
                || timeline.MaxLayer != baselineItemMaxLayer
                || timeline.LayerSettings.MaxLayer != baselineSettingsMaxLayer)
            {
                throw new InvalidOperationException(
                    "Tail insert Undo did not restore host/group structure: " +
                    $"group_range={groupA.GroupRange} expected=2; " +
                    $"item_max={timeline.MaxLayer} expected={baselineItemMaxLayer}; " +
                    $"settings_max={timeline.LayerSettings.MaxLayer} " +
                    $"expected={baselineSettingsMaxLayer}.");
            }

            var restoreAfterTailUndo = FolderProductStateRules.RestoreMap(
                state.ProductState,
                key);
            if (restoreAfterTailUndo.ContainsKey(4))
            {
                throw new InvalidOperationException(
                    "Tail insert Undo left visibility restore ownership for removed L4.");
            }

            ExecuteHostCommand(CommandType.Redo, null);
            await Task.Delay(350);
            AssertS3Folder(folderA, 1, 4, "tail_redo_A");
            AssertS3Folder(folderB, 6, 7, "tail_redo_B");
            if (groupA.GroupRange != 3
                || timeline.MaxLayer != baselineItemMaxLayer
                || timeline.LayerSettings.MaxLayer != baselineSettingsMaxLayer + 1)
            {
                throw new InvalidOperationException(
                    "Tail insert Redo did not reapply host/group structure: " +
                    $"group_range={groupA.GroupRange} expected=3; " +
                    $"item_max={timeline.MaxLayer} expected={baselineItemMaxLayer}; " +
                    $"settings_max={timeline.LayerSettings.MaxLayer} " +
                    $"expected={baselineSettingsMaxLayer + 1}.");
            }

            var restoreAfterTailRedo = FolderProductStateRules.RestoreMap(
                state.ProductState,
                key);
            if (!restoreAfterTailRedo.ContainsKey(4)
                || timeline.LayerSettings.IsVisibles[4])
            {
                throw new InvalidOperationException(
                    "Tail insert Redo did not restore hidden ownership for L4.");
            }

            commands.SetHidden(folderA, false);
            await Task.Delay(250);

            // Standard native Add must also correct a GroupRange in the same
            // YMM4-owned history unit.
            ExecuteHostCommand(CommandType.AddLayer, 2);
            await Task.Delay(350);
            AssertS3Folder(folderA, 1, 5, "standard_add_A");
            AssertS3Folder(folderB, 7, 8, "standard_add_B");
            if (groupA.GroupRange != 4)
                throw new InvalidOperationException(
                    $"Standard Add GroupRange mismatch: {groupA.GroupRange}.");

            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(350);
            if (groupA.GroupRange != 3)
                throw new InvalidOperationException(
                    "Standard Add Undo did not restore GroupRange.");

            ExecuteHostCommand(CommandType.Redo, null);
            await Task.Delay(350);
            if (groupA.GroupRange != 4)
                throw new InvalidOperationException(
                    "Standard Add Redo did not restore GroupRange correction.");

            // Fit only the mismatched GroupRange and keep it in one native unit.
            groupA.GroupRange = 6;
            undo.Record();
            await Task.Delay(150);

            var fitIssues = commands.GetGroupIssues(folderA);
            if (fitIssues.Count != 1)
                throw new InvalidOperationException(
                    $"Expected one GroupRange issue, got {fitIssues.Count}.");

            if (commands.FitGroupRanges(folderA) != 1
                || groupA.GroupRange != 4)
            {
                throw new InvalidOperationException(
                    "Fit GroupRange did not align to folder A.");
            }

            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(300);
            if (groupA.GroupRange != 6)
                throw new InvalidOperationException(
                    "Fit GroupRange Undo failed.");

            ExecuteHostCommand(CommandType.Redo, null);
            await Task.Delay(300);
            if (groupA.GroupRange != 4)
                throw new InvalidOperationException(
                    "Fit GroupRange Redo failed.");

            // Group Control add is a plugin-owned structural composite.
            if (!commands.AddGroupControl(folderB))
                throw new InvalidOperationException(
                    "Group Control add returned false.");
            await Task.Delay(350);

            AssertS3Folder(folderB, 7, 9, "group_add_B");
            var groupsAfterAdd = timeline.Items
                .OfType<YmmGroupItem>()
                .OrderBy(x => x.Layer)
                .ToArray();
            if (groupsAfterAdd.Length != 2)
                throw new InvalidOperationException(
                    $"Expected two GroupItems, got {groupsAfterAdd.Length}.");

            var groupB = groupsAfterAdd.Single(x => !ReferenceEquals(x, groupA));
            if (groupB.Layer != 7 || groupB.GroupRange != 2)
                throw new InvalidOperationException(
                    $"Group B span mismatch: L{groupB.Layer} R{groupB.GroupRange}.");

            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(350);
            AssertS3Folder(folderB, 7, 8, "group_add_undo_B");
            if (timeline.Items.OfType<YmmGroupItem>().Count() != 1)
                throw new InvalidOperationException(
                    "Group Control add Undo did not remove the new YmmGroupItem.");

            ExecuteHostCommand(CommandType.Redo, null);
            await Task.Delay(350);
            AssertS3Folder(folderB, 7, 9, "group_add_redo_B");
            if (timeline.Items.OfType<YmmGroupItem>().Count() != 2)
                throw new InvalidOperationException(
                    "Group Control add Redo did not restore the YmmGroupItem.");

            // Destructive folder delete must remove metadata + rows + YmmGroupItem
            // and come back with one Undo.
            var itemMaxBeforeDelete = timeline.MaxLayer;
            var settingsMaxBeforeDelete = timeline.LayerSettings.MaxLayer;
            commands.DeleteFolderContents(folderB);
            await Task.Delay(400);

            if (commands.FindFolder(folderB) is not null)
                throw new InvalidOperationException(
                    "Destructive folder delete left folder B metadata.");
            if (timeline.Items
                .OfType<YmmGroupItem>()
                .Any(x => x.Layer >= 7 && x.Layer <= 9))
            {
                throw new InvalidOperationException(
                    "Destructive folder delete left GroupItems in removed rows.");
            }
            if (timeline.LayerSettings.MaxLayer
                    != settingsMaxBeforeDelete - 3)
            {
                throw new InvalidOperationException(
                    "Destructive folder delete did not remove three logical rows: " +
                    $"{timeline.LayerSettings.MaxLayer} != {settingsMaxBeforeDelete - 3}.");
            }

            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(400);
            AssertS3Folder(folderB, 7, 9, "delete_undo_B");
            if (timeline.Items.OfType<YmmGroupItem>().Count() != 2
                || timeline.LayerSettings.MaxLayer != settingsMaxBeforeDelete
                || timeline.MaxLayer != itemMaxBeforeDelete)
            {
                throw new InvalidOperationException(
                    "Destructive delete Undo did not restore host state: " +
                    $"groups={timeline.Items.OfType<YmmGroupItem>().Count()}; " +
                    $"settings_max={timeline.LayerSettings.MaxLayer}/{settingsMaxBeforeDelete}; " +
                    $"item_max={timeline.MaxLayer}/{itemMaxBeforeDelete}.");
            }

            ExecuteHostCommand(CommandType.Redo, null);
            await Task.Delay(400);
            if (commands.FindFolder(folderB) is not null
                || timeline.Items.OfType<YmmGroupItem>().Count() != 1
                || timeline.LayerSettings.MaxLayer != settingsMaxBeforeDelete - 3)
            {
                throw new InvalidOperationException(
                    "Destructive delete Redo did not reapply host state: " +
                    $"groups={timeline.Items.OfType<YmmGroupItem>().Count()}; " +
                    $"settings_max={timeline.LayerSettings.MaxLayer}/" +
                    $"{settingsMaxBeforeDelete - 3}.");
            }

            WriteS3Result(
                "PASS_S3_INTEGRATION\n" +
                $"timeline={key}\n" +
                "insert_into_hidden_folder=true\n" +
                "plugin_structural_undo_redo=true\n" +
                "standard_group_autofix=true\n" +
                "group_fit_undo_redo=true\n" +
                "group_add_undo_redo=true\n" +
                "destructive_delete_undo_redo=true\n");
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic("s3_integration_smoke_error=" + ex);
            WriteS3Result("FAIL_S3_INTEGRATION\n" + ex + "\n");
        }
    }

    private void AssertS3Folder(
        Guid folderId,
        int expectedStart,
        int expectedEnd,
        string phase)
    {
        var folder = commands.FindFolder(folderId)
            ?? throw new InvalidOperationException(
                $"{phase}: folder {folderId} is missing.");

        if (folder.Start != expectedStart
            || folder.End != expectedEnd)
        {
            throw new InvalidOperationException(
                $"{phase}: folder range {folder.Start}-{folder.End}, " +
                $"expected {expectedStart}-{expectedEnd}.");
        }

        display.ThrowIfFailed();
        HandsOnRuntime.Diagnostic(
            $"s3_folder phase={phase} id={folderId} " +
            $"range={folder.Start}-{folder.End}");
    }

    private static void WriteS3Result(string text)
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_P4_HANDS_ON_DIAG_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "s3-result.txt"), text);
    }

    private void ScheduleS2IntegrationSmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P4_S2_INTEGRATION_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunS2IntegrationSmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunS2IntegrationSmokeAsync()
    {
        try
        {
            await Task.Delay(500);

            if (timeline.Items.Any())
                throw new InvalidOperationException(
                    "S2 integration smoke must start from a resource-free Timeline.");

            // Materialize enough native layer settings for visibility/color
            // verification before installing folder metadata.
            for (var layer = 0; layer <= 4; layer++)
            {
                ExecuteHostCommand(CommandType.AddLayer, layer);
                await Task.Delay(180);
            }

            var key = timeline.ID.ToString("D");
            var outerId = Guid.Parse(
                "22222222-2222-2222-2222-222222222222");
            var innerId = Guid.Parse(
                "33333333-3333-3333-3333-333333333333");

            state.ReplaceProductState(
                FolderProductStateRules.ReplaceCore(
                    FolderProductState.Empty,
                    FolderDocumentRules.NormalizeAndValidate(
                        new FolderDocument
                        {
                            Timelines =
                            [
                                new TimelineFolderState
                                {
                                    TimelineKey = key,
                                    Folders =
                                    [
                                        new PersistedFolder
                                        {
                                            Id = outerId,
                                            Start = 1,
                                            End = 4,
                                            Name = "Outer",
                                            IsCollapsed = false
                                        },
                                        new PersistedFolder
                                        {
                                            Id = innerId,
                                            Start = 2,
                                            End = 3,
                                            Name = "Inner",
                                            IsCollapsed = false
                                        }
                                    ]
                                }
                            ]
                        })));

            // Mixed original visibility proves exact restoration rather than
            // blindly showing every row when the final Hidden reason disappears.
            timeline.LayerSettings.IsVisibles[1] = true;
            timeline.LayerSettings.IsVisibles[2] = false;
            timeline.LayerSettings.IsVisibles[3] = true;
            timeline.LayerSettings.IsVisibles[4] = false;
            undo.Record();
            await Task.Delay(250);

            commands.SetHidden(innerId, true);
            await Task.Delay(250);
            AssertS2Visibility(
                "inner_hidden",
                expectedHidden: new[] { 2, 3 },
                expectedVisible: new Dictionary<int, bool>
                {
                    [1] = true,
                    [2] = false,
                    [3] = false,
                    [4] = false
                },
                expectedRestore: new Dictionary<int, bool>
                {
                    [2] = false,
                    [3] = true
                });

            // Simulate the native eye action while the folder still has a
            // Hidden reason. The plugin must not immediately steal visibility
            // back, and its restore value must follow native Undo/Redo.
            timeline.LayerSettings.IsVisibles[2] = true;
            undo.Record();
            await Task.Delay(350);

            AssertS2Visibility(
                "external_eye_show",
                expectedHidden: new[] { 2, 3 },
                expectedVisible: new Dictionary<int, bool>
                {
                    [1] = true,
                    [2] = true,
                    [3] = false,
                    [4] = false
                },
                expectedRestore: new Dictionary<int, bool>
                {
                    [2] = true,
                    [3] = true
                });

            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(350);
            AssertS2Visibility(
                "external_eye_undo",
                expectedHidden: new[] { 2, 3 },
                expectedVisible: new Dictionary<int, bool>
                {
                    [1] = true,
                    [2] = false,
                    [3] = false,
                    [4] = false
                },
                expectedRestore: new Dictionary<int, bool>
                {
                    [2] = false,
                    [3] = true
                });

            ExecuteHostCommand(CommandType.Redo, null);
            await Task.Delay(350);
            AssertS2Visibility(
                "external_eye_redo",
                expectedHidden: new[] { 2, 3 },
                expectedVisible: new Dictionary<int, bool>
                {
                    [1] = true,
                    [2] = true,
                    [3] = false,
                    [4] = false
                },
                expectedRestore: new Dictionary<int, bool>
                {
                    [2] = true,
                    [3] = true
                });

            // Return to the original pre-override state before the nested
            // parent/child restoration matrix below.
            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(350);

            commands.SetHidden(outerId, true);
            await Task.Delay(250);
            AssertS2Visibility(
                "outer_and_inner_hidden",
                expectedHidden: new[] { 1, 2, 3, 4 },
                expectedVisible: new Dictionary<int, bool>
                {
                    [1] = false,
                    [2] = false,
                    [3] = false,
                    [4] = false
                },
                expectedRestore: new Dictionary<int, bool>
                {
                    [1] = true,
                    [2] = false,
                    [3] = true,
                    [4] = false
                });

            commands.SetHidden(outerId, false);
            await Task.Delay(250);
            AssertS2Visibility(
                "outer_shown_inner_still_hidden",
                expectedHidden: new[] { 2, 3 },
                expectedVisible: new Dictionary<int, bool>
                {
                    [1] = true,
                    [2] = false,
                    [3] = false,
                    [4] = false
                },
                expectedRestore: new Dictionary<int, bool>
                {
                    [2] = false,
                    [3] = true
                });

            commands.SetHidden(innerId, false);
            await Task.Delay(250);
            AssertS2Visibility(
                "all_shown_restored",
                expectedHidden: Array.Empty<int>(),
                expectedVisible: new Dictionary<int, bool>
                {
                    [1] = true,
                    [2] = false,
                    [3] = true,
                    [4] = false
                },
                expectedRestore: new Dictionary<int, bool>());

            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(350);
            AssertS2Visibility(
                "hidden_undo",
                expectedHidden: new[] { 2, 3 },
                expectedVisible: new Dictionary<int, bool>
                {
                    [1] = true,
                    [2] = false,
                    [3] = false,
                    [4] = false
                },
                expectedRestore: new Dictionary<int, bool>
                {
                    [2] = false,
                    [3] = true
                });

            ExecuteHostCommand(CommandType.Redo, null);
            await Task.Delay(350);
            AssertS2Visibility(
                "hidden_redo",
                expectedHidden: Array.Empty<int>(),
                expectedVisible: new Dictionary<int, bool>
                {
                    [1] = true,
                    [2] = false,
                    [3] = true,
                    [4] = false
                },
                expectedRestore: new Dictionary<int, bool>());

            const string blue = "#FF4F7FE0";
            commands.SetColor(outerId, blue);
            await Task.Delay(200);

            if (commands.FindOption(outerId)?.Color != blue)
                throw new InvalidOperationException("Folder color was not stored.");

            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(250);
            if (commands.FindOption(outerId)?.Color is not null)
                throw new InvalidOperationException(
                    "Folder color Undo did not restore the default option.");

            ExecuteHostCommand(CommandType.Redo, null);
            await Task.Delay(250);
            if (commands.FindOption(outerId)?.Color != blue)
                throw new InvalidOperationException(
                    "Folder color Redo did not restore the color.");

            var originalLayerColors = Enumerable.Range(1, 4)
                .ToDictionary(
                    layer => layer,
                    layer => timeline.LayerSettings.Colors[layer]);

            commands.ApplyFolderColorToLayers(outerId);
            await Task.Delay(250);

            var expectedBlue = (Color)ColorConverter.ConvertFromString(blue);
            for (var layer = 1; layer <= 4; layer++)
            {
                if (timeline.LayerSettings.Colors[layer] != expectedBlue)
                {
                    throw new InvalidOperationException(
                        $"Layer color was not applied at L{layer}.");
                }
            }

            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(300);
            foreach (var (layer, original) in originalLayerColors)
            {
                if (timeline.LayerSettings.Colors[layer] != original)
                {
                    throw new InvalidOperationException(
                        $"Layer color Undo mismatch at L{layer}.");
                }
            }

            ExecuteHostCommand(CommandType.Redo, null);
            await Task.Delay(300);
            for (var layer = 1; layer <= 4; layer++)
            {
                if (timeline.LayerSettings.Colors[layer] != expectedBlue)
                {
                    throw new InvalidOperationException(
                        $"Layer color Redo mismatch at L{layer}.");
                }
            }

            var raw = state.SaveRaw()
                ?? throw new InvalidOperationException(
                    "S2 state unexpectedly serialized as empty.");
            if (!raw.Contains("\"schemaVersion\":2", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "S2 runtime did not save outer schema v2.");

            var root = window.DataContext
                ?? throw new InvalidOperationException(
                    "Main window DataContext is missing.");
            HandsOnHostAccess.WriteToolAreaSavedState(root, raw);
            var toolAreaRaw = HandsOnHostAccess.ReadToolAreaSavedState(root);
            if (!string.Equals(raw, toolAreaRaw, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "S2 v2 ToolArea SavedState roundtrip changed the payload.");
            }

            var reloaded = new FolderPersistenceSession();
            var load = reloaded.Load(toolAreaRaw);
            if (!load.Success)
                throw new InvalidOperationException(
                    "S2 v2 runtime reload failed: " + load.Error);

            var reloadedOption = FolderProductStateRules.FindOption(
                reloaded.ProductState,
                key,
                outerId);
            if (reloadedOption?.Color != blue
                || reloadedOption.Hidden)
            {
                throw new InvalidOperationException(
                    "S2 v2 reload did not preserve folder options.");
            }

            if (reloaded.ProductState.VisibilityRestore.Count != 0)
                throw new InvalidOperationException(
                    "Visibility restore map should be empty after all folders are shown.");

            WriteS2Result(
                "PASS_S2_INTEGRATION\n" +
                $"timeline={key}\n" +
                "nested_visibility=true\n" +
                "external_eye_override=true\n" +
                "external_eye_undo_redo=true\n" +
                "visibility_undo_redo=true\n" +
                "original_visibility_restore=true\n" +
                "folder_color=true\n" +
                "folder_color_undo_redo=true\n" +
                "layer_color_apply=true\n" +
                "layer_color_undo_redo=true\n" +
                "toolstate_v2_roundtrip=true\n" +
                "schema_v2_reload=true\n");
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic("s2_integration_smoke_error=" + ex);
            WriteS2Result("FAIL_S2_INTEGRATION\n" + ex + "\n");
        }
    }

    private void AssertS2Visibility(
        string phase,
        IReadOnlyCollection<int> expectedHidden,
        IReadOnlyDictionary<int, bool> expectedVisible,
        IReadOnlyDictionary<int, bool> expectedRestore)
    {
        var key = timeline.ID.ToString("D");
        var hidden = FolderProductStateRules.HiddenLayers(
            state.ProductState,
            key);

        if (!hidden.SetEquals(expectedHidden))
        {
            throw new InvalidOperationException(
                $"{phase}: hidden union mismatch: " +
                string.Join(",", hidden.OrderBy(x => x)));
        }

        foreach (var (layer, expected) in expectedVisible)
        {
            var actual = timeline.LayerSettings.IsVisibles[layer];
            if (actual != expected)
            {
                throw new InvalidOperationException(
                    $"{phase}: visibility L{layer}={actual}, expected {expected}.");
            }
        }

        var restore = FolderProductStateRules.RestoreMap(
            state.ProductState,
            key);

        if (!restore.OrderBy(x => x.Key)
            .SequenceEqual(expectedRestore.OrderBy(x => x.Key)))
        {
            throw new InvalidOperationException(
                $"{phase}: restore map mismatch: " +
                string.Join(
                    ",",
                    restore.OrderBy(x => x.Key)
                        .Select(x => $"L{x.Key}={x.Value}")));
        }

        display.ThrowIfFailed();
        HandsOnRuntime.Diagnostic(
            $"s2_state phase={phase} hidden={string.Join(",", hidden.OrderBy(x => x))} " +
            $"restore={string.Join(",", restore.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}"))}");
    }

    private static void WriteS2Result(string text)
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_P4_HANDS_ON_DIAG_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "s2-result.txt"), text);
    }

    private void ScheduleS1IntegrationSmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P4_S1_INTEGRATION_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunS1IntegrationSmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunS1IntegrationSmokeAsync()
    {
        try
        {
            await Task.Delay(500);

            if (timeline.Items.Any())
            {
                throw new InvalidOperationException(
                    "S1 create smoke must start from a resource-free Timeline.");
            }

            var baselineSettingsCount = timeline.LayerSettings.Items.Count;
            undo.Record();

            timeline.LayerSelection.Clear();
            var folderId = Guid.Parse(
                "11111111-2222-3333-4444-555555555555");

            var decision = commands.CreateFromContext(
                clickedLayer: 1,
                folderId,
                "S1 Smoke");

            await Task.Delay(500);

            if (!decision.NeedsAdditionalLayer
                || decision.Start != 1
                || decision.End != 1)
            {
                throw new InvalidOperationException(
                    "S1 one-row decision did not request the insertion convenience.");
            }

            AssertS1CreateState(
                "after_create",
                folderId,
                expectedFolder: (1, 2, false),
                expectedSettingsCount: baselineSettingsCount + 1);

            if (structural.PendingEdits != 1)
            {
                throw new InvalidOperationException(
                    $"Expected one composite history command, got {structural.PendingEdits}.");
            }

            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(500);

            AssertS1CreateState(
                "after_undo",
                folderId,
                expectedFolder: null,
                expectedSettingsCount: baselineSettingsCount);

            if (structural.UndoCallbacks != 1)
            {
                throw new InvalidOperationException(
                    $"Expected one composite Undo callback, got {structural.UndoCallbacks}.");
            }

            ExecuteHostCommand(CommandType.Redo, null);
            await Task.Delay(500);

            AssertS1CreateState(
                "after_redo",
                folderId,
                expectedFolder: (1, 2, false),
                expectedSettingsCount: baselineSettingsCount + 1);

            if (structural.RedoCallbacks != 1)
            {
                throw new InvalidOperationException(
                    $"Expected one composite Redo callback, got {structural.RedoCallbacks}.");
            }

            commands.SetAllCollapsed(true);
            await Task.Delay(250);
            AssertS1CreateState(
                "after_collapse_all",
                folderId,
                expectedFolder: (1, 2, true),
                expectedSettingsCount: baselineSettingsCount + 1);

            commands.SetAllCollapsed(false);
            await Task.Delay(250);
            AssertS1CreateState(
                "after_expand_all",
                folderId,
                expectedFolder: (1, 2, false),
                expectedSettingsCount: baselineSettingsCount + 1);

            WriteS1Result(
                "PASS_S1_INTEGRATION\n" +
                $"timeline={timeline.ID:D}\n" +
                $"baseline_settings={baselineSettingsCount}\n" +
                $"after_create_settings={timeline.LayerSettings.Items.Count}\n" +
                $"pending={structural.PendingEdits}\n" +
                $"undo_callbacks={structural.UndoCallbacks}\n" +
                $"redo_callbacks={structural.RedoCallbacks}\n" +
                "single_row_create=true\n" +
                "bulk_collapse_expand=true\n");
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic("s1_integration_smoke_error=" + ex);
            WriteS1Result("FAIL_S1_INTEGRATION\n" + ex + "\n");
        }
    }

    private void AssertS1CreateState(
        string phase,
        Guid folderId,
        (int Start, int End, bool Collapsed)? expectedFolder,
        int expectedSettingsCount)
    {
        var timelineState = FolderDocumentRules.FindTimeline(
            state.Document,
            timeline.ID.ToString("D"));
        var folder = timelineState?.Folders
            .FirstOrDefault(x => x.Id == folderId);

        if (expectedFolder is null)
        {
            if (folder is not null)
            {
                throw new InvalidOperationException(
                    $"{phase}: folder should not exist but was " +
                    $"{folder.Start}-{folder.End}.");
            }
        }
        else
        {
            var expected = expectedFolder.Value;
            if (folder is null
                || folder.Start != expected.Start
                || folder.End != expected.End
                || folder.IsCollapsed != expected.Collapsed)
            {
                throw new InvalidOperationException(
                    $"{phase}: folder mismatch: " +
                    (folder is null
                        ? "<null>"
                        : $"{folder.Start}-{folder.End}:collapsed={folder.IsCollapsed}"));
            }
        }

        var settingsCount = timeline.LayerSettings.Items.Count;
        if (settingsCount != expectedSettingsCount)
        {
            throw new InvalidOperationException(
                $"{phase}: LayerSettings count mismatch: " +
                $"{settingsCount} != {expectedSettingsCount}");
        }

        display.ThrowIfFailed();
        HandsOnRuntime.Diagnostic(
            $"s1_state phase={phase} settings={settingsCount} " +
            $"folder={(folder is null ? "<null>" : $"{folder.Start}-{folder.End}:{folder.IsCollapsed}")}");
    }

    private static void WriteS1Result(string text)
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_P4_HANDS_ON_DIAG_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "s1-result.txt"), text);
    }

    private void ScheduleS0IntegrationSmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P4_S0_INTEGRATION_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunS0IntegrationSmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunS0IntegrationSmokeAsync()
    {
        try
        {
            await Task.Delay(500);

            if (timeline.Items.Any())
            {
                throw new InvalidOperationException(
                    "S0 structural smoke must start from a resource-free Timeline.");
            }

            var baselineSettingsCount = timeline.LayerSettings.Items.Count;

            // Match the frozen P1 native gate: establish a clean history boundary
            // before measuring the host-owned structural command.
            undo.Record();

            var key = timeline.ID.ToString("D");
            var folderId = Guid.Parse("10101010-2020-3030-4040-505050505050");
            state.ReplaceDocument(FolderDocumentRules.ReplaceTimeline(
                state.Document,
                key,
                [
                    new PersistedFolder
                    {
                        Id = folderId,
                        Start = 1,
                        End = 2,
                        Name = "S0 Smoke",
                        IsCollapsed = true
                    }
                ]));

            await Task.Delay(350);
            ExecuteHostCommand(CommandType.AddLayer, 2);
            await Task.Delay(500);

            AssertS0State(
                "after_add",
                expectedStart: 1,
                expectedEnd: 3,
                expectedSettingsCount: baselineSettingsCount + 1);

            if (structural.PendingEdits != 1)
                throw new InvalidOperationException(
                    $"Expected one pending structural edit, got {structural.PendingEdits}.");

            ExecuteHostCommand(CommandType.Undo, null);
            await Task.Delay(500);

            AssertS0State(
                "after_undo",
                expectedStart: 1,
                expectedEnd: 2,
                expectedSettingsCount: baselineSettingsCount);

            if (structural.UndoCallbacks != 1)
                throw new InvalidOperationException(
                    $"Expected one folder Undo callback, got {structural.UndoCallbacks}.");

            ExecuteHostCommand(CommandType.Redo, null);
            await Task.Delay(500);

            AssertS0State(
                "after_redo",
                expectedStart: 1,
                expectedEnd: 3,
                expectedSettingsCount: baselineSettingsCount + 1);

            if (structural.RedoCallbacks != 1)
                throw new InvalidOperationException(
                    $"Expected one folder Redo callback, got {structural.RedoCallbacks}.");

            WriteS0Result(
                "PASS_S0_INTEGRATION\n" +
                $"timeline={key}\n" +
                $"baseline_settings={baselineSettingsCount}\n" +
                $"after_redo_settings={timeline.LayerSettings.Items.Count}\n" +
                $"pending={structural.PendingEdits}\n" +
                $"undo_callbacks={structural.UndoCallbacks}\n" +
                $"redo_callbacks={structural.RedoCallbacks}\n" +
                "folded_input_attached=true\n" +
                "filedrop_attached=true\n");
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic("s0_integration_smoke_error=" + ex);
            WriteS0Result("FAIL_S0_INTEGRATION\n" + ex + "\n");
        }
    }

    private void AssertS0State(
        string phase,
        int expectedStart,
        int expectedEnd,
        int expectedSettingsCount)
    {
        var key = timeline.ID.ToString("D");
        var folder = FolderDocumentRules.FindTimeline(state.Document, key)
            ?.Folders.SingleOrDefault();

        if (folder is null
            || folder.Start != expectedStart
            || folder.End != expectedEnd)
        {
            throw new InvalidOperationException(
                $"{phase}: folder range mismatch: " +
                (folder is null ? "<null>" : $"{folder.Start}-{folder.End}"));
        }

        var settingsCount = timeline.LayerSettings.Items.Count;
        if (settingsCount != expectedSettingsCount)
        {
            throw new InvalidOperationException(
                $"{phase}: LayerSettings count mismatch: " +
                $"{settingsCount} != {expectedSettingsCount}");
        }

        display.ThrowIfFailed();
        HandsOnRuntime.Diagnostic(
            $"s0_state phase={phase} folder={folder.Start}-{folder.End} " +
            $"layer_settings={settingsCount}");
    }

    private void ExecuteHostCommand(CommandType type, object? parameter)
    {
        if (!HandsOnHostAccess.TryExecuteTimelineCommand(
                host,
                window,
                type,
                parameter))
        {
            throw new InvalidOperationException(
                $"No executable route for {type} with parameter {parameter ?? "<null>"}.");
        }
    }

    private static void WriteS0Result(string text)
    {
        var dir = Environment.GetEnvironmentVariable("CNWL_P4_HANDS_ON_DIAG_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "s0-result.txt"), text);
    }

    internal bool Matches(Guid timelineId) =>
        !disposed && timeline.ID == timelineId;

    internal Timeline PanelTimeline => timeline;
    internal FolderCommands PanelCommands => commands;
    internal bool VisualSummaryAttached =>
        visualSummary is not null;
    internal int VisualTimingBandCount =>
        visualSummary?.TimingBandCount ?? 0;
    internal int VisualGroupSegmentCount =>
        visualSummary?.GroupSegmentCount ?? 0;

    internal void ScrollPanelToLayer(int layer)
    {
        if (disposed || layer < 0)
            return;

        display.ThrowIfFailed();

        var logical = Math.Min(layer, display.Layout.MaxLayer);
        if (display.Layout.IsHidden(logical))
            return;

        var viewportHeight = host.Scroll.ViewportHeight;
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0)
            return;

        var top = display.Layout.VisualRowOfLogical(logical)
            * (double)display.Height;
        var bottom = top + display.Height;
        var offset = host.Scroll.VerticalOffset;

        if (top < offset)
            host.Scroll.ScrollToVerticalOffset(top);
        else if (bottom > offset + viewportHeight)
            host.Scroll.ScrollToVerticalOffset(
                bottom - viewportHeight);

        HandsOnRuntime.Diagnostic(
            $"panel_follow layer={layer} logical={logical} " +
            $"row={display.Layout.VisualRowOfLogical(logical)}");
    }

    internal void RefreshFromDocument()
    {
        if (disposed)
            return;

        var timelineState = FolderDocumentRules.FindTimeline(
            state.Document,
            timeline.ID.ToString("D"));

        var spans = timelineState?.Folders
            .Where(x => x.IsCollapsed)
            .Select(x => new CollapsedSpan(x.Start, x.End))
            .ToArray() ?? [];

        display.SetSpans(spans);
    }

    private void OnDisplayApplied(object? sender, EventArgs e)
    {
        if (disposed || overlayRefreshQueued)
            return;

        overlayRefreshQueued = true;
        labels.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                overlayRefreshQueued = false;
                if (!disposed)
                    RebuildOverlay();
            }),
            DispatcherPriority.Render);
    }

    private void RebuildOverlay()
    {
        if (disposed)
            return;

        if (adorner is not null)
        {
            adornerLayer.Remove(adorner);
            adorner = null;
        }

        var folders = FolderDocumentRules.FindTimeline(
                state.Document,
                timeline.ID.ToString("D"))
            ?.Folders
            .ToArray() ?? [];

        if (folders.Length == 0)
            return;

        adorner = new FolderOverlayAdorner(
            labels,
            display,
            folders,
            state.ProductState,
            timeline.ID.ToString("D"));
        adornerLayer.Add(adorner);
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (disposed)
            return;

        var point = e.GetPosition(labels);
        if (point.X < 0 || point.X >= labels.ActualWidth
            || point.Y < 0 || point.Y >= labels.ActualHeight)
            return;

        if (e.ChangedButton == MouseButton.Left)
        {
            if (adorner?.TryToggleAt(point, out var toggleId) == true)
            {
                e.Handled = true;
                RunFolderCommand(() => commands.ToggleCollapsed(toggleId));
                return;
            }

            if (e.ClickCount >= 2
                && adorner?.TryNameAt(point, out var renameId) == true)
            {
                e.Handled = true;
                PromptRename(renameId);
                return;
            }

            return;
        }

        if (e.ChangedButton != MouseButton.Right)
            return;

        try
        {
            var logical = display.Layout.DisplayYToLogical(point.Y, display.Height);
            var owner = HandsOnHostAccess.FindLayerContextOwner(labels, logical);
            var menu = owner.ContextMenu;
            if (menu is null)
                return;

            PrepareMenu(menu, logical);
            e.Handled = true;
            menu.PlacementTarget = owner;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;

            HandsOnRuntime.Diagnostic(
                $"folder_menu display={point} logical={logical} timeline={timeline.ID:D}");
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic("folder_menu_error=" + ex);
        }
    }

    private void PrepareMenu(ContextMenu menu, int logicalLayer)
    {
        RemoveTagged(menu);

        var root = new MenuItem
        {
            Header = "レイヤーフォルダ",
            Tag = MenuTag
        };

        if (state.IsRecoveryBlocked)
        {
            root.Items.Add(new MenuItem
            {
                Header = "保存済みフォルダ情報を読み込めません",
                IsEnabled = false
            });
            root.Items.Add(new MenuItem
            {
                Header = "元データは保持中です。新規編集は行いません。",
                IsEnabled = false
            });
        }
        else
        {
            AddCreateAction(root, logicalLayer);
            AddExistingFolderActions(root, logicalLayer);

            var timelineState = FolderDocumentRules.FindTimeline(
                state.Document,
                timeline.ID.ToString("D"));
            if (timelineState is { Folders.Count: > 0 })
            {
                root.Items.Add(new Separator());

                var expandAll = new MenuItem { Header = "すべてのフォルダを開く" };
                expandAll.Click += (_, _) =>
                    RunFolderCommand(() => commands.SetAllCollapsed(false));
                root.Items.Add(expandAll);

                var collapseAll = new MenuItem { Header = "すべてのフォルダを畳む" };
                collapseAll.Click += (_, _) =>
                    RunFolderCommand(() => commands.SetAllCollapsed(true));
                root.Items.Add(collapseAll);
            }
        }

        menu.Items.Add(new Separator { Tag = MenuTag });
        menu.Items.Add(root);

        RoutedEventHandler? closed = null;
        closed = (_, _) =>
        {
            menu.Closed -= closed;
            RemoveTagged(menu);
        };
        menu.Closed += closed;
    }

    private void AddCreateAction(MenuItem root, int logicalLayer)
    {
        var candidateId = Guid.NewGuid();
        var decision = commands.EvaluateCreate(logicalLayer, candidateId);

        string Header()
        {
            if (!decision.Allowed
                || decision.Start is null
                || decision.End is null)
            {
                return "フォルダを作成できません（"
                    + CreationReason(decision.Status)
                    + "）";
            }

            var range = decision.Start == decision.End
                ? $"L{decision.Start:00}"
                : $"L{decision.Start:00}–L{decision.End:00}";

            return decision.NeedsAdditionalLayer
                ? $"{range} をフォルダにする（下に空レイヤーを1つ追加）..."
                : $"{range} をフォルダにまとめる...";
        }

        var create = new MenuItem
        {
            Header = Header(),
            IsEnabled = decision.Allowed
        };

        create.Click += (_, _) =>
        {
            if (!decision.Allowed)
                return;

            var count = FolderDocumentRules.FindTimeline(
                    state.Document,
                    timeline.ID.ToString("D"))
                ?.Folders.Count ?? 0;
            var fallback = $"フォルダ {count + 1}";
            var name = TextPrompt.Show("新しいフォルダ名", fallback);
            if (name is null)
                return;
            if (string.IsNullOrWhiteSpace(name))
                name = fallback;

            RunFolderCommand(
                () => commands.CreateFromContext(
                    logicalLayer,
                    candidateId,
                    name));
        };

        root.Items.Add(create);

        if (decision.AdjustedForFolderHead
            && decision.RequestedStart is not null
            && decision.Start is not null)
        {
            root.Items.Add(new MenuItem
            {
                Header =
                    $"  (L{decision.RequestedStart:00} は既存フォルダの先頭のため、" +
                    $"L{decision.Start:00} から作成します)",
                IsEnabled = false
            });
        }
    }

    private void AddExistingFolderActions(MenuItem root, int logicalLayer)
    {
        var timelineState = FolderDocumentRules.FindTimeline(
            state.Document,
            timeline.ID.ToString("D"));
        if (timelineState is null)
            return;

        var containing = timelineState.Folders
            .Where(x => x.Start <= logicalLayer && logicalLayer <= x.End)
            .OrderBy(x => x.End - x.Start)
            .ThenByDescending(x => x.Start)
            .ToArray();

        if (containing.FirstOrDefault() is { } innermost)
        {
            root.Items.Add(new Separator());

            var addTail = new MenuItem
            {
                Header = $"「{innermost.Name}」の末尾にレイヤーを追加"
            };
            addTail.Click += (_, _) =>
                RunFolderCommand(
                    () => commands.AddLayerAtFolderEnd(innermost.Id));
            root.Items.Add(addTail);

            if (logicalLayer != innermost.Start)
            {
                var addBelow = new MenuItem
                {
                    Header =
                        $"L{logicalLayer:00} の下にレイヤーを追加" +
                        $"（「{innermost.Name}」内）"
                };
                addBelow.Click += (_, _) =>
                    RunFolderCommand(
                        () => commands.AddLayerBelowInsideFolder(
                            innermost.Id,
                            logicalLayer));
                root.Items.Add(addBelow);
            }
        }

        foreach (var folder in containing)
        {
            root.Items.Add(new Separator());

            var sub = new MenuItem
            {
                Header = $"「{folder.Name}」 (L{folder.Start}–L{folder.End})"
            };

            var toggle = new MenuItem
            {
                Header = folder.IsCollapsed ? "開く" : "畳む"
            };
            toggle.Click += (_, _) =>
                RunFolderCommand(() => commands.ToggleCollapsed(folder.Id));
            sub.Items.Add(toggle);

            var rename = new MenuItem { Header = "名前を変更..." };
            rename.Click += (_, _) => PromptRename(folder.Id);
            sub.Items.Add(rename);

            var selectItems = new MenuItem { Header = "中のアイテムを選択" };
            selectItems.Click += (_, _) =>
                RunFolderCommand(() => commands.SelectItems(folder.Id));
            sub.Items.Add(selectItems);

            var option = commands.FindOption(folder.Id);
            var colors = new MenuItem { Header = "色" };
            foreach (var choice in FolderCommands.Palette)
            {
                var swatch = new Border
                {
                    Width = 12,
                    Height = 12,
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Background = ParseFolderColor(choice.Color)
                        ?? Brushes.Transparent
                };
                var item = new MenuItem
                {
                    Header = choice.Name,
                    Icon = swatch
                };
                var capturedColor = choice.Color;
                item.Click += (_, _) =>
                    RunFolderCommand(
                        () => commands.SetColor(
                            folder.Id,
                            capturedColor));
                colors.Items.Add(item);
            }
            sub.Items.Add(colors);

            var applyColor = new MenuItem
            {
                Header = "フォルダの色を YMM4 のレイヤー色にする"
            };
            applyColor.Click += (_, _) =>
                RunFolderCommand(
                    () => commands.ApplyFolderColorToLayers(folder.Id));
            sub.Items.Add(applyColor);

            var visibilityItem = new MenuItem
            {
                Header = option?.Hidden == true
                    ? "フォルダを表示（各レイヤーの状態に戻す）"
                    : "フォルダを非表示"
            };
            visibilityItem.Click += (_, _) =>
                RunFolderCommand(
                    () => commands.SetHidden(
                        folder.Id,
                        option?.Hidden != true));
            sub.Items.Add(visibilityItem);

            var addLayer = new MenuItem
            {
                Header = "フォルダの末尾にレイヤーを追加"
            };
            addLayer.Click += (_, _) =>
                RunFolderCommand(
                    () => commands.AddLayerAtFolderEnd(folder.Id));
            sub.Items.Add(addLayer);

            sub.Items.Add(new Separator());

            var addGroup = new MenuItem
            {
                Header = "フォルダ全体を制御するグループ制御を追加"
            };
            addGroup.Click += (_, _) =>
                RunFolderCommand(
                    () => commands.AddGroupControl(folder.Id));
            sub.Items.Add(addGroup);

            IReadOnlyList<GroupIssue> issues;
            try
            {
                issues = commands.GetGroupIssues(folder.Id);
            }
            catch (Exception ex)
            {
                HandsOnRuntime.Diagnostic(
                    "group_issue_scan_error=" + ex);
                issues = [];
            }

            var fitGroups = new MenuItem
            {
                Header = issues.Count > 0
                    ? $"グループ制御の範囲をフォルダに合わせる" +
                      $"（{issues.Count}件のずれ）"
                    : "グループ制御の範囲をフォルダに合わせる",
                IsEnabled = issues.Count > 0
            };
            fitGroups.Click += (_, _) =>
                RunFolderCommand(
                    () => commands.FitGroupRanges(folder.Id));
            sub.Items.Add(fitGroups);

            sub.Items.Add(new Separator());

            var ungroup = new MenuItem
            {
                Header = "フォルダを解除（レイヤーは残す）"
            };
            ungroup.Click += (_, _) =>
                RunFolderCommand(() => commands.Ungroup(folder.Id));
            sub.Items.Add(ungroup);

            var deleteContents = new MenuItem
            {
                Header = "フォルダと中のレイヤーを削除..."
            };
            deleteContents.Click += (_, _) =>
            {
                var layerCount = folder.End - folder.Start + 1;
                var itemCount = 0;
                try
                {
                    itemCount = commands.CountItemsInFolder(folder.Id);
                }
                catch (Exception ex)
                {
                    HandsOnRuntime.Diagnostic(
                        "delete_count_error=" + ex);
                }

                var message =
                    $"フォルダ「{folder.Name}」と中の {layerCount} レイヤー" +
                    $"（アイテム {itemCount} 個）を削除します。\n" +
                    "元に戻すには YMM4 の「元に戻す」を使ってください。";

                if (MessageBox.Show(
                        message,
                        FolderToolViewModel.DisplayTitle,
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning)
                    != MessageBoxResult.OK)
                {
                    return;
                }

                RunFolderCommand(
                    () => commands.DeleteFolderContents(folder.Id));
            };
            sub.Items.Add(deleteContents);

            root.Items.Add(sub);
        }
    }

    private void PromptRename(Guid folderId)
    {
        var folder = commands.FindFolder(folderId);
        if (folder is null)
            return;

        var name = TextPrompt.Show("フォルダ名", folder.Name);
        if (name is null)
            return;
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(
                "フォルダ名を入力してください。",
                FolderToolViewModel.DisplayTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        RunFolderCommand(() => commands.Rename(folderId, name));
    }

    private static void RunFolderCommand(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic("folder_command_error=" + ex);
            MessageBox.Show(
                "フォルダ操作に失敗しました。\n" + ex.Message,
                FolderToolViewModel.DisplayTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static Brush? ParseFolderColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            return new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(text));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string CreationReason(FolderCreationStatus status) =>
        status switch
        {
            FolderCreationStatus.EmptySelection => "レイヤーを選択してください",
            FolderCreationStatus.TooSmall => "2レイヤー以上を選択してください",
            FolderCreationStatus.InvalidSelection => "選択範囲が不正です",
            FolderCreationStatus.NonContiguous => "連続したレイヤーを選択してください",
            FolderCreationStatus.InvalidRange => "既存フォルダと交差しています",
            _ => "作成できません"
        };

    private static void RemoveTagged(ContextMenu menu)
    {
        foreach (var item in menu.Items
            .OfType<FrameworkElement>()
            .Where(x => Equals(x.Tag, MenuTag))
            .ToArray())
        {
            menu.Items.Remove(item);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        window.RemoveHandler(Mouse.PreviewMouseDownEvent, mouseHandler);

        if (adorner is not null)
        {
            adornerLayer.Remove(adorner);
            adorner = null;
        }

        display.Applied -= OnDisplayApplied;
        visualSummary?.Dispose();
        fileDrop.Dispose();
        input.Dispose();
        visibility.Dispose();
        structural.Dispose();
        display.Dispose();
    }

    private sealed class FolderOverlayAdorner : Adorner
    {
        private readonly Canvas canvas = new()
        {
            ClipToBounds = true,
            IsHitTestVisible = false
        };
        private readonly VisualCollection children;
        private readonly DirectDisplay display;
        private readonly PersistedFolder[] folders;
        private readonly FolderProductState productState;
        private readonly string timelineKey;
        private sealed record FolderHit(Rect Toggle, Rect Name);
        private readonly Dictionary<Guid, FolderHit> hitRects = [];
        private readonly Dictionary<Guid, Rect> folderRects = [];

        internal FolderOverlayAdorner(
            UIElement adornedElement,
            DirectDisplay display,
            PersistedFolder[] folders,
            FolderProductState productState,
            string timelineKey)
            : base(adornedElement)
        {
            this.display = display;
            this.folders = folders;
            this.productState = productState;
            this.timelineKey = timelineKey;
            children = new VisualCollection(this) { canvas };
            IsHitTestVisible = false;
            Rebuild();
        }

        internal bool TryToggleAt(Point point, out Guid folderId)
        {
            foreach (var pair in hitRects)
            {
                if (pair.Value.Toggle.Contains(point))
                {
                    folderId = pair.Key;
                    return true;
                }
            }

            folderId = Guid.Empty;
            return false;
        }

        internal bool TryNameAt(Point point, out Guid folderId)
        {
            foreach (var pair in hitRects)
            {
                if (pair.Value.Name.Contains(point))
                {
                    folderId = pair.Key;
                    return true;
                }
            }

            folderId = Guid.Empty;
            return false;
        }

        internal bool TryGetFolderRect(Guid folderId, out Rect rect) =>
            folderRects.TryGetValue(folderId, out rect);

        internal bool TryGetRenderedRowRect(int logicalLayer, out Rect rect)
        {
            FrameworkElement? best = null;
            double bestTop = 0;
            var bestArea = -1.0;

            foreach (var element in Host.Elements(AdornedElement))
            {
                if (ReferenceEquals(element, AdornedElement)
                    || !element.IsVisible
                    || element.ActualWidth <= 0
                    || element.ActualHeight <= 0
                    || HandsOnHostAccess.LayerId(element.DataContext)
                        != logicalLayer)
                {
                    continue;
                }

                Point point;
                try
                {
                    point = element.TranslatePoint(
                        new Point(),
                        AdornedElement);
                }
                catch
                {
                    continue;
                }

                if (!double.IsFinite(point.Y)
                    || point.Y + element.ActualHeight < -0.5
                    || point.Y > AdornedElement.RenderSize.Height + 0.5)
                {
                    continue;
                }

                var area =
                    element.ActualWidth
                    * element.ActualHeight;

                if (area <= bestArea)
                    continue;

                best = element;
                bestTop = point.Y;
                bestArea = area;
            }

            if (best is null)
            {
                rect = Rect.Empty;
                return false;
            }

            rect = new Rect(
                0,
                bestTop,
                Math.Max(
                    0,
                    AdornedElement.RenderSize.Width),
                best.ActualHeight);
            return true;
        }

        private void Rebuild()
        {
            canvas.Children.Clear();
            hitRects.Clear();
            folderRects.Clear();

            foreach (var folder in folders
                .OrderBy(x => x.Start)
                .ThenByDescending(x => x.End))
            {
                if (display.Layout.IsHidden(folder.Start))
                    continue;

                var depth = folders.Count(parent =>
                    parent.Id != folder.Id
                    && parent.Start < folder.Start
                    && folder.End <= parent.End);

                var x = 2 + depth * 9;
                var rowTop =
                    display.Layout.VisualRowOfLogical(folder.Start)
                    * (double)display.Height;
                var rowHeight = (double)display.Height;

                if (TryGetRenderedRowRect(
                        folder.Start,
                        out var renderedRow))
                {
                    rowTop = renderedRow.Y;
                    rowHeight = renderedRow.Height;
                }

                var width = Math.Max(
                    48,
                    Math.Min(
                        120,
                        Math.Max(48, AdornedElement.RenderSize.Width - x - 4)));

                var verticalInset = Math.Min(
                    3.0,
                    Math.Max(
                        0,
                        (rowHeight - 18.0) / 2.0));
                var height = Math.Max(
                    1,
                    rowHeight - verticalInset * 2.0);
                var y = rowTop + verticalInset;
                var rect = new Rect(x, y, width, height);
                folderRects[folder.Id] = rect;
                var toggleWidth = Math.Min(18, width);
                hitRects[folder.Id] = new FolderHit(
                    new Rect(x, y, toggleWidth, height),
                    new Rect(
                        x + toggleWidth,
                        y,
                        Math.Max(0, width - toggleWidth),
                        height));

                var border = new Border
                {
                    Width = width,
                    Height = height,
                    CornerRadius = new CornerRadius(3),
                    Background =
                        ParseFolderColor(
                            FolderProductStateRules.FindOption(
                                productState,
                                timelineKey,
                                folder.Id)?.Color)
                        ?? new SolidColorBrush(
                            Color.FromArgb(215, 78, 78, 78)),
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = (folder.IsCollapsed ? "▶ " : "▼ ") + folder.Name,
                        Foreground = Brushes.White,
                        FontSize = 10,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 3, 0),
                        IsHitTestVisible = false
                    }
                };

                Canvas.SetLeft(border, rect.X);
                Canvas.SetTop(border, rect.Y);
                canvas.Children.Add(border);
            }
        }

        protected override int VisualChildrenCount => children.Count;
        protected override Visual GetVisualChild(int index) => children[index];

        protected override Size MeasureOverride(Size constraint)
        {
            canvas.Measure(constraint);
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            canvas.Arrange(new Rect(AdornedElement.RenderSize));
            return finalSize;
        }
    }
}

internal static class TextPrompt
{
    internal static string? Show(string title, string initial)
    {
        var box = new TextBox
        {
            Text = initial,
            MinWidth = 240,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var ok = new Button
        {
            Content = "OK",
            IsDefault = true,
            MinWidth = 72,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var cancel = new Button
        {
            Content = "キャンセル",
            IsCancel = true,
            MinWidth = 72
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { ok, cancel }
        };
        var window = new Window
        {
            Title = title,
            Content = new StackPanel
            {
                Margin = new Thickness(12),
                Children = { box, buttons }
            },
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
            ShowInTaskbar = false
        };

        ok.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
            Keyboard.Focus(box);
        };

        return window.ShowDialog() == true ? box.Text : null;
    }
}
