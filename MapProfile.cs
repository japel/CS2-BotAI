using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace BotAI;

public enum MapKind { Unknown, Bomb, Hostage, Other }

// Map objective type, detected from the entities the map actually spawns.
// The name prefix is only a fallback for maps whose entities aren't up yet.
public static class MapProfile
{
    private static readonly string[] HostageEntities = ["hostage_entity", "info_hostage_spawn", "func_hostage_rescue"];
    private static readonly string[] BombEntities = ["func_bomb_target"];

    public static MapKind Detect()
    {
        if (HostageEntities.Any(Exists)) return MapKind.Hostage;
        if (BombEntities.Any(Exists)) return MapKind.Bomb;
        return FromName(Server.MapName);
    }

    public static MapKind FromName(string? mapName) => mapName switch
    {
        null or "" => MapKind.Unknown,
        _ when mapName.StartsWith("cs_", StringComparison.OrdinalIgnoreCase) => MapKind.Hostage,
        _ when mapName.StartsWith("de_", StringComparison.OrdinalIgnoreCase) => MapKind.Bomb,
        _ => MapKind.Other,
    };

    private static bool Exists(string designerName) =>
        Utilities.FindAllEntitiesByDesignerName<CEntityInstance>(designerName).Any();
}
