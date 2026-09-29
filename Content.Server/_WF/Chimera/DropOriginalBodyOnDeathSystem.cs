using Content.Server.Body.Components;
using Content.Server.Polymorph.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Polymorph;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Chimera;

/// <summary>
/// Drops the original body out of a dead polymorph with <see cref="DropOriginalBodyOnDeathComponent"/>.
/// </summary>
public sealed partial class DropOriginalBodyOnDeathSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DropOriginalBodyOnDeathComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<DropOriginalBodyOnDeathComponent, BeforeGibbedEvent>(OnBeforeGibbed);
    }

    private void OnMobStateChanged(Entity<DropOriginalBodyOnDeathComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            DropOriginal(ent);
    }

    /// <summary>Gibbing a living mob skips its death, so the body drops before the gib instead.</summary>
    private void OnBeforeGibbed(Entity<DropOriginalBodyOnDeathComponent> ent, ref BeforeGibbedEvent args)
    {
        DropOriginal(ent);
    }

    /// <summary>
    /// Puts the body the mob was polymorphed from next to it, purged and holding the mob's mind. The corpse stops being
    /// a polymorph, so nothing reverts it later, and drops the person's name.
    /// </summary>
    public void DropOriginal(Entity<DropOriginalBodyOnDeathComponent> ent)
    {
        if (!TryComp<PolymorphedEntityComponent>(ent, out var polymorphed))
            return;

        var original = polymorphed.Parent;
        var config = polymorphed.Configuration;

        // Removed first: a revert-on-death polymorph would otherwise swap the corpse for the body next update.
        RemComp(ent, polymorphed);

        if (TerminatingOrDeleted(original))
            return;

        Purge(original, ent.Comp.PurgedReagents);
        _transform.DropNextTo(original, ent.Owner);

        if (_mind.TryGetMind(ent, out var mindId, out var mind))
            _mind.TransferTo(mindId, original, mind: mind);

        // The person's name leaves the corpse with them.
        if (config.TransferName && Prototype(ent) is { } proto)
            _metaData.SetEntityName(ent, proto.Name);

        var ev = new PolymorphedEvent(ent, original, true);
        RaiseLocalEvent(ent, ref ev);

        _audio.PlayPvs(config.ExitPolymorphSound, original);
        _popup.PopupEntity(Loc.GetString(ent.Comp.Popup, ("body", Identity.Entity(original, EntityManager))),
            original,
            PopupType.Medium);
    }

    /// <summary>Removes the reagents from the body's own solutions and those of its organs, such as the stomach.</summary>
    private void Purge(EntityUid body, List<ProtoId<ReagentPrototype>> reagents)
    {
        if (reagents.Count == 0)
            return;

        PurgeSolutions(body, reagents);
        foreach (var organ in _body.GetBodyOrgans(body))
        {
            PurgeSolutions(organ.Id, reagents);
        }
    }

    private void PurgeSolutions(EntityUid uid, List<ProtoId<ReagentPrototype>> reagents)
    {
        foreach (var (_, soln) in _solution.EnumerateSolutions(uid))
        {
            var solution = soln.Comp.Solution;
            var removed = false;
            foreach (var reagent in reagents)
            {
                var quantity = solution.GetTotalPrototypeQuantity(reagent);
                if (quantity <= FixedPoint2.Zero)
                    continue;

                // Blood-borne copies carry DNA data, so match on the prototype alone.
                solution.RemoveReagent(reagent, quantity, ignoreReagentData: true);
                removed = true;
            }

            if (removed)
                _solution.UpdateChemicals(soln);
        }
    }
}
