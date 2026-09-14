# YMM4 ItemTemplate Identity — Experiment 005

## Goal

Find what a plugin-owned Library can safely store to re-identify a live YMM4 `ItemTemplate` without inventing a second template database.

This discovery asks:

- Does `ItemTemplate` expose a public ID / Guid / key-like value?
- Can two live Templates have the same Name?
- Does a constructor Guid map to an observable public identity field?
- Does `ItemSettings` expose an obvious public persistence/save surface?

The first phase does **not** claim restart persistence. If a plausible public identity exists, a follow-up phase should restart the same temporary YMM4 host and verify it survives serialization.

## Product boundary

Regardless of the outcome, Template Placer will not fuzzy-match broken references. A missing or ambiguous source becomes a visible broken Library entry requiring manual relink/removal.

## NOT PROVEN in discovery phase

- identity survives YMM4 restart
- identity survives Template rename
- compatibility with other YMM4 versions
