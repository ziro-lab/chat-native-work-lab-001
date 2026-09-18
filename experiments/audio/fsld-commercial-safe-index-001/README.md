# FSLD commercial-safe index 001

## Goal

Use the Freesound Loop Dataset as a larger Music Fit training/evaluation source **without downloading the 8.8 GB archive on every run** and without allowing restricted Creative Commons audio into the product-safe corpus.

The official Zenodo record states that FSLD contains 9,455 loops and that each sound's Creative Commons license is stored in `FSL10K/metadata.json`.

This experiment:

1. opens the remote ZIP with HTTP Range requests;
2. reads the ZIP central directory only;
3. extracts only `FSL10K/metadata.json`;
4. applies the strict gate from `commercial-safe-corpus-001`;
5. writes a runtime-only index of accepted CC0 / CC BY items.

No WAV member is fetched in this experiment.

## Safety policy

Accepted:

- CC0 1.0
- CC BY 2.0 / 2.5 / 3.0 / 4.0 with creator attribution metadata

Rejected:

- NC / ND
- SA in v1
- unknown/custom licenses
- attribution-required items without creator metadata

The filter is intentionally conservative. Passing the gate means "eligible for the next controlled experiment", not blanket legal advice.

## Why ranged ZIP access

The FSL10K archive is large, while the metadata needed for license selection is small. The reader rejects servers that ignore `Range`; it never silently falls back to a full archive download.

Future experiments can reuse the same reader to extract only a deterministic sample of accepted `FSL10K/audio/<id>.wav` members.

## PASS boundary

A green run proves:

- the official remote archive exposes a valid ZIP central directory;
- `metadata.json` is extracted without reading the whole archive;
- more than 9,000 sound records are license-gated;
- at least one CC0/CC-BY sound is accepted;
- fetched network bytes remain below a fixed metadata-only budget;
- no audio member is read or uploaded.

It does not yet train or modify Music Fit scoring.
