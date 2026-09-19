using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Chemical burns: caustic that is still on the skin keeps working. A part with an open chemical burn
/// carries <see cref="WolfmedChemicalBurnComponent"/> and takes a little more damage every few seconds
/// until the patient is washed off - a splash of water, a spray bottle, an extinguisher, a puddle, or
/// anything else that puts water on them.
/// </summary>
/// <remarks>
/// The wash hook is <see cref="Content.Shared._WF.Wolfmed.EntityEffects.WashChemicalBurns"/>, an entity
/// effect on the humanoid base's existing water touch reaction, so every way water already reaches a mob
/// works without a second code path. Shared for that effect's sake; every write is server-gated.
/// </remarks>
public sealed class WolfmedChemicalBurnSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WoundDamageRoutingSystem _routing = default!;
    [Dependency] private WoundSystem _wounds = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        if (!_net.IsServer || !_prototypes.TryIndex(args.Prototype, out var prototype) ||
            !prototype.TryGetBehavior(args.Severity, out WolfmedCausticResidueBehavior _))
            return;

        Refresh(args.Part);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;

        // Buffered: applying the damage raises wound events that can add or remove the component.
        var due = new List<EntityUid>();
        var query = EntityQueryEnumerator<WolfmedChemicalBurnComponent>();
        while (query.MoveNext(out var uid, out var residue))
        {
            residue.Accumulator += frameTime;
            due.Add(uid);
        }

        foreach (var part in due)
            Tick(part);
    }

    private void Tick(EntityUid part)
    {
        if (TerminatingOrDeleted(part) ||
            !TryComp(part, out WolfmedChemicalBurnComponent? residue) ||
            CompOrNull<BodyPartComponent>(part)?.Body is not { } body)
            return;

        if (FindResidue(part) is not { } behavior)
        {
            RemComp<WolfmedChemicalBurnComponent>(part);
            return;
        }

        if (residue.Accumulator < behavior.Interval.TotalSeconds)
            return;

        residue.Accumulator = 0f;
        if (!behavior.Damage.Empty)
            _routing.TryApplyPartDamage(body, part, behavior.Damage, healWounds: false);
    }

    /// <summary>Arms or disarms the part from the chemical burns it is actually carrying.</summary>
    public void Refresh(EntityUid part)
    {
        if (TerminatingOrDeleted(part))
            return;

        if (FindResidue(part) == null)
        {
            RemComp<WolfmedChemicalBurnComponent>(part);
            return;
        }

        EnsureComp<WolfmedChemicalBurnComponent>(part);
    }

    /// <summary>
    /// Rinses every part of the body clean. The burns stay and heal like any other wound; what stops is the
    /// residue eating away at them. Returns how many parts had something on them.
    /// </summary>
    public int Wash(EntityUid body)
    {
        if (!_net.IsServer || TerminatingOrDeleted(body))
            return 0;

        var washed = 0;
        foreach (var (part, _) in _body.GetBodyChildren(body).ToArray())
        {
            if (!HasComp<WolfmedChemicalBurnComponent>(part))
                continue;

            RemComp<WolfmedChemicalBurnComponent>(part);
            washed++;
        }

        if (washed > 0)
            _popup.PopupEntity(Loc.GetString("wolfmed-chemical-burn-washed"), body, body);

        return washed;
    }

    /// <summary>The worst residue on the part, or null when nothing there is still corroding.</summary>
    private WolfmedCausticResidueBehavior? FindResidue(EntityUid part)
    {
        WolfmedCausticResidueBehavior? found = null;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                !_prototypes.TryIndex(wound.Comp.Prototype, out var prototype) ||
                !prototype.TryGetBehavior(wound.Comp.Severity, out WolfmedCausticResidueBehavior behavior))
                continue;

            if (found == null || behavior.Interval < found.Interval)
                found = behavior;
        }

        return found;
    }
}
