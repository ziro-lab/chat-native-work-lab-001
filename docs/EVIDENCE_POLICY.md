# Evidence Policy

This lab treats evidence as a chain, not a screenshot or a green badge.

## Minimum evidence chain

For a direct branch/push run:

```text
source commit SHA
→ workflow run ID
→ runner / exact host identity
→ assertion output
→ artifact ID
→ artifact digest/hash
```

For pull-request workflows, record the source branch identity separately from the commit/tree actually tested when GitHub checks out a synthetic merge commit:

```text
source HEAD
→ checkout commit / checkout tree
→ workflow run ID
→ runner / exact host identity
→ assertion output
→ artifact ID
→ artifact digest/hash
```

Do not describe a GitHub-generated PR merge commit as though it were the source branch HEAD.

When a compiled artifact is part of the claim, preserve its SHA256. When the host loads a specific binary, record or assert the loaded binary identity when practical.

## Artifact rules

- Do not upload downloaded third-party host distributions as experiment artifacts by default.
- Artifacts should contain only original probe binaries/scripts and evidence needed to reproduce or audit the result.
- Keep evidence compact and text-first where possible.

## Result language

Use:

- `PASS` — all stated assertions passed.
- `FAIL` — an assertion or required experiment condition failed after reaching the relevant assertion boundary.
- `BLOCKED` — the experiment could not reach the assertion boundary, including runner, host-download or external distribution failures.

A workflow job that merely uploaded diagnostics successfully is not a product/experiment PASS.

If a run is BLOCKED by infrastructure before any relevant source behavior is exercised, a same-source retry is valid. Record that the successful retry used the same source identity; do not silently reinterpret the blocked run as a failed product assertion.

## Human verification

If a behavior was inferred through internal API state rather than a literal mouse/keyboard action, state that boundary. Do not turn a programmatic Undo call into a claim that physical Ctrl+Z key delivery was tested, for example.

## UI and layout evidence

For UI claims, distinguish semantic availability from physical discoverability.

A control being logically visible is weaker evidence than proving that its full rendered bounds are inside the tested viewport. When a claim depends on a narrow or constrained layout, prefer an evidence chain such as:

```text
semantic state / IsVisible
→ non-zero rendered size
→ translated control bounds are inside the tested surface
→ screenshot for human audit
```

Screenshots are supporting evidence, not a substitute for machine-checkable state and geometry when those are available.

When testing progressive disclosure, assert the transitions themselves: changing the relevant choice should reveal the needed controls and hide irrelevant controls. Do not treat the existence of all controls in XAML as proof that the user sees the intended state.

If navigation, tabs, Set switching or other organizational UI is claimed to be non-mutating, capture a semantic host-state signature before and after the navigation and assert that it is unchanged.

## Package / install / upgrade evidence

A successful native DLL load does not prove that an installer/archive has safe install or upgrade semantics.

If package layout affects where the host installs a plugin, treat package/install behavior as a separate claim. When practical, verify:

- exact archive entry paths, not only the presence of expected filenames;
- a stable internal install root when upgrades are expected to replace the same plugin location;
- rejection of flat or unintended version-named plugin roots when those would cause side-by-side installs;
- an allowlist of package payload files;
- the hash of the archived plugin binary matches the exact distribution binary that passed native host loading;
- cleanup/migration guidance when earlier package layouts may have installed to a different location.

Do not infer upgrade behavior from a successful first install, and do not infer installer correctness from a direct loose-DLL smoke test.

## Acceptance-manifest validation

A producer-generated `PASS` field should not be the only authority for release evidence.

For high-value acceptance manifests, prefer a separate validation step that checks identity, version, required stages, requirement IDs/results and native failure markers. Where practical, add negative fixtures proving that weakened or stale evidence is rejected, such as missing stages, duplicate IDs, old versions, explicit failures or wrong host identity.

This creates a stronger chain:

```text
producer emits evidence
→ independent consumer validates it
→ negative fixtures prove weakened evidence is rejected
```

## Downstream product evidence

A product may later adopt an experiment result and pass a larger integrated acceptance suite. That is useful downstream feedback, but it does not expand the original experiment's PASS boundary unless the additional evidence is itself brought into this lab under an auditable experiment contract.

Keep different downstream claim layers explicit when relevant:

```text
native host/API behavior
→ product functional integration
→ UI/UX behavior
→ package/install/upgrade behavior
→ human/user acceptance
```

Passing one layer does not automatically prove the next.

## Public safety

Evidence must not contain credentials, private project paths, user data, paid assets, or redistributable copies of third-party applications unless intentionally allowed and documented.
