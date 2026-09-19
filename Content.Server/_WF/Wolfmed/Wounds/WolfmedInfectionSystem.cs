using System.Linq;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Alert;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>
/// Infection, from a contaminated cut to a body dying of sepsis. A wound whose prototype declares
/// <see cref="WolfmedInfectionRiskBehavior"/> carries <see cref="WolfmedInfectionComponent"/> and gains
/// progress on a slow batched tick, scaled by that risk, by what has been done to the wound and by
/// whether something dirty has been in it. Past the profile's thresholds it hurts and widens (local),
/// then feverish and toxic (spreading), then it feeds <see cref="WolfmedSepsisComponent"/> on the body,
/// which is the stage that kills.
/// </summary>
/// <remarks>
/// One tick walks only wounds that are already contaminated and bodies that are already septic, so the
/// cost is in injuries rather than in players. Everything here is server-side: wound creation, damage,
/// temperature and pain all are. <see cref="Update"/> advances by whatever time has accumulated, so a
/// test can hand it ten minutes in one call.
/// </remarks>
public sealed class WolfmedInfectionSystem : EntitySystem
{
    /// <summary>The shipped profile. A downstream server retunes the prototype, not this file.</summary>
    public const string DefaultProfile = "WolfmedDefaultInfection";

    /// <summary>Alert shown while the patient is septic.</summary>
    public static readonly ProtoId<AlertPrototype> SepsisAlert = "WolfmedSepsis";

    private static readonly ProtoId<DamageTypePrototype> Poison = "Poison";

    /// <summary>Seconds between batches. Nothing in the model needs finer resolution than this.</summary>
    private const float TickSeconds = 5f;

    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private WolfmedDamageableSystem _damageable = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private float _accumulator;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
        SubscribeLocalEvent<WoundHostComponent, WolfmedCleanWoundsEvent>(OnClean);
        SubscribeLocalEvent<WoundHostComponent, WolfmedAntibioticEvent>(OnAntibiotic);
        SubscribeLocalEvent<WolfmedSepsisComponent, ComponentShutdown>(OnSepsisShutdown);
    }

    /// <summary>The profile every timer in the model comes from.</summary>
    public WolfmedInfectionProfilePrototype Profile => _prototypes.Index<WolfmedInfectionProfilePrototype>(DefaultProfile);

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        // WOLFGATE (W6): a chassis does not go septic. The gate is the part profile rather than the wound,
        // so a wound that can be infected in flesh (an embedded fragment, a surgical incision) is simply
        // inert on an IPC or a cybernetic limb, and no mechanical wound has to opt out one at a time.
        if (args.Kind == WolfmedWoundLifecycle.Removed || TerminatingOrDeleted(args.Wound) ||
            !_traits.IsOrganic(args.Part) ||
            !_prototypes.TryIndex(args.Prototype, out var prototype) ||
            !prototype.TryGetBehavior(args.Severity, out WolfmedInfectionRiskBehavior behavior) ||
            behavior.RiskMultiplier <= 0f)
            return;

        EnsureComp<WolfmedInfectionComponent>(args.Wound);
    }

    private void OnClean(Entity<WoundHostComponent> body, ref WolfmedCleanWoundsEvent args)
    {
        args.Cleaned += Clean(body);
    }

    private void OnAntibiotic(Entity<WoundHostComponent> body, ref WolfmedAntibioticEvent args)
    {
        args.Treated |= Treat(body, args.Units);
    }

    private void OnSepsisShutdown(Entity<WolfmedSepsisComponent> body, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(body))
            _alerts.ClearAlert(body, SepsisAlert);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < TickSeconds)
            return;

        var elapsed = _accumulator;
        _accumulator = 0f;

        if (!_config.GetCVar(WolfmedCVars.InfectionEnabled))
            return;

        var minutes = elapsed / 60f * _config.GetCVar(WolfmedCVars.InfectionRate);
        var profile = Profile;

        var wounds = EntityQueryEnumerator<WolfmedInfectionComponent, WoundComponent>();
        while (wounds.MoveNext(out var uid, out var infection, out var wound))
            TickWound((uid, infection, wound), profile, minutes);

        var sepsis = EntityQueryEnumerator<WolfmedSepsisComponent>();
        while (sepsis.MoveNext(out var uid, out var component))
            TickSepsis((uid, component), profile, minutes);
    }

    private void TickWound(Entity<WolfmedInfectionComponent, WoundComponent> wound,
        WolfmedInfectionProfilePrototype profile,
        float minutes)
    {
        var (infection, core) = (wound.Comp1, wound.Comp2);
        var part = core.HoldingPart;

        // A closed wound has nothing left to infect. The contamination goes with it.
        if (TerminatingOrDeleted(part) || core.State is WoundState.Healed or WoundState.Scarred)
        {
            RemComp<WolfmedInfectionComponent>(wound);
            return;
        }

        var body = CompOrNull<BodyPartComponent>(part)?.Body;
        var treatment = TryComp(wound, out WoundBleedingComponent? bleeding)
            ? bleeding.Treatment
            : BleedingTreatment.None;
        var openness = profile.TreatmentMultipliers.GetValueOrDefault(treatment, 1f);

        if (infection.Cleaned && infection.Stage < WolfmedInfectionStage.Spreading)
            SetProgress(wound, infection.Progress - profile.CleanDecayPerMinute * minutes, profile);
        else
            SetProgress(wound,
                infection.Progress +
                profile.ProgressPerMinute * minutes * openness * infection.Contamination *
                _traits.GetInfectionRisk(wound.Owner),
                profile);

        if (infection.Progress <= 0f)
            return;

        if (infection.Stage >= WolfmedInfectionStage.Local)
        {
            _pain.ChangePain(part, profile.LocalPainPerMinute * minutes);

            // "Slower healing", expressed as the wound reopening: treatment has to outrun the infection
            // rather than be cancelled by it. Capped so an ignored cut cannot widen forever.
            var creep = FixedPoint2.Min(profile.SeverityPerMinute * minutes,
                profile.MaxSeverityAdded - infection.SeverityAdded);
            if (creep > FixedPoint2.Zero && _wounds.ChangeSeverity(wound.Owner, creep))
                infection.SeverityAdded += creep;
        }

        if (infection.Stage < WolfmedInfectionStage.Spreading || body is not { } host)
            return;

        Fever(host, profile, minutes);
        _damageable.ChangeDamage(host, Damage(profile.SpreadingPoisonPerMinute * minutes), ignoreResistances: true);

        if (infection.Stage == WolfmedInfectionStage.Septic && _config.GetCVar(WolfmedCVars.SepsisEnabled))
            EnsureComp<WolfmedSepsisComponent>(host);
    }

    private void TickSepsis(Entity<WolfmedSepsisComponent> body,
        WolfmedInfectionProfilePrototype profile,
        float minutes)
    {
        if (TerminatingOrDeleted(body))
            return;

        var sources = CountSources(body);
        body.Comp.Progress = Math.Clamp(body.Comp.Progress + (sources > 0
                ? profile.SepsisPerMinute * minutes * sources
                : -profile.SepsisRecoveryPerMinute * minutes),
            0f,
            100f);
        Dirty(body);

        if (body.Comp.Progress <= 0f)
        {
            RemComp<WolfmedSepsisComponent>(body);
            return;
        }

        _alerts.ShowAlert(body, SepsisAlert);
        Fever(body, profile, minutes);

        // A quarter of the rate at onset, the whole of it at 100: ignoring sepsis kills, noticing it late
        // still leaves a window.
        var scale = 0.25f + 0.75f * body.Comp.Progress / 100f;
        _damageable.ChangeDamage(body.Owner,
            Damage(profile.SepsisPoisonPerMinute * minutes * scale),
            ignoreResistances: true);
    }

    /// <summary>Spreading wounds and dead limbs, both of which keep a systemic infection fed.</summary>
    private int CountSources(EntityUid body)
    {
        var sources = 0;
        foreach (var part in Parts(body))
        {
            if (CompOrNull<WolfmedNecrosisComponent>(part)?.Necrotic == true)
                sources++;

            foreach (var wound in _wounds.GetWounds(part))
            {
                if (CompOrNull<WolfmedInfectionComponent>(wound)?.Stage >= WolfmedInfectionStage.Spreading)
                    sources++;
            }
        }

        return sources;
    }

    private void Fever(EntityUid body, WolfmedInfectionProfilePrototype profile, float minutes)
    {
        if (!TryComp(body, out TemperatureComponent? temperature) ||
            temperature.CurrentTemperature >= profile.FeverTemperature)
            return;

        _temperature.ForceChangeTemperature(body,
            Math.Min(profile.FeverTemperature, temperature.CurrentTemperature + profile.FeverRise * minutes * 60f),
            temperature);
    }

    private void SetProgress(Entity<WolfmedInfectionComponent, WoundComponent> wound,
        float progress,
        WolfmedInfectionProfilePrototype profile)
    {
        var infection = wound.Comp1;
        infection.Progress = Math.Clamp(progress, 0f, profile.SepsisAt);

        // Cleaning and antibiotics buy a reset, not immunity: an open wound that has been cleared starts
        // accumulating again from nothing.
        if (infection.Progress <= 0f)
        {
            infection.Cleaned = false;
            infection.SeverityAdded = FixedPoint2.Zero;
        }

        infection.Stage = infection.Progress switch
        {
            var value when value >= profile.SepsisAt => WolfmedInfectionStage.Septic,
            var value when value >= profile.SpreadingAt => WolfmedInfectionStage.Spreading,
            var value when value >= profile.LocalAt => WolfmedInfectionStage.Local,
            _ => WolfmedInfectionStage.None,
        };
        Dirty(wound.Owner, infection);
    }

    private static DamageSpecifier Damage(FixedPoint2 poison) =>
        new() { DamageDict = { [Poison] = poison } };

    private IEnumerable<EntityUid> Parts(EntityUid body)
    {
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (HasComp<WoundableComponent>(part))
                yield return part;
        }
    }

    /// <summary>
    /// Marks a wound as having had something dirty in it, which multiplies everything it will ever
    /// accumulate. W1's knife removal is the caller.
    /// </summary>
    public void Contaminate(EntityUid wound)
    {
        // WOLFGATE (W6): nothing to contaminate on a chassis, so digging a fragment out of one with a knife
        // costs nothing later.
        if (TerminatingOrDeleted(wound) || !TryComp(wound, out WoundComponent? core) ||
            !_traits.IsOrganic(core.HoldingPart))
            return;

        var infection = EnsureComp<WolfmedInfectionComponent>(wound);
        infection.Contamination = Profile.ContaminationMultiplier;
        infection.Cleaned = false;
        Dirty(wound, infection);
    }

    /// <summary>
    /// Antiseptic over every open wound on the body. Returns how many it reached. Cleaning is prevention:
    /// it sheds a local infection over the next minute but does nothing for one that has already spread.
    /// </summary>
    public int Clean(EntityUid body)
    {
        var cleaned = 0;
        foreach (var part in Parts(body))
        {
            foreach (var wound in _wounds.GetWounds(part).ToArray())
            {
                if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                    !TryComp(wound, out WolfmedInfectionComponent? infection) || infection.Cleaned)
                    continue;

                infection.Cleaned = true;
                infection.Contamination = 1f;
                Dirty(wound.Owner, infection);
                cleaned++;
            }
        }

        if (cleaned > 0)
            _popup.PopupEntity(Loc.GetString("wolfmed-wound-cleaned"), body, body);

        return cleaned;
    }

    /// <summary>
    /// An antibiotic dose: clears contamination everywhere at once and pulls sepsis back. Returns whether
    /// there was anything to treat.
    /// </summary>
    public bool Treat(EntityUid body, float units)
    {
        var profile = Profile;
        var treated = false;

        foreach (var part in Parts(body))
        {
            foreach (var wound in _wounds.GetWounds(part).ToArray())
            {
                if (!TryComp(wound, out WolfmedInfectionComponent? infection))
                    continue;

                treated |= infection.Progress > 0f;
                SetProgress((wound.Owner, infection, wound.Comp),
                    infection.Progress - profile.AntibioticPerUnit * units,
                    profile);
            }
        }

        if (!TryComp(body, out WolfmedSepsisComponent? sepsis))
            return treated;

        sepsis.Progress -= profile.SepsisAntibioticPerUnit * units;
        if (sepsis.Progress <= 0f)
            RemComp<WolfmedSepsisComponent>(body);
        else
            Dirty(body, sepsis);

        return true;
    }

    /// <summary>The infection on one wound, for the analyzer and for tests.</summary>
    public WolfmedInfectionStage GetStage(EntityUid wound) =>
        CompOrNull<WolfmedInfectionComponent>(wound)?.Stage ?? WolfmedInfectionStage.None;

    /// <summary>The worst infection stage among a part's wounds.</summary>
    public WolfmedInfectionStage GetPartStage(Entity<WoundableComponent?> part)
    {
        var stage = WolfmedInfectionStage.None;
        foreach (var wound in _wounds.GetWounds(part))
            stage = (WolfmedInfectionStage) Math.Max((byte) stage, (byte) GetStage(wound));

        return stage;
    }

    /// <summary>How far the body's systemic infection has got, or 0 when it has none.</summary>
    public float GetSepsis(EntityUid body) => CompOrNull<WolfmedSepsisComponent>(body)?.Progress ?? 0f;
}
