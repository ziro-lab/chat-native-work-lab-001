# Additive H4 boundary observation

Question: on pinned YMM4 4.55.1.1 Lite, does public PropertyChanging on Timeline or a child Item precede command collection sufficiently to separate a short native expression trial from an unrelated edit? Does native UndoAsync flush an open Record session before applying history, and when does Recorded fire?

Evidence kind: native automated, detached synthetic Timeline and public UndoRedoManager only. Use the existing pinned official host/hash and runner. BoundaryProbeEntry records direct Timeline mutation, direct TextItem text mutation, Timeline-only child-bubbling observation, and Undo/Redo with an unclosed trial. COMPLETE means every observation ran; individual boolean outcomes are evidence, not a blanket support claim.

NOT PROVEN: arbitrary nested plugin effect/animation changes, real input-route ordering, scene replacement, and production coordinator lifecycle. These require downstream acceptance or another narrowly scoped observation before relying on them. No host binaries, user projects, internal Undo collectors, custom history or history clearing are introduced.

The runner now rejects error lines even if an earlier startup status line says PASS, and requires the original P0 final trial marker as well as the completed boundary trace. Record source commit/run/artifact/digest after the run before publishing a downstream conclusion.
