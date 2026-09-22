using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyState;
using Ymm4NoHarmonyUx;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.UndoRedo;

namespace Ymm4NoHarmonyFolderLayoutProbe;

internal sealed class HandsOnController : IDisposable
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
    private readonly FolderCommands commands;
    private readonly InputMapAdapter input;
    private readonly FileDropMapAdapter fileDrop;
    private readonly AdornerLayer adornerLayer;
    private readonly MouseButtonEventHandler mouseHandler;

    private FolderOverlayAdorner? adorner;
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
        structural = new StructuralFolderBridge(
            window,
            timeline,
            undo,
            state,
            HandsOnRuntime.Diagnostic);
        commands = new FolderCommands(
            window,
            host,
            undo,
            state,
            structural,
            HandsOnRuntime.Diagnostic);
        input = new InputMapAdapter(host, display, HandsOnRuntime.Diagnostic);
        fileDrop = new FileDropMapAdapter(host, display, HandsOnRuntime.Diagnostic);
        adornerLayer = AdornerLayer.GetAdornerLayer(labels)
            ?? throw new InvalidOperationException("LayerLabels has no AdornerLayer.");

        mouseHandler = OnPreviewMouseDown;
        window.AddHandler(Mouse.PreviewMouseDownEvent, mouseHandler, true);
        RefreshFromDocument();
        ScheduleS0IntegrationSmoke();
        ScheduleS1IntegrationSmoke();
        ScheduleS2MetadataSurfaceSmoke();
    }

    private void ScheduleS2MetadataSurfaceSmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P4_S2_METADATA_SURFACE_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunS2MetadataSurfaceSmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunS2MetadataSurfaceSmokeAsync()
    {
        EventHandler? recorded = null;
        EventHandler? undone = null;
        EventHandler? redone = null;

        var recordedCount = 0;
        var undoCount = 0;
        var redoCount = 0;

        try
        {
            await Task.Delay(500);

            if (timeline.Items.Any())
            {
                throw new InvalidOperationException(
                    "S2 metadata surface smoke must start resource-free.");
            }

            // Flush any host initialization unit before observing setter history.
            undo.Record();

            recorded = (_, _) => recordedCount++;
            undone = (_, _) => undoCount++;
            redone = (_, _) => redoCount++;
            undo.Recorded += recorded;
            undo.Undoed += undone;
            undo.Redoed += redone;

            var visibilityLayer = 1;
            var originalVisible =
                timeline.LayerSettings.IsVisibles[visibilityLayer];
            var changedVisible = !originalVisible;

            timeline.LayerSettings.IsVisibles[visibilityLayer] =
                changedVisible;
            var visibilityAfterSetter =
                timeline.LayerSettings.IsVisibles[visibilityLayer];

            var recordedBeforeVisibilityCommit = recordedCount;
            undo.Record();
            var visibilityRecordDelta =
                recordedCount - recordedBeforeVisibilityCommit;

            var visibilityUndoRestored = false;
            var visibilityRedoRestored = false;

            if (visibilityRecordDelta > 0)
            {
                ExecuteHostCommand(CommandType.Undo, null);
                await Task.Delay(250);
                visibilityUndoRestored =
                    timeline.LayerSettings.IsVisibles[visibilityLayer]
                    == originalVisible;

                ExecuteHostCommand(CommandType.Redo, null);
                await Task.Delay(250);
                visibilityRedoRestored =
                    timeline.LayerSettings.IsVisibles[visibilityLayer]
                    == changedVisible;
            }

            // Return to the baseline before probing color.
            if (visibilityRedoRestored)
            {
                ExecuteHostCommand(CommandType.Undo, null);
                await Task.Delay(250);
            }
            else
            {
                timeline.LayerSettings.IsVisibles[visibilityLayer] =
                    originalVisible;
                undo.Record();
            }

            var colorLayer = 2;
            var originalColor = timeline.LayerSettings.Colors[colorLayer];
            var changedColor = Color.FromArgb(255, 79, 127, 224);

            if (changedColor == originalColor)
                changedColor = Color.FromArgb(255, 224, 90, 90);

            timeline.LayerSettings.Colors[colorLayer] = changedColor;
            var colorAfterSetter =
                timeline.LayerSettings.Colors[colorLayer];

            var recordedBeforeColorCommit = recordedCount;
            undo.Record();
            var colorRecordDelta =
                recordedCount - recordedBeforeColorCommit;

            var colorUndoRestored = false;
            var colorRedoRestored = false;

            if (colorRecordDelta > 0)
            {
                ExecuteHostCommand(CommandType.Undo, null);
                await Task.Delay(250);
                colorUndoRestored =
                    timeline.LayerSettings.Colors[colorLayer]
                    == originalColor;

                ExecuteHostCommand(CommandType.Redo, null);
                await Task.Delay(250);
                colorRedoRestored =
                    timeline.LayerSettings.Colors[colorLayer]
                    == changedColor;
            }

            // Leave the disposable project at baseline.
            if (colorRedoRestored)
            {
                ExecuteHostCommand(CommandType.Undo, null);
                await Task.Delay(250);
            }
            else
            {
                timeline.LayerSettings.Colors[colorLayer] =
                    originalColor;
                undo.Record();
            }

            WriteS2MetadataSurfaceResult(
                "PASS_S2_METADATA_SURFACE\n" +
                $"visibility_setter_changed={visibilityAfterSetter == changedVisible}\n" +
                $"visibility_record_delta={visibilityRecordDelta}\n" +
                $"visibility_undo_restored={visibilityUndoRestored}\n" +
                $"visibility_redo_restored={visibilityRedoRestored}\n" +
                $"color_setter_changed={colorAfterSetter == changedColor}\n" +
                $"color_record_delta={colorRecordDelta}\n" +
                $"color_undo_restored={colorUndoRestored}\n" +
                $"color_redo_restored={colorRedoRestored}\n" +
                $"recorded_events={recordedCount}\n" +
                $"undo_events={undoCount}\n" +
                $"redo_events={redoCount}\n");
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic(
                "s2_metadata_surface_smoke_error=" + ex);
            WriteS2MetadataSurfaceResult(
                "FAIL_S2_METADATA_SURFACE\n" + ex + "\n");
        }
        finally
        {
            if (recorded is not null)
                undo.Recorded -= recorded;
            if (undone is not null)
                undo.Undoed -= undone;
            if (redone is not null)
                undo.Redoed -= redone;
        }
    }

    private static void WriteS2MetadataSurfaceResult(string text)
    {
        var dir = Environment.GetEnvironmentVariable(
            "CNWL_P4_HANDS_ON_DIAG_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "s2-metadata-surface-result.txt"),
            text);
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

        Application.Current.Dispatcher.BeginInvoke(
            new Action(RebuildOverlay),
            DispatcherPriority.ContextIdle);
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
            state.State,
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

            sub.Items.Add(new Separator());

            var option = FolderSessionDocumentRules.FindOption(
                state.State,
                timeline.ID.ToString("D"),
                folder.Id);

            var hidden = option?.Hidden ?? false;
            var hiddenItem = new MenuItem
            {
                Header = hidden ? "フォルダを表示" : "フォルダを非表示",
                IsCheckable = true,
                IsChecked = hidden
            };
            hiddenItem.Click += (_, _) =>
                RunFolderCommand(
                    () => commands.SetHidden(folder.Id, !hidden));
            sub.Items.Add(hiddenItem);

            var colorMenu = new MenuItem { Header = "フォルダの色" };
            foreach (var choice in FolderCommands.ColorChoices)
            {
                var colorItem = new MenuItem
                {
                    Header = choice.Name,
                    IsCheckable = true,
                    IsChecked = string.Equals(
                        option?.Color,
                        choice.Color,
                        StringComparison.OrdinalIgnoreCase),
                    Icon = CreateColorSwatch(choice.Color)
                };

                var capturedColor = choice.Color;
                colorItem.Click += (_, _) =>
                    RunFolderCommand(
                        () => commands.SetColor(
                            folder.Id,
                            capturedColor));
                colorMenu.Items.Add(colorItem);
            }
            sub.Items.Add(colorMenu);

            var applyColor = new MenuItem
            {
                Header = "フォルダ色をYMM4レイヤー色へ適用"
            };
            applyColor.Click += (_, _) =>
                RunFolderCommand(
                    () => commands.ApplyColorToLayers(folder.Id));
            sub.Items.Add(applyColor);

            sub.Items.Add(new Separator());

            var ungroup = new MenuItem
            {
                Header = "フォルダを解除（レイヤーは残す）"
            };
            ungroup.Click += (_, _) =>
                RunFolderCommand(() => commands.Ungroup(folder.Id));
            sub.Items.Add(ungroup);

            root.Items.Add(sub);
        }
    }

    private static FrameworkElement CreateColorSwatch(string? color)
    {
        var brush = color is null
            ? Brushes.Transparent
            : new SolidColorBrush(ParseArgbColor(color));

        return new Border
        {
            Width = 14,
            Height = 14,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gray,
            Background = brush
        };
    }

    private static Color ParseArgbColor(string text)
    {
        var value = text.AsSpan();
        if (value.Length != 9 || value[0] != '#')
            throw new ArgumentException("Color must use #AARRGGBB format.");

        static byte ParseByte(ReadOnlySpan<char> hex) =>
            Convert.ToByte(hex.ToString(), 16);

        return Color.FromArgb(
            ParseByte(value.Slice(1, 2)),
            ParseByte(value.Slice(3, 2)),
            ParseByte(value.Slice(5, 2)),
            ParseByte(value.Slice(7, 2)));
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

        fileDrop.Dispose();
        input.Dispose();
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
        private readonly IReadOnlyDictionary<Guid, FolderOptionState> options;
        private sealed record FolderHit(Rect Toggle, Rect Name);
        private readonly Dictionary<Guid, FolderHit> hitRects = [];

        internal FolderOverlayAdorner(
            UIElement adornedElement,
            DirectDisplay display,
            PersistedFolder[] folders,
            FolderSessionDocument session,
            string timelineKey)
            : base(adornedElement)
        {
            this.display = display;
            this.folders = folders;
            options = session.FolderOptions
                .Where(x => string.Equals(
                    x.TimelineKey,
                    timelineKey,
                    StringComparison.Ordinal))
                .ToDictionary(x => x.FolderId);
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

        private void Rebuild()
        {
            canvas.Children.Clear();
            hitRects.Clear();

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
                var y = display.Layout.VisualRowOfLogical(folder.Start)
                    * display.Height + 3;
                var width = Math.Max(
                    48,
                    Math.Min(
                        120,
                        Math.Max(48, AdornedElement.RenderSize.Width - x - 4)));
                var height = Math.Max(18, display.Height - 6);
                var rect = new Rect(x, y, width, height);
                var toggleWidth = Math.Min(18, width);
                hitRects[folder.Id] = new FolderHit(
                    new Rect(x, y, toggleWidth, height),
                    new Rect(
                        x + toggleWidth,
                        y,
                        Math.Max(0, width - toggleWidth),
                        height));

                options.TryGetValue(folder.Id, out var option);
                var baseColor = option?.Color is { } text
                    ? ParseArgbColor(text)
                    : Color.FromRgb(78, 78, 78);
                var tagColor = Color.FromArgb(
                    option?.Hidden == true ? (byte)150 : (byte)215,
                    baseColor.R,
                    baseColor.G,
                    baseColor.B);

                var border = new Border
                {
                    Width = width,
                    Height = height,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(tagColor),
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text =
                            (folder.IsCollapsed ? "▶ " : "▼ ")
                            + (option?.Hidden == true ? "[非表示] " : "")
                            + folder.Name,
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
