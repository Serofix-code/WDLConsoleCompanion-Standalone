# Contributing

Thank you for improving the Watch Dogs: Legion community research reference.

## Ground rules

- Research single-player/offline builds only. Do not submit multiplayer abuse or anti-cheat bypasses.
- Never upload Ubisoft binaries, proprietary source, copyrighted game assets, save files, credentials, personal data, or unredacted crash dumps.
- Never upgrade confidence merely because a result looks plausible.
- Record unknown values as `unknown`; do not guess signatures, return types, or ownership rules.
- Identify the tested distribution, renderer/module, date, and cryptographic module hash when possible.

## Discovery workflow

1. Add or update a record under `database/records/` using the schema in `database/schemas/research-record.schema.json`.
2. Put reproducible, non-proprietary notes under `research/notes/` when a record needs more context.
3. Include evidence references and related record IDs.
4. Run:

   ```powershell
   python tools/validate_database.py
   python tools/check_duplicate_symbols.py
   python tools/check_broken_references.py
   python tools/generate_docs.py --check
   dotnet build .\WDLConsoleCompanion.sln -c Release
   dotnet run --project .\tests\WDLConsoleCompanion.SmokeTests
   ```

5. Explain what was observed, what remains unknown, and whether a game process was modified.

## Confidence changes

A promotion to `confirmed` needs a named build and reproducible evidence. A single string reference or one successful run is normally `inferred`. Conflicting evidence should lower confidence until resolved.

## Corrections

When reporting an incorrect discovery, provide the record ID, build, module hash if available, reproduction steps, and actual behavior. Corrections do not need a replacement theory.
