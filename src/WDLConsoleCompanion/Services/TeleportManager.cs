namespace WDLConsoleCompanion.Services;

internal readonly record struct GamePosition(float X, float Y, float Z)
{
    public override string ToString() => $"X {X:0.###}, Y {Y:0.###}, Z {Z:0.###}";
}

internal sealed class TeleportManager : IDisposable
{
    private readonly RemoteProcess _remote;
    private readonly CodeCaveHook _playerCapture;
    private readonly CodeCaveHook _coordinateCapture;
    private readonly CodeCaveHook _waypointCapture;
    private GamePosition? _saved;
    private GamePosition? _undo;
    private readonly List<GamePosition> _safetyHistory = [];
    private const int SafetyHistoryLimit = 12;
    internal bool RecoveredPreviousHooks => _playerCapture.WasAdopted || _coordinateCapture.WasAdopted || _waypointCapture.WasAdopted;

    private TeleportManager(RemoteProcess remote, CodeCaveHook playerCapture, CodeCaveHook coordinateCapture, CodeCaveHook waypointCapture)
    { _remote = remote; _playerCapture = playerCapture; _coordinateCapture = coordinateCapture; _waypointCapture = waypointCapture; }

    internal static TeleportManager Install(RemoteProcess remote, System.Diagnostics.ProcessModule module)
    {
        CodeCaveHook? player = null, coordinates = null, waypoint = null;
        try
        {
            byte[] playerOriginal = PatternScanner.ParseBytes("48 8B B8 D8 02 00 00");
            player = CodeCaveHook.InstallOrAdopt(remote, module,
                "48 8B B8 D8 02 00 00 48 85 FF 0F ?? ?? ?? ?? ?? 48 8B 07 48 89 F9 FF 90 B8",
                "E9 ?? ?? ?? ?? 90 90 48 85 FF 0F ?? ?? ?? ?? ?? 48 8B 07 48 89 F9 FF 90 B8", playerOriginal, (codeBase, dataAddress, patchAddress) =>
            {
                var code = new List<byte>(playerOriginal); code.AddRange([0x48, 0x89, 0x3D]); CodeCaveHook.AddRel32(code, codeBase, dataAddress); CodeCaveHook.AddJumpBack(code, codeBase, patchAddress + (ulong)playerOriginal.Length); return code.ToArray();
            }, new byte[8]);

            byte[] coordinateOriginal = PatternScanner.ParseBytes("48 8B 89 80 00 00 00"); ulong playerData = player.DataAddress;
            coordinates = CodeCaveHook.InstallOrAdopt(remote, module, "48 8B 89 80 00 00 00 48 89 08 89", "E9 ?? ?? ?? ?? 90 90 48 89 08 89", coordinateOriginal, (codeBase, dataAddress, patchAddress) =>
            {
                var code = new List<byte>();
                code.AddRange([0x48, 0x39, 0x1D]); CodeCaveHook.AddRel32(code, codeBase, playerData); // cmp [player],rbx
                code.AddRange([0x75, 0x07]); // jne original
                code.AddRange([0x48, 0x89, 0x0D]); CodeCaveHook.AddRel32(code, codeBase, dataAddress); // mov [coords],rcx
                code.AddRange(coordinateOriginal); CodeCaveHook.AddJumpBack(code, codeBase, patchAddress + (ulong)coordinateOriginal.Length); return code.ToArray();
            }, new byte[8]);

            byte[] waypointOriginal = PatternScanner.ParseBytes("F3 0F 5D 86 B8 00 00 00");
            waypoint = CodeCaveHook.InstallOrAdopt(remote, module, "F3 0F 5D 86 B8 00 00 00", "E9 ?? ?? ?? ?? 90 90 90", waypointOriginal, (codeBase, dataAddress, patchAddress) =>
            {
                var code = new List<byte>();
                code.Add(0x56); code.AddRange([0x48, 0x8B, 0x76, 0x18]);
                AddWaypointRead(code, codeBase, 0x70, dataAddress); AddWaypointRead(code, codeBase, 0x78, dataAddress + 4); AddWaypointRead(code, codeBase, 0x74, dataAddress + 8);
                code.Add(0x5E); code.AddRange(waypointOriginal); CodeCaveHook.AddJumpBack(code, codeBase, patchAddress + (ulong)waypointOriginal.Length); return code.ToArray();
            }, new byte[12]);
            return new TeleportManager(remote, player, coordinates, waypoint);
        }
        catch { try { waypoint?.Dispose(); } catch { } try { coordinates?.Dispose(); } catch { } try { player?.Dispose(); } catch { } throw; }
    }

    private static void AddWaypointRead(List<byte> code, ulong codeBase, byte sourceOffset, ulong destination)
    {
        code.AddRange([0xF3, 0x0F, 0x10, 0x46, sourceOffset]); code.AddRange([0xF3, 0x0F, 0x11, 0x05]); CodeCaveHook.AddRel32(code, codeBase, destination);
    }

    internal GamePosition ReadCurrent()
    {
        ulong pointer = _remote.Read<ulong>(_coordinateCapture.DataAddress);
        if (pointer == 0 || !_remote.IsRangeReadable(pointer + 0x80, 12)) throw new InvalidOperationException("Player coordinates are waiting for the game to publish the active-player pointer. Switch to another operative and back, then press Refresh.");
        return new GamePosition(_remote.Read<float>(pointer + 0x80), _remote.Read<float>(pointer + 0x88), _remote.Read<float>(pointer + 0x84));
    }

    internal GamePosition ObserveCurrent()
    {
        return ReadCurrent();
    }

    internal GamePosition ReadWaypoint()
    {
        var position = new GamePosition(_remote.Read<float>(_waypointCapture.DataAddress), _remote.Read<float>(_waypointCapture.DataAddress + 4), _remote.Read<float>(_waypointCapture.DataAddress + 8));
        if (position.X == 0 && position.Y == 0 && position.Z == 0) throw new InvalidOperationException("Waypoint coordinates have not been captured. Place a map waypoint, return to the world, then refresh.");
        return position;
    }

    internal void SaveCurrent() => _saved = ReadCurrent();
    internal GamePosition? LatestSafetyPosition => _safetyHistory.Count == 0 ? null : _safetyHistory[^1];
    internal int SafetyPositionCount => _safetyHistory.Count;
    internal GamePosition LoadSaved() => TeleportTo(_saved ?? throw new InvalidOperationException("No position has been saved in this companion session."));
    internal GamePosition TeleportToWaypoint() => TeleportTo(ReadWaypoint());
    internal GamePosition TeleportForward(float distance, FacingQuery facing)
    {
        if (!float.IsFinite(distance) || distance is < 0.5f or > 50f) throw new InvalidOperationException("Forward distance must be between 0.5 and 50 metres.");
        GamePosition current = ReadCurrent();
        float dx, dz;
        if (facing.Reticle is GamePosition facingTarget) { dx = facingTarget.X - current.X; dz = facingTarget.Z - current.Z; }
        else if (facing.AngleDegrees is float angle) { float radians = angle * MathF.PI / 180f; dx = MathF.Cos(radians); dz = MathF.Sin(radians); }
        else throw new InvalidOperationException("No facing direction was returned by the game.");
        float length = MathF.Sqrt(dx * dx + dz * dz);
        if (!float.IsFinite(length) || length < 0.01f) throw new InvalidOperationException("The reticle is too close to determine a forward direction. Aim at a wall or the ground and try again.");
        return TeleportTo(new GamePosition(current.X + dx / length * distance, current.Y, current.Z + dz / length * distance));
    }
    internal GamePosition Undo() => TeleportTo(_undo ?? throw new InvalidOperationException("No teleport is available to undo."), captureSafety: false);
    internal GamePosition ReturnToSafety()
    {
        if (_safetyHistory.Count == 0) throw new InvalidOperationException("No pre-teleport safety position is available.");
        int index = _safetyHistory.Count - 1;
        GamePosition result = TeleportTo(_safetyHistory[index], captureSafety: false);
        _safetyHistory.RemoveAt(index);
        return result;
    }
    internal GamePosition TeleportTo(GamePosition destination, bool captureSafety = true)
    {
        if (!float.IsFinite(destination.X) || !float.IsFinite(destination.Y) || !float.IsFinite(destination.Z)) throw new InvalidOperationException("Coordinates must be finite numbers.");
        ulong pointer = _remote.Read<ulong>(_coordinateCapture.DataAddress); if (pointer == 0 || !_remote.IsRangeReadable(pointer + 0x80, 12)) throw new InvalidOperationException("Player coordinate pointer is unavailable.");
        GamePosition before;
        using (_remote.SuspendThreads())
        {
            before = ReadCurrent();
            // Save recovery state before the first coordinate write, so it survives partial writes and
            // post-write verification failures as well as ordinary successful teleports.
            if (captureSafety)
            {
                _safetyHistory.Add(before);
                if (_safetyHistory.Count > SafetyHistoryLimit) _safetyHistory.RemoveAt(0);
                _undo = before;
            }
            _remote.Write(pointer + 0x80, destination.X); _remote.Write(pointer + 0x88, destination.Y); _remote.Write(pointer + 0x84, destination.Z);
        }
        GamePosition verified = ReadCurrent(); if (Math.Abs(verified.X - destination.X) > 0.01f || Math.Abs(verified.Y - destination.Y) > 0.01f || Math.Abs(verified.Z - destination.Z) > 0.01f) throw new InvalidOperationException("Teleport did not pass coordinate read-back verification.");
        return verified;
    }

    public void Dispose()
    {
        var errors = new List<Exception>();
        foreach (IDisposable hook in new IDisposable[] { _waypointCapture, _coordinateCapture, _playerCapture })
            try { hook.Dispose(); } catch (Exception ex) { errors.Add(ex); }
        if (errors.Count > 0) throw new AggregateException("One or more teleport hooks could not be restored.", errors);
    }
}
