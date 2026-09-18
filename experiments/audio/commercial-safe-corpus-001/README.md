# Commercial-safe Music Fit Corpus 001

## Goal

Build a **commercial-use-safe intake layer** for improving `music-fit-core-001` without turning the eventual YMM4 plugin into a research-only product.

This experiment separates two resources:

1. **audio corpus** — only audio whose per-item terms clearly allow commercial use and modification;
2. **annotation-only corpus** — permissively licensed structure/beat annotations whose underlying copyrighted audio is **not** copied, downloaded or used here.

The policy is deliberately narrower than what copyright licenses may technically permit. Ambiguous material is rejected rather than interpreted optimistically.

## Audio allowlist

Accepted automatically:

- CC0 1.0
- CC BY 2.0 / 2.5 / 3.0 / 4.0

Rejected automatically:

- CC BY-NC / any non-commercial restriction
- CC BY-ND / no-derivatives
- CC BY-SA (commercially usable, but excluded from v1 to avoid downstream share-alike ambiguity)
- unknown/custom/research-only terms
- records without enough attribution metadata when attribution is required

Existing Colorosse fixtures remain valid runtime-only sources: Arcade/Vellum are CC0; Anvil/Prism/Rust/Timber are CC BY 4.0.

## Freesound Loop Dataset

FSLD contains 9,455 loops and records each sound's Creative Commons license in `FSL10K/metadata.json`. The full archive is 8.8 GB, so CI does **not** download it on every run.

`filter_fsld_metadata.py` is the canonical intake gate. Once `metadata.json` is supplied, it emits:

- accepted CC0/CC-BY records;
- rejected records plus explicit reason;
- attribution fields;
- stable source IDs;
- a license distribution report.

Audio bytes remain external/runtime-only. This repository never blanket-relicenses FSLD audio.

## Annotation-only sources

Pinned, annotation-only resources:

- SALAMI data commit `8e4f95d18a3ab628c53011fa5a43e9d3be27965d` — annotations/metadata CC0.
- Harmonix Set commit `64abeb509429e73d74559fb98e621dac866efea1` — repository annotation data under MIT.

For Harmonix, original commercial audio and downloadable mel spectrograms are outside this experiment. For SALAMI, matching source audio is also outside this experiment.

`build_annotation_priors.py` turns only the text annotations into aggregate structural priors useful for:

- phrase/section boundary penalties;
- common section-duration ranges;
- functional transition priors;
- ending-route priors such as `chorus -> outro -> end`.

These are **priors**, not proof that a specific audio jump is natural.

## Explicit v1 exclusions

- FMA audio: per-track licenses exist, but the dataset documentation also describes the dataset as intended for research; excluded from the production-safe pool for now.
- MUSDB18 / MTG-Jamendo and other research/non-commercial datasets.
- copyrighted Harmonix/SALAMI source audio.
- any Creative Commons NC/ND content.
- CC BY-SA until downstream obligations are reviewed separately.

## PASS boundary

A green run proves:

- the license gate admits only the v1 allowlist and rejects NC/ND/SA/unknown cases;
- CC BY records without attribution metadata are rejected;
- pinned SALAMI and Harmonix repositories can be checked out;
- aggregate structure priors can be produced from annotation text only;
- no third-party audio is committed or uploaded.

It does **not** prove Music Fit perceptual accuracy improved. Adoption into the scorer is a later A/B experiment.
