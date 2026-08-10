using System.Diagnostics;
using WDLConsoleCompanion.Models;

namespace WDLConsoleCompanion.Services;

internal sealed class CheatManager : IDisposable
{
    private readonly RemoteProcess _remote;
    private readonly ProcessModule _module;
    private readonly Dictionary<string, List<CheatPatchConfig>> _configuredPatches;
    private readonly Dictionary<string, IDisposable> _active = new(StringComparer.OrdinalIgnoreCase);
    private CodeCaveHook? _playerCapture;

    internal CheatManager(RemoteProcess remote, ProcessModule module, Dictionary<string, List<CheatPatchConfig>> configuredPatches) { _remote = remote; _module = module; _configuredPatches = configuredPatches; }

    internal IReadOnlyCollection<string> Active => _active.Keys.OrderBy(x => x).ToArray();
    internal bool IsActive(string name) => _active.ContainsKey(Normalize(name));
    internal bool IsSupported(string name) => Normalize(name) != "noreload" || FindConfiguredPatch("noreload") is not null;

    internal string Set(string requestedName, bool enabled)
    {
        string name = Normalize(requestedName);
        if (!IsSupported(name)) throw new InvalidOperationException($"{Display(name)} is unavailable for {_module.ModuleName}. The configured patch targets a different engine build; the live DX11 module has no verified equivalent.");
        bool active = _active.ContainsKey(name);
        if (active == enabled) return $"{Display(name)} is already {(enabled ? "ON" : "OFF")}.";
        if (enabled) Enable(name); else Disable(name);
        return $"{Display(name)}: {(enabled ? "ON" : "OFF")}";
    }

    private void Enable(string name)
    {
        IDisposable resource = name switch
        {
            "godmode" => EnableGodMode(),
            "notrace" => EnableNoTrace(),
            "infammo" => EnableInfiniteAmmo(),
            "noreload" => EnableConfiguredPatch("noreload"),
            "norecoil" => EnablePatch("8B 09 89 4E 08", 0, "8B 09", "90 90"),
            "fastsearch" => EnableFastSearch(),
            "hackcooldown" => EnableHackerCooldown(),
            "freezehack" => EnablePatch("F3 0F 5C C6 F3 0F 11 86 E4 00 00 00", 0, "F3 0F 5C C6", "90 90 90 90"),
            "dronerange" => EnableDroneRange(),
            "dronehealth" => EnableDroneHealth(),
            "onehitkill" => EnableOneHitKill(),
            _ => throw new InvalidOperationException($"Unknown cheat '{name}'.")
        };
        _active.Add(name, resource);
    }

    private void Disable(string name)
    {
        if (!_active.Remove(name, out IDisposable? resource)) return;
        try { resource.Dispose(); }
        catch { _active[name] = resource; throw; }
    }

    private IDisposable EnablePatch(string pattern, int offset, string expected, string replacement)
    {
        ulong match = PatternScanner.FindUnique(_remote, _module, pattern);
        return MemoryPatch.Install(_remote, Add(match, offset), PatternScanner.ParseBytes(expected), PatternScanner.ParseBytes(replacement));
    }

    private CheatPatchConfig? FindConfiguredPatch(string name) => _configuredPatches.TryGetValue(name, out List<CheatPatchConfig>? candidates)
        ? candidates.FirstOrDefault(candidate => candidate.Modules.Contains(_module.ModuleName, StringComparer.OrdinalIgnoreCase)) : null;

    private IDisposable EnableConfiguredPatch(string name)
    {
        CheatPatchConfig patch = FindConfiguredPatch(name) ?? throw new InvalidOperationException($"No {Display(name)} signature is configured for {_module.ModuleName}.");
        return EnablePatch(patch.Pattern, patch.Offset, patch.ExpectedBytes, patch.ReplacementBytes);
    }

    private IDisposable EnableNoTrace()
    {
        IDisposable? wanted = null;
        IDisposable? stealth = null;
        try
        {
            ulong wantedMatch = PatternScanner.FindUnique(_remote, _module, "B3 01 80 B9 3C 01 00 00 00");
            byte[] wantedOriginal = PatternScanner.ParseBytes("B3 01 80 B9 3C 01 00 00 00");
            wanted = CodeCaveHook.Install(_remote, wantedMatch, wantedOriginal, (codeBase, _) =>
            {
                var code = new List<byte>();
                code.AddRange([0xC6, 0x81, 0x3C, 0x01, 0x00, 0x00, 0x00]); // mov byte [rcx+13C],0
                code.AddRange([0xB3, 0x01]);                               // mov bl,1
                code.AddRange([0x80, 0xB9, 0x44, 0x01, 0x00, 0x00, 0x00]); // cmp byte [rcx+144],0
                CodeCaveHook.AddJumpBack(code, codeBase, wantedMatch + (ulong)wantedOriginal.Length);
                return code.ToArray();
            });
            stealth = EnablePatch("41 80 7E 58 00 0F 85", 0, "41 80 7E 58 00", "41 C6 46 58 00");
            return new CompositeResource(stealth, wanted);
        }
        catch { try { stealth?.Dispose(); } catch { } try { wanted?.Dispose(); } catch { } throw; }
    }

    private IDisposable EnableInfiniteAmmo()
    {
        ulong match = PatternScanner.FindUnique(_remote, _module, "29 E9 89 48 10");
        byte[] original = PatternScanner.ParseBytes("29 E9 89 48 10");
        return CodeCaveHook.Install(_remote, match, original, (codeBase, _) =>
        {
            var code = new List<byte>();
            code.AddRange([0xB9, 0xE7, 0x03, 0x00, 0x00]); // mov ecx,999
            code.AddRange([0x89, 0x48, 0x10]);             // mov [rax+10],ecx
            CodeCaveHook.AddJumpBack(code, codeBase, match + (ulong)original.Length);
            return code.ToArray();
        });
    }

    private IDisposable EnableFastSearch()
    {
        ulong match = PatternScanner.FindUnique(_remote, _module, "F3 0F 58 B6 88 02 00 00");
        byte[] original = PatternScanner.ParseBytes("F3 0F 58 B6 88 02 00 00");
        return CodeCaveHook.Install(_remote, match, original, (codeBase, dataAddress) =>
        {
            var code = new List<byte>();
            code.AddRange([0xD9, 0x86, 0x88, 0x02, 0x00, 0x00]); // fld dword [rsi+288]
            code.AddRange([0xD8, 0x0D]);                          // fmul dword [rip+disp32]
            CodeCaveHook.AddRel32(code, codeBase, dataAddress);
            code.AddRange([0xD9, 0x9E, 0x88, 0x02, 0x00, 0x00]); // fstp dword [rsi+288]
            code.AddRange(original);                              // addss xmm6,[rsi+288]
            CodeCaveHook.AddJumpBack(code, codeBase, match + (ulong)original.Length);
            return code.ToArray();
        }, BitConverter.GetBytes(2.0f));
    }

    private IDisposable EnableHackerCooldown()
    {
        ulong match = PatternScanner.FindUnique(_remote, _module, "F3 41 0F 10 4C 34 10"); byte[] original = PatternScanner.ParseBytes("F3 41 0F 10 4C 34 10");
        return CodeCaveHook.Install(_remote, match, original, (codeBase, _) =>
        {
            var code = new List<byte>();
            code.AddRange([0x43, 0xC7, 0x44, 0x34, 0x10, 0, 0, 0, 0]); // mov dword [r12+rsi+10],0
            code.AddRange(original); CodeCaveHook.AddJumpBack(code, codeBase, match + (ulong)original.Length); return code.ToArray();
        });
    }

    private IDisposable EnableDroneRange()
    {
        ulong match = PatternScanner.FindUnique(_remote, _module, "F3 0F 59 86 C8 01 00 00 F3"); byte[] original = PatternScanner.ParseBytes("F3 0F 59 86 C8 01 00 00");
        return CodeCaveHook.Install(_remote, match, original, (codeBase, _) =>
        {
            var code = new List<byte>();
            code.AddRange([0xC7, 0x86, 0xC0, 0x01, 0x00, 0x00, 0x00, 0x00, 0x96, 0x43]); // mov [rsi+1C0],300.0f
            code.AddRange(original); CodeCaveHook.AddJumpBack(code, codeBase, match + (ulong)original.Length); return code.ToArray();
        });
    }

    private IDisposable EnableGodMode()
    {
        EnsurePlayerCapture();
        try
        {
            byte[] healthOriginal = PatternScanner.ParseBytes("F3 0F 10 41 18");
            ulong playerData = _playerCapture!.DataAddress;
            return CodeCaveHook.InstallOrAdopt(_remote, _module, "F3 0F 10 41 18 F3 0F 5C 41 20", "E9 ?? ?? ?? ?? F3 0F 5C 41 20", healthOriginal, (codeBase, _, patchAddress) =>
            {
                var code = new List<byte>();
                code.AddRange([0x48, 0x39, 0x35]);       // cmp [rip+disp32],rsi
                CodeCaveHook.AddRel32(code, codeBase, playerData);
                code.AddRange([0x75, 0x0A]);             // jne original
                code.AddRange([0xF3, 0x0F, 0x10, 0x41, 0x20]); // movss xmm0,[rcx+20]
                code.AddRange([0xF3, 0x0F, 0x11, 0x41, 0x18]); // movss [rcx+18],xmm0
                code.AddRange(healthOriginal);           // movss xmm0,[rcx+18]
                CodeCaveHook.AddJumpBack(code, codeBase, patchAddress + (ulong)healthOriginal.Length);
                return code.ToArray();
            });
        }
        catch { throw; }
    }

    private void EnsurePlayerCapture()
    {
        if (_playerCapture is not null) return;
        byte[] playerOriginal = PatternScanner.ParseBytes("48 8B 49 40 48 8B 79 30");
        _playerCapture = CodeCaveHook.InstallOrAdopt(_remote, _module, "48 8B 49 40 48 8B 79 30", "E9 ?? ?? ?? ?? 90 90 90", playerOriginal, (codeBase, dataAddress, patchAddress) =>
        {
            var code = new List<byte>(); code.AddRange([0x48, 0x8B, 0x49, 0x40]); code.AddRange([0x48, 0x89, 0x0D]); CodeCaveHook.AddRel32(code, codeBase, dataAddress); code.AddRange([0x48, 0x8B, 0x79, 0x30]); CodeCaveHook.AddJumpBack(code, codeBase, patchAddress + (ulong)playerOriginal.Length); return code.ToArray();
        }, new byte[8], adoptionFragments: [PatternScanner.ParseBytes("48 8B 49 40"), PatternScanner.ParseBytes("48 8B 79 30")]);
    }

    private IDisposable EnableOneHitKill()
    {
        EnsurePlayerCapture(); ulong playerData = _playerCapture!.DataAddress; byte[] original = PatternScanner.ParseBytes("48 89 5F 28 F3 0F 11 77 18");
        return CodeCaveHook.InstallOrAdopt(_remote, _module, "48 89 5F 28 F3 0F 11 77 18", "E9 ?? ?? ?? ?? 90 90 90 90", original, (codeBase, _, patchAddress) =>
        {
            var code = new List<byte>(); code.Add(0x56); code.AddRange([0x48, 0x8B, 0x77, 0x08]); code.AddRange([0x48, 0x39, 0x35]); CodeCaveHook.AddRel32(code, codeBase, playerData); code.AddRange([0x74, 0x03, 0x0F, 0x57, 0xF6, 0x5E]); code.AddRange(original); CodeCaveHook.AddJumpBack(code, codeBase, patchAddress + (ulong)original.Length); return code.ToArray();
        });
    }

    private IDisposable EnableDroneHealth()
    {
        CodeCaveHook? capture = null;
        try
        {
            ulong captureMatch = PatternScanner.FindUnique(_remote, _module, "49 8B BE 20 01 00 00 48 C1 EB"); byte[] captureOriginal = PatternScanner.ParseBytes("49 8B BE 20 01 00 00");
            capture = CodeCaveHook.Install(_remote, captureMatch, captureOriginal, (codeBase, dataAddress) =>
            {
                var code = new List<byte>(); code.Add(0x52); code.AddRange([0x49, 0x8B, 0x96, 0x48, 0x01, 0x00, 0x00]); code.AddRange([0x48, 0x89, 0x15]); CodeCaveHook.AddRel32(code, codeBase, dataAddress); code.Add(0x5A); code.AddRange(captureOriginal); CodeCaveHook.AddJumpBack(code, codeBase, captureMatch + (ulong)captureOriginal.Length); return code.ToArray();
            }, new byte[8]);
            ulong healthMatch = PatternScanner.FindUnique(_remote, _module, "F3 0F 11 71 18 48"); byte[] healthOriginal = PatternScanner.ParseBytes("F3 0F 11 71 18"); ulong droneData = capture.DataAddress;
            CodeCaveHook health = CodeCaveHook.Install(_remote, healthMatch, healthOriginal, (codeBase, _) =>
            {
                var code = new List<byte>(); code.Add(0x56); code.AddRange([0x48, 0x8B, 0x71, 0x08]); code.AddRange([0x48, 0x39, 0x35]); CodeCaveHook.AddRel32(code, codeBase, droneData); code.AddRange([0x75, 0x05, 0xF3, 0x0F, 0x10, 0x71, 0x20, 0x5E]); code.AddRange(healthOriginal); CodeCaveHook.AddJumpBack(code, codeBase, healthMatch + (ulong)healthOriginal.Length); return code.ToArray();
            });
            return new CompositeResource(health, capture);
        }
        catch { try { capture?.Dispose(); } catch { } throw; }
    }

    public void Dispose()
    {
        var errors = new List<Exception>();
        foreach (var pair in _active.Reverse().ToArray())
        {
            try { pair.Value.Dispose(); _active.Remove(pair.Key); } catch (Exception ex) { errors.Add(ex); }
        }
        try { _playerCapture?.Dispose(); _playerCapture = null; } catch (Exception ex) { errors.Add(ex); }
        if (errors.Count > 0) throw new AggregateException("One or more cheats could not be restored.", errors);
    }

    internal static string Normalize(string name) => name.ToLowerInvariant().Replace("_", "").Replace("-", "") switch
    {
        "god" or "godmode" or "gfodmode" => "godmode",
        "notrace" or "nowanted" or "stealth" => "notrace",
        "ammo" or "infammo" or "infiniteammo" => "infammo",
        "noreload" => "noreload",
        "norecoil" => "norecoil",
        "fastsearch" or "fastendsearch" => "fastsearch",
        "hackcooldown" or "instantcooldowns" or "cooldowns" => "hackcooldown",
        "freezehack" or "freezetime" => "freezehack",
        "dronerange" or "maxdronerange" => "dronerange",
        "dronehealth" or "infdronehealth" => "dronehealth",
        "onehitkill" or "ohk" => "onehitkill",
        "immortal" or "immortalmode" => "immortal",
        "disablefelony" or "nofelony" => "disablefelony",
        "disabledetection" or "undetectable" => "disabledetection",
        "endchase" => "endchase",
        "spawnracecar" or "racecar" => "spawnracecar",
        "spawnshop" or "dedsecshop" => "spawnshop",
        "distractall" => "distractall",
        "disruptall" => "disruptall",
        "tech" or "techpoints" => "techpoints",
        var value => value
    };

    internal static string Display(string name) => Normalize(name) switch
    {
        "godmode" => "God Mode", "notrace" => "No Trace", "infammo" => "Infinite Ammo",
        "noreload" => "No Reload", "norecoil" => "No Recoil", "fastsearch" => "Fast Search",
        "hackcooldown" => "Instant Hacker Cooldowns", "freezehack" => "Freeze Hack Timer", "dronerange" => "Maximum Drone Range",
        "dronehealth" => "Infinite Drone Health", "onehitkill" => "One Hit Kill", "immortal" => "Immortal Mode",
        "disablefelony" => "Disable Felony System", "disabledetection" => "Disable Detection", "endchase" => "End Felony Chase",
        "spawnracecar" => "Spawn Racecar", "spawnshop" => "Spawn DedSec Shop", "distractall" => "Distract Everyone", "disruptall" => "Disrupt Everyone", _ => name
    };
    private static ulong Add(ulong address, int offset) => checked((ulong)((long)address + offset));

    private sealed class CompositeResource(params IDisposable[] resources) : IDisposable
    {
        public void Dispose()
        {
            var errors = new List<Exception>();
            foreach (IDisposable resource in resources)
                try { resource.Dispose(); } catch (Exception ex) { errors.Add(ex); }
            if (errors.Count > 0) throw new AggregateException(errors);
        }
    }
}
