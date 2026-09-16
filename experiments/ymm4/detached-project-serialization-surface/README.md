# Detached Project serialization surface

## Question

After `MainModel.LoadProjectFile(path)` returns a detached `YukkuriMovieMaker.Project.Project`, does YMM4 v4.56.1.0 expose a native save/serialize/write route that can persist that detached object without switching or mutating the live project?

## Method

A disposable project is saved and reloaded as a detached `Project`. The probe inspects:

- the detached Project public/non-public members;
- methods across loaded YMM4 assemblies whose names suggest save/write/serialize/json and whose parameters or return type involve `Project`;
- serializer/project-file candidate types.

## PASS boundary

A discovery PASS means at least one concrete native serialization candidate has been identified for a follow-up roundtrip proof.

## NOT PROVEN

Discovery alone does not prove the candidate writes a valid `.ymmp` or preserves unknown/plugin-owned data. A separate mutation roundtrip must prove that before product adoption.
