using Content.Shared.Atmos.Rotting;

namespace Content.Server.Atmos.Rotting;

/// <summary>Rot for time a grid spent in stasis or the cold store.</summary>
public sealed partial class RottingSystem
{
    /// <summary>
    /// Advances rot by an absence, at the rate and under the rules of where the entity is now. Something that
    /// passes its rot time starts rotting on the system's next pass; the damage rot deals while away is not applied.
    /// </summary>
    public void WGAdvanceRot(EntityUid uid, TimeSpan elapsed)
    {
        if (!TryComp<PerishableComponent>(uid, out var perishable) || !IsRotProgressing(uid, perishable))
            return;

        var rate = GetRotRate(uid);
        if (TryComp<RottingComponent>(uid, out var rotting))
        {
            rotting.TotalRotTime += elapsed * rate;
            return;
        }

        var left = perishable.RotAfter - perishable.RotAccumulator;
        perishable.RotAccumulator += elapsed * rate;
        if (perishable.RotAccumulator < perishable.RotAfter)
            return;

        // What is left over after it turned goes to the rot itself.
        var rot = EnsureComp<RottingComponent>(uid);
        rot.NextRotUpdate = _timing.CurTime + rot.RotUpdateRate;
        if (rate > 0f)
            rot.TotalRotTime += elapsed * rate - left;
    }
}
