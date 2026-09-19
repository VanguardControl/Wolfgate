using Content.Server.Construction;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared.Destructible;
using Content.Shared.Examine;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Repairable;

namespace Content.Server._WF.PlanetCracker.Cracker;

/// <summary>
/// The hull's gravity projector: its part tier sets the crack-time multiplier, and power and damage set what it shows.
/// </summary>
/// <remarks>
/// Server-only because RefreshPartsEvent and UpgradeExamineEvent are declared in Content.Server/_NF/Construction.
/// </remarks>
public sealed partial class WFGravityProjectorSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedPowerReceiverSystem _receiver = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFGravityProjectorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFGravityProjectorComponent, RefreshPartsEvent>(OnRefreshParts);
        SubscribeLocalEvent<WFGravityProjectorComponent, UpgradeExamineEvent>(OnUpgradeExamine);
        SubscribeLocalEvent<WFGravityProjectorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFGravityProjectorComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<WFGravityProjectorComponent, BreakageEventArgs>(OnBreakage);
        SubscribeLocalEvent<WFGravityProjectorComponent, RepairedEvent>(OnRepaired);
    }

    /// <summary>Pushes the initial appearance so a mapped or spawned projector looks right on its first frame.</summary>
    private void OnMapInit(Entity<WFGravityProjectorComponent> ent, ref MapInitEvent args)
    {
        _appearance.SetData(ent.Owner, WFProjectorVisuals.State, ent.Comp.State);
    }

    /// <summary>Machine parts drive the crack time: tier 1 is 1.0 and tier 4 is PartScaling cubed, the design's 0.70 floor.</summary>
    private void OnRefreshParts(Entity<WFGravityProjectorComponent> ent, ref RefreshPartsEvent args)
    {
        // GetPartsRatings fills an entry for every MachinePartPrototype and defaults an absent part to 1.0.
        if (!args.PartRatings.TryGetValue(ent.Comp.RatedPart.Id, out var rating))
            return;

        ent.Comp.CrackTimeMultiplier = MathF.Pow(ent.Comp.PartScaling, rating - 1f);
        Dirty(ent);
    }

    /// <summary>Adds the projector's crack-time line to the machine upgrade tooltip.</summary>
    private void OnUpgradeExamine(Entity<WFGravityProjectorComponent> ent, ref UpgradeExamineEvent args)
    {
        args.AddPercentageUpgrade("wf-projector-upgrade-crack-time", ent.Comp.CrackTimeMultiplier);
    }

    /// <summary>Examine: the rated crack time, and the broken housing line once the Breakage threshold has fired.</summary>
    private void OnExamined(Entity<WFGravityProjectorComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-projector-examine-multiplier",
            ("percent", (int)MathF.Round(ent.Comp.CrackTimeMultiplier * 100f))));

        if (ent.Comp.Broken)
            args.PushMarkup(Loc.GetString("wf-projector-examine-broken"));
    }

    /// <summary>A broken projector stays broken; otherwise power alone decides between off and idle.</summary>
    private void OnPowerChanged(Entity<WFGravityProjectorComponent> ent, ref PowerChangedEvent args)
    {
        if (ent.Comp.Broken)
            return;

        SetState(ent, args.Powered ? WFProjectorState.Idle : WFProjectorState.Off);
    }

    /// <summary>The Destructible Breakage threshold; there is no engine-side broken flag, so the component keeps its own.</summary>
    private void OnBreakage(Entity<WFGravityProjectorComponent> ent, ref BreakageEventArgs args)
    {
        ent.Comp.Broken = true;
        Dirty(ent);
        SetState(ent, WFProjectorState.Broken);
    }

    /// <summary>Repaired: clear the broken flag and fall back to whatever the power state says.</summary>
    private void OnRepaired(Entity<WFGravityProjectorComponent> ent, ref RepairedEvent args)
    {
        if (!ent.Comp.Broken)
            return;

        ent.Comp.Broken = false;
        Dirty(ent);
        SetState(ent, _receiver.IsPowered(ent.Owner) ? WFProjectorState.Idle : WFProjectorState.Off);
    }

    /// <summary>Single writer of the projector state; also pushes the sprite.</summary>
    public void SetState(Entity<WFGravityProjectorComponent> ent, WFProjectorState state)
    {
        if (ent.Comp.State != state)
        {
            ent.Comp.State = state;
            Dirty(ent);
        }

        _appearance.SetData(ent.Owner, WFProjectorVisuals.State, state);
    }
}
