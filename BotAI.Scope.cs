using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace BotAI;

public partial class BotAI
{
    // Writes or restores map-scoped patches to match the current map. Idempotent;
    // called on map start and every round start.
    private void ApplyMapScope()
    {
        var kind = MapProfile.Detect();
        if (kind != _mapKind) Logger.LogInformation($"Map '{Server.MapName}' detected as {kind}.");
        _mapKind = kind;

        foreach (var (name, sp) in _scoped)
        {
            bool wanted = _patchOverrides.TryGetValue(name, out bool forced)
                ? forced
                : _forceAllPatches || PatchScopes.Wanted(sp.Scope, kind);
            var applied = _appliedPatches.FirstOrDefault(p => p.Name == name);

            if (wanted && applied == null)
            {
                bool ok = WritePatch(name, sp.Address, sp.Bytes, sp.ExpectedOriginal);
                _status[name] = ok ? "applied" : "FAILED";
                if (!ok) Logger.LogError($"{name}: FAILED.");
            }
            else if (!wanted && applied != null)
            {
                RestorePatch(applied);
                _appliedPatches.Remove(applied);
                Logger.LogInformation($"{name}: restored for {kind} map.");
            }

            if (!wanted) _status[name] = _patchOverrides.ContainsKey(name) ? "off (forced)"
                : sp.Scope == PatchScope.Off ? "off (default)" : $"off ({kind} map)";
        }
    }

    // A/B testing: "all" applies every scoped patch regardless of map.
    private bool _forceAllPatches;

    [ConsoleCommand("css_botmod_scope", "auto = scope patches by map type, all = apply every patch")]
    [CommandHelper(minArgs: 1, usage: "auto|all", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnScopeCommand(CCSPlayerController? caller, CommandInfo command)
    {
        _forceAllPatches = command.GetArg(1) == "all";
        ApplyMapScope();
        command.ReplyToCommand($"[botmod] scope={(_forceAllPatches ? "all" : "auto")}");
    }

    // A/B testing: force one scoped patch on/off regardless of map ("auto" clears).
    private readonly Dictionary<string, bool> _patchOverrides = [];

    [ConsoleCommand("css_botmod_patch", "Force a map-scoped patch on/off, or auto")]
    [CommandHelper(minArgs: 2, usage: "<patch> on|off|auto", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnPatchCommand(CCSPlayerController? caller, CommandInfo command)
    {
        string name = command.GetArg(1), mode = command.GetArg(2);
        if (!_scoped.ContainsKey(name))
        {
            command.ReplyToCommand($"[botmod] not a map-scoped patch: {name} (scoped: {string.Join(", ", _scoped.Keys)})");
            return;
        }
        if (mode == "auto") _patchOverrides.Remove(name);
        else _patchOverrides[name] = mode == "on";
        ApplyMapScope();
        command.ReplyToCommand($"[botmod] {name} -> {_status[name]}");
    }

    // Utilities.GetPlayers() only scans slots < Server.MaxPlayers, which is 1 on a listen
    // server started with `map` from the console, so it misses every bot. Scan controllers instead.
    private static List<CCSPlayerController> Players() =>
        Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller")
            .Where(p => p.IsValid && !p.IsHLTV && p.Connected == PlayerConnectedState.Connected)
            .ToList();

    // ── Hostage round stats (for measuring bot objective play) ───────────────

    private record RoundStats(string Map, int Round, float? FirstPickupSec, int Pickups, int Rescues, string Winner, int Ct, int T);

    private readonly List<RoundStats> _rounds = [];
    private int _roundNo;
    private float _freezeEndTime;
    private float? _firstPickupSec;
    private int _pickups, _rescues, _ctCount, _tCount;

    private void RegisterHostageStats()
    {
        RegisterEventHandler<EventRoundFreezeEnd>((_, _) =>
        {
            _freezeEndTime = Server.CurrentTime;
            _firstPickupSec = null;
            _pickups = _rescues = 0;
            var players = Players();
            _ctCount = players.Count(p => p.Team == CsTeam.CounterTerrorist);
            _tCount = players.Count(p => p.Team == CsTeam.Terrorist);
            return HookResult.Continue;
        });
        RegisterEventHandler<EventHostageFollows>((e, _) =>
        {
            _pickups++;
            _firstPickupSec ??= Server.CurrentTime - _freezeEndTime;
            return HookResult.Continue;
        });
        RegisterEventHandler<EventHostageRescued>((_, _) => { _rescues++; return HookResult.Continue; });
        RegisterEventHandler<EventRoundEnd>((e, _) =>
        {
            if (_mapKind != MapKind.Hostage) return HookResult.Continue;
            string winner = e.Winner switch { (int)CsTeam.CounterTerrorist => "CT", (int)CsTeam.Terrorist => "T", _ => "-" };
            if (winner == "-") return HookResult.Continue;   // game commencing / restart, not a played round
            _rounds.Add(new RoundStats(Server.MapName, ++_roundNo, _firstPickupSec, _pickups, _rescues, winner, _ctCount, _tCount));
            return HookResult.Continue;
        });
    }

    [ConsoleCommand("css_botmod_status", "Show BotAI patch state, map type and hostage round stats")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnStatusCommand(CCSPlayerController? caller, CommandInfo command)
    {
        command.ReplyToCommand($"[botmod] BotAI {ModuleVersion}  map={Server.MapName} kind={_mapKind}");
        foreach (var group in _status.GroupBy(kv => kv.Value.StartsWith("applied") ? "applied" : kv.Value))
        {
            if (group.Key == "applied")
                command.ReplyToCommand($"[botmod] applied: {group.Count()}");
            else
                foreach (var kv in group) command.ReplyToCommand($"[botmod] {kv.Key}: {kv.Value}");
        }

        if (_rounds.Count == 0) return;
        int withPickup = _rounds.Count(r => r.Pickups > 0);
        var times = _rounds.Where(r => r.FirstPickupSec.HasValue).Select(r => r.FirstPickupSec!.Value).ToList();
        command.ReplyToCommand(
            $"[botmod] hostage rounds={_rounds.Count} pickupRounds={withPickup} " +
            $"rescues={_rounds.Sum(r => r.Rescues)} ctWins={_rounds.Count(r => r.Winner == "CT")} " +
            $"avgFirstPickup={(times.Count > 0 ? $"{times.Average():F0}s" : "-")} " +
            $"unevenRounds={_rounds.Count(r => r.Ct != r.T)}");
        foreach (var r in _rounds.TakeLast(10))
            command.ReplyToCommand(
                $"[botmod]   #{r.Round} {r.Map} pickup={(r.FirstPickupSec is { } t ? $"{t:F0}s" : "-")} " +
                $"follows={r.Pickups} rescued={r.Rescues} winner={r.Winner} {r.Ct}v{r.T}");
    }

    [ConsoleCommand("css_botmod_stats_reset", "Clear hostage round stats")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnStatsResetCommand(CCSPlayerController? caller, CommandInfo command)
    {
        _rounds.Clear();
        _roundNo = 0;
        command.ReplyToCommand("[botmod] stats cleared");
    }
}
