# WDL Console Companion

Standalone x64 WPF trainer for the single-player/offline mode of Watch Dogs: Legion. It provides roster editing, readable metadata, reversible gameplay options, and teleport tools without requiring another runtime application.

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

`op` is a short alias for `operative`. The console also supports reversible gameplay options: `godmode`, `notrace`, `infammo`, `noreload`, `norecoil`, and `fastsearch`. Add `on` or `off`, or omit the state to toggle. Type `cheats` to open the visual green/red status panel, `cheatstatus` for console status, and `help` for the command list. All enabled patches are restored during detach.

Choose **Shortcuts** or type `shortcuts` to assign unique global F1–F12 keys to gameplay cheats. Bindings are stored in `config\hotkeys.json` and work while the companion is running even when the game has focus. Windows may reject a key already registered by another application; the event console reports that conflict.

The main event console is a read-only selectable text box. **Copy all**, **Clear**, and **Save .txt** are available as buttons, with `copyconsole`, `clear`, and `saveconsole` command equivalents. Roster reads and all supported risky editor operations are timestamped there. Failures include a stable `WDL-*` error code, the exception type, and hexadecimal HRESULT so screenshots and exported logs contain actionable diagnostics.

The redesigned **Operative Studio** keeps the roster compact and opens focused editors for names/status, perks, statistics, recent events and birthplace, structured appearance, advanced metadata, and contacts. Risky controls are colored orange or red and every supported mutation verifies the operative identity immediately before writing.

The experimental perk editor resolves PersistentHuman/NPC data and uses the game's allocator with a verified 80-slot progression-item layout. Existing perk hashes are displayed in a readable grid with names and descriptions instead of as an unexplained number list. The searchable catalog contains 373 IDs assembled from game data and the supplied English `main_languages` workbook; 160 entries have localized display names and every entry has at least its internal name. Duplicate IDs are permitted because some valid rosters already contain them; every selected entry is written in order, up to the verified 80-slot layout. The editor displays a prominent crash/save-corruption warning and requires confirmation for every save. Back up the save first.

**Recent Events & City-Level Birthplace (HIGH RISK)** reads the primary biography metadata and every recent-event slot. Its complete 12,324-entry catalog includes every event category—not only birthplaces—and resolves hashes to readable text. Search by internal name (for example `NoParent09`), description (`deaf parents`), city, country, or numeric ID; optionally restrict the results to birthplaces. Choosing a replacement automatically selects its exact grid row, and Save also detects the sole changed row if WPF focus moves elsewhere.

The Recent Events search controls are initialization-safe: their XAML change events are ignored until the complete catalog and status controls exist, and editor-construction errors are reported in the console instead of terminating the companion.

**Statistics (HIGH RISK)** edits and verifies age, income, and NPC status (available, dead, injured, arrested, or pending deportation). Its **All demographic statistics** tab contains all 24 known demographic/career values for complete hash-resolved editing.

Statistics now contains those 24 named demographic/career values directly in a second tab. Each row supports readable known-value selection or an exact raw hash, and saves only the selected statistic with identity revalidation, immediate read-back, and rollback on failure.

Statistics also displays both birthplace sources used by the game: the country/region tag from advanced career metadata and the separate primary/public biography event list. Every stored `BIRTH_*` event is resolved to its complete `Born in city, country` text; when an operative has no city event, the UI says so instead of substituting the country tag.

**Structured Appearance (HIGH RISK)** decodes the game's 24-byte packed format into friendly selectors for model, clothing layers, headwear, gloves, footwear, ears, bag, outfit, hair, hair colour, tattoos, and makeup. It can target current appearance (`+0x150`) or wardrobe defaults (`+0xF8`), and refuses to write unless the data is unpacked format version 12/type 2. Its 15 field catalogs contain 32,598 named choices. Switch to the operative and then away before editing if the guard reports stale packed data.

**Advanced metadata (HIGH RISK)** resolves known dropdown dictionaries and shows friendly names alongside the raw hashes. It covers animation set, active voice persona, DedSec affinity, working hours, voice profile, character deck, occupation group, occupation, country-level birthplace tag, personality, ethnicity, aggressor/victim identity, combat alignment, income, immigration status, age range, gender, religion, name and surname filters, sexual orientation, fashion, inactive-NPC voice actor, and social tolerance. The companion checks exact length, revalidates the operative, reads the write back, and attempts rollback on failure. Optional missing pointer chains are disabled per-field rather than breaking the complete metadata panel. A structurally valid but incompatible tag can still crash the game or damage a save.

**Contacts and Contracts** safely reads the contract graph and resolves contract types and both participants against the census. The separate **SUPER RISKY** attendance editor can change verified start hour, end hour, and priority fields with identity checks, read-back verification, and rollback. Contract types and participants remain read-only because replacements require engine-owned bind/unbind routines.

The gameplay panel includes live-DX11-verified options for **Instant Hacker Cooldowns**, **Freeze Hack Timer**, **Maximum Drone Range**, **Infinite Drone Health**, and **One Hit Kill**. They are all marked **SUPER RISKY**. It also contains teleport tools with current/waypoint coordinates, save/load position, manual XYZ, movement-forward teleport, and undo. Every teleport asks for confirmation.

Features that are visible but not executable are labeled **UNDER DEVELOPMENT**. Their Details dialog explains that they are actively being worked on and will remain disabled until the required game functions and object layouts are validated.

Tool and operative-editor windows are non-modal, so Teleport, Events, Appearance, Perks, Statistics, Contracts, metadata, schedules, and shortcut settings can remain open while the main and parent windows are still usable. Teleport can also be opened with the `teleport` console command. If a previous companion instance exited without cleanup, the teleport installer validates and adopts recognized old hooks, then owns their normal restoration on detach/exit. Unrecognized modified code is never overwritten.

Only one shared Teleport window and hook set can exist per attached session, even if the button is clicked repeatedly. After first opening Teleport, switch to another operative and back once so the game publishes the active-player pointer, then press **Refresh**. Ordinary walking does not trigger that player-identification routine. Waiting for this pointer is shown as status guidance rather than repeated console errors.

**Noclip / Fly** remains listed but unavailable. No verified collision/physics signature exists in the local build, and the local game installation does not contain Legion ScriptHook. The known public implementation calls ScriptHook's `SetLocalPlayerNoclip` API; pretending that an unrelated memory patch is equivalent would be unsafe. The status is deliberately truthful rather than showing a fake ON state.

The supplied **No Reload** patch is explicitly scoped to `DuniaDemo_clang_64_dx12_plus.dll`. It is disabled with a clear compatibility message when attached to the DX11 engine because a read-only scan confirms the patch bytes do not exist there. Infinite Ammo remains independent.

No Reload candidates live in `config\cheats.json`, so a verified DX11 signature can be added later without recompiling. Each candidate is scoped to an exact module name and includes pattern, patch offset, expected original bytes, and replacement bytes.

Availability and Origin saves are read back immediately and reported as verified. Close and reopen the in-game Team menu after changing Availability because the menu caches roster state.

The remaining requests—potential-recruit insertion, recruit-any-NPC, whole-operative cross-save copy/paste, a raw save-file editor, unlock-all-clothing, infinite mind-control range, and fly—are not enabled in this release. Safe native signatures and calling contracts are not available for them. Public ScriptHook trainers perform noclip through ScriptHook's game-thread API, while recruitment is exposed as Lua/game-engine functions; calling either through the current generic remote-call stub would risk a crash or corrupt roster/save ownership. The WDL save codec is also not available, so the program will not pretend an encrypted/unknown save is a writable roster file.

Operative profile portraits are not stored as a directly readable bitmap or resource hash in the known operative data. The game appears to generate the Team-menu portrait dynamically from the 3D operative model, so this release does not pretend that the appearance hash is an image path.

The UI defaults to a soft charcoal/gray dark mode with white text, including every window's surrounding canvas and every dropdown. Open **Settings** to switch to Light or follow the Windows app theme; light mode uses dark text so editor values, search boxes, grids, and metadata remain readable.

## Teleport, rewards, and settings

The Teleport panel polls and displays captured player coordinates live. **Forward** uses the game's live reticle-hit location, with the player Z-facing angle as a protected fallback when the reticle API is temporarily unavailable. The verified transform uses X/Z as its horizontal plane in the panel's established coordinate layout, so Forward preserves the elevation coordinate. The game-thread request can wait up to eight seconds and reports whether the queue was consumed if no result arrives. Immediately before every successful teleport, the current position is captured under the same short thread suspension used for the coordinate write. Teleport actions are serialized so a double-click cannot overwrite the Undo point while the first action is running.

Every teleport destination—saved position, waypoint, forward, and manual XYZ—now pushes the current coordinates into a 12-entry safety history **before the first memory write**. This also preserves recovery coordinates when a write or verification fails. **Emergency Return** restores and removes the newest safety point without replacing the remaining history; **Undo** returns to the most recent pre-teleport point without capturing a falling location.

Open **Memory Scanner** from the main toolbar or type `scan`. It supports byte, 32/64-bit integer, float, and double values; exact or unknown first scans; and exact, changed, unchanged, increased, or decreased filtering. Scans can target the engine module or committed writable game regions. Unknown scans are intentionally capped at 256 MB and 2,000,000 candidates, results are previewed in a readable grid, scans can be cancelled, and selected writes require a prominent confirmation plus read-back verification. A changing address is only a candidate—not proof of noclip or any other feature—and must be reproduced across several controlled scans before it is promoted to a built-in option.

The Cheats panel includes one-shot **Add 1000 ETO** and **Add 10 Tech Points** actions. The equivalent console commands are `eto` and `tech`. They queue the confirmed RuleSmith reward IDs on the game's own script-update thread. Both are marked **SUPER RISKY** and should only be used after a save backup.

Version 0.8 adds verified game-thread controls for **Immortal Mode**, **Disable Felony System**, **Disable Detection**, **End Felony Chase**, **Spawn Racecar**, **Spawn DedSec Shop**, **Distract Everyone**, and **Disrupt Everyone**. Toggle state is tracked in the panel, and reversible game-thread toggles are returned to their normal state during detach when cleanup is enabled. Every one-shot world action remains marked **SUPER RISKY** because unloaded geometry and unexpected mission state can still cause instability.

Influence and seasonal XP are displayed as unavailable online-only entries. Ubisoft separated those account-backed systems from single-player: Influence replaces tech points online, while XP advances the online seasonal reward track. This offline companion does not modify multiplayer/account progression.

**Settings** stores theme, auto-inject enablement, the 3–60 second attach delay, live-coordinate refresh rate, close cleanup behavior, and an optional companion-only working-set trim threshold in `config\settings.json`. Cleanup-on-close defaults to enabled. Disabling it intentionally leaves remote changes active until the game exits and may require a game restart before a later instance can recover every optional hook. RAM trimming is best-effort and never limits the game's memory.

## Updating for a game version

All version-sensitive signatures and offsets are in `config\trainer.json`. Numeric offsets are decimal JSON values. The shipped values correspond to the currently supported game build:

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

Roster removal is implemented by compacting the manager's inline pointer array, clearing the final slot, and decrementing the count. No validated internal remove-operative function is currently available, so this remains a high-risk direct roster edit. Back up saves before using it.

## Localization catalog

`config\localization.json` contains the loc-ID-to-text maps used by the companion. Names are game localization values, not arbitrary strings; the game stores 32-bit localization IDs. Duplicate visible names can map to multiple IDs, so ambiguous edits are rejected instead of guessing.
