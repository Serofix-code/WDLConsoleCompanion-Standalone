# Watch Dogs: Legion Community SDK

An unofficial, community-maintained collection of independently created research, machine-readable databases, examples, and Windows tooling for modding the single-player/offline PC version of *Watch Dogs: Legion*.

This repository is not affiliated with, authorized by, or endorsed by Ubisoft. *Watch Dogs*, *Watch Dogs: Legion*, Ubisoft, and related names are trademarks of their respective owners. No game executables, proprietary source code, copyrighted game assets, encryption keys, account data, or anti-cheat bypasses belong in this repository.

## Project status

| Classification | Meaning |
|---|---|
| **CONFIRMED** | Reproduced on a named game build with direct evidence and validation. |
| **STRONGLY INFERRED** | Multiple independent observations agree, but a complete calling contract or structure is missing. |
| **INFERRED** | A plausible interpretation supported by limited evidence. Do not treat it as stable API. |
| **UNKNOWN** | Unresolved purpose, signature, layout, build compatibility, or behavior. |

These labels describe evidence quality, not safety. A confirmed write can still crash the game or damage a save.

## What is here

- `database/` — schemas, catalog manifest, and curated research records.
- `sdk/` — documentation for reusable C# components and the planned stable SDK boundary.
- `tools/` — standard-library Python validation, duplicate, reference, query, and documentation tools.
- `examples/` — small, game-file-free database and SDK examples.
- `research/` — evidence policy, templates, unresolved questions, and reproducible research notes.
- `docs/` — architecture, confidence, builds, database format, legal rules, and trainer documentation.
- `src/WDLConsoleCompanion/` — an experimental .NET 9/WPF offline companion and research tool.
- `tests/` — game-independent smoke tests. Tests requiring proprietary game files are intentionally excluded.

Currently documented systems include operative roster access, census/name resolution, metadata and recent events, perks, appearance fields, contracts, memory scanning, pattern scanning, reversible patches, teleport research, game-thread Lua actions, clothing/shop identifiers, and ongoing camera research. See [Research progress](RESEARCH_PROGRESS.md) for honest coverage and unknowns.

## Supported builds

Build support is signature-specific. The current runtime recognizes the PC Dunia module names listed in `trainer.json`, but most confirmed live testing has been against the Steam PC DX11 module `DuniaDemo_clang_64_dx11.dll` observed in August 2026. A module name alone is not a build identifier. Exact executable/module hashes are still required before compatibility can be claimed broadly.

Never assume a signature is portable. The runtime verifies expected bytes and refuses ambiguous matches.

## Search the database

```powershell
python tools/query_database.py "recruit NPC"
python tools/query_database.py camera
python tools/validate_database.py
python tools/check_duplicate_symbols.py
python tools/check_broken_references.py
python tools/generate_docs.py --check
```

The query tool searches record IDs, names, summaries, tags, evidence notes, and related symbols. Runtime catalogs such as events and perks are indexed by [database/catalog.json](database/catalog.json).

## Build and run the research companion

Requirements: Windows 10/11 x64, .NET 9 SDK or Visual Studio 2022 with the desktop workload, and the PC game in single-player/offline mode.

```powershell
dotnet build .\WDLConsoleCompanion.sln -c Release -p:Platform=x64
dotnet run --project .\src\WDLConsoleCompanion\WDLConsoleCompanion.csproj
```

Do not attach in multiplayer or an anti-cheat-protected session. Back up saves before experimental writes. See [Trainer and research tool](docs/TRAINER.md).

## Add or correct a discovery

1. Copy [research/templates/discovery.example.json](research/templates/discovery.example.json).
2. Use a stable namespaced ID and one allowed `kind` and `confidence` value.
3. State exact game/module build evidence. Use `unknown` when unavailable.
4. Add reproducible evidence without uploading proprietary files.
5. Link related records by ID.
6. Run all scripts in `tools/` and the game-independent tests.
7. Open a pull request using the research template.

Incorrect discoveries are valuable bug reports. Open the **Incorrect discovery** issue template and include the record ID, tested build, observed result, expected result, and non-proprietary reproduction evidence.

## Known limitations

- This is research software, not a stable Ubisoft SDK.
- Many engine functions have unknown calling conventions, ownership rules, or thread requirements.
- Camera/freecam identification is incomplete.
- Raw save decoding and safe cross-save operative transfer are unresolved.
- Multiplayer/account progression is out of scope.
- Machine-readable runtime catalogs contain identifiers and factual mappings; provenance and confidence need continued auditing.
- Temporary camera CSV diagnostics must be disabled or removed after camera research concludes.

## Repository integrity

Contributions must not include game binaries, extracted textures/audio/models, decompiled proprietary source, personal paths, credentials, save files, crash dumps containing personal data, or bulk raw exports whose redistribution is unclear. Submit hashes, offsets, signatures, independently written descriptions, minimal evidence, and reproducible tooling instead.

No open-source license has been selected yet. Until the maintainer chooses one and adds `LICENSE`, normal copyright rules apply; see [LICENSE_PENDING.md](LICENSE_PENDING.md). This is deliberately not a guessed license.

See [CONTRIBUTING.md](CONTRIBUTING.md), [CHANGELOG.md](CHANGELOG.md), and [RESEARCH_PROGRESS.md](RESEARCH_PROGRESS.md).
