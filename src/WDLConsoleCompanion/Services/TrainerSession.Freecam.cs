using System.Diagnostics;

namespace WDLConsoleCompanion.Services;

internal sealed record FreecamApiProbeRow(string Name, string LuaType, string Note);
internal sealed record FreecamSessionResult(bool Success, string Status, string Route);

internal sealed partial class TrainerSession
{
    private const int FreecamHelperVersion = 72701;
    private readonly object _freecamGate = new();
    private int? _freecamHelperPid;
    private bool _freecamSessionActive;

    internal bool FreecamSessionActive => _freecamSessionActive;

    internal FacingQuery QueryFacingForFly() => RequireLuaQueue().QueryFacing();

    internal void HoldPhaseFlyAltitude(float altitude)
    {
        if (!float.IsFinite(altitude)) throw new InvalidOperationException("Altitude is not finite.");
        GamePosition current = ReadCurrentTeleportPosition();
        if (MathF.Abs(current.Y - altitude) >= 0.04f)
            RequireTeleport().TeleportTo(new GamePosition(current.X, altitude, current.Z), captureSafety: false);
    }

    internal string PrepareFreecamRuntime()
    {
        EnsureFreecamHelperInstalled();
        return "Freecam helper installed. Native CCameraFreeComponent activation remains unresolved; Domino and Phase Fly routes are separate experiments.";
    }

    internal FreecamSessionResult ActivateDominoFreecam(bool hideOperative)
    {
        EnsureFreecamHelperInstalled();
        string raw = ExecuteLuaFileProbe($"local h=rawget(_G,'__WDL_FC826');if type(h)~='table' or h.version~={FreecamHelperVersion} then return end;h.activate('__OUT__',{(hideOperative ? 1 : 0)})", "wdl-freecam-on", TimeSpan.FromSeconds(10)).Trim();
        if (raw.StartsWith("OK\t", StringComparison.Ordinal))
        {
            _freecamSessionActive = true;
            string route = raw.Length > 3 ? raw[3..] : "unknown";
            Report($"SUPER RISKY: Domino camera route accepted: {route}.");
            return new(true, "Domino camera route accepted. Verify gameplay remains active.", route);
        }
        throw new InvalidOperationException(raw.StartsWith("FAIL\t", StringComparison.Ordinal) ? raw[5..] : "No Domino camera route accepted.");
    }

    internal FreecamSessionResult DeactivateDominoFreecam()
    {
        if (!IsAttachedProcessAlive) { _freecamSessionActive = false; return new(true, "Detached; camera session cleared.", "none"); }
        EnsureFreecamHelperInstalled();
        string raw = ExecuteLuaFileProbe("local h=rawget(_G,'__WDL_FC826');if type(h)=='table' then h.deactivate('__OUT__') end", "wdl-freecam-off", TimeSpan.FromSeconds(10)).Trim();
        _freecamSessionActive = false;
        Report("Domino camera route released; normal camera restore was requested.");
        return new(true, "Domino camera route released; normal camera restore requested.", raw);
    }

    internal bool QueueFreecamGhostToggle(bool enable)
    {
        if (!IsAttachedProcessAlive || RequireLuaQueue().PendingCount > 2) return false;
        string p = enable ? "0" : "1";
        RequireLuaQueue().Enqueue($"local p=GetLocalPlayerEntityId();if type(SetCanBeDetected)=='function' then pcall(SetCanBeDetected,p,{p}) end;if type(FelonySystemEnable)=='function' then pcall(FelonySystemEnable,{(enable ? "0" : "1")}) end");
        return true;
    }

    internal IReadOnlyList<FreecamApiProbeRow> ProbeFreecamApis()
    {
        const string script = """
local rows={}
for _,n in ipairs({'ActivateDominoCameraContext','SetDominoCameraReference','ReleaseDominoCameraContext','ReleaseDominoAnimatedCamera','SetEntityVisible','GetLocalPlayerEntityId','GetEntityPosition','GetEntityAngle'}) do rows[#rows+1]=n..'\t'..type(rawget(_G,n)) end
local m=rawget(_G,'Mission');for _,n in ipairs({'Event__SwitchCinematicCamera','Event__SwitchToFirstPersonCamera'}) do rows[#rows+1]='Mission.'..n..'\t'..(type(m)=='table' and type(rawget(m,n)) or 'nil') end
local f=io.open('__OUT__','w');if f then f:write(table.concat(rows,'\n'));f:close() end
""";
        string text = ExecuteLuaFileProbe(script, "wdl-freecam-api", TimeSpan.FromSeconds(8));
        var rows = new List<FreecamApiProbeRow>();
        foreach (string line in text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('\t');
            if (parts.Length < 2) continue;
            rows.Add(new(parts[0], parts[1], "Read-only availability probe; call shape remains build-dependent."));
        }
        return rows;
    }

    private void EnsureFreecamHelperInstalled()
    {
        if (!IsAttachedProcessAlive) throw new InvalidOperationException("Attach to Watch Dogs: Legion first.");
        lock (_freecamGate)
        {
            if (_freecamHelperPid == ProcessId) return;
            string raw = ExecuteLuaFileProbe(BuildFreecamHelperInstaller(), "wdl-freecam-helper", TimeSpan.FromSeconds(12)).Trim();
            if (!raw.StartsWith("READY\t", StringComparison.Ordinal)) throw new InvalidOperationException("Freecam helper did not initialize: " + raw);
            _freecamHelperPid = ProcessId;
            _freecamSessionActive = false;
            Report("Installed the guarded Domino/Phase Fly helper; native camera writes were not used.");
        }
    }

    private static string BuildFreecamHelperInstaller() => """
local out='__OUT__';local H={};H.version=72701;H.active=false;H.route=nil
local function emit(p,s)local f=io.open(p,'w');if f then f:write(s);f:close()end end
local function player()local f=rawget(_G,'GetLocalPlayerEntityId');if type(f)~='function'then return nil end;local ok,id=pcall(f);return ok and id or nil end
function H.activate(path,hide)
 if H.active then emit(path,'OK\talready-active:'..tostring(H.route));return end
 local errors={};local function accept(label)H.active=true;H.route=label;emit(path,'OK\t'..label)end;local p=player();local m=rawget(_G,'Mission')
 if type(m)=='table' and type(rawget(m,'Event__SwitchCinematicCamera'))=='function' then local ok,e=pcall(rawget(m,'Event__SwitchCinematicCamera'));if ok then accept('Mission.Event__SwitchCinematicCamera');return else errors[#errors+1]='Mission:'..tostring(e)end end
 local a=rawget(_G,'ActivateDominoCameraContext');if type(a)=='function' then for _,v in ipairs({{'()',function()return a()end},{'(player)',function()return p and a(p)end},{'(0)',function()return a(0)end},{'(true)',function()return a(true)end}})do local ok,e=pcall(v[2]);if ok and e~=false then accept('ActivateDominoCameraContext'..v[1]);return else errors[#errors+1]='ActivateDominoCameraContext'..v[1]..':'..tostring(e)end end else errors[#errors+1]='ActivateDominoCameraContext:missing'end
 local r=rawget(_G,'SetDominoCameraReference');if type(r)=='function' and p~=nil then local ok,e=pcall(r,p);if ok and e~=false then accept('SetDominoCameraReference(player)');return else errors[#errors+1]='SetDominoCameraReference:'..tostring(e)end else errors[#errors+1]='SetDominoCameraReference:missing'end
 emit(path,'FAIL\t'..table.concat(errors,' | '))
end
function H.deactivate(path)local notes={};for _,n in ipairs({'ReleaseDominoCameraContext','ReleaseDominoAnimatedCamera'})do local f=rawget(_G,n);if type(f)=='function'then local ok,e=pcall(f);notes[#notes+1]=ok and n..':ok' or n..':'..tostring(e)end end;local p=player();local v=rawget(_G,'SetEntityVisible');if p and type(v)=='function'then pcall(v,p,1)end;H.active=false;H.route=nil;emit(path,'OK\t'..table.concat(notes,'; '))end
rawset(_G,'__WDL_FC826',H);emit(out,'READY\tfreecam-helper')
""";
}
