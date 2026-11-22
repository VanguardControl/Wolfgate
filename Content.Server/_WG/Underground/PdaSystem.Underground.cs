using Content.Server._WG.Underground;

namespace Content.Server.PDA;

/// <summary>Shows the Underground's Heat on the PDA, under the war level and the DEFCON.</summary>
public sealed partial class PdaSystem
{
    [Dependency] private WGUndergroundHeatSystem _undergroundHeat = default!;

    private void InitializeUnderground()
    {
        SubscribeLocalEvent<WGUndergroundHeatChangedEvent>(_ => UpdateOpenPdasForDefcon());
    }

    private string? WithUnderground(string? warLevel)
    {
        return _undergroundHeat.PdaLine() is { } line ? $"{warLevel}\n{line}" : warLevel;
    }
}
