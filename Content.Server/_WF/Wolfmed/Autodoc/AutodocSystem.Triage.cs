using System.Linq;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.CCVar;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Autodoc;

public sealed partial class AutodocSystem
{
    /// <summary>Whether the vital alarm is allowed to make any noise at all.</summary>
    private bool _alarmEnabled = true;

    /// <summary>Runs of one step that change nothing before the pod abandons the procedure.</summary>
    private int _stepRetries = 3;

    /// <summary>Sedation the pod will not push a patient past.</summary>
    private float _sedationCap = 0.5f;

    private void InitializeTriage()
    {
        Subs.CVar(_cfg, WolfmedCVars.AutodocAlarm, value => _alarmEnabled = value, true);
        Subs.CVar(_cfg, WolfmedCVars.AutodocStepRetries, value => _stepRetries = Math.Max(1, value), true);
        Subs.CVar(_cfg, WolfmedCVars.AutodocSedationCap, value => _sedationCap = Math.Clamp(value, 0f, 1f), true);
    }

    #region The plan

    public bool HasAutofixModule(Entity<AutodocComponent> ent) =>
        _slots.GetItemOrNull(ent.Owner, AutodocComponent.AutofixSlotId) is { } module &&
        HasComp<AutodocAutofixModuleComponent>(module);

    /// <summary>
    /// The queue the pod would write for itself: the triage order walked top to bottom, taking every
    /// procedure the occupant's condition currently allows. It never queues anything an operator could not
    /// queue by hand, so the module only saves the scheduling.
    /// </summary>
    public List<AutodocProcedureEntry> Plan(Entity<AutodocComponent> ent, EntityUid body)
    {
        var result = new List<AutodocProcedureEntry>();
        if (!_protos.TryIndex(ent.Comp.Triage, out var triage))
            return result;

        var available = GetAvailable(ent, body);
        var taken = new HashSet<(string, TargetBodyPart)>();

        foreach (var step in triage.Steps)
        {
            if (step.Condition == AutodocTriageCondition.Arrest && !_life.InArrest(body))
                continue;

            // The one step that is not a surgery: the paddles come before anything else is worth doing.
            if (step.Defibrillate)
            {
                TryDefibrillateOccupant(ent, body);
                continue;
            }

            foreach (var surgery in StepSurgeries(step))
            {
                foreach (var entry in available)
                {
                    if (entry.Surgery != surgery || !entry.Known ||
                        ent.Comp.FailedProcedures.Contains((entry.Surgery.Id, entry.Part)) ||
                        IsBlockedByEmbedded(ent, body, entry) ||
                        step.RequiresStarted && !AlreadyStarted(body, entry) ||
                        !taken.Add((entry.Surgery.Id, entry.Part)) ||
                        !CanPlanWithoutHelp(ent, entry.Surgery))
                        continue;

                    result.Add(entry);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// True when the next step the body needs belongs to the procedure itself rather than to something it
    /// requires first. Closing an incision is a valid surgery on an unopened body, because the pod would
    /// open one to close it; this is what tells an already-open patient from an intact one.
    /// </summary>
    private bool AlreadyStarted(EntityUid body, AutodocProcedureEntry entry)
    {
        return ResolvePart(body, entry.Part) is { } part &&
               IsProcedureStarted(body, part, entry.Surgery);
    }

    /// <summary>The same question for one named surgery on one resolved part.</summary>
    public bool IsProcedureStarted(EntityUid body, EntityUid part, EntProtoId surgery)
    {
        return _surgery.GetSingleton(surgery) is { } surgeryEnt &&
               _surgery.GetNextStep(body, part, surgeryEnt) is { } next &&
               MetaData(next.Surgery.Owner).EntityPrototype?.ID == surgery.Id;
    }

    /// <summary>
    /// A part with something still stuck in it takes no other work. WolfmedEmbeddedObjectComponent refuses
    /// every treatment on the wound it sits in, so anything else planned on that part would repeat until
    /// the stall guard stopped it. The removal is planned first and the rest of the part waits for it.
    /// </summary>
    private bool IsBlockedByEmbedded(Entity<AutodocComponent> ent, EntityUid body, AutodocProcedureEntry entry)
    {
        return entry.Surgery != RemoveEmbedded &&
               ResolvePart(body, entry.Part) is { } part &&
               _embedded.GetPartCount(part) > 0;
    }

    /// <summary>The surgeries one triage step covers: the ones it names, then its categories' own.</summary>
    private IEnumerable<EntProtoId> StepSurgeries(AutodocTriageStep step)
    {
        foreach (var surgery in step.Surgeries)
            yield return surgery;

        foreach (var category in step.Categories)
        {
            if (!_protos.TryIndex(category, out var proto))
                continue;

            foreach (var surgery in proto.Surgeries)
                yield return surgery;
        }
    }

    /// <summary>
    /// A procedure the pod can finish on its own. Anything that wants a limb or an organ handed to it is
    /// left out of the plan unless the tray already holds something that fits.
    /// </summary>
    private bool CanPlanWithoutHelp(Entity<AutodocComponent> ent, EntProtoId surgery)
    {
        var tray = _slots.GetItemOrNull(ent.Owner, AutodocComponent.TraySlotId);
        foreach (var requirement in BuildRequirements(ent, surgery))
        {
            if (requirement.Kind == AutodocRequirementKind.Reagent)
                continue;

            if (tray == null || !Matches(requirement, tray.Value))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Replaces the queue with the triage plan. Returns how many procedures it wrote; zero means there was
    /// nothing the pod could do for this patient.
    /// </summary>
    public int TryPlan(Entity<AutodocComponent> ent)
    {
        if (GetOccupant(ent) is not { } body ||
            ent.Comp.State is not (AutodocState.Idle or AutodocState.Complete))
            return 0;

        var plan = Plan(ent, body);

        // An empty plan leaves the queue alone. It used to clear it first, so with the autofix module on
        // anything an operator queued by hand was thrown away within a few seconds and never ran.
        if (plan.Count == 0)
            return 0;

        ent.Comp.Queue.Clear();

        // Self-service is one procedure at a time, so the plan gives it the first thing that matters.
        foreach (var entry in ent.Comp.SelfService ? plan.Take(1) : plan.AsEnumerable())
            TryQueue(ent, entry.Surgery, entry.Part);

        return ent.Comp.Queue.Count;
    }

    #endregion

    #region Automatic mode

    public void SetAuto(Entity<AutodocComponent> ent, bool auto)
    {
        if (ent.Comp.Auto == auto)
            return;

        ent.Comp.Auto = auto && HasAutofixModule(ent);
        ent.Comp.AutoNextPlan = TimeSpan.Zero;
        ent.Comp.AutoSaidNothing = false;
        ent.Comp.AutoSignature = null;
        ent.Comp.AutoReplans = 0;

        if (!ent.Comp.Auto)
            Speak(ent, AutodocVoiceEvent.AutoOff);
    }

    /// <summary>
    /// With the module switched on the pod plans and starts by itself whenever there is somebody in it and
    /// nothing running. An emagged pod is left to <see cref="TickEmag"/>: it has its own plans.
    /// </summary>
    private void TickAuto(Entity<AutodocComponent> ent)
    {
        if (!ent.Comp.Auto || IsEmagged(ent) || !IsPowered(ent) || !HasAutofixModule(ent))
            return;

        if (ent.Comp.State is not (AutodocState.Idle or AutodocState.Complete))
            return;

        if (GetOccupant(ent) is not { } body)
        {
            ent.Comp.AutoSaidNothing = false;
            ent.Comp.AutoSignature = null;
            ent.Comp.AutoReplans = 0;
            return;
        }

        if (_timing.CurTime < ent.Comp.AutoNextPlan)
            return;

        ent.Comp.AutoNextPlan = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.AutoPlanInterval);

        // Somebody queued something by hand. Running it is the module's job; replacing it is not.
        if (ent.Comp.Queue.Count > 0)
        {
            TryStart(ent, null);
            return;
        }

        // A body the pod is not changing gets a bounded number of looks. Without this a procedure that
        // lists again the moment it finishes would have the module plan, run and plan again for ever.
        var signature = BodySignature(body);
        if (signature != ent.Comp.AutoSignature)
        {
            ent.Comp.AutoSignature = signature;
            ent.Comp.AutoReplans = 0;
        }
        else if (ent.Comp.AutoReplans >= ent.Comp.AutoReplanLimit)
        {
            if (!ent.Comp.AutoSaidNothing)
                Speak(ent, AutodocVoiceEvent.AutoNothing);

            ent.Comp.AutoSaidNothing = true;
            UpdateUi(ent);
            return;
        }
        else
        {
            ent.Comp.AutoReplans++;
        }

        if (TryPlan(ent) == 0)
        {
            // Said once per patient: the pod keeps looking in case a bleed starts, but it only announces
            // the empty plan the first time.
            if (!ent.Comp.AutoSaidNothing)
                Speak(ent, AutodocVoiceEvent.AutoNothing);

            ent.Comp.AutoSaidNothing = true;
            UpdateUi(ent);
            return;
        }

        ent.Comp.AutoSaidNothing = false;
        Speak(ent, AutodocVoiceEvent.AutoEngaged);
        TryStart(ent, null);
    }

    #endregion

    #region Vital alarm

    /// <summary>
    /// A medical monitor on the lid. It beeps while the occupant is failing and says so once; a brain that
    /// has stopped gets one long tone and then nothing, because there is no longer anything to call for.
    /// </summary>
    private void TickAlarm(Entity<AutodocComponent> ent)
    {
        var level = _alarmEnabled && IsPowered(ent) && GetOccupant(ent) is { } body
            ? GetAlarm(body)
            : AutodocAlarm.None;

        if (level != ent.Comp.AlarmLevel)
        {
            ent.Comp.AlarmLevel = level;
            ent.Comp.AlarmNext = TimeSpan.Zero;

            if (level == AutodocAlarm.Flatline)
            {
                _audio.PlayPvs(ent.Comp.FlatlineSound, ent.Owner,
                    AudioParams.Default.WithVolume(ent.Comp.AlarmGain));
                return;
            }

            // One line per escalation, with the beeps: the operator hears what changed, not every beep.
            if (level != AutodocAlarm.None)
                Speak(ent, level == AutodocAlarm.Dying ? AutodocVoiceEvent.Dying : AutodocVoiceEvent.Critical);
        }

        if (level is AutodocAlarm.None or AutodocAlarm.Flatline || _timing.CurTime < ent.Comp.AlarmNext)
            return;

        var interval = level == AutodocAlarm.Arrest
            ? ent.Comp.AlarmArrestInterval
            : ent.Comp.AlarmCritInterval;

        ent.Comp.AlarmNext = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(0.5f, interval));
        _audio.PlayPvs(ent.Comp.AlarmSound, ent.Owner, AudioParams.Default.WithVolume(ent.Comp.AlarmGain));
    }

    /// <summary>How bad the occupant is. Public so a test can read the same value the alarm acts on.</summary>
    public AutodocAlarm GetAlarm(EntityUid body)
    {
        if (_life.IsBrainDead(body))
            return AutodocAlarm.Flatline;

        if (_life.InArrest(body))
            return AutodocAlarm.Arrest;

        if (_mobState.IsCritical(body))
            return AutodocAlarm.Critical;

        return _life.GetBrain(body) is { } brain && _life.DrainRate(body, brain) > 0f
            ? AutodocAlarm.Dying
            : AutodocAlarm.None;
    }

    #endregion
}
