using System.Linq;
using Content.Shared.Body.Organ; // WOLFGATE: Wolfgate keeps OrganComponent in Content.Shared.Body.Organ.
using Content.Shared._WF.Wolfmed.Body; // WOLFGATE: D8, organ health lives on WolfmedOrganComponent.
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Body;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._Onyx.Wounds;

public sealed partial class OrganDamageSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WoundFractureSystem _fractures = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private WoundBleedingSystem _bleeding = default!;
    [Dependency] private AmputationSystem _amputation = default!; // WOLFGATE: D26 lifted in phase 3 (WP11-1); AmputationSystem is now vendored.
    [Dependency] private OrganHealthSystem _organHealth = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WoundableComponent, PartDamageAppliedEvent>(OnPartDamageApplied);
    }

    private void OnPartDamageApplied(Entity<WoundableComponent> part, ref PartDamageAppliedEvent args)
    {
        _wounds.HandlePartDamageApplied(part, ref args);
        _fractures.HandlePartDamageApplied(part, ref args);
        _amputation.HandlePartDamageApplied(part, ref args); // WOLFGATE: D26 lifted in phase 3 (WP11-1); order wounds -> fractures -> amputation -> bleeding is load-bearing.
        _bleeding.HandlePartDamageApplied(part, ref args);

        if (!_net.IsServer || !TryComp(part, out BodyPartComponent? bodyPart) || bodyPart.Body == null ||
            !_prototypes.TryIndex(part.Comp.Profile, out var profile))
            return;

        var settings = profile.OrganDamage;
        var chance = settings.Chances.GetValueOrDefault(bodyPart.PartType);
        if (chance <= 0f || !_random.Prob(Math.Clamp(chance, 0f, 1f)))
            return;

        // WOLFGATE: D8, organ health is on WolfmedOrganComponent; Wolfgate's OrganComponent carries none.
        var organs = _body.GetPartOrgans(part)
            .Select(organ => (organ.Id, Component: CompOrNull<WolfmedOrganComponent>(organ.Id)))
            .Where(organ => organ.Component != null && organ.Component.Health > FixedPoint2.Zero &&
                            HasComp<OrganDamageComponent>(organ.Id))
            .Select(organ => (organ.Id, Component: organ.Component!))
            .ToList();
        if (organs.Count == 0)
            return;

        var affected = Math.Max(1, settings.MaxAffected);
        for (var i = 0; i < affected && organs.Count > 0; i++)
        {
            var organ = PickOrgan(organs);
            organs.Remove(organ);

            var policy = Comp<OrganDamageComponent>(organ.Id);
            if (!_random.Prob(Math.Clamp(policy.HitChance, 0f, 1f)))
                continue;

            var applied = GetOrganDamage(args.Damage, policy);
            if (applied <= FixedPoint2.Zero)
                continue;

            if (policy.MaxDamageFraction > 0f)
                applied = FixedPoint2.Min(applied, organ.Component.MaxHealth * policy.MaxDamageFraction);

            _organHealth.ChangeHealth((organ.Id, organ.Component), -applied);
        }
    }

    private static FixedPoint2 GetOrganDamage(
        DamageSpecifier damage,
        OrganDamageComponent policy)
    {
        var result = FixedPoint2.Zero;
        foreach (var (type, amount) in damage.DamageDict)
        {
            result += amount * Math.Max(0f, policy.DamageMultipliers.GetValueOrDefault(type));
        }
        return result;
    }

    // WOLFGATE: D8, the organ list carries WolfmedOrganComponent instead of Onyx's health-bearing OrganComponent.
    private (EntityUid Id, WolfmedOrganComponent Component) PickOrgan(
        List<(EntityUid Id, WolfmedOrganComponent Component)> organs)
    {
        var totalWeight = 0f;
        foreach (var organ in organs)
            totalWeight += Math.Max(0f, Comp<OrganDamageComponent>(organ.Id).SelectionWeight);

        if (totalWeight <= 0f)
            return _random.Pick(organs);

        var roll = _random.NextFloat() * totalWeight;
        foreach (var organ in organs)
        {
            roll -= Math.Max(0f, Comp<OrganDamageComponent>(organ.Id).SelectionWeight);
            if (roll <= 0f)
                return organ;
        }
        return organs[organs.Count - 1];
    }

}
