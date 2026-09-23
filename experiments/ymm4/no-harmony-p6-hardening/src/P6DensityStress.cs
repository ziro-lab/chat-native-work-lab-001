using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyProductState;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4NoHarmonyFolderLayoutProbe;

internal sealed partial class HandsOnController
{
    partial void ScheduleP6Probes()
    {
        ScheduleP6DensityStressSmoke();
        ScheduleP6HistoryStressSmoke();
        ScheduleP6LifecycleStressSmoke();
    }

    private void ScheduleP6DensityStressSmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P6_DENSITY_STRESS_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunP6DensityStressSmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunP6DensityStressSmokeAsync()
    {
        try
        {
            await Task.Delay(700);

            if (timeline.Items.Any())
                throw new InvalidOperationException(
                    "P6.1 density stress must start from an empty Timeline.");

            const int logicalLayers = 128;
            const int itemsPerLayer = 3;
            const int foldCycles = 12;
            const int viewportCycles = 12;

            var fixtures = new List<IItem>(
                logicalLayers * itemsPerLayer);

            for (var layer = 0; layer < logicalLayers; layer++)
            {
                for (var slot = 0; slot < itemsPerLayer; slot++)
                {
                    var item = new ShapeItem
                    {
                        Frame = layer * 20 + slot * 6,
                        Layer = layer,
                        Length = 16,
                        Remark = $"CNWL_P6_L{layer:000}_{slot}"
                    };

                    if (!timeline.TryAddItems(
                            [item],
                            item.Frame,
                            item.Layer,
                            isItemSelectionEnabled: false))
                    {
                        throw new InvalidOperationException(
                            $"P6.1 fixture add failed L{layer} slot={slot}.");
                    }

                    fixtures.Add(item);
                }
            }

            await Task.Delay(1200);
            display.ThrowIfFailed();

            if (fixtures.Count != logicalLayers * itemsPerLayer
                || timeline.Items.Count(item =>
                    fixtures.Any(fixture =>
                        ReferenceEquals(item, fixture)))
                    != fixtures.Count)
            {
                throw new InvalidOperationException(
                    "P6.1 fixture item count mismatch.");
            }

            var key = timeline.ID.ToString("D");
            var folderIds = new List<Guid>();
            var folderSeed = new List<(Guid Id, int Start, int End)>();

            // Eight nested folders: max nesting depth = 8.
            for (var depth = 0; depth < 8; depth++)
            {
                var id = Guid.Parse(
                    $"60000000-0000-0000-0000-{depth + 1:000000000000}");
                var start = 1 + depth;
                var end = 64 - depth * 7;
                folderIds.Add(id);
                folderSeed.Add((id, start, end));
            }

            // Sixteen disjoint small folders after the nested region.
            for (var index = 0; index < 16; index++)
            {
                var id = Guid.Parse(
                    $"60000000-0000-0000-0001-{index + 1:000000000000}");
                var start = 65 + index * 4;
                var end = start + 2;
                folderIds.Add(id);
                folderSeed.Add((id, start, end));
            }

            if (folderSeed.Count != 24
                || folderSeed.Max(folder => folder.End) >= logicalLayers)
            {
                throw new InvalidOperationException(
                    "P6.1 folder fixture bounds are invalid.");
            }

            FolderProductState Product(bool collapsed)
            {
                var folders = folderSeed
                    .Select((folder, index) =>
                        new PersistedFolder
                        {
                            Id = folder.Id,
                            Start = folder.Start,
                            End = folder.End,
                            Name = $"P6-{index:00}",
                            IsCollapsed = collapsed
                        })
                    .ToList();

                return FolderProductStateRules.ReplaceCore(
                    FolderProductState.Empty,
                    FolderDocumentRules.NormalizeAndValidate(
                        new FolderDocument
                        {
                            Timelines =
                            [
                                new TimelineFolderState
                                {
                                    TimelineKey = key,
                                    Folders = folders
                                }
                            ]
                        }));
            }

            var expanded = Product(false);
            var collapsed = Product(true);

            state.ReplaceProductState(expanded);
            await Task.Delay(700);
            display.ThrowIfFailed();

            // Counters start after the density fixture is constructed. Subscription
            // stability is measured later, after two identical viewport warm-up
            // sweeps have allowed YMM4 to realize/cache any host-owned row/item VMs.
            var applicationsStart = display.Applications;
            var mutationsStart = display.Mutations;
            var refreshesStart = display.CanvasRefreshes;

            for (var cycle = 0; cycle < foldCycles; cycle++)
            {
                state.ReplaceProductState(collapsed);
                await Task.Delay(180);
                display.ThrowIfFailed();

                var hiddenActualLayers = Enumerable.Range(0, logicalLayers)
                    .Count(display.Layout.IsHidden);

                if (hiddenActualLayers != 95)
                {
                    throw new InvalidOperationException(
                        $"P6.1 hidden layer count={hiddenActualLayers}, expected 95, cycle={cycle}.");
                }

                if (!display.GeometryMatches())
                    throw new InvalidOperationException(
                        $"P6.1 collapsed geometry mismatch cycle={cycle}.");

                if (visualSummary is null)
                    throw new InvalidOperationException(
                        "P6.1 visual summary overlay unavailable.");

                visualSummary.Refresh();

                if (visualSummary.TimingBandCount != 285)
                {
                    throw new InvalidOperationException(
                        $"P6.1 timing bands={visualSummary.TimingBandCount}, expected 285, cycle={cycle}.");
                }

                state.ReplaceProductState(expanded);
                await Task.Delay(180);
                display.ThrowIfFailed();

                if (!display.GeometryMatches())
                    throw new InvalidOperationException(
                        $"P6.1 expanded geometry mismatch cycle={cycle}.");

                visualSummary.Refresh();

                if (visualSummary.TimingBandCount != 0)
                {
                    throw new InvalidOperationException(
                        $"P6.1 expanded timing bands={visualSummary.TimingBandCount}, expected 0, cycle={cycle}.");
                }
            }

            var zoomHolder = Host.Get(host.Vm, "TimelineZoom")
                ?? throw new MissingMemberException("TimelineZoom");
            var zoomValue = zoomHolder.GetType()
                .GetProperty(
                    "Value",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public)
                ?? throw new MissingMemberException(
                    zoomHolder.GetType().FullName,
                    "Value");

            if (zoomValue.SetMethod?.IsPublic != true)
                throw new InvalidOperationException(
                    "P6.1 TimelineZoom.Value public setter missing.");

            var zoomOriginal = zoomValue.GetValue(zoomHolder);

            async Task RunViewportPassAsync(string phase)
            {
                state.ReplaceProductState(collapsed);
                await Task.Delay(350);

                for (var cycle = 0; cycle < viewportCycles; cycle++)
                {
                    var zoom = cycle % 3 switch
                    {
                        0 => 80.0,
                        1 => 125.0,
                        _ => 100.0
                    };

                    zoomValue.SetValue(zoomHolder, zoom);

                    var horizontalMax = Math.Max(
                        0,
                        host.Scroll.ExtentWidth
                            - host.Scroll.ViewportWidth);
                    var verticalMax = Math.Max(
                        0,
                        host.Scroll.ExtentHeight
                            - host.Scroll.ViewportHeight);

                    host.Scroll.ScrollToHorizontalOffset(
                        horizontalMax <= 0
                            ? 0
                            : horizontalMax
                                * ((cycle % 5) / 4.0));
                    host.Scroll.ScrollToVerticalOffset(
                        verticalMax <= 0
                            ? 0
                            : verticalMax
                                * ((cycle % 4) / 3.0));

                    window.Width = cycle % 2 == 0
                        ? 1040
                        : 1180;
                    window.Height = cycle % 2 == 0
                        ? 700
                        : 760;

                    await Task.Delay(180);
                    display.ThrowIfFailed();

                    if (!display.GeometryMatches())
                        throw new InvalidOperationException(
                            $"P6.1 viewport geometry mismatch phase={phase} cycle={cycle}.");
                }

                if (zoomOriginal is not null)
                    zoomValue.SetValue(zoomHolder, zoomOriginal);

                host.Scroll.ScrollToHorizontalOffset(0);
                host.Scroll.ScrollToVerticalOffset(0);
                window.Width = 1100;
                window.Height = 720;

                state.ReplaceProductState(expanded);
                await Task.Delay(700);
                display.ThrowIfFailed();

                if (!display.GeometryMatches())
                    throw new InvalidOperationException(
                        $"P6.1 viewport pass final geometry mismatch phase={phase}.");
            }

            // The first pass may legitimately create host-owned virtualized VMs.
            // Repeat the same coverage once more before taking the leak baseline,
            // then require a third identical pass to add no subscriptions.
            await RunViewportPassAsync("warmup-1");
            var subscriptionsAfterWarmup1 = display.SubscriptionCount;

            await RunViewportPassAsync("warmup-2");
            var settledSubscriptions = display.SubscriptionCount;

            await RunViewportPassAsync("measured");
            var finalSubscriptions = display.SubscriptionCount;

            var finalTimelineState =
                FolderDocumentRules.FindTimeline(
                    state.Document,
                    key)
                ?? throw new InvalidOperationException(
                    "P6.1 final timeline folder state missing.");

            FolderDocumentRules.NormalizeAndValidate(
                state.Document);

            if (finalTimelineState.Folders.Count != 24)
                throw new InvalidOperationException(
                    $"P6.1 final folder count={finalTimelineState.Folders.Count}, expected 24.");

            if (display.Reentries != 0)
                throw new InvalidOperationException(
                    $"P6.1 display reentries={display.Reentries}.");

            if (display.Failure is not null)
                throw new InvalidOperationException(
                    "P6.1 display failure remained set.",
                    display.Failure);

            if (finalSubscriptions != settledSubscriptions)
            {
                throw new InvalidOperationException(
                    $"P6.1 steady-state subscription count grew from {settledSubscriptions} to {finalSubscriptions}; " +
                    $"warmup1={subscriptionsAfterWarmup1}.");
            }

            if (!display.GeometryMatches())
                throw new InvalidOperationException(
                    "P6.1 final expanded geometry mismatch.");

            WriteP6DensityResult(
                string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        "PASS_P6_DENSITY_STRESS",
                        $"timeline={key}",
                        $"layers={logicalLayers}",
                        $"items={fixtures.Count}",
                        $"folders={finalTimelineState.Folders.Count}",
                        "max_nesting_depth=8",
                        $"fold_cycles={foldCycles}",
                        $"viewport_cycles={viewportCycles}",
                        "collapsed_hidden_layers=95",
                        "collapsed_timing_bands=285",
                        $"applications_delta={display.Applications - applicationsStart}",
                        $"mutations_delta={display.Mutations - mutationsStart}",
                        $"canvas_refreshes_delta={display.CanvasRefreshes - refreshesStart}",
                        $"applications_total={display.Applications}",
                        $"mutations_total={display.Mutations}",
                        $"canvas_refreshes_total={display.CanvasRefreshes}",
                        $"reentries={display.Reentries}",
                        "viewport_warmup_passes=2",
                        $"subscriptions_after_warmup1={subscriptionsAfterWarmup1}",
                        $"subscriptions_settled={settledSubscriptions}",
                        $"subscriptions_final={finalSubscriptions}",
                        $"subscriptions_measured_growth={finalSubscriptions - settledSubscriptions}",
                        $"display_failure={(display.Failure is null ? "none" : display.Failure.GetType().Name)}",
                        "final_geometry=true",
                        "folder_validation=true",
                        "no_harmony=true"
                    })
                + Environment.NewLine);
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic(
                "p6_density_stress_error=" + ex);

            WriteP6DensityResult(
                "FAIL_P6_DENSITY_STRESS\n"
                + ex
                + "\n");
        }
    }

    private static void WriteP6DensityResult(string text)
    {
        var dir = Environment.GetEnvironmentVariable(
            "CNWL_P4_HANDS_ON_DIAG_DIR");

        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "p6-density-result.txt"),
            text);
    }


}
