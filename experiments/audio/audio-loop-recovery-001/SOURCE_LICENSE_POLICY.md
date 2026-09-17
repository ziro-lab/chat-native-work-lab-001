# Source audio license policy

This file defines the **project intake policy** for future loop-benchmark source audio. It is deliberately stricter than merely asking whether a file can be downloaded.

## Required metadata

Every external source entry must record at least:

- source URL / canonical identifier;
- author or publisher when available;
- exact license identifier or license text reference;
- whether modification / derivative generation is permitted;
- whether commercial use is permitted;
- whether redistribution of the original is permitted;
- whether redistribution of generated derivatives is permitted;
- attribution requirements;
- a SHA256 digest for the exact downloaded source when practical.

## Default allow policy

Prefer sources that clearly permit the benchmark transformations used here.

Preferred classes:

- repository-owned / intentionally contributed fixtures;
- CC0 / public-domain material where provenance is clear;
- permissively licensed material that explicitly allows modification and the intended use;
- CC BY material only when attribution and derivative obligations can be tracked correctly.

## Default exclusions

Do not ingest sources into the public benchmark when the applicable terms are unclear or incompatible with the planned transformations.

Examples that are excluded by default:

- `ND` / no-derivatives terms;
- `NC` / non-commercial terms (the benchmark should not create an avoidable downstream commercial-use restriction);
- unclear or missing license terms;
- paid/private assets without explicit redistribution and derivative rights;
- material where the benchmark cannot satisfy required attribution or notice terms.

## Repository vs runtime-only sources

A source can be useful for evaluation without being committed to Git.

- **Redistribution-safe** sources may be stored only when doing so is useful and their terms are recorded.
- **Runtime-only** sources must be fetched from their canonical source and verified by digest/version when practical; the repository stores metadata and acquisition instructions, not the audio bytes.

Generated artifacts must not be blanket-relicensed in a way that contradicts the source license. Repository-authored code and fully synthetic fixtures follow the repository's Apache-2.0 policy unless otherwise noted.

This policy is an engineering guardrail for the lab; it is not a substitute for reviewing the actual terms attached to each source.
