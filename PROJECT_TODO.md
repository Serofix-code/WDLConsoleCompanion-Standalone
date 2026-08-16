# WDL Console Companion — working TODO

## Implemented in v7.4 test source

- [x] `Console(...)` game-Lua -> main Event Console bridge.
- [x] Keep the bridge alive when Lua Script Studio is closed.
- [x] `lua` / `luastudio` console command.
- [x] Update `help` to include current console commands, teleport movement, and Lua calls.
- [x] Add `00b_Console_Event_Log_Test.lua`.
- [x] Add read-only `00c_Progression_API_Probe.lua`.
- [x] Document all currently discovered progression/passive-related game Lua globals.

## Next after the probe results

- [ ] Confirm `GetProgressionTagId` accepted input types.
- [ ] Confirm `HasProgressionTag` argument order / player requirement.
- [ ] Map `GetProgressionUpgradeLevel`.
- [ ] Map `RegisterCharacterAbilityMonitorEvent` callback signature.
- [ ] Merge stored progression perks + Character Deck + live progression-tag state into one active-traits view.
- [ ] Investigate professional-uniform entitlement separately from packed Appearance Outfit.
- [ ] Test `HasAccessUniformEquipped` and then map the non-destructive uniform UI controls.
- [ ] Keep adding every confirmed Companion-defined Lua helper to `help` and `docs/LUA_CUSTOM_CALLS.md`.

## GitHub

- [ ] Push v7.4 source/docs to `Serofix-code/WDLConsoleCompanion-Standalone`.
  The ChatGPT GitHub connector has repeatedly returned `403 Resource not accessible by integration`
  on repository-content writes, so source changes are also packaged locally until GitHub accepts a write.
