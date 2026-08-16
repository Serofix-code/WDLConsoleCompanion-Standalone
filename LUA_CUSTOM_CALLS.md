# WDL Console Companion — Lua Script Studio API

## Companion-defined Lua calls

### `Console(...)`

Writes one line from Watch Dogs: Legion's Lua VM into the WDL Console Companion **Event Console**.

```lua
Console("hello")
Console("player", GetLocalPlayerEntityId())
Console("value", 123, "enabled", true)
```

Arguments are converted with Lua `tostring()` and separated with tabs. Embedded newlines are escaped so
one `Console()` invocation produces one Event Console entry.

The Companion intentionally **does not override the game's `print()` function**.

At v7.4, `Console(...)` is the only Companion-defined game-side Lua global.

## Discovered Watch Dogs: Legion Lua globals

These were found by `WDL_Perk_Discovery_Exporter_v0.1.lua`. They are **game functions**, not Companion
helpers, and most signatures are still being mapped:

- `GetProgressionTagId`
- `HasProgressionTag`
- `GetProgressionUpgradeLevel`
- `ResetToProgressionState`
- `RegisterCharacterAbilityMonitorEvent`
- `UnregisterCharacterAbilityMonitorEvent`
- `SetTraitTracked`
- `SelectAbility`
- `EquipAccessUniform`
- `HasAccessUniformEquipped`
- `IsAccessCodeAcquired`
- `WaitForProgressionLayerToLoad`
- `WaitForProgressionLayerToLoadUnregister`
- `GetLayerNameFromProgressionLayerItem`
- `AddHackCategoryAvailabilityOverride`
- `RemoveHackCategoryAvailabilityOverride`
- `AddHackingAvailabilityOverride`
- `RemoveHackingAvailabilityOverride`
- `ShowGadgetUIElement`
- `HideGadgetUIElement`

Do not treat the above names as having known parameters until a probe confirms them.

## Included probe

`00c_Progression_API_Probe.lua` is read-only. It prints function availability to `Console(...)`, then
tries guarded `pcall` queries for:

- `GetProgressionTagId("Uniform_AlbionCaptainElite")`
- `HasProgressionTag(player, id)` and, if that signature errors, `HasProgressionTag(id)`
- `HasAccessUniformEquipped(GetLocalPlayerEntityId())`

The probe does **not** call `ResetToProgressionState`, `SetTraitTracked`, or the ability-monitor
registration functions.
