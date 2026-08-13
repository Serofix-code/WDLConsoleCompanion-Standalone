# v0.1.0 — Initial Research Release (planned)

This is a planned milestone, not a claim of complete game coverage.

## Validated research-record counts

- Functions: 0
- Events: 0 evidence-bearing records; 12,324 entries in the inferred runtime event catalog
- Commands: 0 evidence-bearing records
- UI symbols: 0
- Lua bindings: 0
- Types: 0
- Systems: 3
- Signatures: 1
- Total evidence-bearing records: 4

## Additional indexed catalogs

- 373 perk entries
- 15 packed appearance fields
- 24 advanced metadata categories
- 34 clothing shop archetypes and 779 clothing reward records

## Supported build evidence

Observed on the Steam PC DX11 module `DuniaDemo_clang_64_dx11.dll` during August 2026. Exact module SHA-256 is still unknown, so compatibility with other distributions/renderers/builds is not claimed.

## Major discoveries

- Guarded operative-manager capture signature.
- Roster count, pointer array, and operative ID offsets for the observed build.
- Census-backed name localization resolution.
- Searchable runtime catalogs and game-independent validation tools.

## Known unknowns

- True freecam camera transform and safe write contract.
- Recruitment insertion and ownership calls.
- Raw save format and safe cross-save operative transfer.
- Complete Lua, command, event-dispatch, UI-factory, reflection, and native-type registries.

Before release, choose an open-source license, record exact module hashes, and review catalog provenance/redistributability.
