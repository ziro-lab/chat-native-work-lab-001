# YMM4 tachie refresh surface

## Question

On YMM4 Lite v4.56.1.0, is there a managed/public surface suggesting a plugin can request a paused tachie preview refresh without changing timeline position, and how are tachie/timeline PropertyChanged events wired internally?

## Evidence type

Static managed-code observation only.

## PASS boundary

The probe inventories tachie/timeline/player PropertyChanged subscription/handler candidates, public refresh/invalidate/render-looking methods, and decoded IL for selected candidate methods.

## NOT PROVEN

Static results alone do not prove that raising a parameter PropertyChanged event repaints a paused preview. A positive-looking path requires one bounded runtime probe before product use.
