using System.IO;
using System.Windows;
using System.Windows.Threading;
using Ymm4NoHarmonyPanel;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyProductState;
using Ymm4NoHarmonyStructuralConvenience;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4NoHarmonyFolderLayoutProbe;

internal sealed partial class HandsOnController
{
    private void ScheduleP6HistoryStressSmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P6_HISTORY_STRESS_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunP6HistoryStressSmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunP6HistoryStressSmokeAsync()
    {
        try
        {
            await Task.Delay(700);

            if (timeline.Items.Any())
                throw new InvalidOperationException(
                    "P6.2 history stress must start from an empty Timeline.");

            const int cycles = 4;
            const int settleMs = 320;

            for (var layer = 0; layer <= 13; layer++)
            {
                ExecuteHostCommand(CommandType.AddLayer, layer);
                await Task.Delay(45);
            }

            var fixtures = new IItem[]
            {
                new ShapeItem
                {
                    Frame = 20,
                    Layer = 2,
                    Length = 30,
                    Remark = "CNWL_P6H_PARENT"
                },
                new ShapeItem
                {
                    Frame = 70,
                    Layer = 4,
                    Length = 30,
                    Remark = "CNWL_P6H_CHILD"
                },
                new ShapeItem
                {
                    Frame = 120,
                    Layer = 6,
                    Length = 30,
                    Remark = "CNWL_P6H_EDGE"
                },
                new ShapeItem
                {
                    Frame = 170,
                    Layer = 9,
                    Length = 30,
                    Remark = "CNWL_P6H_SIBLING"
                },
                new ShapeItem
                {
                    Frame = 220,
                    Layer = 11,
                    Length = 30,
                    Remark = "CNWL_P6H_OUTSIDE"
                },
                new GroupItem
                {
                    Frame = 0,
                    Layer = 2,
                    Length = 260,
                    GroupRange = 4,
                    Remark = "CNWL_P6H_GROUP"
                }
            };

            foreach (var item in fixtures)
            {
                if (!timeline.TryAddItems(
                        [item],
                        item.Frame,
                        item.Layer,
                        isItemSelectionEnabled: false))
                {
                    throw new InvalidOperationException(
                        $"P6.2 fixture add failed: {item.Remark}.");
                }
            }

            await Task.Delay(900);

            var key = timeline.ID.ToString("D");
            var parentId = Guid.Parse(
                "61000000-0000-0000-0000-000000000001");
            var childId = Guid.Parse(
                "61000000-0000-0000-0000-000000000002");
            var siblingId = Guid.Parse(
                "61000000-0000-0000-0000-000000000003");

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
                                            Id = parentId,
                                            Start = 2,
                                            End = 6,
                                            Name = "P6 Parent",
                                            IsCollapsed = false
                                        },
                                        new PersistedFolder
                                        {
                                            Id = childId,
                                            Start = 3,
                                            End = 5,
                                            Name = "P6 Child",
                                            IsCollapsed = false
                                        },
                                        new PersistedFolder
                                        {
                                            Id = siblingId,
                                            Start = 8,
                                            End = 10,
                                            Name = "P6 Sibling",
                                            IsCollapsed = false
                                        }
                                    ]
                                }
                            ]
                        })));

            await Task.Delay(650);
            display.ThrowIfFailed();
            FolderProductStateRules.NormalizeAndValidate(
                state.ProductState);

            string Snapshot()
            {
                var itemState = string.Join(
                    "|",
                    timeline.Items
                        .Where(item =>
                            item.Remark?.StartsWith(
                                "CNWL_P6H_",
                                StringComparison.Ordinal)
                            == true)
                        .OrderBy(
                            item => item.Remark,
                            StringComparer.Ordinal)
                        .Select(item =>
                            item.Remark
                            + ":"
                            + item.GetType().Name
                            + "@L"
                            + item.Layer
                            + ":F"
                            + item.Frame
                            + ":N"
                            + item.Length
                            + (item is GroupItem group
                                ? ":G" + group.GroupRange
                                : "")));

                var visibility = string.Join(
                    "",
                    Enumerable.Range(
                            0,
                            Math.Min(
                                14,
                                timeline.LayerSettings.MaxLayer + 1))
                        .Select(layer =>
                            timeline.LayerSettings.IsVisibles[layer]
                                ? "1"
                                : "0"));

                return FolderProductStateCodec.Save(
                        FolderProductStateRules.NormalizeAndValidate(
                            state.ProductState))
                    + "||items="
                    + itemState
                    + "||max="
                    + timeline.MaxLayer
                    + "/"
                    + timeline.LayerSettings.MaxLayer
                    + "||settings="
                    + timeline.LayerSettings.Items.Count
                    + "||visible="
                    + visibility;
            }

            async Task SettleAsync(string phase)
            {
                await Task.Delay(settleMs);
                display.ThrowIfFailed();
                FolderProductStateRules.NormalizeAndValidate(
                    state.ProductState);

                if (!display.GeometryMatches())
                {
                    throw new InvalidOperationException(
                        $"P6.2 geometry mismatch at {phase}.");
                }
            }

            var baseline = Snapshot();
            var operations = 0;
            var historyTransitions = 0;

            async Task ExerciseAsync(
                string name,
                Action operation)
            {
                var before = Snapshot();
                if (!string.Equals(
                        before,
                        baseline,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"P6.2 baseline drift before {name}.");
                }

                operation();
                operations++;
                await SettleAsync(name + "-after");

                var after = Snapshot();
                if (string.Equals(
                        after,
                        before,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"P6.2 {name} produced no observable state change.");
                }

                ExecuteHostCommand(CommandType.Undo, null);
                historyTransitions++;
                await SettleAsync(name + "-undo");

                if (!string.Equals(
                        Snapshot(),
                        before,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"P6.2 {name} Undo did not restore the exact baseline.");
                }

                ExecuteHostCommand(CommandType.Redo, null);
                historyTransitions++;
                await SettleAsync(name + "-redo");

                if (!string.Equals(
                        Snapshot(),
                        after,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"P6.2 {name} Redo did not restore the exact post-state.");
                }

                ExecuteHostCommand(CommandType.Undo, null);
                historyTransitions++;
                await SettleAsync(name + "-reset");

                if (!string.Equals(
                        Snapshot(),
                        before,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"P6.2 {name} final reset drifted from baseline.");
                }
            }

            PanelDragBlock ResolveParentBlock()
            {
                var rows = PanelProjection.Build(
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
                    timeline.Items
                        .OfType<GroupItem>()
                        .OrderBy(group => group.Layer)
                        .ThenBy(group => group.Frame)
                        .Select(group =>
                            new GroupSpan(
                                group.Layer,
                                group.GroupRange))
                        .ToArray());

                var row = rows.Single(current =>
                    current.FolderId == parentId);

                return PanelMoveRules.TryGetDragBlock([row])
                    ?? throw new InvalidOperationException(
                        "P6.2 parent folder did not resolve to a drag block.");
            }

            for (var cycle = 0; cycle < cycles; cycle++)
            {
                await ExerciseAsync(
                    $"cycle{cycle}-standard-add",
                    () => ExecuteHostCommand(
                        CommandType.AddLayer,
                        4));

                await ExerciseAsync(
                    $"cycle{cycle}-standard-delete",
                    () => ExecuteHostCommand(
                        CommandType.DeleteLayer,
                        4));

                await ExerciseAsync(
                    $"cycle{cycle}-standard-movedown",
                    () => ExecuteHostCommand(
                        CommandType.MoveDownLayer,
                        6));

                await ExerciseAsync(
                    $"cycle{cycle}-plugin-add-inside",
                    () => commands.AddLayerBelowInsideFolder(
                        parentId,
                        4));

                await ExerciseAsync(
                    $"cycle{cycle}-hidden",
                    () => commands.SetHidden(
                        parentId,
                        true));

                await ExerciseAsync(
                    $"cycle{cycle}-block-move",
                    () =>
                    {
                        var block = ResolveParentBlock();
                        var drop = new PanelDropTarget(
                            OriginalInsertionBoundary: 12,
                            IntoFolderId: null);

                        if (!commands.CanMovePanelRows(
                                block,
                                drop))
                        {
                            throw new InvalidOperationException(
                                "P6.2 representative block move was rejected.");
                        }

                        commands.MovePanelRows(
                            block,
                            drop);
                    });
            }

            if (!string.Equals(
                    Snapshot(),
                    baseline,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "P6.2 final state drifted from baseline.");
            }

            if (display.Reentries != 0)
                throw new InvalidOperationException(
                    $"P6.2 display reentries={display.Reentries}.");

            if (display.Failure is not null)
                throw new InvalidOperationException(
                    "P6.2 display failure remained set.",
                    display.Failure);

            FolderProductStateRules.NormalizeAndValidate(
                state.ProductState);

            WriteP6HistoryResult(
                string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        "PASS_P6_HISTORY_STRESS",
                        $"timeline={key}",
                        $"cycles={cycles}",
                        $"operations={operations}",
                        $"history_transitions={historyTransitions}",
                        "standard_add_delete_movedown=true",
                        "plugin_add_inside=true",
                        "hidden_undo_redo=true",
                        "block_move_undo_redo=true",
                        "exact_snapshot_restore=true",
                        "folder_validation=true",
                        $"display_reentries={display.Reentries}",
                        $"display_failure={(display.Failure is null ? "none" : display.Failure.GetType().Name)}",
                        "no_harmony=true"
                    })
                + Environment.NewLine);
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic(
                "p6_history_stress_error=" + ex);

            WriteP6HistoryResult(
                "FAIL_P6_HISTORY_STRESS\n"
                + ex
                + "\n");
        }
    }

    private static void WriteP6HistoryResult(
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
                "p6-history-result.txt"),
            text);
    }
}
