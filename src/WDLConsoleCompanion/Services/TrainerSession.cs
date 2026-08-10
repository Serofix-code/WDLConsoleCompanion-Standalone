using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using WDLConsoleCompanion.Models;

namespace WDLConsoleCompanion.Services;

internal sealed class TrainerSession : IDisposable
{
    internal event Action<string>? Activity;
    private readonly TrainerConfig _config;
    private readonly LocalizationCatalog _catalog;
    private readonly Dictionary<string, List<MetadataOption>> _metadataCatalog;
    private readonly Dictionary<uint, string> _eventCatalog;
    private readonly List<AppearanceFieldDefinition> _appearanceCatalog;
    private readonly ContractCatalog _contractCatalog;
    private readonly Dictionary<string, List<CheatPatchConfig>> _cheatPatches;
    private RemoteProcess? _remote;
    private InlineHook? _hook;
    private CheatManager? _cheats;
    private TeleportManager? _teleport;
    private GameLuaQueue? _luaQueue;
    private readonly HashSet<string> _luaToggles = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _luaGate = new();
    private readonly object _teleportGate = new();
    private ProcessModule? _duniaModule;
    private ulong _persistentHumanFunction;
    private ulong _allocatorFunction;
    private ulong _censusGlobal;
    internal bool IsAttached => _remote is not null;
    internal int? ProcessId => _remote?.Process.Id;
    internal bool IsAttachedProcessAlive
    {
        get
        {
            try { return _remote is not null && !_remote.Process.HasExited; }
            catch { return false; }
        }
    }

    internal bool TargetProcessIsRunning()
    {
        Process[] processes = Process.GetProcessesByName(_config.ProcessName);
        try { return processes.Length > 0; }
        finally { foreach (Process process in processes) process.Dispose(); }
    }

    internal int? ReadyTargetProcessId()
    {
        Process[] processes = Process.GetProcessesByName(_config.ProcessName);
        try
        {
            if (processes.Length != 1) return null;
            Process process = processes[0];
            process.Refresh();
            string? executable = process.MainModule?.FileName;
            if (!string.Equals(Path.GetFileName(executable), _config.ProcessName + ".exe", StringComparison.OrdinalIgnoreCase)) return null;
            bool engineLoaded = process.Modules.Cast<ProcessModule>()
                .Any(module => _config.DuniaModules.Contains(module.ModuleName, StringComparer.OrdinalIgnoreCase));
            return engineLoaded ? process.Id : null;
        }
        catch { return null; }
        finally { foreach (Process process in processes) process.Dispose(); }
    }

    internal TrainerSession(string configDirectory)
    {
        string configPath = Path.Combine(configDirectory, "trainer.json");
        _config = JsonSerializer.Deserialize<TrainerConfig>(File.ReadAllText(configPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        }) ?? throw new InvalidOperationException("trainer.json is empty.");
        _catalog = LocalizationCatalog.Load(Path.Combine(configDirectory, "localization.json"));
        _metadataCatalog = JsonSerializer.Deserialize<Dictionary<string, List<MetadataOption>>>(File.ReadAllText(Path.Combine(configDirectory, "metadata.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new(StringComparer.OrdinalIgnoreCase);
        var events = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(configDirectory, "events.json"))) ?? [];
        _eventCatalog = events.ToDictionary(pair => uint.Parse(pair.Key), pair => pair.Value);
        _appearanceCatalog = JsonSerializer.Deserialize<List<AppearanceFieldDefinition>>(File.ReadAllText(Path.Combine(configDirectory, "appearance.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        _contractCatalog = JsonSerializer.Deserialize<ContractCatalog>(File.ReadAllText(Path.Combine(configDirectory, "contracts.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        _cheatPatches = JsonSerializer.Deserialize<Dictionary<string, List<CheatPatchConfig>>>(File.ReadAllText(Path.Combine(configDirectory, "cheats.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    internal string Attach()
    {
        if (IsAttached) return $"Already attached to PID {ProcessId}.";
        Process[] processes = Process.GetProcessesByName(_config.ProcessName);
        if (processes.Length != 1) throw new InvalidOperationException(processes.Length == 0
            ? $"{_config.ProcessName}.exe is not running."
            : $"Found {processes.Length} matching processes; close duplicate game instances.");
        var remote = new RemoteProcess(processes[0]);
        try
        {
            processes[0].Refresh();
            ProcessModule? dunia = processes[0].Modules.Cast<ProcessModule>()
                .FirstOrDefault(m => _config.DuniaModules.Contains(m.ModuleName, StringComparer.OrdinalIgnoreCase));
            if (dunia is null) throw new InvalidOperationException("No configured Dunia engine DLL is loaded.");

            ulong hookMatch = PatternScanner.FindUnique(remote, dunia, _config.OperativeManagerHook.Pattern);
            ulong patchAddress = Add(hookMatch, _config.OperativeManagerHook.MatchOffset);
            byte[] expected = PatternScanner.ParseBytes(_config.OperativeManagerHook.ExpectedBytes);

            ulong censusMatch = PatternScanner.FindUnique(remote, dunia, _config.CensusManagerGlobal.Pattern);
            ulong instruction = Add(censusMatch, _config.CensusManagerGlobal.MatchOffset);
            int displacement = remote.Read<int>(Add(instruction, _config.CensusManagerGlobal.DisplacementOffset));
            _censusGlobal = checked((ulong)((long)instruction + _config.CensusManagerGlobal.InstructionLength + displacement));
            if (!remote.IsRangeReadable(_censusGlobal, 8)) throw new InvalidOperationException($"Resolved census global 0x{_censusGlobal:X} is invalid.");

            _hook = InlineHook.Install(remote, patchAddress, expected);
            _cheats = new CheatManager(remote, dunia, _cheatPatches);
            _duniaModule = dunia;
            _remote = remote;
            string hookState = _hook.WasAdopted ? "adopted existing companion hook" : "installed new hook";
            return $"Attached to PID {remote.Process.Id}; module {dunia.ModuleName}; {hookState} at 0x{patchAddress:X}; census global 0x{_censusGlobal:X}.";
        }
        catch { remote.Dispose(); throw; }
    }

    internal IReadOnlyList<OperativeRecord> ReadRoster()
    {
        Report("Roster read requested.");
        var remote = RequireRemote();
        ulong manager = RequireHook().ReadCapturedPointer();
        int count = remote.Read<int>(Add(manager, _config.Offsets.RosterCount));
        if (count < 0 || count > _config.MaxRosterCount) throw new InvalidOperationException($"Roster count {count} is outside 0..{_config.MaxRosterCount}; stopping.");
        var census = ReadCensus();
        var result = new List<OperativeRecord>(count);
        for (int i = 0; i < count; i++)
        {
            ulong slot = Add(Add(manager, _config.Offsets.RosterArray), checked(i * 8));
            ulong operative = remote.ReadPointer(slot);
            ulong id = remote.Read<ulong>(Add(operative, _config.Offsets.OperativeId));
            census.TryGetValue(id, out var names);
            result.Add(new OperativeRecord
            {
                Index = i, OperativeAddress = operative, Id = id,
                FirstNameLocId = names.FirstNameId,
                SurnameLocId = names.SurnameId,
                Availability = remote.Read<int>(Add(operative, _config.Offsets.OperativeAvailability)),
                Origin = remote.Read<int>(Add(operative, _config.Offsets.OperativeOrigin)),
                CurrentAppearanceCode = FormatHex(remote.ReadBytes(Add(operative, _config.Offsets.CurrentAppearance), 24)),
                DefaultAppearanceCode = FormatHex(remote.ReadBytes(Add(operative, _config.Offsets.DefaultAppearance), 24)),
                FirstName = names.Found ? _catalog.Name(names.FirstNameId) : "<not in census>",
                Surname = names.Found ? _catalog.Surname(names.SurnameId) : "<not in census>"
            });
        }
        return result;
    }

    internal string UpdateNames(OperativeRecord edited)
    {
        Report($"Saving roster fields for operative {edited.IdHex}.");
        var remote = RequireRemote();
        ValidateRosterIdentity(edited, out _, out _);
        var census = ReadCensus();
        if (!census.TryGetValue(edited.Id, out var names) || !names.Found) throw new InvalidOperationException("Selected operative is not present in the census.");
        int first = _catalog.ResolveName(edited.FirstName, edited.FirstNameLocId);
        int surname = _catalog.ResolveSurname(edited.Surname, edited.SurnameLocId);
        if (edited.Availability is < 0 or > 4) throw new InvalidOperationException("Availability must be between 0 and 4.");
        if (edited.Origin is not (0 or 1 or 2 or 4 or 5 or 6 or 7)) throw new InvalidOperationException("Origin is not a supported roster value.");
        ulong availabilityAddress = Add(edited.OperativeAddress, _config.Offsets.OperativeAvailability);
        ulong originAddress = Add(edited.OperativeAddress, _config.Offsets.OperativeOrigin);
        int oldFirst = remote.Read<int>(names.FirstNameAddress);
        int oldSurname = remote.Read<int>(names.SurnameAddress);
        int oldAvailability = remote.Read<int>(availabilityAddress);
        int oldOrigin = remote.Read<int>(originAddress);
        using (remote.SuspendThreads())
        {
            ValidateRosterIdentity(edited, out _, out _);
            try
            {
                remote.Write(names.FirstNameAddress, first);
                remote.Write(names.SurnameAddress, surname);
                remote.Write(availabilityAddress, edited.Availability);
                remote.Write(originAddress, edited.Origin);
                if (remote.Read<int>(availabilityAddress) != edited.Availability || remote.Read<int>(originAddress) != edited.Origin)
                    throw new InvalidOperationException("The game did not retain one or more operative values during immediate write verification.");
            }
            catch
            {
                remote.Write(names.FirstNameAddress, oldFirst);
                remote.Write(names.SurnameAddress, oldSurname);
                remote.Write(availabilityAddress, oldAvailability);
                remote.Write(originAddress, oldOrigin);
                throw;
            }
        }
        return $"Saved and verified {edited.FirstName} {edited.Surname}: names, availability {edited.Availability}, and origin {edited.Origin}.";
    }

    internal string SetCheat(string name, bool enabled)
    {
        if (!IsAttachedProcessAlive) throw new InvalidOperationException("The game process is not attached and running.");
        return (_cheats ?? throw new InvalidOperationException("Cheat manager is unavailable.")).Set(name, enabled);
    }

    internal string ToggleCheat(string name, string? requestedState)
    {
        string normalized = CheatManager.Normalize(name);
        if (LuaToggleScripts.ContainsKey(normalized))
        {
            bool luaEnabled = requestedState?.ToLowerInvariant() switch
            {
                null or "" or "toggle" => !_luaToggles.Contains(normalized),
                "on" or "enable" or "enabled" or "1" => true,
                "off" or "disable" or "disabled" or "0" => false,
                _ => throw new InvalidOperationException("Cheat state must be on, off, or toggle.")
            };
            return SetLuaToggle(normalized, luaEnabled);
        }
        var cheats = _cheats ?? throw new InvalidOperationException("Cheat manager is unavailable while detached.");
        bool enabled = requestedState?.ToLowerInvariant() switch
        {
            null or "" or "toggle" => !cheats.IsActive(name),
            "on" or "enable" or "enabled" or "1" => true,
            "off" or "disable" or "disabled" or "0" => false,
            _ => throw new InvalidOperationException("Cheat state must be on, off, or toggle.")
        };
        return SetCheat(name, enabled);
    }

    internal string CheatStatus()
    {
        if (_cheats is null) return "Cheats unavailable while detached.";
        string[] names = ["godmode", "immortal", "notrace", "disablefelony", "disabledetection", "infammo", "noreload", "norecoil", "fastsearch", "hackcooldown", "freezehack", "dronerange", "dronehealth", "onehitkill"];
        var active = _cheats.Active.Concat(_luaToggles).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return string.Join(Environment.NewLine, names.Select(name => $"  {CheatManager.Display(name),-15} {(active.Contains(name) ? "ON" : "OFF")}"));
    }
    internal bool IsCheatActive(string name) => _luaToggles.Contains(CheatManager.Normalize(name)) || _cheats?.IsActive(name) == true;
    internal bool IsCheatAvailable(string name) => LuaToggleScripts.ContainsKey(CheatManager.Normalize(name)) || _cheats?.IsSupported(name) == true;
    internal MemoryScanner CreateMemoryScanner()
    {
        MemoryScanner scanner = new(RequireRemote(), _duniaModule ?? throw new InvalidOperationException("Dunia module is unavailable."));
        Report("Memory Scanner opened. Scans are bounded and operate only on readable engine or writable game regions.");
        return scanner;
    }
    internal void ReportMemoryScan(string message) => Report(message);

    private TeleportManager RequireTeleport()
    {
        lock (_teleportGate)
        {
            var remote = RequireRemote(); var module = _duniaModule ?? throw new InvalidOperationException("Dunia module is unavailable.");
            if (_teleport is not null) return _teleport;
            _teleport = TeleportManager.Install(remote, module);
            Report(_teleport.RecoveredPreviousHooks ? "Recovered compatible teleport hooks left by a previous companion instance." : "Installed teleport coordinate hooks.");
            return _teleport;
        }
    }

    internal (GamePosition Current, GamePosition? Waypoint, GamePosition? Safety, int SafetyCount) ReadTeleportPositions()
    {
        Report("Reading player and waypoint coordinates."); TeleportManager teleport = RequireTeleport(); GamePosition current = teleport.ObserveCurrent();
        try { return (current, teleport.ReadWaypoint(), teleport.LatestSafetyPosition, teleport.SafetyPositionCount); } catch { return (current, null, teleport.LatestSafetyPosition, teleport.SafetyPositionCount); }
    }
    internal GamePosition ReadCurrentTeleportPosition() => RequireTeleport().ObserveCurrent();
    internal void SaveTeleportPosition() { RequireTeleport().SaveCurrent(); Report("SUPER RISKY teleport position saved in the companion session."); }
    internal string LoadTeleportPosition() { GamePosition result = RequireTeleport().LoadSaved(); Report("SUPER RISKY: teleported to saved position."); return "Teleported to saved position: " + result; }
    internal string TeleportToWaypoint() { GamePosition result = RequireTeleport().TeleportToWaypoint(); Report("SUPER RISKY: teleported to map waypoint."); return "Teleported to waypoint: " + result; }
    internal string TeleportForward(float distance)
    {
        TeleportManager teleport = RequireTeleport();
        FacingQuery facing = RequireLuaQueue().QueryFacing();
        GamePosition result = teleport.TeleportForward(distance, facing);
        string source = facing.Reticle.HasValue ? "live reticle" : $"player facing angle {facing.AngleDegrees:0.##}°";
        Report($"SUPER RISKY: saved the pre-teleport location, then teleported {distance:0.##} metres using the {source}.");
        return "Teleported using " + source + ": " + result + ". Undo saved.";
    }
    internal string UndoTeleport() { GamePosition result = RequireTeleport().Undo(); Report("SUPER RISKY: undid teleport."); return "Teleport undone: " + result; }
    internal string ReturnToSafeTeleportPosition() { GamePosition result = RequireTeleport().ReturnToSafety(); Report("EMERGENCY RETURN: restored the latest pre-teleport safety position."); return "Returned to safe position: " + result; }
    internal string TeleportTo(GamePosition destination) { GamePosition result = RequireTeleport().TeleportTo(destination); Report("SUPER RISKY: teleported to manual coordinates."); return "Teleported to: " + result; }

    private GameLuaQueue RequireLuaQueue()
    {
        lock (_luaGate)
        {
            if (_luaQueue is not null) return _luaQueue;
            _luaQueue = GameLuaQueue.Install(RequireRemote(), _duniaModule ?? throw new InvalidOperationException("Dunia module is unavailable."));
            Report("Installed the game-thread action queue for RuleSmith rewards.");
            return _luaQueue;
        }
    }
    internal string AddEto() { RequireLuaQueue().Enqueue("TriggerRuleSmithRule('589221860', '', GetLocalPlayerEntityId())"); Report("SUPER RISKY: queued +1000 ETO through RuleSmith."); return "+1000 ETO queued. Check the in-game balance in a moment."; }
    internal string AddTechPoints() { RequireLuaQueue().Enqueue("TriggerRuleSmithRule('189922678', '', GetLocalPlayerEntityId())"); Report("SUPER RISKY: queued +10 tech points through RuleSmith."); return "+10 tech points queued. Check the in-game balance in a moment."; }

    private static readonly Dictionary<string, (string Enable, string Disable, string Label)> LuaToggleScripts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["immortal"] = ("SetPawnImmuneToDeath(GetLocalPlayerEntityId(),1)", "SetPawnImmuneToDeath(GetLocalPlayerEntityId(),0)", "Immortal Mode"),
        ["disablefelony"] = ("FelonySystemEnable(0)", "FelonySystemEnable(1)", "Disable Felony System"),
        ["disabledetection"] = ("SetCanBeDetected(GetLocalPlayerEntityId(),0)", "SetCanBeDetected(GetLocalPlayerEntityId(),1)", "Disable Detection")
    };

    private string SetLuaToggle(string name, bool enabled)
    {
        if (!IsAttachedProcessAlive) throw new InvalidOperationException("The game process is not attached and running.");
        var definition = LuaToggleScripts[name];
        bool active = _luaToggles.Contains(name);
        if (active == enabled) return $"{definition.Label} is already {(enabled ? "ON" : "OFF")}.";
        RequireLuaQueue().Enqueue(enabled ? definition.Enable : definition.Disable);
        if (enabled) _luaToggles.Add(name); else _luaToggles.Remove(name);
        Report($"SUPER RISKY: {definition.Label} queued {(enabled ? "ON" : "OFF")} on the game thread.");
        return $"{definition.Label}: {(enabled ? "ON" : "OFF")}";
    }

    internal string RunGameAction(string name)
    {
        string normalized = CheatManager.Normalize(name);
        (string Script, string Result) action = normalized switch
        {
            "eto" => ("TriggerRuleSmithRule('589221860','',GetLocalPlayerEntityId())", "+1000 ETO queued."),
            "techpoints" => ("TriggerRuleSmithRule('189922678','',GetLocalPlayerEntityId())", "+10 tech points queued."),
            "endchase" => ("FelonyEndChase(GetLocalPlayerEntityId())", "End chase queued."),
            "spawnracecar" => ("l=GetReticleHitLocation();SpawnEntityFromArchetype('{B785212C-DE03-4049-8FD7-45E9130C4B2F}',l[1],l[2],l[3],0,0,0)", "Racecar spawn queued at the reticle."),
            "spawnshop" => ("l=GetReticleHitLocation();SpawnEntityFromArchetype('{5991467D-8E99-431F-AE1B-724D46EDE1E9}',l[1],l[2],l[3],0,0,180+GetEntityAngle(GetLocalPlayerEntityId(),2))", "DedSec shop spawn queued at the reticle."),
            "distractall" => ("h=CAIAgentManager_GetInstance():GetAIAgentsOfGroupFromLUA_v2('Human',0,'',0,0);for i,v in ipairs(h)do TryTriggerHack('Distract',GetLocalPlayerEntityId(),v)end", "Distract-all queued."),
            "disruptall" => ("h=CAIAgentManager_GetInstance():GetAIAgentsOfGroupFromLUA_v2('Human',0,'',0,0);for i,v in ipairs(h)do TryTriggerHack('DisruptComm',GetLocalPlayerEntityId(),v)end", "Disrupt-all queued."),
            _ => throw new InvalidOperationException($"Unknown game action '{name}'.")
        };
        RequireLuaQueue().Enqueue(action.Script);
        Report($"SUPER RISKY: {action.Result}");
        return action.Result;
    }

    internal PerkSnapshot ReadPerks(OperativeRecord operative)
    {
        Report($"Reading perks for {operative.FirstName} {operative.Surname}.");
        var remote = RequireRemote();
        ValidateRosterIdentity(operative, out ulong manager, out _);
        ulong npcData = ResolveNpcData(remote, manager, operative);
        ulong arrayBase = Add(npcData, 0x90);
        int capacity = remote.Read<int>(arrayBase);
        ushort length = remote.Read<ushort>(arrayBase + 4);
        ushort flags = remote.Read<ushort>(arrayBase + 6);
        if (length > 80 || capacity < 0 || capacity > 4096) throw new InvalidOperationException("The perk array header is invalid.");
        ulong data = length == 0 || ((flags & 0x8000) != 0 && length <= 2) ? arrayBase + 8 : remote.ReadPointer(arrayBase + 8);
        var ids = new List<uint>(length);
        for (int i = 0; i < length; i++) ids.Add(remote.Read<uint>(data + (ulong)(i * 4)));
        return new PerkSnapshot(ids, capacity, (flags & 0x8000) != 0);
    }

    internal string SavePerks(OperativeRecord operative, IReadOnlyList<uint> ids)
    {
        Report($"SUPER RISKY: saving {ids.Count} perks for operative {operative.IdHex}.");
        if (ids.Count > 80 || ids.Any(id => id == 0)) throw new InvalidOperationException("Use 1-80 non-zero perk IDs.");
        var remote = RequireRemote();
        ValidateRosterIdentity(operative, out ulong manager, out _);
        ulong npcData = ResolveNpcData(remote, manager, operative);
        ulong arrayBase = Add(npcData, 0x90);
        byte[] oldHeader = remote.ReadBytes(arrayBase, 16);
        int oldCapacity = remote.Read<int>(arrayBase);
        ushort oldLength = remote.Read<ushort>(arrayBase + 4);
        ushort oldFlags = remote.Read<ushort>(arrayBase + 6);
        if (oldLength > 80 || oldCapacity < 0 || oldCapacity > 4096) throw new InvalidOperationException("The existing perk array header is invalid.");
        ulong oldData = oldLength == 0 || ((oldFlags & 0x8000) != 0 && oldLength <= 2) ? arrayBase + 8 : remote.ReadPointer(arrayBase + 8);
        byte[] oldItems = oldLength == 0 ? [] : remote.ReadBytes(oldData, oldLength * 4);

        ulong newData;
        bool inline = ids.Count <= 2;
        if (inline) newData = arrayBase + 8;
        else if ((oldFlags & 0x8000) == 0 && ids.Count <= oldCapacity) newData = oldData;
        else
        {
            _allocatorFunction = ResolveRelativeCall(_allocatorFunction, "B9 B0 00 00 00 BA 10 00 00 00 E8 ?? ?? ?? ??", 10);
            newData = remote.ExecuteFunction(_allocatorFunction, (ulong)(ids.Count * 4), 0x10);
            if (newData == 0 || !remote.IsRangeReadable(newData, ids.Count * 4)) throw new InvalidOperationException("The game's allocator did not return valid perk storage.");
        }
        byte[] bytes = ids.SelectMany(BitConverter.GetBytes).ToArray();
        using (remote.SuspendThreads())
        {
            ValidateRosterIdentity(operative, out _, out _);
            try
            {
                if (inline)
                {
                    remote.WriteBytes(arrayBase + 8, new byte[8]);
                    if (bytes.Length > 0) remote.WriteBytes(arrayBase + 8, bytes);
                    remote.Write(arrayBase, 2);
                    remote.Write(arrayBase + 6, (ushort)0x8000);
                }
                else
                {
                    remote.WriteBytes(newData, bytes);
                    remote.Write(arrayBase, Math.Max(ids.Count, oldCapacity));
                    remote.Write(arrayBase + 6, (ushort)0);
                    remote.Write(arrayBase + 8, newData);
                }
                remote.Write(arrayBase + 4, (ushort)ids.Count);
            }
            catch
            {
                remote.WriteBytes(arrayBase, oldHeader);
                if (oldItems.Length > 0) remote.WriteBytes(oldData, oldItems);
                throw;
            }
        }
        return $"Saved {ids.Count} perk IDs. Switch away from and back to the operative before testing perks.";
    }

    private static readonly AdvancedFieldDescriptor[] AdvancedFieldDescriptors =
    [
        new("animationset", "Animation set", "demographic", 0xE0, 16, "Animation-set tag value"),
        new("playerpersona", "Player voice actor / persona", "npc", 0x48, 4, "Active operative voice persona"),
        new("dedsecaffinity", "DedSec affinity", "affinity", 0x2C, 1, "Relationship affinity toward DedSec"),
        new("workinghours", "Working hours", "working", 0x278, 2, "NPC working-hours schedule"),
        new("voiceprofile", "Voice profile", "voiceprofile", 0x8, 8, "Pitch, volume and modulation profile"),
        new("characterdeck", "Character deck", "characterdeck", 0x8, 8, "Character archetype/deck ID"),
        new("occupationgroup", "Occupation group", "demographic", 0x1B8, 16, "Occupation-group tag value"),
        new("occupation", "Occupation", "career", 0x304, 4, "Occupation localization/tag ID"),
        new("birthplace", "Birthplace", "career", 0x2B4, 4, "Birthplace localization/tag ID"),
        new("personality", "Personality", "demographic", 0x38, 16, "Personality tag value"),
        new("ethnicity", "Ethnicity", "demographic", 0x68, 16, "Ethnicity tag value"),
        new("identity", "Identity (aggressor/victim)", "demographic", 0x80, 16, "Identity tag value"),
        new("combat", "Combat alignment", "demographic", 0xB0, 16, "Combat-alignment tag value"),
        new("income", "Income", "demographic", 0xC8, 16, "Income tag value"),
        new("immigration", "Immigration status", "demographic", 0xF8, 16, "Immigration-status tag value"),
        new("gender", "Gender", "demographic", 0x98, 16, "Gender tag value"),
        new("age", "Age range", "demographic", 0x110, 16, "Age-range tag value"),
        new("religion", "Religion", "demographic", 0x128, 16, "Religion tag value"),
        new("namefilter", "Name filter", "demographic", 0x14C, 4, "Name-generation filter ID"),
        new("orientation", "Sexual orientation", "demographic", 0x158, 16, "Sexual-orientation tag value"),
        new("fashion", "Fashion", "demographic", 0x170, 16, "Fashion tag value"),
        new("surnamefilter", "Surname filter", "demographic", 0x1F4, 4, "Surname-generation filter ID"),
        new("voiceactor", "Inactive NPC voice actor", "demographic", 0x230, 16, "Voice used for emotes and while inactive"),
        new("tolerance", "Social tolerance", "demographic", 0x260, 16, "Social-tolerance tag value")
    ];

    internal IReadOnlyList<AdvancedOperativeField> ReadAdvancedFields(OperativeRecord operative)
    {
        Report($"Reading advanced metadata for {operative.FirstName} {operative.Surname}.");
        var remote = RequireRemote();
        ValidateRosterIdentity(operative, out ulong manager, out _);
        ulong npcData = ResolveNpcData(remote, manager, operative);
        var result = new List<AdvancedOperativeField>(AdvancedFieldDescriptors.Length);
        foreach (AdvancedFieldDescriptor field in AdvancedFieldDescriptors)
        {
            IReadOnlyList<MetadataOption> options = _metadataCatalog.TryGetValue(field.Key, out List<MetadataOption>? known) ? known : [];
            try
            {
                result.Add(new AdvancedOperativeField { Key = field.Key, DisplayName = field.DisplayName, Value = Convert.ToHexString(remote.ReadBytes(ResolveAdvancedAddress(remote, npcData, field), field.Length)), Risk = "HIGH RISK", Description = field.Description, Options = options });
            }
            catch (Exception ex)
            {
                result.Add(new AdvancedOperativeField { Key = field.Key, DisplayName = field.DisplayName, Value = "", Risk = "UNAVAILABLE", Description = $"{field.Description}. Pointer unavailable: {ex.Message}", Options = options, IsAvailable = false });
            }
        }
        return result;
    }

    internal string SaveAdvancedField(OperativeRecord operative, AdvancedOperativeField edited)
    {
        Report($"HIGH RISK: saving metadata field {edited.DisplayName} for operative {operative.IdHex}.");
        AdvancedFieldDescriptor descriptor = AdvancedFieldDescriptors.SingleOrDefault(field => field.Key == edited.Key)
            ?? throw new InvalidOperationException("Unknown advanced field.");
        byte[] wanted = ParseExactHex(edited.Value, descriptor.Length, descriptor.DisplayName);
        var remote = RequireRemote();
        ValidateRosterIdentity(operative, out ulong manager, out _);
        ulong npcData = ResolveNpcData(remote, manager, operative);
        ulong address = ResolveAdvancedAddress(remote, npcData, descriptor);
        byte[] original = remote.ReadBytes(address, descriptor.Length);
        using (remote.SuspendThreads())
        {
            ValidateRosterIdentity(operative, out _, out _);
            try
            {
                remote.WriteBytes(address, wanted);
                if (!remote.ReadBytes(address, wanted.Length).SequenceEqual(wanted))
                    throw new InvalidOperationException("The game did not retain the value during immediate verification.");
            }
            catch { remote.WriteBytes(address, original); throw; }
        }
        return $"Saved and verified {descriptor.DisplayName}. Switch operatives before testing the result.";
    }

    internal OperativeStatistics ReadStatistics(OperativeRecord operative)
    {
        Report($"Reading statistics for {operative.FirstName} {operative.Surname}.");
        var remote = RequireRemote(); ValidateRosterIdentity(operative, out ulong manager, out _);
        ulong npcData = ResolveNpcData(remote, manager, operative);
        string country = "Unavailable";
        try
        {
            AdvancedFieldDescriptor birthplace = AdvancedFieldDescriptors.Single(field => field.Key == "birthplace");
            string raw = Convert.ToHexString(remote.ReadBytes(ResolveAdvancedAddress(remote, npcData, birthplace), birthplace.Length));
            country = _metadataCatalog.TryGetValue("birthplace", out List<MetadataOption>? countries)
                ? countries.FirstOrDefault(option => option.Value.Equals(raw, StringComparison.OrdinalIgnoreCase))?.Label ?? $"Unknown tag {raw}" : $"Tag {raw}";
        }
        catch (Exception ex) { country = "Unavailable: " + ex.Message; }

        int count = remote.Read<int>(npcData + 0x54); if (count < 0 || count > 256) throw new InvalidOperationException($"Metadata event count {count} is invalid.");
        uint primary = remote.Read<uint>(npcData + 0x78); string primaryLabel = ResolveEvent(primary); var birthplaces = new List<string>();
        AddBirthplace(primaryLabel, birthplaces);
        if (count > 0)
        {
            ulong events = remote.ReadPointer(npcData + 0x58);
            for (int i = 0; i < count; i++) AddBirthplace(ResolveEvent(remote.Read<uint>(events + (ulong)(i * 8))), birthplaces);
        }
        string detailed = birthplaces.Distinct(StringComparer.OrdinalIgnoreCase).Any()
            ? string.Join("; ", birthplaces.Distinct(StringComparer.OrdinalIgnoreCase))
            : "No city-level BIRTH event is currently stored in public/primary metadata.";
        return new OperativeStatistics { Age = remote.Read<int>(npcData + 0x28), Income = remote.Read<int>(npcData + 0x30), Status = remote.Read<byte>(npcData + 0xC8), CountryTag = country, DetailedBirthplace = detailed, PrimaryBiography = primaryLabel };
    }

    private static void AddBirthplace(string label, List<string> results)
    {
        int separator = label.IndexOf('|'); string readable = separator >= 0 ? label[(separator + 1)..].Trim() : label;
        if (readable.Contains("Born in ", StringComparison.OrdinalIgnoreCase)) results.Add(readable);
    }

    internal string SaveStatistics(OperativeRecord operative, OperativeStatistics statistics)
    {
        Report($"HIGH RISK: saving statistics for operative {operative.IdHex}.");
        if (statistics.Age is < 0 or > 130) throw new InvalidOperationException("Age must be between 0 and 130.");
        if (statistics.Income is < 0 or > 100_000_000) throw new InvalidOperationException("Income must be between 0 and 100,000,000.");
        if (statistics.Status > 4) throw new InvalidOperationException("Status must be Available, Dead, Injured, Arrested, or Pending Deportation.");
        var remote = RequireRemote(); ValidateRosterIdentity(operative, out ulong manager, out _); ulong npcData = ResolveNpcData(remote, manager, operative);
        int oldAge = remote.Read<int>(npcData + 0x28); int oldIncome = remote.Read<int>(npcData + 0x30); byte oldStatus = remote.Read<byte>(npcData + 0xC8);
        using (remote.SuspendThreads())
        {
            ValidateRosterIdentity(operative, out _, out _);
            try
            {
                remote.Write(npcData + 0x28, statistics.Age); remote.Write(npcData + 0x30, statistics.Income); remote.Write(npcData + 0xC8, statistics.Status);
                if (remote.Read<int>(npcData + 0x28) != statistics.Age || remote.Read<int>(npcData + 0x30) != statistics.Income || remote.Read<byte>(npcData + 0xC8) != statistics.Status) throw new InvalidOperationException("Statistics did not pass immediate verification.");
            }
            catch { remote.Write(npcData + 0x28, oldAge); remote.Write(npcData + 0x30, oldIncome); remote.Write(npcData + 0xC8, oldStatus); throw; }
        }
        return "Age, income, and NPC status saved and verified.";
    }

    internal IReadOnlyList<EventCatalogItem> EventCatalog() => _eventCatalog.Select(pair => new EventCatalogItem { Id = pair.Key, Label = pair.Value }).OrderBy(item => item.Label).ToArray();

    internal IReadOnlyList<OperativeEventRow> ReadRecentEvents(OperativeRecord operative)
    {
        Report($"Reading biography events and city-level birthplace for {operative.FirstName} {operative.Surname}.");
        var remote = RequireRemote(); ValidateRosterIdentity(operative, out ulong manager, out _); ulong npcData = ResolveNpcData(remote, manager, operative);
        int count = remote.Read<int>(npcData + 0x54); if (count < 0 || count > 256) throw new InvalidOperationException($"Metadata event count {count} is invalid.");
        var rows = new List<OperativeEventRow>(count + 1); uint primary = remote.Read<uint>(npcData + 0x78);
        rows.Add(new OperativeEventRow { Index = -1, IsPrimary = true, Id = primary, Label = ResolveEvent(primary) });
        if (count > 0)
        {
            ulong array = remote.ReadPointer(npcData + 0x58);
            for (int i = 0; i < count; i++) { uint id = remote.Read<uint>(array + (ulong)(i * 8)); rows.Add(new OperativeEventRow { Index = i, Id = id, Label = ResolveEvent(id) }); }
        }
        return rows;
    }

    internal string SaveRecentEvent(OperativeRecord operative, OperativeEventRow row, uint newId)
    {
        Report($"HIGH RISK: replacing biography event for operative {operative.IdHex}.");
        if (!_eventCatalog.ContainsKey(newId)) throw new InvalidOperationException("Select a known metadata event.");
        var remote = RequireRemote(); ValidateRosterIdentity(operative, out ulong manager, out _); ulong npcData = ResolveNpcData(remote, manager, operative);
        int count = remote.Read<int>(npcData + 0x54); if (count < 0 || count > 256 || (!row.IsPrimary && (row.Index < 0 || row.Index >= count))) throw new InvalidOperationException("The event list changed; reload it first.");
        ulong address = row.IsPrimary ? npcData + 0x78 : remote.ReadPointer(npcData + 0x58) + (ulong)(row.Index * 8);
        uint original = remote.Read<uint>(address); if (original != row.Id) throw new InvalidOperationException("The selected event changed; reload it first.");
        using (remote.SuspendThreads())
        {
            ValidateRosterIdentity(operative, out _, out _);
            try
            {
                remote.Write(address, newId);
                if (remote.Read<uint>(address) != newId) throw new InvalidOperationException("The event write did not pass verification.");
            }
            catch { remote.Write(address, original); throw; }
        }
        return $"Saved {ResolveEvent(newId)}. Reload the game/Team menu to refresh displayed biography text.";
    }

    internal AppearanceSnapshot ReadAppearance(OperativeRecord operative, bool defaults)
    {
        Report($"Reading {(defaults ? "wardrobe-default" : "current")} appearance for {operative.FirstName} {operative.Surname}.");
        var remote = RequireRemote(); ValidateRosterIdentity(operative, out _, out _); ulong address = operative.OperativeAddress + (ulong)(defaults ? _config.Offsets.DefaultAppearance : _config.Offsets.CurrentAppearance);
        byte[] code = remote.ReadBytes(address, 24); int version = ReadPacked(code, 0, 0, 9); int type = ReadPacked(code, 0, 9, 4);
        return new AppearanceSnapshot { IsDefault = defaults, FormatVersion = version, FormatType = type, Fields = _appearanceCatalog.Select(def => new AppearanceFieldValue { Definition = def, Value = ReadPacked(code, def.ByteOffset, def.BitOffset, def.BitLength) }).ToArray() };
    }

    internal string SaveAppearanceField(OperativeRecord operative, bool defaults, AppearanceFieldValue field)
    {
        Report($"HIGH RISK: saving appearance component {field.DisplayName} for operative {operative.IdHex}.");
        if (!field.Options.Any(option => option.Value == field.Value)) throw new InvalidOperationException("Select a known appearance value.");
        var remote = RequireRemote(); ValidateRosterIdentity(operative, out _, out _); ulong address = operative.OperativeAddress + (ulong)(defaults ? _config.Offsets.DefaultAppearance : _config.Offsets.CurrentAppearance);
        byte[] original = remote.ReadBytes(address, 24); int version = ReadPacked(original, 0, 0, 9); int type = ReadPacked(original, 0, 9, 4);
        if (version != 12 || type != 2) throw new InvalidOperationException($"Appearance must be unpacked format version 12/type 2; selected operative is version {version}/type {type}. Switch to the operative and away, then reload.");
        byte[] edited = original.ToArray(); WritePacked(edited, field.Definition.ByteOffset, field.Definition.BitOffset, field.Definition.BitLength, field.Value);
        using (remote.SuspendThreads())
        {
            ValidateRosterIdentity(operative, out _, out _);
            try
            {
                remote.WriteBytes(address, edited);
                if (!remote.ReadBytes(address, 24).SequenceEqual(edited)) throw new InvalidOperationException("Appearance write did not pass verification.");
            }
            catch { remote.WriteBytes(address, original); throw; }
        }
        return $"Saved {field.DisplayName}: {field.ResolvedName}. Switch to another operative and back to rebuild the model.";
    }

    internal IReadOnlyList<OperativeContract> ReadContracts(OperativeRecord operative)
    {
        Report($"Reading contracts and contacts for {operative.FirstName} {operative.Surname}.");
        var remote = RequireRemote(); ValidateRosterIdentity(operative, out ulong manager, out _); ulong npcData = ResolveNpcData(remote, manager, operative);
        ulong zeroCell = remote.ReadPointer(npcData + 0xE0); int arraySize = remote.Read<int>(npcData + 0xF0); int size = remote.Read<int>(npcData + 0xF4);
        if (arraySize < 0 || arraySize > 2048 || size < 0 || size > arraySize) throw new InvalidOperationException("Contract table header is invalid.");
        ulong array = size == 0 ? 0 : remote.ReadPointer(npcData + 0xE8); var census = ReadCensus(); var result = new List<OperativeContract>(size);
        for (int i = 0; i < arraySize && result.Count < size; i++)
        {
            try
            {
                ulong handle = remote.ReadPointer(array + (ulong)(i * 8)); if (handle == zeroCell) continue; ulong contract = remote.ReadPointer(handle + 0x18);
                ulong idHandle = remote.ReadPointer(contract + 0x18); ulong contractId = remote.Read<ulong>(idHandle + 0x10); ulong definitionHandle = remote.ReadPointer(contract + 0x20); ulong definition = remote.Read<ulong>(definitionHandle + 0x8);
                ulong roleA = remote.ReadPointer(contract + 0x28); ulong roleB = remote.ReadPointer(contract + 0x30); ulong actorA = remote.Read<ulong>(remote.ReadPointer(roleA + 0x18) + 0x10); ulong actorB = remote.Read<ulong>(remote.ReadPointer(roleB + 0x18) + 0x10);
                string typeKey = definition.ToString("X"); string typeName = _contractCatalog.Types.TryGetValue(typeKey, out string? known) ? known : $"Unknown 0x{typeKey}";
                result.Add(new OperativeContract { ContractId = contractId.ToString("X16"), Type = typeName, ParticipantA = ResolveCensusName(actorA, census), ParticipantB = ResolveCensusName(actorB, census), CurrentAttendances = remote.Read<ulong>(contract + 0x48), PreviousAttendances = remote.Read<ulong>(contract + 0x68) });
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Skipping invalid contract slot {i}: {ex.Message}"); }
        }
        return result;
    }

    internal IReadOnlyList<OperativeAttendance> ReadAttendances(OperativeRecord operative)
    {
        Report($"Reading editable contract schedules for {operative.FirstName} {operative.Surname}.");
        var remote = RequireRemote(); ValidateRosterIdentity(operative, out ulong manager, out _); ulong npcData = ResolveNpcData(remote, manager, operative);
        ulong zeroCell = remote.ReadPointer(npcData + 0xE0); int arraySize = remote.Read<int>(npcData + 0xF0); int size = remote.Read<int>(npcData + 0xF4);
        if (arraySize < 0 || arraySize > 2048 || size < 0 || size > arraySize) throw new InvalidOperationException("Contract table header is invalid.");
        ulong array = size == 0 ? 0 : remote.ReadPointer(npcData + 0xE8); var result = new List<OperativeAttendance>();
        for (int i = 0, seen = 0; i < arraySize && seen < size; i++)
        {
            ulong handle;
            try { handle = remote.ReadPointer(array + (ulong)(i * 8)); } catch { continue; }
            if (handle == zeroCell) continue; seen++;
            try
            {
                ulong contract = remote.ReadPointer(handle + 0x18); ulong idHandle = remote.ReadPointer(contract + 0x18); ulong contractId = remote.Read<ulong>(idHandle + 0x10);
                ulong definitionHandle = remote.ReadPointer(contract + 0x20); ulong definition = remote.Read<ulong>(definitionHandle + 0x8); string typeKey = definition.ToString("X");
                string contractType = _contractCatalog.Types.TryGetValue(typeKey, out string? knownType) ? knownType : $"Unknown 0x{typeKey}";
                ulong attendanceCount = remote.Read<ulong>(contract + 0x48); ulong tableSize = remote.Read<ulong>(contract + 0x50); ulong table = remote.Read<ulong>(contract + 0x58);
                if (attendanceCount > 512 || tableSize > 4096) throw new InvalidOperationException("Attendance table bounds are invalid.");
                int added = 0;
                for (ulong bucket = 0; bucket < tableSize && added < (int)attendanceCount; bucket++)
                {
                    ulong item = remote.ReadPointer(table + bucket * 8); int collisionGuard = 0;
                    while (item != 0 && added < (int)attendanceCount && collisionGuard++ < 64)
                    {
                        ulong attendance = remote.ReadPointer(item + 0x10); byte priority = remote.Read<byte>(attendance + 0x11); ulong attendanceDefinitionHandle = remote.ReadPointer(attendance + 0x8); ulong attendanceDefinition = remote.Read<ulong>(attendanceDefinitionHandle + 0x8); ulong activity = remote.ReadPointer(attendance + 0x20);
                        float start = remote.Read<float>(activity + 0xC) / 3600f; float end = remote.Read<float>(activity + 0x14) / 3600f; string attendanceKey = attendanceDefinition.ToString("X");
                        string attendanceType = _contractCatalog.Attendance.TryGetValue(attendanceKey, out string? knownAttendance) ? knownAttendance : $"Unknown 0x{attendanceKey}";
                        result.Add(new OperativeAttendance { ContractId = contractId.ToString("X16"), ContractType = contractType, AttendanceType = attendanceType, StartHour = start, EndHour = end, Priority = priority, AttendanceAddress = attendance, ActivityAddress = activity, OriginalStartHour = start, OriginalEndHour = end, OriginalPriority = priority });
                        added++; item = remote.ReadPointer(item);
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Skipping invalid contract attendance slot {i}: {ex.Message}"); }
        }
        return result;
    }

    internal string SaveAttendance(OperativeRecord operative, OperativeAttendance edited)
    {
        Report($"SUPER RISKY: saving contract schedule for operative {operative.IdHex}.");
        if (!float.IsFinite(edited.StartHour) || edited.StartHour is < 0 or > 24 || !float.IsFinite(edited.EndHour) || edited.EndHour is < 0 or > 24) throw new InvalidOperationException("Start and end hours must be between 0 and 24.");
        var remote = RequireRemote(); ValidateRosterIdentity(operative, out _, out _);
        if (!remote.IsRangeReadable(edited.AttendanceAddress + 0x11, 1) || !remote.IsRangeReadable(edited.ActivityAddress + 0xC, 0xC)) throw new InvalidOperationException("The attendance object is no longer valid; reload first.");
        byte currentPriority = remote.Read<byte>(edited.AttendanceAddress + 0x11); float currentStart = remote.Read<float>(edited.ActivityAddress + 0xC) / 3600f; float currentEnd = remote.Read<float>(edited.ActivityAddress + 0x14) / 3600f;
        if (currentPriority != edited.OriginalPriority || Math.Abs(currentStart - edited.OriginalStartHour) > 0.001f || Math.Abs(currentEnd - edited.OriginalEndHour) > 0.001f) throw new InvalidOperationException("The contract schedule changed in game; reload before saving.");
        using (remote.SuspendThreads())
        {
            ValidateRosterIdentity(operative, out _, out _);
            try
            {
                remote.Write(edited.AttendanceAddress + 0x11, edited.Priority); remote.Write(edited.ActivityAddress + 0xC, edited.StartHour * 3600f); remote.Write(edited.ActivityAddress + 0x14, edited.EndHour * 3600f);
                if (remote.Read<byte>(edited.AttendanceAddress + 0x11) != edited.Priority || Math.Abs(remote.Read<float>(edited.ActivityAddress + 0xC) / 3600f - edited.StartHour) > 0.001f || Math.Abs(remote.Read<float>(edited.ActivityAddress + 0x14) / 3600f - edited.EndHour) > 0.001f) throw new InvalidOperationException("Contract schedule did not pass read-back verification.");
            }
            catch { remote.Write(edited.AttendanceAddress + 0x11, currentPriority); remote.Write(edited.ActivityAddress + 0xC, currentStart * 3600f); remote.Write(edited.ActivityAddress + 0x14, currentEnd * 3600f); throw; }
        }
        return $"Saved attendance schedule {edited.StartHour:0.##}:00–{edited.EndHour:0.##}:00, priority {edited.Priority}.";
    }

    private string ResolveEvent(uint id) => _eventCatalog.TryGetValue(id, out string? label) ? label : $"Unknown metadata ID {id}";
    private string ResolveCensusName(ulong id, Dictionary<ulong, CensusNames> census) => census.TryGetValue(id, out CensusNames names) && names.Found ? $"{_catalog.Name(names.FirstNameId)} {_catalog.Surname(names.SurnameId)} (0x{id:X})" : $"0x{id:X}";
    private static int ReadPacked(byte[] code, int byteOffset, int bitOffset, int bitLength) { uint word = BinaryPrimitives.ReadUInt32BigEndian(code.AsSpan(byteOffset, 4)); int shift = 32 - bitOffset - bitLength; return (int)((word >> shift) & ((1u << bitLength) - 1)); }
    private static void WritePacked(byte[] code, int byteOffset, int bitOffset, int bitLength, int value) { int shift = 32 - bitOffset - bitLength; uint mask = ((1u << bitLength) - 1u) << shift; uint word = BinaryPrimitives.ReadUInt32BigEndian(code.AsSpan(byteOffset, 4)); word = (word & ~mask) | (((uint)value << shift) & mask); BinaryPrimitives.WriteUInt32BigEndian(code.AsSpan(byteOffset, 4), word); }

    private static (ulong Demographic, ulong Career) ResolveMetadataRoots(RemoteProcess remote, ulong npcData)
    {
        ulong metadata = remote.ReadPointer(Add(npcData, 0xB8));
        ulong demographic = remote.ReadPointer(Add(metadata, 0x18));
        ulong career = remote.ReadPointer(Add(metadata, 0x60));
        if (!remote.IsRangeReadable(demographic, 0x280) || !remote.IsRangeReadable(career, 0x310))
            throw new InvalidOperationException("The selected operative's metadata chain is incomplete.");
        return (demographic, career);
    }

    private static ulong ResolveAdvancedAddress(RemoteProcess remote, ulong npcData, AdvancedFieldDescriptor field)
    {
        ulong root = field.Scope switch
        {
            "npc" => npcData,
            "demographic" => ResolveMetadataRoots(remote, npcData).Demographic,
            "career" => ResolveMetadataRoots(remote, npcData).Career,
            "affinity" => remote.ReadPointer(Add(npcData, 0xB0)),
            "working" => remote.ReadPointer(Add(npcData, 0xC0)),
            "voiceprofile" => remote.ReadPointer(Add(npcData, 0x40)),
            "characterdeck" => remote.ReadPointer(Add(npcData, 0x70)),
            _ => throw new InvalidOperationException($"Unknown metadata scope {field.Scope}.")
        };
        ulong address = Add(root, field.Offset);
        if (!remote.IsRangeReadable(address, field.Length)) throw new InvalidOperationException($"{field.DisplayName} address is invalid.");
        return address;
    }

    private static byte[] ParseExactHex(string text, int length, string label)
    {
        string withoutPrefixes = text.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        string compact = new(withoutPrefixes.Where(character => !char.IsWhiteSpace(character) && character is not '-' and not ':').ToArray());
        if (compact.Length != length * 2) throw new InvalidOperationException($"{label} must contain exactly {length} bytes ({length * 2} hexadecimal digits).");
        if (compact.Any(character => !Uri.IsHexDigit(character))) throw new InvalidOperationException($"{label} contains invalid hexadecimal text.");
        try { return Convert.FromHexString(compact); }
        catch (FormatException) { throw new InvalidOperationException($"{label} contains invalid hexadecimal text."); }
    }

    private ulong ResolveNpcData(RemoteProcess remote, ulong manager, OperativeRecord operative)
    {
        _persistentHumanFunction = ResolveRelativeCall(_persistentHumanFunction, "48 81 C2 60 04 00 00 48 89 C1 E8 ?? ?? ?? ??", 10);
        ulong persistent = remote.ExecuteFunction(_persistentHumanFunction, manager, Add(operative.OperativeAddress, 0x10));
        if (persistent == 0 || !remote.IsRangeReadable(persistent, 0xB0)) throw new InvalidOperationException("The game did not return PersistentHuman data for this operative.");
        ulong handle = remote.ReadPointer(Add(persistent, 0xA8));
        return remote.ReadPointer(Add(handle, 0x18));
    }

    private ulong ResolveRelativeCall(ulong cached, string pattern, int callOffset)
    {
        if (cached != 0) return cached;
        var remote = RequireRemote();
        var module = _duniaModule ?? throw new InvalidOperationException("Dunia module is unavailable.");
        ulong call = PatternScanner.FindUnique(remote, module, pattern) + (ulong)callOffset;
        int displacement = remote.Read<int>(call + 1);
        return checked((ulong)((long)call + 5 + displacement));
    }

    internal void RemoveOperative(OperativeRecord selected)
    {
        Report($"SUPER RISKY: roster removal requested for {selected.FirstName} {selected.Surname} ({selected.IdHex}).");
        var remote = RequireRemote();
        ValidateRosterIdentity(selected, out ulong manager, out int count);
        ulong originalManager = manager;
        if (count <= 1) throw new InvalidOperationException("Refusing to remove the final operative.");
        ulong array = Add(manager, _config.Offsets.RosterArray);
        var backup = new ulong[count];
        for (int i = 0; i < count; i++) backup[i] = remote.Read<ulong>(array + (ulong)(i * 8));
        using (remote.SuspendThreads())
        {
            ValidateRosterIdentity(selected, out manager, out count);
            if (manager != originalManager || count != backup.Length)
                throw new InvalidOperationException("Roster manager or count changed during removal; no write was performed.");
            array = Add(manager, _config.Offsets.RosterArray);
            try
            {
                for (int i = selected.Index; i < count - 1; i++) remote.Write(array + (ulong)(i * 8), backup[i + 1]);
                remote.Write(array + (ulong)((count - 1) * 8), 0UL);
                remote.Write(Add(manager, _config.Offsets.RosterCount), count - 1);
            }
            catch
            {
                for (int i = 0; i < backup.Length; i++) remote.Write(array + (ulong)(i * 8), backup[i]);
                remote.Write(Add(manager, _config.Offsets.RosterCount), backup.Length);
                throw;
            }
        }
    }

    internal string Detach()
    {
        if (_remote is null) return "Not attached.";
        Exception? cleanupError = null;
        lock (_luaGate)
        {
            if (_luaQueue is not null)
            {
                foreach (string name in _luaToggles.ToArray())
                {
                    try { _luaQueue.Enqueue(LuaToggleScripts[name].Disable); }
                    catch (Exception ex) { cleanupError = cleanupError is null ? ex : new AggregateException(cleanupError, ex); }
                }
            }
            _luaToggles.Clear();
            try { _luaQueue?.Dispose(); } catch (Exception ex) { cleanupError = cleanupError is null ? ex : new AggregateException(cleanupError, ex); }
            _luaQueue = null;
        }
        lock (_teleportGate)
        {
            try { _teleport?.Dispose(); } catch (Exception ex) { cleanupError = cleanupError is null ? ex : new AggregateException(cleanupError, ex); }
            _teleport = null;
        }
        try { _cheats?.Dispose(); } catch (Exception ex) { cleanupError = cleanupError is null ? ex : new AggregateException(cleanupError, ex); }
        _cheats = null;
        try { _hook?.Dispose(); } catch (Exception ex) { cleanupError = cleanupError is null ? ex : new AggregateException(cleanupError, ex); }
        _hook = null;
        _remote.Dispose();
        _remote = null;
        _censusGlobal = 0;
        _duniaModule = null; _persistentHumanFunction = 0; _allocatorFunction = 0;
        if (cleanupError is not null) throw new InvalidOperationException("Detached, but hook cleanup reported: " + cleanupError.Message, cleanupError);
        return "Detached; original game bytes restored and remote allocation released.";
    }

    private Dictionary<ulong, CensusNames> ReadCensus()
    {
        var remote = RequireRemote();
        ulong manager = remote.ReadPointer(_censusGlobal);
        int count = remote.Read<int>(Add(manager, _config.Offsets.CensusCount));
        if (count < 0 || count > 100_000) throw new InvalidOperationException($"Census count {count} is invalid.");
        ulong array = remote.ReadPointer(Add(manager, _config.Offsets.CensusArray));
        var result = new Dictionary<ulong, CensusNames>();
        for (int i = 0; i < count; i++)
        {
            try
            {
                ulong entry = remote.ReadPointer(array + (ulong)(i * 8));
                ulong id = remote.Read<ulong>(Add(entry, _config.Offsets.CensusEntryId));
                ulong actor = remote.ReadPointer(Add(entry, _config.Offsets.CensusEntryActor));
                ulong descriptor = remote.ReadPointer(Add(actor, _config.Offsets.CensusActorDescriptor));
                ulong nameData = remote.ReadPointer(Add(descriptor, _config.Offsets.CensusDescriptorNameData));
                ulong firstAddress = Add(nameData, _config.Offsets.FirstNameLocId);
                ulong surnameAddress = Add(nameData, _config.Offsets.SurnameLocId);
                result[id] = new CensusNames(true, remote.Read<int>(firstAddress), remote.Read<int>(surnameAddress), firstAddress, surnameAddress);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Skipping invalid census entry {i}: {ex.Message}");
            }
        }
        return result;
    }

    private void ValidateRosterIdentity(OperativeRecord selected, out ulong manager, out int count)
    {
        var remote = RequireRemote();
        manager = RequireHook().ReadCapturedPointer();
        count = remote.Read<int>(Add(manager, _config.Offsets.RosterCount));
        if (count < 0 || count > _config.MaxRosterCount || selected.Index < 0 || selected.Index >= count)
            throw new InvalidOperationException("Roster changed or its count is invalid; refresh before editing.");
        ulong current = remote.ReadPointer(Add(Add(manager, _config.Offsets.RosterArray), checked(selected.Index * 8)));
        ulong currentId = remote.Read<ulong>(Add(current, _config.Offsets.OperativeId));
        if (current != selected.OperativeAddress || currentId != selected.Id)
            throw new InvalidOperationException("Roster changed since the panel was refreshed; no write was performed.");
    }

    private RemoteProcess RequireRemote() => _remote ?? throw new InvalidOperationException("Not attached. Type 'attach' first.");
    private void Report(string message) => Activity?.Invoke(message);
    internal string ReportError(string code, string operation, Exception error)
    {
        Exception root = error.GetBaseException();
        string message = $"[{code}] {operation}: {root.Message} | {root.GetType().Name} | HRESULT 0x{root.HResult:X8}";
        Report("ERROR " + message);
        return message;
    }
    private InlineHook RequireHook() => _hook ?? throw new InvalidOperationException("The manager hook is not installed.");
    private static ulong Add(ulong address, int offset) => checked((ulong)((long)address + offset));
    private static string FormatHex(byte[] bytes) => string.Join(" ", bytes.Select(value => value.ToString("X2")));
    private static byte[] ParseAppearanceCode(string text, string label)
    {
        string withoutPrefixes = text.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        string compact = new(withoutPrefixes.Where(character => !char.IsWhiteSpace(character) && character is not '-' and not ':').ToArray());
        if (compact.Length != 48) throw new InvalidOperationException($"The {label} code must contain exactly 24 bytes (48 hexadecimal digits). No changes were written.");
        if (compact.Any(character => !Uri.IsHexDigit(character))) throw new InvalidOperationException($"The {label} code contains invalid hexadecimal data. No changes were written.");
        try { return Convert.FromHexString(compact); }
        catch (FormatException) { throw new InvalidOperationException($"The {label} code contains invalid hexadecimal data. No changes were written."); }
    }
    public void Dispose() { if (_remote is not null) Detach(); }
    internal void AbandonWithoutCleanup()
    {
        if (_remote is null) return;
        Report("WARNING: closing without restoring active hooks or cheats, as selected in Settings. They remain until the game exits.");
        _teleport = null; _luaQueue = null; _cheats = null; _hook = null;
        _luaToggles.Clear();
        _remote.Dispose(); _remote = null; _duniaModule = null; _censusGlobal = 0;
    }
    private readonly record struct CensusNames(bool Found, int FirstNameId, int SurnameId, ulong FirstNameAddress, ulong SurnameAddress);
}

internal sealed record AdvancedFieldDescriptor(string Key, string DisplayName, string Scope, int Offset, int Length, string Description);

internal sealed record PerkSnapshot(IReadOnlyList<uint> Ids, int Capacity, bool Inline);
