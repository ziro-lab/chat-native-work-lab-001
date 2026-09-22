# P2 navigation V2 secondary-host trigger

This file intentionally triggers the one-shot secondary-host acceptance workflow for the current P2 navigation candidate.

Primary-host runtime evidence:
- YMM4 4.56.1.0 Lite
- source runtime commit: `0d2fecfd1d73a79c17a40dc9f79b647ef662faaa`
- run: `35704217666`
- artifact: `10684021588`

The secondary gate checks the same runtime bits on YMM4 4.55.1.1 Lite without replaying P0/P1 golden suites.
