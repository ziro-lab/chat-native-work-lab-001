# P3.4 — new-project initialization lifecycle

Narrow V1 question: after a saved project contains a non-empty FolderDocument in the host-owned ToolArea state, does public `CreateProject()` produce a fresh Timeline identity and an empty/non-inherited folder state?

This is intentionally separate from P3.2 A/B project-file roundtrip. It does not re-test project reopen, malformed recovery or P1 structural behavior.

Primary pinned host only during iteration: YMM4 4.56.1.0 Lite.
