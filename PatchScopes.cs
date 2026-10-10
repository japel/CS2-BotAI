namespace BotAI;

public enum PatchScope { Always, NotHostage }

// Patches that only make sense on some map types. Everything not listed is Always.
// Bomb-only patches stay Always: their code paths never run without a bomb.
public static class PatchScopes
{
    // Empty by default. Benchmarked on cs_office (5v5, 20 rounds each, 2026-10-10):
    // turning OnAudibleEvent_GlobalHearRange + InvestigateNoise_SkipSelfDefenseCheck
    // off on hostage maps cut CT hostage pickups from 15/20 to 7/20 rounds.
    // Use css_botmod_patch <name> on|off to experiment; add entries only with data.
    private static readonly Dictionary<string, PatchScope> Scopes = new();

    // Patches resolved up front but written per map, so css_botmod_patch can flip
    // them at runtime. Listed patches follow their scope (default Always).
    public static readonly HashSet<string> Toggleable =
    [
        "OnAudibleEvent_GlobalHearRange",
        "InvestigateNoise_SkipSelfDefenseCheck",
    ];

    public static bool IsDeferred(string patchName) =>
        Toggleable.Contains(patchName) || Of(patchName) != PatchScope.Always;

    public static PatchScope Of(string patchName) =>
        Scopes.GetValueOrDefault(patchName, PatchScope.Always);

    public static bool Wanted(PatchScope scope, MapKind map) => scope switch
    {
        PatchScope.NotHostage => map != MapKind.Hostage,
        _ => true,
    };
}
