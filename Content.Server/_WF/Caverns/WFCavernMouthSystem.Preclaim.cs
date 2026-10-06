using System.Diagnostics;
using System.Numerics;
using Content.Server._WF.Planets.Bounds;

namespace Content.Server._WF.Caverns;

/// <summary>
/// Claims every mouth cell of a bounded world before its ground is preloaded: once the whole ground is loaded the
/// lazy claims, which only ever cut unloaded ground, would never place another mouth.
/// </summary>
public sealed partial class WFCavernMouthSystem
{
    private void InitializePreclaim()
    {
        SubscribeLocalEvent<WFPlanetPreloadStartingEvent>(OnPreloadStarting);
    }

    private void OnPreloadStarting(ref WFPlanetPreloadStartingEvent args)
    {
        if (!_claimsEnabled || !TryComp<WFCavernGroundComponent>(args.Ground, out var comp))
            return;

        var ground = new Entity<WFCavernGroundComponent>(args.Ground, comp);
        if (!TryGetContext(ground, out var context))
            return;

        var watch = Stopwatch.StartNew();
        var claimed = 0;
        var min = CellOf(context.Spec, Floor(args.Centre - new Vector2(args.Radius)));
        var max = CellOf(context.Spec, Floor(args.Centre + new Vector2(args.Radius)));

        for (var x = min.X; x <= max.X; x++)
        for (var y = min.Y; y <= max.Y; y++)
        {
            if (ClaimCell(ground, context, new Vector2i(x, y), WFCavernMouthKind.Cell) == WFCavernClaim.Claimed)
                claimed++;
        }

        Log.Info($"Claimed {claimed} cavern mouths on {ToPrettyString(ground)} ahead of its preload in {watch.Elapsed.TotalMilliseconds:F0} ms.");
    }
}
