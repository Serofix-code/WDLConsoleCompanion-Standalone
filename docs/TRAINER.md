# WDL Console Companion

The companion is an experimental Windows x64 .NET 9/WPF application and one consumer of the research database. It detects the real game process and a configured Dunia module, waits before attachment, scans guarded signatures, captures the operative manager, and exposes offline research/editor panels.

Build with `dotnet build WDLConsoleCompanion.sln -c Release -p:Platform=x64`. Run only in single-player/offline mode and back up saves before risky changes. `inject`, `op`, `cheats`, `clothes`, `camera`, `teleport`, `scan`, `settings`, `status`, `detach`, and `help` are the principal console commands.

Features marked **UNDER DEVELOPMENT**, **HIGH RISK**, or **SUPER RISKY** are not stable SDK promises. Temporary camera research CSVs are written under `%LOCALAPPDATA%\WDLConsoleCompanion\ResearchLogs` and should be disabled after the camera is identified.
