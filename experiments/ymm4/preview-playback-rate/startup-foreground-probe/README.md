# YMM4 preview startup foreground probe

Purpose: reproduce the real YMM4 shutdown/restart lifecycle with a persisted preview
PlaybackRate above the native x8 selector range, then test whether a one-shot
`DispatcherPriority.ApplicationIdle` binding refresh + local Foreground re-apply
remains stable after startup settles.

This is a host-lifecycle probe, not the downstream product plugin.

## Acceptance

On exact YMM4 Lite v4.56.1.0:

1. Seed `PlaybackRate=63` (x16) and close through YMM4's normal main-window path.
2. Relaunch a fresh YMM4 process and confirm the setting persisted.
3. Extend the native selector so index 63 is representable, then refresh the binding.
4. Re-apply a local high-speed marker brush once at ApplicationIdle.
5. After an additional settle interval, the selector must still be index 63 and the
   local marker brush must still be owned by the selector.
6. Restore the original PlaybackRate before exit.

The result distinguishes a YMM4-host late overwrite from downstream plugin ordering.


## Result — PASS

Validated on exact YMM4 Lite v4.56.1.0.

Workflow run: `36298471754`  
Artifact: `ymm4-preview-startup-foreground` / ID `10924457486`  
Artifact SHA256: `87c24775c2e97b924dc19ae4e742226c9cf128ba693855531e13cac0067b49ef`

Observed restart lifecycle:

```text
loaded_setting=63
persisted_x16=true
native_item_count=32
native_selected_index=-1
native_foreground=Solid:#FFFF0000:1
selected_after_extend=63
foreground_after_extend=Solid:#FFFF0000:1
selected_at_settled_reapply=63
final_selected_index=63
final_marker_owned=true
final_foreground=LinearGradient:stops=2:opacity=1
```

The persisted high-speed value survives normal shutdown/restart. A fresh native selector
cannot initially represent index 63, but after the selector is extended and its binding is
refreshed it resolves to 63. A local high-speed Foreground re-applied once at
`DispatcherPriority.ApplicationIdle` remained owned after the additional settle interval.

For downstream code this supports a bounded startup/recovery fix: refresh the native binding
once after startup settles and then re-apply the DOPAGAKI presentation. No polling loop or
permanent timer is required for this host lifecycle.
