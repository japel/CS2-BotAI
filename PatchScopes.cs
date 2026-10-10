namespace BotAI;

public enum PatchScope { Always, NotHostage }

// Patches that only make sense on some map types. Everything not listed is Always.
// Bomb-only patches stay Always: their code paths never run without a bomb.
public static class PatchScopes
{
    private static readonly Dictionary<string, PatchScope> Scopes = new()
    {
        // Global hearing + investigating every noise pull CT bots off hostage
        // escort and T bots off hostage guard; vanilla ranges on hostage maps.
        ["OnAudibleEvent_GlobalHearRange"] = PatchScope.NotHostage,
        ["InvestigateNoise_SkipSelfDefenseCheck"] = PatchScope.NotHostage,
    };

    public static PatchScope Of(string patchName) =>
        Scopes.GetValueOrDefault(patchName, PatchScope.Always);

    public static bool Wanted(PatchScope scope, MapKind map) => scope switch
    {
        PatchScope.NotHostage => map != MapKind.Hostage,
        _ => true,
    };
}
