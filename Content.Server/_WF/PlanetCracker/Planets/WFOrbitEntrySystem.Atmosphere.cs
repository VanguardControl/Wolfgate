using System.Diagnostics.CodeAnalysis;
using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._WF.PlanetCracker.Flight;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.PlanetCracker.Flight;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.PlanetCracker.Planets;

/// <summary>
/// Entering the atmosphere: the one place a hull leaves orbit downward, and therefore the one place the lift warning
/// can be shown and confirmed. The raw pilot descend input out of orbit is refused for exactly this reason
/// (CEZLevelsSystem.WFFlight.cs), so there is no way past the confirm.
/// </summary>
public sealed partial class WFOrbitEntrySystem
{
    [Dependency] private CEZLevelsSystem _zLevels = default!;
    [Dependency] private WFFlightSystem _flight = default!;

    /// <summary>Lift ratio change under which the console readout is not re-sent; it is a two-decimal display.</summary>
    private const float LiftRatioEpsilon = 0.01f;

    /// <summary>
    /// Where in the gap below orbit a hull is placed when it drops out. Below CE's own settle zone (0.25 of the gap
    /// from either plane), because a hull WITH lift and no pilot input inside that band drifts back onto the plane it
    /// came from - which would pop it straight back into orbit. A hull without lift keeps falling from here anyway.
    /// </summary>
    private const float AtmosphereEntryProgress = 0.7f;

    /// <summary>
    /// Downward speed the hull is seeded with, in levels per second. Above CE's ExitTransitMaxSpeed (0.1) so the
    /// first settle check does not read the hull as already touched down and put it back where it started.
    /// </summary>
    private const float AtmosphereEntrySeed = 0.15f;

    /// <summary>
    /// Drops the console's hull out of orbit into the gap below it. A hull that cannot hold itself up is refused
    /// unless <paramref name="confirmed"/>, which is the client's answer to the lift warning.
    /// </summary>
    /// <param name="console">The shuttle console the request came from.</param>
    /// <param name="confirmed">Whether the pilot has already answered the lift warning.</param>
    /// <param name="reason">Why the descent was refused, already localised.</param>
    public bool TryEnterAtmosphere(EntityUid console, bool confirmed, [NotNullWhen(false)] out string? reason)
    {
        if (!TryGetHull(console, out var hull, out reason))
            return false;

        var grid = hull.Value.Owner;

        if (Transform(grid).MapUid is not { } mapUid || !HasComp<WFOrbitLayerComponent>(mapUid))
        {
            reason = Loc.GetString("wf-orbit-not-in-orbit");
            return false;
        }

        if (!confirmed
            && _zLevels.WfTryGetLiftRatio(grid, out var ratio)
            && ratio < CEZLevelsSystem.WFFullLiftRatio)
        {
            reason = Loc.GetString("wf-flight-lift-warning", ("ratio", ratio.ToString("F2")));
            return false;
        }

        _zLevels.WfGetAtmospherePower(grid, out _, out var powerDeficit);
        if (!confirmed && powerDeficit)
        {
            reason = Loc.GetString("wf-flight-confirm-power-deficit");
            return false;
        }

        return TryDropFromOrbit(grid, out reason);
    }

    /// <summary>
    /// Puts one grid into the gap below the orbit layer and, when it cannot hold itself up there, into lift lost.
    /// The pilot's confirmed descent and the orbit-decay countdown (F11) both end here, so an unmanned wreck falls
    /// through the same seed, the same transit and the same GPWS sequence a piloted hull gets.
    /// </summary>
    /// <param name="grid">The grid leaving orbit; it needs no console, no pilot and no shuttle component.</param>
    /// <param name="reason">Why the descent was refused, already localised.</param>
    public bool TryDropFromOrbit(EntityUid grid, [NotNullWhen(false)] out string? reason)
    {
        if (Transform(grid).MapUid is not { } mapUid || !HasComp<WFOrbitLayerComponent>(mapUid))
        {
            reason = Loc.GetString("wf-orbit-not-in-orbit");
            return false;
        }

        if (HasComp<FTLComponent>(grid))
        {
            reason = Loc.GetString("wf-flight-busy");
            return false;
        }

        if (!TryComp<MapGridComponent>(grid, out var gridComp))
        {
            reason = Loc.GetString("wf-orbit-no-hull");
            return false;
        }

        var hasRatio = _zLevels.WfTryGetLiftRatio(grid, out var ratio);

        // The sweep's pooled-lift memo is up to half a second stale and would otherwise decide the first tick of the
        // descent, which is the tick that settles a hull straight back onto the layer it just left.
        _zLevels.WfInvalidateGravgenCapacity();

        var faller = EnsureComp<CEZGridFallerComponent>(grid);
        faller.Velocity = AtmosphereEntrySeed;
        faller.GravityTime = _timing.CurTime;

        if (!_zLevels.TryEnterTransit((grid, gridComp), AtmosphereEntryProgress))
        {
            reason = Loc.GetString("wf-flight-descent-refused");
            return false;
        }

        // Orbit parks a grid by switching its z-gravity off; off the orbit layer it has to be handed back.
        _zLevels.WfRearmZGravity(grid);

        if (hasRatio && ratio < CEZLevelsSystem.WFFullLiftRatio)
            _flight.EnterLiftLost(grid, ratio);

        _nextRefresh = TimeSpan.Zero;
        reason = null;
        return true;
    }

    /// <summary>Console request to enter the atmosphere; the actor has to be the pilot of this very console.</summary>
    private void OnEnterAtmosphereMessage(EntityUid uid, ShuttleConsoleComponent component, WFEnterAtmosphereMessage args)
    {
        if (GetEntity(args.Console) != uid || !IsPilot(args.Actor, uid))
            return;

        if (!TryEnterAtmosphere(uid, args.Confirmed, out var reason))
            _popup.PopupEntity(reason, uid, args.Actor);
    }
}
