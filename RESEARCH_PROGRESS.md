# Research progress

Last updated: 2026-08-14.

## Confirmed on the observed Steam PC DX11 session

- Process/module discovery and guarded signature scanning.
- Operative-team-manager capture hook with expected-byte validation and cleanup.
- Roster count, operative array, and operative ID traversal for the observed build.
- Census-backed localization ID resolution.
- Read-only catalog search and game-independent database tooling.

## Strongly inferred

- Several operative metadata and appearance field meanings reproduced across multiple records.
- Game-thread action queue behavior for selected Lua actions.
- Teleport/player-transform relationships for the observed session.

## Inferred

- Many readable event, perk, appearance, clothing, and metadata identifier descriptions.
- Camera candidates produced by differential scans.

## Unknown / unresolved

- True freecam transform, camera owner, and safe reversible write contract.
- Recruit-any-NPC and potential-recruit insertion ownership semantics.
- Safe contract participant mutation.
- Full save format, integrity checks, and cross-save operative identity dependencies.
- Complete Lua registration table and native calling conventions.
- Exact hashes for every supported store/renderer build.

## Next systematic passes

1. Capture exact module hashes and map all signatures by build.
2. Inspect registration mechanisms and Lua binding tables.
3. Map event dispatch and UI factory call sites.
4. Group camera candidates into adjacent transforms and validate reversible read-only correlations before writing.
5. Cross-reference unknown symbols against types, strings, and call sites.
6. Re-run database validation and regenerate documentation after every accepted discovery.

Guiding question: **What systems have we not mapped yet?**
