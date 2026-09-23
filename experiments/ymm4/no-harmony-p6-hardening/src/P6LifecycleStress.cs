using System.IO;
using System.Windows;
using System.Windows.Threading;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyProductState;

namespace Ymm4NoHarmonyFolderLayoutProbe;

internal sealed partial class HandsOnController
{
    private static bool p6LifecycleScheduled;

    private void ScheduleP6LifecycleStressSmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P6_LIFECYCLE_STRESS_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        if (p6LifecycleScheduled)
            return;

        p6LifecycleScheduled = true;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunP6LifecycleStressSmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunP6LifecycleStressSmokeAsync()
    {
        try
        {
            await Task.Delay(700);

            const int transitions = 4;
            var root = window.DataContext
                ?? throw new InvalidOperationException(
                    "P6.4 MainViewModel missing.");

            var createProject = root.GetType()
                .GetMethod(
                    "CreateProject",
                    Type.EmptyTypes)
                ?? throw new MissingMethodException(
                    root.GetType().FullName,
                    "CreateProject()");

            var timelineIds = new HashSet<Guid>
            {
                timeline.ID
            };

            var disposedControllers = 0;
            var zeroSubscriptionControllers = 0;
            var emptyNewStates = 0;
            var staleVisibilityIgnored = 0;

            var current = this;

            async Task<HandsOnController> WaitForReplacementAsync(
                Guid previousId)
            {
                for (var attempt = 0; attempt < 50; attempt++)
                {
                    await Task.Delay(180);

                    var active =
                        HandsOnHostAccess.ActiveTimelineViewModel(root);
                    var nextTimeline =
                        HandsOnHostAccess.TimelineOf(active);

                    if (nextTimeline is null
                        || nextTimeline.ID == Guid.Empty
                        || nextTimeline.ID == previousId)
                    {
                        continue;
                    }

                    var next =
                        HandsOnRuntime.ControllerFor(
                            nextTimeline.ID);

                    if (next is not null)
                        return next;
                }

                throw new TimeoutException(
                    "P6.4 replacement controller did not attach.");
            }

            void SeedCurrentFolder(
                HandsOnController controller,
                int cycle)
            {
                var key =
                    controller.timeline.ID.ToString("D");
                var folderId = Guid.Parse(
                    $"64000000-0000-0000-0000-{cycle + 1:000000000000}");

                controller.state.ReplaceProductState(
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
                                                Start = 0,
                                                End = 0,
                                                Name =
                                                    $"P6 Lifecycle {cycle}",
                                                IsCollapsed = false
                                            }
                                        ]
                                    }
                                ]
                            })));
            }

            for (var cycle = 0;
                cycle < transitions;
                cycle++)
            {
                var old = current;
                var oldTimelineId = old.timeline.ID;
                var oldTimelineKey =
                    oldTimelineId.ToString("D");

                SeedCurrentFolder(old, cycle);
                await Task.Delay(450);

                if (FolderDocumentRules.FindTimeline(
                        old.state.Document,
                        oldTimelineKey)
                    is not { Folders.Count: 1 })
                {
                    throw new InvalidOperationException(
                        $"P6.4 old project seed failed cycle={cycle}.");
                }

                createProject.Invoke(root, null);

                var replacement =
                    await WaitForReplacementAsync(
                        oldTimelineId);

                current = replacement;

                if (!timelineIds.Add(
                        replacement.timeline.ID))
                {
                    throw new InvalidOperationException(
                        $"P6.4 Timeline.ID repeated cycle={cycle}.");
                }

                if (!old.disposed)
                {
                    throw new InvalidOperationException(
                        $"P6.4 old controller was not disposed cycle={cycle}.");
                }

                disposedControllers++;

                if (old.display.SubscriptionCount != 0)
                {
                    throw new InvalidOperationException(
                        $"P6.4 disposed display retained {old.display.SubscriptionCount} subscriptions cycle={cycle}.");
                }

                zeroSubscriptionControllers++;

                var currentTimelineState =
                    FolderDocumentRules.FindTimeline(
                        replacement.state.Document,
                        replacement.timeline.ID
                            .ToString("D"));
                var leakedOldState =
                    FolderDocumentRules.FindTimeline(
                        replacement.state.Document,
                        oldTimelineKey);

                if (currentTimelineState is not null
                    || leakedOldState is not null)
                {
                    throw new InvalidOperationException(
                        $"P6.4 new unsaved project inherited folder state cycle={cycle}.");
                }

                emptyNewStates++;

                var sharedBefore =
                    FolderProductStateCodec.Save(
                        replacement.state.ProductState);

                if (old.timeline.LayerSettings.MaxLayer >= 0)
                {
                    var oldVisible =
                        old.timeline.LayerSettings
                            .IsVisibles[0];
                    old.timeline.LayerSettings
                        .IsVisibles[0] =
                        !oldVisible;

                    await Task.Delay(350);

                    var sharedAfter =
                        FolderProductStateCodec.Save(
                            replacement.state.ProductState);

                    if (!string.Equals(
                            sharedBefore,
                            sharedAfter,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"P6.4 disposed visibility callback mutated current state cycle={cycle}.");
                    }

                    staleVisibilityIgnored++;

                    old.timeline.LayerSettings
                        .IsVisibles[0] =
                        oldVisible;
                }

                if (HandsOnRuntime.ControllerFor(
                        oldTimelineId)
                    is not null)
                {
                    throw new InvalidOperationException(
                        $"P6.4 old controller remained discoverable cycle={cycle}.");
                }
            }

            if (timelineIds.Count
                != transitions + 1)
            {
                throw new InvalidOperationException(
                    $"P6.4 unique timeline count={timelineIds.Count}, expected {transitions + 1}.");
            }

            WriteP6LifecycleResult(
                string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        "PASS_P6_LIFECYCLE_STRESS",
                        $"transitions={transitions}",
                        $"unique_timeline_ids={timelineIds.Count}",
                        $"disposed_controllers={disposedControllers}",
                        $"zero_subscription_controllers={zeroSubscriptionControllers}",
                        $"empty_new_states={emptyNewStates}",
                        $"stale_visibility_ignored={staleVisibilityIgnored}",
                        "old_controller_lookup_cleared=true",
                        "no_harmony=true"
                    })
                + Environment.NewLine);
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic(
                "p6_lifecycle_stress_error=" + ex);

            WriteP6LifecycleResult(
                "FAIL_P6_LIFECYCLE_STRESS\n"
                + ex
                + "\n");
        }
    }

    private static void WriteP6LifecycleResult(
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
                "p6-lifecycle-result.txt"),
            text);
    }
}
