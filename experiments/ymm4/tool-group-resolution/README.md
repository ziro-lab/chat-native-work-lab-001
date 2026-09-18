# YMM4 Tool Group Resolution — Round 2 / L1

## Goal

Determine which `DefaultGroupName` value makes a Tool Plugin join YMM4's existing Utilities / ユーティリティ group instead of creating a separate group.

## Environment

- GitHub-hosted Windows runner
- real YMM4 4.55.1.1 Lite
- .NET 10
- pinned YMM4 ZIP SHA256: `125860147cc33b831fc1a6d6ea996958001c2ead3b0d37f7d900251d5617db9b`

## Method

Install three synthetic Tool Plugins whose only relevant difference is `DefaultGroupName`:

- `Utilities`
- `ユーティリティ`
- empty/default

The real YMM4 Tool menu tree is then observed in-process. The probe records the parent menu path for each synthetic tool, the current YMM4 culture, the public `IToolPlugin` surface and group-related members/constants visible in the pinned assemblies.

## PASS boundary

A PASS means the real pinned host loaded the probe tools and their actual Tool menu parent paths were observed. It does not claim the result for other YMM4 versions or locales.
