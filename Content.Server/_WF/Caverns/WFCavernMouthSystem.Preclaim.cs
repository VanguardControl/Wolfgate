using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.Planets.Bounds;

namespace Content.Server._WF.Caverns;

/// <summary>
/// Claims every mouth cell of a bounded world before its ground is preloaded: once the whole ground is loaded the
/// lazy claims, which only ever cut unloaded ground, would never place another mouth.
/// </summary>
public sealed partial class WFCavernMouthSystem
{
    [Dependency] private WFPlanetPreloadSystem _preload = default!;

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

        // A cell a step, over as many ticks as the generation budget takes; the sites wait for it.
        var preclaim = PreclaimAsync(ground, context, args.Centre, args.Radius);
        args.Holds.Add(preclaim);
        args.Claims.Add(preclaim);
    }

    private async Task PreclaimAsync(Entity<WFCavernGroundComponent> ground, MouthContext context, Vector2 centre, float radius)
    {
        var watch = Stopwatch.StartNew();
        var claimed = 0;
        var min = CellOf(context.Spec, Floor(centre - new Vector2(radius)));
        var max = CellOf(context.Spec, Floor(centre + new Vector2(radius)));

        for (var x = min.X; x <= max.X; x++)
        for (var y = min.Y; y <= max.Y; y++)
        {
            await _preload.Breathe();
            if (TerminatingOrDeleted(ground))
                return;

            if (ClaimCell(ground, context, new Vector2i(x, y), WFCavernMouthKind.Cell) == WFCavernClaim.Claimed)
                claimed++;
        }

        Log.Info($"Claimed {claimed} cavern mouths on {ToPrettyString(ground)} ahead of its preload over {watch.Elapsed.TotalMilliseconds:F0} ms.");
    }
}
