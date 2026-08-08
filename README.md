# WDL Console Companion

Standalone x64 WPF trainer for the single-player/offline mode of Watch Dogs: Legion. It replaces the operative-roster portion of the referenced Cheat Engine table; Cheat Engine is not required at runtime.

## Requirements

- Windows 10/11 x64
- Visual Studio 2022 with the .NET desktop development workload, or the .NET 9 SDK
- Watch Dogs: Legion running without BattlEye, in single-player/offline mode

Do not attach while connected to multiplayer or any anti-cheat protected session.

## Build

Open `WDLConsoleCompanion.sln`, choose **x64**, and build, or run:

```powershell
dotnet build .\WDLConsoleCompanion.sln -c Release -p:Platform=x64
```

The executable is produced under `src\WDLConsoleCompanion\bin\x64\Release\net9.0-windows\`.

## Run

1. Start Watch Dogs: Legion and load single-player.
2. Run `WDLConsoleCompanion.exe` at the same or higher integrity level as the game. The app checks every three seconds, requires the real `WatchDogsLegion.exe` plus a configured Dunia DLL, and waits 12 additional seconds before automatic injection so scanning does not overlap early launcher/game initialization.
3. If automatic injection is delayed or fails, click **Manual Inject** or type `inject`. Both paths scan the DLL, verify the original instruction, allocate a nearby code cave, and install the same guarded capture hook.
4. Type `operative`. The roster panel reads the captured manager and census pointer chains.
5. Edit a first name or surname using an exact value present in `config\localization.json`, select the row, and choose **Save names**.
6. Use **Detach** or close the main window before exiting the game. Cleanup verifies ownership of the hook bytes, restores the original instruction, and frees the remote allocation.

The manager capture occurs when the hooked game routine executes. If the panel says the pointer has not been captured, enter or resume the game world and press **Refresh**.

`op` is a short alias for `operative`. The console also supports reversible CT-derived cheats: `godmode`, `notrace`, `infammo`, `noreload`, `norecoil`, and `fastsearch`. Add `on` or `off`, or omit the state to toggle. Type `cheats` to open the visual green/red status panel, `cheatstatus` for console status, and `help` for the command list. All enabled patches are restored during detach.

The operative window includes an experimental perk editor. It resolves PersistentHuman/NPC data and uses the game's allocator following the CT's progression-item array rules. Existing perk hashes are displayed in a readable grid with names and descriptions instead of as an unexplained number list. The searchable catalog contains 373 IDs assembled from the CT and the supplied English `main_languages` workbook; 160 entries have localized display names and every entry has at least its internal CT name. The editor limits the list to 80, rejects duplicates, displays a prominent crash/save-corruption warning, and requires confirmation for every save. Back up the save first.

**Advanced metadata (HIGH RISK)** resolves the supplied CT's dropdown dictionaries and shows friendly names alongside the raw hashes. It covers animation set, active voice persona, DedSec affinity, working hours, voice profile, character deck, occupation group, occupation, birthplace, personality, ethnicity, aggressor/victim identity, combat alignment, income, immigration status, age range, gender, religion, name and surname filters, sexual orientation, fashion, inactive-NPC voice actor, and social tolerance. The companion checks exact length, revalidates the operative, reads the write back, and attempts rollback on failure. Optional missing pointer chains are disabled per-field rather than breaking the complete metadata panel. A structurally valid but incompatible tag can still crash the game or damage a save.

The gameplay panel lists **Noclip / Fly** in the **SUPER RISKY** category but leaves it unavailable. The supplied CT has no noclip/collision signature, and the local game installation does not contain Legion ScriptHook. The known public implementation calls ScriptHook's `SetLocalPlayerNoclip` API; pretending that an unrelated memory patch is equivalent would be unsafe. The status is deliberately truthful rather than showing a fake ON state.

The operative editor also exposes the CT's 24-byte current and default appearance codes at operative offsets `+0x150` and `+0xF8`. Codes accept spaced or compact hexadecimal text and are rejected unless exactly 24 bytes. After saving an appearance, switch away from and back to that operative so the game rebuilds its rendered model.

Availability and Origin saves are read back immediately and reported as verified. Close and reopen the in-game Team menu after changing Availability because the menu caches roster state.

The remaining CT sections—mask/outfit constructors, wardrobe defaults, personal vehicles, inventory add/remove, and contracts—are not raw operative fields. They depend on internal Lua, tag-assignment, object-construction, or binding routines. They are not enabled in this release because their function-call contracts have not yet been ported and verified.

Operative profile portraits are not stored as a bitmap or resource hash in the supplied table. The game appears to generate the Team-menu portrait dynamically from the 3D operative model, so this release does not pretend that the appearance hash is an image path.

## Updating for a game version

All version-sensitive signatures and offsets are in `config\trainer.json`. Numeric offsets are decimal JSON values. The shipped values correspond to the supplied `WDLOpEditor_r2k_v1.3.4` table:

- operative-manager signature patch at `+0x11`
- original instruction `4C 8B 81 20 02 00 00`
- roster count `+0xE0`, inline pointer array `+0x108`, operative ID `+0x1A0`
- census count `+0x9C`, census array pointer `+0xA0`
- first/surname localization IDs at the resolved name-data object `+0x214` / `+0x32C`

The scanner requires exactly one match and refuses to hook if the expected original bytes differ.

## Safety behavior

- Every pointer and complete read/write range is checked with `VirtualQueryEx` before use.
- Counts are bounded before loops or allocation.
- A roster row is re-identified by index, pointer, and operative ID immediately before a write.
- Hook changes and roster mutations suspend game threads for the shortest practical interval.
- Hook code and captured-pointer storage use separate RX and RW pages; the capture write never targets executable read-only memory.
- Roster removal backs up the pointer array and attempts rollback if any write fails. The final operative cannot be removed.
- Cleanup does not overwrite a hook location changed by another tool.
- If the companion was terminated without cleanup while the game stayed open, a new instance verifies and adopts its exact existing hook instead of scanning forever or stacking another hook.

Roster removal is implemented by compacting the manager's inline pointer array, clearing the final slot, and decrementing the count. This mechanism was inferred from the supplied roster layout because the table does not include an internal remove-operative function. Back up saves before using it.

## Localization catalog

`config\localization.json` contains the loc-ID-to-text maps extracted from the table. Names are game localization values, not arbitrary strings; the game stores 32-bit localization IDs. Duplicate visible names can map to multiple IDs, so ambiguous edits are rejected instead of guessing.
