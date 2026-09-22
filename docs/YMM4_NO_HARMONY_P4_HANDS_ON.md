# YMM4 no-Harmony Full — P4 Hands-on Task Script v0.1

Status: **manual P4 exit track — NEXT**.

Automation already owns mechanism correctness:

- P4.1 UI surface: 19/19 PASS on YMM4 4.56.1.0;
- P4.2 pure UX policy: 27/27 PASS;
- P4.3 minimum integrated workflow: 24/24 PASS on YMM4 4.56.1.0.

This script must **not** re-prove those assertions manually. It evaluates discoverability, comfort, wording and perceptual continuity.

## Test setup

Use a disposable YMM4 project with at least 8 ordinary layers and a few visible timeline items.

Do not use Lab state injection during the hands-on run.

Record:

- exact YMM4 version;
- plugin candidate/source version;
- task result;
- anything surprising or ambiguous;
- whether the issue is correctness, discoverability, wording, or visual comfort.

## H1 — find and create a folder

Goal: can a user discover the feature from the normal Timeline?

1. Select a contiguous range of at least 2 layers using ordinary YMM4 interaction.
2. Right-click a selected/target layer in the layer-name column.
3. Find the folder action without opening a separate Tool panel.
4. Confirm the menu shows the exact range that will be grouped.
5. Create a folder and enter a short name.

Observe:

- Was the folder action where you expected it?
- Was the range obvious before committing?
- Was the naming prompt lightweight?
- Did anything imply that items/layers would be deleted or moved when it would not?

Pass direction:

- no Lab-only controls;
- no mandatory Tool panel;
- exact range visible before creation;
- cancel means no state change.

## H2 — collapse / expand

1. Click the visible folder control.
2. Confirm the folder collapses to one visible owner row.
3. Click again to expand.
4. Repeat several times.
5. Scroll before and after toggling.

Observe:

- Is the chevron/marker obviously clickable?
- Does the folder name remain understandable in the narrow layer column?
- Is any jump/flicker distracting?
- Can the user still distinguish a folder row from an ordinary layer row?

Pass direction:

- one obvious click;
- no ambiguous full-row click target;
- outside the folder control, normal Timeline interaction still feels native.

## H3 — right-click while folded

1. Collapse a folder.
2. Right-click a visible layer row below the collapsed range.
3. Confirm the expected native YMM4 ContextMenu still appears.
4. Confirm the folder submenu/action appears without replacing ordinary YMM4 commands.

Observe:

- menu placement;
- whether standard menu items feel intact;
- whether the folder item is too prominent or too hidden.

## H4 — rename

1. Rename the folder from its folder menu.
2. Try a short name.
3. Try cancelling once.
4. Try entering only whitespace.

Pass direction:

- cancel changes nothing;
- whitespace does not create a blank/invisible identity;
- rename is easy to find but not easy to trigger accidentally.

## H5 — ungroup / remove folder metadata

1. Use the action meaning “remove the folder but keep layers/items”.
2. Read the wording before clicking.
3. Confirm all YMM4 layers/items remain.
4. Confirm folded display returns to ordinary rows.

Critical wording check:

- prefer **フォルダを解除（レイヤーは残す）** or equally explicit wording;
- do not label this merely “削除” if it only removes metadata.

## H6 — nested folder discoverability

1. Create a parent folder over a wider range.
2. Keep it expanded.
3. Select a valid contiguous sub-range inside it.
4. Create a child folder.
5. Collapse/expand child and parent in different orders.

Observe:

- can you tell which folder owns which rows?
- does indentation/marker placement make nesting understandable?
- when the parent is collapsed, does the child disappear in a way that feels expected?

This task may change marker styling/indentation, but should not change the frozen range model unless a real semantic problem is found.

## H7 — invalid / crossing creation

With an existing folder, attempt a selection that would partially cross its boundary.

Pass direction:

- creation is unavailable or clearly rejected;
- the user is not left wondering whether the click failed;
- no existing folder state changes.

Exact wording is a P4 decision. The range validation itself is already automated.

## H8 — save / reopen

1. Create and rename a folder.
2. Leave it collapsed.
3. Save the YMM4 project.
4. Reopen it.
5. Confirm name, range and collapse state.
6. Expand and collapse once after reopen.

This is a perceptual/use-flow check only. P3 owns persistence correctness.

## H9 — project switch / new project sanity

1. Switch between two projects with different folder layouts.
2. Create a new project.

Pass direction:

- no visible folder state leaks between projects;
- new project starts without inherited folder UI.

P3 owns the underlying lifecycle proof.

## H10 — unsupported / recovery wording

If a recoverable stale/unsupported state can be staged safely, inspect only the user-facing message.

Pass direction:

- Timeline remains usable;
- message says what happened and what data was preserved;
- no instruction implies that the project itself is corrupt when only folder metadata is unavailable.

## Manual exit notes

At P4 freeze, summarize findings under four headings:

1. **Correctness surprises** — reopen the relevant technical gate only if necessary.
2. **Discoverability** — placement, labels, initial affordance.
3. **Comfort** — click target, flicker, menu depth, prompt friction.
4. **Wording** — creation range, rename, ungroup, recovery.

Do not turn cosmetic preferences into new native regression suites.

## Current automation boundary

Already green, so manual testing should not spend time counting these again:

- input-transparent Adorner attach/detach;
- folded layer-label display-Y -> logical-Layer right-click mapping;
- current realized ContextMenu reacquisition after virtualization;
- ContextMenu extension/cleanup;
- FolderDocument create/rename/toggle/ungroup invariants;
- item identity/layer preservation during ungroup;
- serialization after UX commands;
- no Harmony.
