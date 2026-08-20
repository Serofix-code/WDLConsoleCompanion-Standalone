using System.Diagnostics;
using System.Text;

namespace WDLConsoleCompanion.Services;

internal sealed class GameLuaQueue : IDisposable
{
    private const int Capacity = 10, SlotSize = 256, BuffersOffset = 0x20;
    private readonly RemoteProcess _remote;
    private readonly CodeCaveHook _hook;
    private readonly object _gate = new();
    private readonly object _queryGate = new();

    private GameLuaQueue(RemoteProcess remote, CodeCaveHook hook) { _remote = remote; _hook = hook; }

    internal ulong PendingCount => _remote.Read<ulong>(_hook.DataAddress);

    internal static GameLuaQueue Install(RemoteProcess remote, ProcessModule module)
    {
        ulong luaMatch = PatternScanner.FindUnique(remote, module, "48 8B 0D ?? ?? ?? ?? 48 8D 15 ?? ?? ?? ?? 45 31 C0 E8 ?? ?? ?? ?? 80 3D ?? ?? ?? ?? 00 74");
        int globalDisplacement = remote.Read<int>(luaMatch + 3);
        ulong scriptSystemGlobal = checked((ulong)((long)luaMatch + 7 + globalDisplacement));
        ulong call = luaMatch + 0x11;
        if (remote.Read<byte>(call) != 0xE8) throw new InvalidOperationException("The game Lua execution call has an unexpected instruction layout.");
        int callDisplacement = remote.Read<int>(call + 1);
        ulong executeLua = checked((ulong)((long)call + 5 + callDisplacement));

        byte[] original = PatternScanner.ParseBytes("66 C7 41 18 00 01");
        CodeCaveHook hook = CodeCaveHook.InstallOrAdopt(remote, module,
            "66 C7 41 18 00 01 48 8B 41 08 4C 8D 61 10 48 85 C0",
            "E9 ?? ?? ?? ?? 90 48 8B 41 08 4C 8D 61 10 48 85 C0", original,
            (codeBase, data, patchAddress) => BuildCode(codeBase, data, patchAddress, original, scriptSystemGlobal, executeLua),
            new byte[BuffersOffset + Capacity * SlotSize], [original]);
        return new GameLuaQueue(remote, hook);
    }

    private static byte[] BuildCode(ulong codeBase, ulong data, ulong patchAddress, byte[] original, ulong scriptSystemGlobal, ulong executeLua)
    {
        var c = new List<byte>();
        c.AddRange([0x48,0x8B,0x05]); CodeCaveHook.AddRel32(c, codeBase, data); // count
        c.AddRange([0x48,0x85,0xC0,0x75,0x05,0xE9]); int emptyJumpDisp = c.Count; c.AddRange([0,0,0,0]);
        c.Add(0x9C); c.AddRange([0x50,0x53,0x51,0x52,0x41,0x50,0x41,0x51,0x41,0x52,0x41,0x53,0x55]);
        c.AddRange([0x48,0x89,0xE5,0x48,0x83,0xE4,0xF0,0x48,0x83,0xEC,0x20]);
        c.AddRange([0x48,0x8B,0x05]); CodeCaveHook.AddRel32(c, codeBase, data + 8); // read index
        c.AddRange([0x48,0x89,0xC3,0x48,0xC1,0xE3,0x08]);
        c.AddRange([0x48,0x8D,0x15]); CodeCaveHook.AddRel32(c, codeBase, data + BuffersOffset);
        c.AddRange([0x48,0x01,0xDA]);
        c.AddRange([0x48,0x8B,0x0D]); CodeCaveHook.AddRel32(c, codeBase, scriptSystemGlobal);
        c.AddRange([0x45,0x31,0xC0,0xE8]); CodeCaveHook.AddRel32(c, codeBase, executeLua);
        c.AddRange([0xF0,0x48,0xFF,0x05]); CodeCaveHook.AddRel32(c, codeBase, data + 0x18); // completed count
        c.AddRange([0x48,0xFF,0xC0,0x48,0x83,0xF8,Capacity,0x72,0x02,0x31,0xC0]);
        c.AddRange([0x48,0x89,0x05]); CodeCaveHook.AddRel32(c, codeBase, data + 8);
        c.AddRange([0xF0,0x48,0xFF,0x0D]); CodeCaveHook.AddRel32(c, codeBase, data);
        c.AddRange([0x48,0x89,0xEC,0x5D,0x41,0x5B,0x41,0x5A,0x41,0x59,0x41,0x58,0x5A,0x59,0x5B,0x58,0x9D]);
        int originalOffset = c.Count; c.AddRange(original); CodeCaveHook.AddJumpBack(c, codeBase, patchAddress + (ulong)original.Length);
        int displacement = checked((int)((long)codeBase + originalOffset - ((long)codeBase + emptyJumpDisp + 4)));
        byte[] value = BitConverter.GetBytes(displacement); for (int i=0;i<4;i++) c[emptyJumpDisp+i]=value[i];
        return c.ToArray();
    }

    internal void Enqueue(string script)
    {
        _ = EnqueueTracked(script);
    }

    private ulong EnqueueTracked(string script)
    {
        byte[] text = Encoding.UTF8.GetBytes(script);
        if (text.Length >= SlotSize) throw new InvalidOperationException("The game script command is too long for the safe queue slot.");
        lock (_gate)
        using (_remote.SuspendThreads())
        {
            ulong count = _remote.Read<ulong>(_hook.DataAddress);
            if (count >= Capacity) throw new InvalidOperationException("The game script queue is full; wait a moment and try again.");
            ulong completed = _remote.Read<ulong>(_hook.DataAddress + 0x18);
            ulong write = _remote.Read<ulong>(_hook.DataAddress + 0x10);
            if (write >= Capacity) throw new InvalidOperationException("The game script queue index is invalid.");
            byte[] slot = new byte[SlotSize]; text.CopyTo(slot, 0);
            _remote.WriteBytes(_hook.DataAddress + BuffersOffset + write * SlotSize, slot);
            _remote.Write(_hook.DataAddress + 0x10, (write + 1) % Capacity);
            _remote.Write(_hook.DataAddress, count + 1);
            return checked(completed + count + 1);
        }
    }

    private async Task WaitForHandoffAsync(ulong completionTarget, CancellationToken cancellationToken, DateTime deadline)
    {
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_remote.Read<ulong>(_hook.DataAddress + 0x18) >= completionTarget) return;
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("The game did not accept the queued action in time. Return to active gameplay and try again.");
    }

    internal async Task EnqueuePacedAsync(string script, CancellationToken cancellationToken, int submissions = 3)
    {
        if (submissions is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(submissions));
        for (int i = 0; i < submissions; i++)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            ulong target = EnqueueTracked(script);
            await WaitForHandoffAsync(target, cancellationToken, deadline).ConfigureAwait(false);
            if (i + 1 < submissions) await Task.Delay(750, cancellationToken).ConfigureAwait(false);
        }
        // Allow the world/entity update that follows the final handoff to become visible.
        await Task.Delay(750, cancellationToken).ConfigureAwait(false);
    }

    internal async Task EnqueueHandoffAsync(string script, CancellationToken cancellationToken)
    {
        ulong target = EnqueueTracked(script);
        await WaitForHandoffAsync(target, cancellationToken, DateTime.UtcNow.AddSeconds(10)).ConfigureAwait(false);
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
    }

    internal Task EnqueueRewardAsync(string recordName, bool displayFeedback, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recordName) || recordName.Length > 180 || recordName.Contains('\'') || recordName.Contains('\\'))
            throw new ArgumentException("The clothing database record name is invalid.", nameof(recordName));
        // Legion's native reward binding ends the current Lua command after accepting a readable ItemDB
        // record, so code placed after ExecuteReward_V2 is not guaranteed to run. Track the game-thread
        // handoff itself and send every reward in a separate command.
        return EnqueueHandoffAsync($"ExecuteReward_V2(GetLocalPlayerEntityId(),'{recordName}',{(displayFeedback ? 1 : 0)})", cancellationToken);
    }

    internal FacingQuery QueryFacing()
    {
        lock (_queryGate)
        {
            string path = Path.Combine(Path.GetTempPath(), $"wfr{_remote.Process.Id}");
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            string luaPath = path.Replace('\\', '/').Replace("'", "\\'");
            ulong completedBefore = _remote.Read<ulong>(_hook.DataAddress + 0x18);
            Enqueue($"f=io.open('{luaPath}','w');if f then o,l=pcall(GetReticleHitLocation);if o and l then f:write('R,'..table.concat(l,','))else f:write('A,'..GetEntityAngle(GetLocalPlayerEntityId(),2))end f:close()end");
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(25);
                try
                {
                    if (!File.Exists(path)) continue;
                    string[] parts = File.ReadAllText(path).Split(',');
                    if (parts.Length == 4 && parts[0] == "R" && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                        float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z) &&
                        float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z))
                    {
                        try { File.Delete(path); } catch { }
                        // Game Lua reports X/Y/Z, while the verified transform stores them at +80/+84/+88.
                        // GamePosition follows the existing display/write layout: X, elevation, horizontal Z.
                        return new FacingQuery(new GamePosition(x, z, y), null);
                    }
                    if (parts.Length == 2 && parts[0] == "A" && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float angle) && float.IsFinite(angle))
                    {
                        try { File.Delete(path); } catch { }
                        return new FacingQuery(null, angle);
                    }
                }
                catch (IOException) { }
            }
            bool consumed = _remote.Read<ulong>(_hook.DataAddress + 0x18) != completedBefore;
            throw new TimeoutException(consumed ? "The game processed the facing request, but its Lua file channel did not return data. Restart the companion once and try again." : "The game did not process the facing request within eight seconds. Resume active gameplay and try again.");
        }
    }

    public void Dispose()
    {
        for (int i=0;i<20 && _remote.Read<ulong>(_hook.DataAddress)>0;i++) Thread.Sleep(25);
        _hook.Dispose();
    }
}

internal readonly record struct FacingQuery(GamePosition? Reticle, float? AngleDegrees);
