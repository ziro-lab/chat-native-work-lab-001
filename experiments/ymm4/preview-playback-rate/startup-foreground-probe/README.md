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
