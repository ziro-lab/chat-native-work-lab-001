using System.IO;
using System.Windows;
using System.Windows.Threading;
using Ymm4NoHarmonyPersistence;
using Ymm4NoHarmonyProductState;

namespace Ymm4NoHarmonyFolderLayoutProbe;

internal sealed partial class HandsOnController
{
    private void ScheduleP6FailureRecoverySmoke()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("CNWL_P6_FAILURE_RECOVERY_SMOKE"),
                "1",
                StringComparison.Ordinal))
            return;

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => _ = RunP6FailureRecoverySmokeAsync()),
            DispatcherPriority.ContextIdle);
    }

    private async Task RunP6FailureRecoverySmokeAsync()
    {
        try
        {
            await Task.Delay(700);

            if (timeline.Items.Any())
                throw new InvalidOperationException(
                    "P6.5 failure/recovery smoke must start from an empty Timeline.");

            var initialMaxLayer = timeline.MaxLayer;
            var initialSettingsCount =
                timeline.LayerSettings.Items.Count;

            const string unreadableRaw =
                "{\"schemaVersion\":999,\"future\":{\"keep\":\"P6\"}}";

            state.LoadRaw(unreadableRaw);
            await Task.Delay(350);

            if (!state.IsRecoveryBlocked)
                throw new InvalidOperationException(
                    "P6.5 unsupported raw state did not enter recovery lock.");

            if (state.LastLoadStatus
                != FolderDocumentLoadStatus.UnsupportedVersion)
            {
                throw new InvalidOperationException(
                    $"P6.5 unexpected load status {state.LastLoadStatus}.");
            }

            if (!string.Equals(
                    state.SaveRaw(),
                    unreadableRaw,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "P6.5 unreadable raw state was not preserved byte-for-byte.");
            }

            var editRejected = false;
            try
            {
                commands.AddStandardLayer(0);
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains(
                    "editing is blocked",
                    StringComparison.OrdinalIgnoreCase))
            {
                editRejected = true;
            }

            if (!editRejected)
                throw new InvalidOperationException(
                    "P6.5 recovery lock did not reject a structural edit.");

            if (timeline.MaxLayer != initialMaxLayer
                || timeline.LayerSettings.Items.Count
                    != initialSettingsCount)
            {
                throw new InvalidOperationException(
                    "P6.5 rejected recovery-lock edit changed native Timeline state.");
            }

            if (!string.Equals(
                    state.SaveRaw(),
                    unreadableRaw,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "P6.5 rejected edit changed preserved raw state.");
            }

            state.ResetDocument();
            await Task.Delay(300);

            if (state.IsRecoveryBlocked
                || state.SaveRaw() is not null)
            {
                throw new InvalidOperationException(
                    "P6.5 explicit reset did not clear recovery lock/raw state.");
            }

            var key = timeline.ID.ToString("D");
            var folderId = Guid.Parse(
                "65000000-0000-0000-0000-000000000001");

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
                                            Start = 0,
                                            End = 0,
                                            Name = "Before",
                                            IsCollapsed = false
                                        }
                                    ]
                                }
                            ]
                        })));

            // Isolate direct fixture setup from the measured metadata history.
            undo.Record();
            await Task.Delay(250);

            commands.Rename(
                folderId,
                "After");
            await Task.Delay(300);

            string FolderName() =>
                FolderDocumentRules.FindTimeline(
                    state.Document,
                    key)
                ?.Folders.Single(folder =>
                    folder.Id == folderId)
                .Name
                ?? throw new InvalidOperationException(
                    "P6.5 fixture folder missing.");

            if (!string.Equals(
                    FolderName(),
                    "After",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "P6.5 valid rename did not apply.");
            }

            var afterRenameState =
                FolderProductStateCodec.Save(
                    state.ProductState);
            var beforeInvalidMaxLayer =
                timeline.MaxLayer;
            var beforeInvalidSettingsCount =
                timeline.LayerSettings.Items.Count;

            var invalidRejected = false;
            try
            {
                commands.DeleteLayers([-1]);
            }
            catch (ArgumentOutOfRangeException)
            {
                invalidRejected = true;
            }

            if (!invalidRejected)
                throw new InvalidOperationException(
                    "P6.5 invalid structural precondition was not rejected.");

            await Task.Delay(200);

            if (!string.Equals(
                    FolderProductStateCodec.Save(
                        state.ProductState),
                    afterRenameState,
                    StringComparison.Ordinal)
                || timeline.MaxLayer
                    != beforeInvalidMaxLayer
                || timeline.LayerSettings.Items.Count
                    != beforeInvalidSettingsCount)
            {
                throw new InvalidOperationException(
                    "P6.5 rejected invalid precondition changed state.");
            }

            // If the rejected operation inserted a history boundary, this Undo
            // would not land on the preceding valid rename.
            ExecuteHostCommand(
                CommandType.Undo,
                null);
            await Task.Delay(350);

            if (!string.Equals(
                    FolderName(),
                    "Before",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "P6.5 rejected precondition polluted native history.");
            }

            ExecuteHostCommand(
                CommandType.Redo,
                null);
            await Task.Delay(350);

            if (!string.Equals(
                    FolderName(),
                    "After",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "P6.5 rename Redo failed after rejected precondition.");
            }

            display.ThrowIfFailed();

            if (display.Reentries != 0)
                throw new InvalidOperationException(
                    $"P6.5 display reentries={display.Reentries}.");

            WriteP6FailureRecoveryResult(
                string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        "PASS_P6_FAILURE_RECOVERY",
                        "unsupported_raw_locked=true",
                        "raw_preserved_exact=true",
                        "locked_structural_edit_rejected=true",
                        "locked_edit_native_unchanged=true",
                        "explicit_reset_recovers=true",
                        "invalid_precondition_rejected=true",
                        "invalid_precondition_state_unchanged=true",
                        "rejected_precondition_history_clean=true",
                        "rename_undo_redo=true",
                        $"display_reentries={display.Reentries}",
                        $"display_failure={(display.Failure is null ? "none" : display.Failure.GetType().Name)}",
                        "no_harmony=true"
                    })
                + Environment.NewLine);
        }
        catch (Exception ex)
        {
            HandsOnRuntime.Diagnostic(
                "p6_failure_recovery_error=" + ex);

            WriteP6FailureRecoveryResult(
                "FAIL_P6_FAILURE_RECOVERY\n"
                + ex
                + "\n");
        }
    }

    private static void WriteP6FailureRecoveryResult(
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
                "p6-failure-recovery-result.txt"),
            text);
    }
}
