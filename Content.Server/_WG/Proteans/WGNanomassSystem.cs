using System.Linq;
using Content.Server._WG.Augments;
using Content.Server.Power.EntitySystems;
using Content.Shared._WG.Proteans;
using Content.Shared.Administration.Logs;
using Content.Shared.Alert;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Emp;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Materials;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Content.Shared.Stacks;
using Content.Shared.StatusEffectNew;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Verbs;
using Content.Shared.Whitelist;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using StatusEffectsSystem = Content.Shared.StatusEffectNew.StatusEffectsSystem;

namespace Content.Server._WG.Proteans;

/// <summary>
/// A Protean's nanomass, kept as the charge of its seed's battery: upkeep, healing, slurry, running thin, EMP,
/// absorbing materials and being fed, and the traces materials leave.
/// </summary>
public sealed class WGNanomassSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ISharedAdminLogManager _adminLog = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private PointLightSystem _lights = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStackSystem _stacks = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StatusEffectsSystem _status = default!;

    /// <summary>Slurry this close to a new spill takes its mass instead of a second puddle.</summary>
    private const float SlurryMergeRange = 0.5f;

    private readonly HashSet<Entity<WGProteanSlurryComponent>> _slurry = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGNanomassComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WGNanomassComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<WGNanomassComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<WGNanomassComponent, EmpPulseEvent>(OnEmp);
        SubscribeLocalEvent<WGNanomassComponent, GetVerbsEvent<InnateVerb>>(OnInnateVerbs);
        SubscribeLocalEvent<WGNanomassComponent, GetVerbsEvent<InteractionVerb>>(OnInteractionVerbs);
        SubscribeLocalEvent<WGProteanSlurryComponent, StepTriggerAttemptEvent>(OnSlurryStepAttempt);
        SubscribeLocalEvent<WGProteanSlurryComponent, StepTriggeredOffEvent>(OnSlurryStepped);
        SubscribeLocalEvent<WGProteanTraceComponent, StatusEffectAppliedEvent>(OnTraceApplied);
        SubscribeLocalEvent<WGProteanTraceComponent, StatusEffectRemovedEvent>(OnTraceRemoved);
    }

    private void OnMapInit(Entity<WGNanomassComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUpdate = _timing.CurTime + ent.Comp.UpdateInterval;
        Refresh(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGNanomassComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextUpdate)
                continue;

            comp.NextUpdate = now + comp.UpdateInterval;
            Tick((uid, comp), (float) comp.UpdateInterval.TotalSeconds, now);
        }
    }

    private void Tick(Entity<WGNanomassComponent> ent, float seconds, TimeSpan now)
    {
        if (GetSeed(ent) is not { } seed)
            return;

        var traces = Traces(ent);
        var upkeep = ent.Comp.Upkeep * traces.Upkeep * seconds;
        if (ent.Comp.Pooled)
            upkeep /= ent.Comp.PooledUpkeepDivisor;
        _battery.UseCharge(seed, upkeep, seed.Comp);

        if (seed.Comp.CurrentCharge > ent.Comp.HealThreshold
            && now >= ent.Comp.LastHit + ent.Comp.HealDelay
            && TryComp(ent, out DamageableComponent? damageable)
            && damageable.TotalDamage > FixedPoint2.Zero)
        {
            var room = (seed.Comp.CurrentCharge - ent.Comp.HealThreshold) / ent.Comp.HealCost;
            var healed = Heal((ent, damageable), Math.Min(ent.Comp.HealRate * traces.Heal * seconds, room));
            _battery.UseCharge(seed, healed * ent.Comp.HealCost, seed.Comp);
        }

        Refresh(ent);
    }

    /// <summary>The Protean's seed and its battery, while the seed is in the body.</summary>
    public Entity<BatteryComponent>? GetSeed(EntityUid protean)
    {
        if (!_body.TryGetBodyOrganEntityComps<WGProteanSeedComponent>(protean, out var seeds))
            return null;

        foreach (var seed in seeds)
        {
            if (TryComp(seed, out BatteryComponent? battery))
                return (seed.Owner, battery);
        }

        return null;
    }

    /// <summary>Current nanomass, or 0 without a seed.</summary>
    public float GetMass(EntityUid protean)
    {
        return GetSeed(protean)?.Comp.CurrentCharge ?? 0f;
    }

    /// <summary>Sets nanomass directly, clamped to the seed's capacity.</summary>
    public void SetMass(Entity<WGNanomassComponent?> ent, float mass)
    {
        if (!Resolve(ent, ref ent.Comp) || GetSeed(ent) is not { } seed)
            return;

        _battery.SetCharge(seed, mass, seed.Comp);
        Refresh((ent, ent.Comp));
    }

    /// <summary>Heals up to the amount, spread over the damage the body has; returns how much it healed.</summary>
    private float Heal(Entity<DamageableComponent> ent, float amount)
    {
        var total = ent.Comp.TotalDamage;
        if (amount <= 0f || total <= FixedPoint2.Zero)
            return 0f;

        amount = Math.Min(amount, total.Float());
        var heal = new DamageSpecifier();
        foreach (var (type, value) in ent.Comp.Damage.DamageDict)
        {
            if (value > FixedPoint2.Zero)
                heal.DamageDict[type] = -value * amount / total;
        }

        var before = ent.Comp.TotalDamage;
        _damageable.TryChangeDamage(ent, heal, ignoreResistances: true, interruptsDoAfters: false, damageable: ent.Comp);
        return Math.Max(0f, (before - ent.Comp.TotalDamage).Float());
    }

    /// <summary>Running thin, the speed that goes with it and the HUD alert, from the current mass.</summary>
    private void Refresh(Entity<WGNanomassComponent> ent)
    {
        if (GetSeed(ent) is not { } seed)
            return;

        var mass = seed.Comp.CurrentCharge;
        var thin = mass < ent.Comp.ThinThreshold;
        if (thin != ent.Comp.Thin)
        {
            ent.Comp.Thin = thin;
            Dirty(ent);
            _speed.RefreshMovementSpeedModifiers(ent);
            if (thin)
                _popup.PopupEntity(Loc.GetString("wg-protean-mass-starving"), ent, ent, PopupType.MediumCaution);
        }

        if (thin)
        {
            _alerts.ClearAlert(ent, ent.Comp.MassAlert);
            _alerts.ShowAlert(ent, ent.Comp.ThinAlert);
        }
        else
        {
            _alerts.ClearAlert(ent, ent.Comp.ThinAlert);
            var max = Math.Max(seed.Comp.MaxCharge, 1f);
            _alerts.ShowAlert(ent, ent.Comp.MassAlert, (short) Math.Clamp(MathF.Round(mass / max * 10f), 0f, 10f));
        }

        ent.Comp.LastMass = mass;
    }

    private void OnDamageChanged(Entity<WGNanomassComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta is not { } delta)
            return;

        var taken = delta.DamageDict.Values.Where(v => v > FixedPoint2.Zero).Sum(v => v.Float());
        if (taken <= 0f)
            return;

        ent.Comp.LastHit = _timing.CurTime;
        if (GetSeed(ent) is not { } seed)
            return;

        var shed = -_battery.UseCharge(seed, taken * ent.Comp.SlurryPerDamage, seed.Comp);
        if (shed > 0f)
            SpillSlurry(Transform(ent).Coordinates, ent.Comp.SlurryPrototype, shed);

        Refresh(ent);
    }

    /// <summary>Adds the mass to slurry already on the spot, or spills new slurry carrying it.</summary>
    public EntityUid SpillSlurry(EntityCoordinates coordinates, EntProtoId prototype, float mass)
    {
        _slurry.Clear();
        _lookup.GetEntitiesInRange(coordinates, SlurryMergeRange, _slurry, LookupFlags.Uncontained);
        foreach (var existing in _slurry)
        {
            existing.Comp.Mass += mass;
            return existing;
        }

        var spilled = Spawn(prototype, coordinates);
        EnsureComp<WGProteanSlurryComponent>(spilled).Mass = mass;
        return spilled;
    }

    private void OnSlurryStepAttempt(Entity<WGProteanSlurryComponent> ent, ref StepTriggerAttemptEvent args)
    {
        args.Continue |= HasComp<WGNanomassComponent>(args.Tripper);
    }

    private void OnSlurryStepped(Entity<WGProteanSlurryComponent> ent, ref StepTriggeredOffEvent args)
    {
        Reclaim(ent, args.Tripper);
    }

    /// <summary>A Protean takes back the slurry's mass, as much as it holds; what's left stays on the floor.</summary>
    public bool Reclaim(Entity<WGProteanSlurryComponent> slurry, EntityUid protean)
    {
        if (!TryComp(protean, out WGNanomassComponent? nanomass) || GetSeed(protean) is not { } seed)
            return false;

        var taken = Math.Min(slurry.Comp.Mass, seed.Comp.MaxCharge - seed.Comp.CurrentCharge);
        if (taken <= 0f)
            return false;

        _battery.SetCharge(seed, seed.Comp.CurrentCharge + taken, seed.Comp);
        slurry.Comp.Mass -= taken;
        if (slurry.Comp.Mass <= 0.01f)
            QueueDel(slurry);

        _popup.PopupEntity(Loc.GetString("wg-protean-slurry-reclaim"), protean, protean);
        Refresh((protean, nanomass));
        return true;
    }

    private void OnDamageModify(Entity<WGNanomassComponent> ent, ref DamageModifyEvent args)
    {
        var traces = Traces(ent);
        if (traces.Damage != 1f)
            args.Damage *= traces.Damage;

        if (traces.Heat != 1f && args.Damage.DamageDict.TryGetValue("Heat", out var heat))
            args.Damage.DamageDict["Heat"] = heat * traces.Heat;
    }

    private void OnEmp(Entity<WGNanomassComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        var heavy = args.EnergyConsumption >= ent.Comp.EmpHeavyEnergy;
        if (GetSeed(ent) is { } seed)
            _battery.UseCharge(seed, heavy ? ent.Comp.EmpHeavyBurn : ent.Comp.EmpLightBurn, seed.Comp);

        _stun.TryKnockdown(ent, ent.Comp.EmpStagger, true);
        _popup.PopupEntity(Loc.GetString(heavy ? "wg-protean-emp" : "wg-protean-emp-light"), ent, ent, PopupType.MediumCaution);
        Refresh(ent);
    }

    private void OnInnateVerbs(Entity<WGNanomassComponent> ent, ref GetVerbsEvent<InnateVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || args.Target == args.User || !Absorbable(ent, args.Target))
            return;

        var user = args.User;
        var item = args.Target;
        args.Verbs.Add(new InnateVerb
        {
            Text = Loc.GetString("wg-protean-absorb-verb"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/eat.svg.192dpi.png")),
            Act = () => TryAbsorb((user, ent.Comp), item, null),
        });
    }

    private void OnInteractionVerbs(Entity<WGNanomassComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || args.User == ent.Owner || args.Using is not { } item
            || !Absorbable(ent, item))
            return;

        var protean = ent.Owner;
        var user = args.User;
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("wg-protean-feed-verb"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/eat.svg.192dpi.png")),
            Act = () => TryAbsorb((protean, ent.Comp), item, user),
        });
    }

    /// <summary>Whether the verb shows: anything with a material in the diet, and living things, which are refused.</summary>
    private bool Absorbable(Entity<WGNanomassComponent> ent, EntityUid item)
    {
        return HasComp<MobStateComponent>(item) || YieldPerItem(ent, item, out _) > 0f;
    }

    /// <summary>Mass one item (one of a stack) gives, and the traces its materials leave.</summary>
    private float YieldPerItem(Entity<WGNanomassComponent> ent, EntityUid item, out List<EntProtoId> traces)
    {
        traces = new List<EntProtoId>();
        if (!TryComp(item, out PhysicalCompositionComponent? composition)
            || !_proto.TryIndex(ent.Comp.Diet, out var diet))
            return 0f;

        var mass = 0f;
        foreach (var (material, units) in composition.MaterialComposition)
        {
            if (!diet.Materials.TryGetValue(material, out var entry))
                continue;

            mass += units * entry.Mass;
            if (entry.Trace is { } trace)
                traces.Add(trace);
        }

        return mass;
    }

    /// <summary>
    /// Absorbs the item, or as much of a stack as fits, into the Protean. With a feeder it is someone else's hand,
    /// which also heals at once. Refuses the living, filled containers and owned or keyed items.
    /// </summary>
    public bool TryAbsorb(Entity<WGNanomassComponent> ent, EntityUid item, EntityUid? feeder)
    {
        var told = feeder ?? ent.Owner;
        if (Refusal(ent, item) is { } refusal)
        {
            _popup.PopupEntity(Loc.GetString(refusal), ent, told);
            return false;
        }

        if (GetSeed(ent) is not { } seed || !_proto.TryIndex(ent.Comp.Diet, out var diet))
            return false;

        var perItem = YieldPerItem(ent, item, out var traces);
        var room = seed.Comp.MaxCharge - seed.Comp.CurrentCharge;
        if (perItem <= 0f)
            return false;

        if (room <= 0.01f)
        {
            _popup.PopupEntity(Loc.GetString("wg-protean-absorb-full"), ent, told);
            return false;
        }

        var count = _stacks.GetCount(item);
        var taken = Math.Clamp((int) MathF.Ceiling(room / perItem), 1, count);
        var gained = Math.Min(room, perItem * taken);
        var itemName = Identity.Entity(item, EntityManager);

        _battery.SetCharge(seed, seed.Comp.CurrentCharge + gained, seed.Comp);
        foreach (var trace in traces.Distinct())
        {
            _status.TrySetStatusEffectDuration(ent, trace, diet.TraceDuration);
        }

        if (feeder is { } user)
        {
            if (TryComp(ent, out DamageableComponent? damageable))
                Heal((ent, damageable), ent.Comp.FedHeal);
            _popup.PopupEntity(Loc.GetString("wg-protean-fed-self", ("user", Identity.Entity(user, EntityManager)), ("item", itemName)), ent, ent);
            _popup.PopupEntity(Loc.GetString("wg-protean-fed-user", ("item", itemName), ("target", Identity.Entity(ent, EntityManager))), ent, user);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("wg-protean-absorb-self", ("item", itemName)), ent, ent);
            _popup.PopupEntity(Loc.GetString("wg-protean-absorb-others", ("user", Identity.Entity(ent, EntityManager)), ("item", itemName)),
                ent, Filter.PvsExcept(ent), true);
        }

        if (seed.Comp.CurrentCharge >= seed.Comp.MaxCharge - 0.01f)
            _popup.PopupEntity(Loc.GetString("wg-protean-mass-full"), ent, ent);

        _adminLog.Add(LogType.WGProtean, LogImpact.Low,
            $"{ToPrettyString(feeder ?? ent.Owner):user} had {ToPrettyString(ent.Owner):protean} absorb {taken} of {ToPrettyString(item):item} for {gained:F1} mass");

        if (taken >= count)
            QueueDel(item);
        else
            _stacks.Use(item, taken);

        Refresh(ent);
        return true;
    }

    /// <summary>Why the item can't be absorbed, or null when it can.</summary>
    private LocId? Refusal(Entity<WGNanomassComponent> ent, EntityUid item)
    {
        if (HasComp<MobStateComponent>(item)
            || HasComp<BodyComponent>(item)
            || TryComp(item, out MindContainerComponent? mind) && mind.HasMind)
            return "wg-protean-absorb-refuse-living";

        if (HasComp<WGAugmentPaidComponent>(item) || _whitelist.IsBlacklistPass(ent.Comp.AbsorbBlacklist, item))
            return "wg-protean-absorb-refuse-owned";

        // Solutions sit in containers too (a sheet's extractable reagents); only held things count.
        if (TryComp(item, out ContainerManagerComponent? containers)
            && containers.Containers.Values.Any(c => c.ContainedEntities.Any(e => !HasComp<SolutionComponent>(e))))
            return "wg-protean-absorb-refuse-container";

        return null;
    }

    /// <summary>The combined effect of the traces on the Protean.</summary>
    private (float Upkeep, float Heal, float Damage, float Heat) Traces(EntityUid protean)
    {
        var result = (Upkeep: 1f, Heal: 1f, Damage: 1f, Heat: 1f);
        foreach (var trace in _status.EnumerateStatusEffects<WGProteanTraceComponent>(protean))
        {
            result.Upkeep *= trace.Comp2.Upkeep;
            result.Heal *= trace.Comp2.Heal;
            result.Damage *= trace.Comp2.Damage;
            result.Heat *= trace.Comp2.Heat;
        }

        return result;
    }

    private void OnTraceApplied(Entity<WGProteanTraceComponent> ent, ref StatusEffectAppliedEvent args)
    {
        if (ent.Comp.Glow is not { } color || HasComp<PointLightComponent>(args.Target))
            return;

        var light = _lights.EnsureLight(args.Target);
        _lights.SetColor(args.Target, color, light);
        _lights.SetRadius(args.Target, ent.Comp.GlowRadius, light);
        EnsureComp<WGProteanGlowComponent>(args.Target);
    }

    private void OnTraceRemoved(Entity<WGProteanTraceComponent> ent, ref StatusEffectRemovedEvent args)
    {
        if (ent.Comp.Glow == null || !RemComp<WGProteanGlowComponent>(args.Target))
            return;

        RemComp<PointLightComponent>(args.Target);
    }
}
