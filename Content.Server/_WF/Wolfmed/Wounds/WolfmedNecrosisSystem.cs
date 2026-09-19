using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Targeting;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>
/// Tissue death. Three things start the clock: a tourniquet nobody took off, a burn or a freeze deep
/// enough to carry <see cref="WolfmedNecrosisRiskBehavior"/>, and a limb put back on long after it came
/// off. All three accumulate on <see cref="WolfmedNecrosisComponent"/> on the part, and all three end the
/// same way - a <c>WolfmedNecrosisWound</c> that nothing treats, a limb that no longer works properly and
/// a standing source of sepsis, until the part is amputated and replaced.
/// </summary>
/// <remarks>
/// The patient gets one warning popup before it happens, and the analyzer flags the part from the moment
/// the clock starts. <see cref="Update"/> advances by whatever time has accumulated so a test can hand it
/// the whole ten minutes at once.
/// </remarks>
public sealed class WolfmedNecrosisSystem : EntitySystem
{
    private const float TickSeconds = 5f;

    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WolfmedInfectionSystem _infection = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundBleedingSystem _bleeding = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private WoundTargetResolver _targeting = default!;

    private float _accumulator;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
        SubscribeLocalEvent<WoundHostComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        if (TerminatingOrDeleted(args.Part))
            return;

        var risk = _traits.GetPartNecrosisRisk(args.Part, out var onset);
        if (risk > 0f)
            Start(args.Part, WolfmedNecrosisSource.Wound, onset);
        else if (TryComp(args.Part, out WolfmedNecrosisComponent? necrosis) &&
                 !necrosis.Necrotic &&
                 necrosis.Source == WolfmedNecrosisSource.Wound)
            RemComp<WolfmedNecrosisComponent>(args.Part);
    }

    private void OnGetVerbs(Entity<WoundHostComponent> body, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanInteract || !args.CanAccess || FindTourniquet(body, args.User) is not { } part)
            return;

        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("wolfmed-tourniquet-loosen-verb"),
            Act = () => Loosen(body, part, user),
        });
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < TickSeconds)
            return;

        var elapsed = _accumulator;
        _accumulator = 0f;

        if (!_config.GetCVar(WolfmedCVars.NecrosisEnabled))
            return;

        var profile = _infection.Profile;
        var seconds = elapsed * _config.GetCVar(WolfmedCVars.NecrosisRate);

        // A tourniquet stays a tourniquet only while something under it is still clamped shut; sutures or
        // surgery on the wound underneath take the clock off with them.
        var tourniquets = EntityQueryEnumerator<WolfmedTourniquetComponent>();
        while (tourniquets.MoveNext(out var uid, out _))
        {
            if (TerminatingOrDeleted(uid))
                continue;

            if (!IsClamped(uid))
            {
                RemComp<WolfmedTourniquetComponent>(uid);
                if (TryComp(uid, out WolfmedNecrosisComponent? applied) && !applied.Necrotic &&
                    applied.Source == WolfmedNecrosisSource.Tourniquet)
                    RemComp<WolfmedNecrosisComponent>(uid);
                continue;
            }

            Start(uid, WolfmedNecrosisSource.Tourniquet, profile.TourniquetOnset);
        }

        var dying = EntityQueryEnumerator<WolfmedNecrosisComponent>();
        while (dying.MoveNext(out var uid, out var necrosis))
        {
            if (necrosis.Necrotic || necrosis.Onset <= TimeSpan.Zero || TerminatingOrDeleted(uid))
                continue;

            necrosis.Progress += seconds;
            Dirty(uid, necrosis);

            var onset = (float) necrosis.Onset.TotalSeconds;
            if (necrosis.Progress >= onset)
            {
                MakeNecrotic(uid);
                continue;
            }

            if (necrosis.Warned || necrosis.Progress < onset * profile.WarnFraction)
                continue;

            necrosis.Warned = true;
            if (CompOrNull<BodyPartComponent>(uid)?.Body is { } host)
                _popup.PopupEntity(Loc.GetString("wolfmed-necrosis-warning"), host, host, PopupType.MediumCaution);
        }
    }

    /// <summary>
    /// Puts the part on the clock, or shortens the clock if this source is faster. Public so the part
    /// lifecycle and a test can start it without a wound.
    /// </summary>
    public void Start(EntityUid part, WolfmedNecrosisSource source, TimeSpan onset)
    {
        // WOLFGATE (W6): a chassis has no tissue to lose, so no source starts the clock on one - not a
        // tourniquet, not a deep freeze, not a limb left on the floor. Gated here and in MakeNecrotic so
        // every entry point is covered.
        if (TerminatingOrDeleted(part) || onset <= TimeSpan.Zero || !HasComp<WoundableComponent>(part) ||
            !_traits.IsOrganic(part))
            return;

        var necrosis = EnsureComp<WolfmedNecrosisComponent>(part);
        if (necrosis.Necrotic || necrosis.Onset > TimeSpan.Zero && necrosis.Onset <= onset)
            return;

        necrosis.Source = source;
        necrosis.Onset = onset;
        Dirty(part, necrosis);
    }

    /// <summary>
    /// Kills the part: a wound nothing treats, already infected enough to feed sepsis. Returns the wound,
    /// or null when the part cannot carry one. Public so a test and the reattachment path can skip the wait.
    /// </summary>
    public EntityUid? MakeNecrotic(EntityUid part)
    {
        if (TerminatingOrDeleted(part) || !HasComp<WoundableComponent>(part) || !_traits.IsOrganic(part))
            return null;

        var necrosis = EnsureComp<WolfmedNecrosisComponent>(part);
        if (necrosis.Necrotic)
            return null;

        necrosis.Necrotic = true;
        necrosis.Progress = 0f;
        necrosis.Onset = TimeSpan.Zero;
        Dirty(part, necrosis);

        // The tourniquet has nothing left to save; take the clock off so it does not keep ticking.
        RemComp<WolfmedTourniquetComponent>(part);

        var profile = _infection.Profile;
        if (!_wounds.CanCreateWound(part, profile.NecrosisWound))
            return null;

        var wound = _wounds.CreateOrMergeWound(part, profile.NecrosisWound, profile.NecrosisSeverity);
        if (wound is { } created)
            _infection.Contaminate(created);

        if (CompOrNull<BodyPartComponent>(part)?.Body is { } body)
            _popup.PopupEntity(Loc.GetString("wolfmed-necrosis-dead"), body, body, PopupType.LargeCaution);

        return wound;
    }

    /// <summary>
    /// Takes a tourniquet off: the bleeding it was holding starts again, and the limb stops dying. The
    /// trade the verb exists for.
    /// </summary>
    public bool Loosen(EntityUid body, EntityUid part, EntityUid user)
    {
        if (!TryComp(part, out WolfmedTourniquetComponent? tourniquet))
            return false;

        foreach (var wound in _wounds.GetWounds(part).ToArray())
        {
            if (TryComp(wound, out WoundBleedingComponent? bleeding) &&
                bleeding.Treatment == BleedingTreatment.Clamped)
                _bleeding.SetTreatment(wound.Owner, BleedingTreatment.None);
        }

        RemComp<WolfmedTourniquetComponent>(part);
        if (TryComp(part, out WolfmedNecrosisComponent? necrosis) && !necrosis.Necrotic &&
            necrosis.Source == WolfmedNecrosisSource.Tourniquet)
            RemComp<WolfmedNecrosisComponent>(part);

        _audio.PlayPvs(tourniquet.LoosenSound, body);
        _popup.PopupEntity(Loc.GetString("wolfmed-tourniquet-loosened"), body, user);
        return true;
    }

    /// <summary>Stamps the time a limb came off, for the reattachment grace period.</summary>
    public void OnDetached(EntityUid part)
    {
        // WOLFGATE (W6): a chassis limb keeps indefinitely, so it is not even stamped.
        if (TerminatingOrDeleted(part) || !HasComp<WoundableComponent>(part) || !_traits.IsOrganic(part))
            return;

        var necrosis = EnsureComp<WolfmedNecrosisComponent>(part);
        necrosis.DetachedAt = _timing.CurTime;
    }

    /// <summary>
    /// Checks a reattached limb against that stamp. A limb put back within the grace period is fine; one
    /// left on the floor too long is dead tissue the moment it goes back on.
    /// </summary>
    public bool OnAttached(EntityUid part)
    {
        if (TerminatingOrDeleted(part) || !TryComp(part, out WolfmedNecrosisComponent? necrosis) ||
            necrosis.DetachedAt is not { } detached)
            return false;

        necrosis.DetachedAt = null;
        if (necrosis.Necrotic || !_config.GetCVar(WolfmedCVars.NecrosisEnabled) ||
            _timing.CurTime - detached <= _infection.Profile.ReattachGrace)
            return false;

        necrosis.Source = WolfmedNecrosisSource.Reattachment;
        Dirty(part, necrosis);
        MakeNecrotic(part);
        return true;
    }

    /// <summary>Whether the part still has a bleed a tourniquet is holding shut.</summary>
    private bool IsClamped(EntityUid part)
    {
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (TryComp(wound, out WoundBleedingComponent? bleeding) &&
                bleeding.Treatment == BleedingTreatment.Clamped)
                return true;
        }

        return false;
    }

    /// <summary>The tourniquet this user would loosen: the part they have selected, or any tourniqueted one.</summary>
    private EntityUid? FindTourniquet(EntityUid body, EntityUid user)
    {
        if (TryComp(user, out TargetingComponent? targeting) &&
            _targeting.TryResolveExact(body, targeting.Target, out var selected) &&
            HasComp<WolfmedTourniquetComponent>(selected))
            return selected;

        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (HasComp<WolfmedTourniquetComponent>(part))
                return part;
        }

        return null;
    }

    /// <summary>Whether this part is dead tissue. The analyzer's flag.</summary>
    public bool IsNecrotic(EntityUid part) => CompOrNull<WolfmedNecrosisComponent>(part)?.Necrotic == true;

    /// <summary>Whether this part is on a necrosis clock that has not run out yet.</summary>
    public bool IsAtRisk(EntityUid part) =>
        CompOrNull<WolfmedNecrosisComponent>(part) is { Necrotic: false, Onset.Ticks: > 0 };

    /// <summary>Marks a part as being under a tourniquet. The one call site is TourniquetSystem.Apply.</summary>
    public void OnTourniquetApplied(EntityUid part)
    {
        // WOLFGATE (W6): a tourniquet on a cybernetic limb is bleeding control and nothing more.
        if (!TerminatingOrDeleted(part) && HasComp<WoundableComponent>(part) && _traits.IsOrganic(part))
            EnsureComp<WolfmedTourniquetComponent>(part);
    }
}
