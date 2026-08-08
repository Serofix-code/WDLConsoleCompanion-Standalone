using System.Diagnostics;
using System.Text.Json;
using WDLConsoleCompanion.Models;

namespace WDLConsoleCompanion.Services;

internal sealed class TrainerSession : IDisposable
{
    private readonly TrainerConfig _config;
    private readonly LocalizationCatalog _catalog;
    private readonly Dictionary<string, List<MetadataOption>> _metadataCatalog;
    private RemoteProcess? _remote;
    private InlineHook? _hook;
    private CheatManager? _cheats;
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
            _cheats = new CheatManager(remote, dunia);
            _duniaModule = dunia;
            _remote = remote;
            string hookState = _hook.WasAdopted ? "adopted existing companion hook" : "installed new hook";
            return $"Attached to PID {remote.Process.Id}; module {dunia.ModuleName}; {hookState} at 0x{patchAddress:X}; census global 0x{_censusGlobal:X}.";
        }
        catch { remote.Dispose(); throw; }
    }

    internal IReadOnlyList<OperativeRecord> ReadRoster()
    {
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
        var remote = RequireRemote();
        ValidateRosterIdentity(edited, out _, out _);
        var census = ReadCensus();
        if (!census.TryGetValue(edited.Id, out var names) || !names.Found) throw new InvalidOperationException("Selected operative is not present in the census.");
        int first = _catalog.ResolveName(edited.FirstName, edited.FirstNameLocId);
        int surname = _catalog.ResolveSurname(edited.Surname, edited.SurnameLocId);
        byte[] currentAppearance = ParseAppearanceCode(edited.CurrentAppearanceCode, "current appearance");
        byte[] defaultAppearance = ParseAppearanceCode(edited.DefaultAppearanceCode, "default appearance");
        if (edited.Availability is < 0 or > 4) throw new InvalidOperationException("Availability must be between 0 and 4.");
        if (edited.Origin is not (0 or 1 or 2 or 4 or 5 or 6 or 7)) throw new InvalidOperationException("Origin is not a value supported by the CT table.");
        ulong availabilityAddress = Add(edited.OperativeAddress, _config.Offsets.OperativeAvailability);
        ulong originAddress = Add(edited.OperativeAddress, _config.Offsets.OperativeOrigin);
        ulong currentAppearanceAddress = Add(edited.OperativeAddress, _config.Offsets.CurrentAppearance);
        ulong defaultAppearanceAddress = Add(edited.OperativeAddress, _config.Offsets.DefaultAppearance);
        int oldFirst = remote.Read<int>(names.FirstNameAddress);
        int oldSurname = remote.Read<int>(names.SurnameAddress);
        int oldAvailability = remote.Read<int>(availabilityAddress);
        int oldOrigin = remote.Read<int>(originAddress);
        byte[] oldCurrentAppearance = remote.ReadBytes(currentAppearanceAddress, 24);
        byte[] oldDefaultAppearance = remote.ReadBytes(defaultAppearanceAddress, 24);
        using (remote.SuspendThreads())
        {
            ValidateRosterIdentity(edited, out _, out _);
            try
            {
                remote.Write(names.FirstNameAddress, first);
                remote.Write(names.SurnameAddress, surname);
                remote.Write(availabilityAddress, edited.Availability);
                remote.Write(originAddress, edited.Origin);
                remote.WriteBytes(currentAppearanceAddress, currentAppearance);
                remote.WriteBytes(defaultAppearanceAddress, defaultAppearance);
                if (remote.Read<int>(availabilityAddress) != edited.Availability || remote.Read<int>(originAddress) != edited.Origin ||
                    !remote.ReadBytes(currentAppearanceAddress, 24).SequenceEqual(currentAppearance) ||
                    !remote.ReadBytes(defaultAppearanceAddress, 24).SequenceEqual(defaultAppearance))
                    throw new InvalidOperationException("The game did not retain one or more operative values during immediate write verification.");
            }
            catch
            {
                remote.Write(names.FirstNameAddress, oldFirst);
                remote.Write(names.SurnameAddress, oldSurname);
                remote.Write(availabilityAddress, oldAvailability);
                remote.Write(originAddress, oldOrigin);
                remote.WriteBytes(currentAppearanceAddress, oldCurrentAppearance);
                remote.WriteBytes(defaultAppearanceAddress, oldDefaultAppearance);
                throw;
            }
        }
        return $"Saved and verified {edited.FirstName} {edited.Surname}: availability {edited.Availability}, origin {edited.Origin}, and both appearance codes.";
    }

    internal string SetCheat(string name, bool enabled)
    {
        if (!IsAttachedProcessAlive) throw new InvalidOperationException("The game process is not attached and running.");
        return (_cheats ?? throw new InvalidOperationException("Cheat manager is unavailable.")).Set(name, enabled);
    }

    internal string ToggleCheat(string name, string? requestedState)
    {
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
        string[] names = ["godmode", "notrace", "infammo", "noreload", "norecoil", "fastsearch"];
        var active = _cheats.Active.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return string.Join(Environment.NewLine, names.Select(name => $"  {CheatManager.Display(name),-15} {(active.Contains(name) ? "ON" : "OFF")}"));
    }
    internal bool IsCheatActive(string name) => _cheats?.IsActive(name) == true;

    internal PerkSnapshot ReadPerks(OperativeRecord operative)
    {
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
        if (ids.Count > 80 || ids.Any(id => id == 0)) throw new InvalidOperationException("Use 1-80 non-zero perk IDs.");
        if (ids.Distinct().Count() != ids.Count) throw new InvalidOperationException("Duplicate perk IDs are not allowed.");
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
        var remote = RequireRemote();
        ValidateRosterIdentity(operative, out ulong manager, out _);
        ulong npcData = ResolveNpcData(remote, manager, operative);
        var result = new List<AdvancedOperativeField>(AdvancedFieldDescriptors.Length);
        foreach (AdvancedFieldDescriptor field in AdvancedFieldDescriptors)
        {
            IReadOnlyList<MetadataOption> options = _metadataCatalog.TryGetValue(field.Key, out List<MetadataOption>? known) ? known : [];
            try
            {
                result.Add(new AdvancedOperativeField { Key = field.Key, DisplayName = field.DisplayName, Value = FormatHex(remote.ReadBytes(ResolveAdvancedAddress(remote, npcData, field), field.Length)), Risk = "HIGH RISK", Description = field.Description, Options = options });
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
        try { _cheats?.Dispose(); } catch (Exception ex) { cleanupError = ex; }
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
    private readonly record struct CensusNames(bool Found, int FirstNameId, int SurnameId, ulong FirstNameAddress, ulong SurnameAddress);
}

internal sealed record AdvancedFieldDescriptor(string Key, string DisplayName, string Scope, int Offset, int Length, string Description);

internal sealed record PerkSnapshot(IReadOnlyList<uint> Ids, int Capacity, bool Inline);
