using System.Linq;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._Shitmed.Medical.Surgery.Effects.Step;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared.Body.Part;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Wolfmed.Autodoc;

public sealed partial class AutodocSystem
{
    /// <summary>The surgery whose requirement chain means the body gets opened, so anaesthesia is wanted.</summary>
    private static readonly EntProtoId OpenIncision = "SurgeryOpenIncision";

    /// <summary>Step families the step's own components and tools cannot tell apart.</summary>
    private static readonly Dictionary<string, AutodocVoiceEvent> StepVoiceOverrides = new()
    {
        ["SurgeryStepCloseIncision"] = AutodocVoiceEvent.StepClose,
        ["SurgeryStepSealWounds"] = AutodocVoiceEvent.StepClose,
        ["SurgeryStepSealTendWound"] = AutodocVoiceEvent.StepClose,
        ["SurgeryStepCloseEvisceration"] = AutodocVoiceEvent.StepEvisceration,
        ["SurgeryStepClampEvisceration"] = AutodocVoiceEvent.StepEvisceration,
    };

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<AutodocComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var ent = (uid, comp);
            TickVoice(ent);
            TickEmag(ent);
            TickIdleChatter(ent);

            switch (comp.State)
            {
                case AutodocState.Preparing:
                    Prepare(ent);
                    break;
                case AutodocState.Step:
                    TickStep(ent, frameTime);
                    break;
                case AutodocState.Waiting:
                    TickWaiting(ent);
                    break;
            }
        }
    }

    #region Library

    /// <summary>True when the pod's base library or the disk in its slot carries this surgery.</summary>
    public bool IsKnown(Entity<AutodocComponent> ent, EntProtoId surgery)
    {
        foreach (var program in ent.Comp.BasePrograms)
        {
            if (_protos.TryIndex(program, out var proto) && proto.Surgeries.Contains(surgery))
                return true;
        }

        return CurrentDisk(ent) is { } disk &&
               _protos.TryIndex(disk.Program, out var diskProto) &&
               diskProto.Surgeries.Contains(surgery);
    }

    public AutodocProgramDiskComponent? CurrentDisk(Entity<AutodocComponent> ent) =>
        _slots.GetItemOrNull(ent.Owner, AutodocComponent.DiskSlotId) is { } disk &&
        TryComp(disk, out AutodocProgramDiskComponent? comp)
            ? comp
            : null;

    public bool HasDefibModule(Entity<AutodocComponent> ent) =>
        _slots.GetItemOrNull(ent.Owner, AutodocComponent.ModuleSlotId) is { } module &&
        HasComp<AutodocDefibModuleComponent>(module);

    /// <summary>Every surgery the occupant's condition currently allows, part by part.</summary>
    public List<AutodocProcedureEntry> GetAvailable(Entity<AutodocComponent> ent, EntityUid body)
    {
        var result = new List<AutodocProcedureEntry>();
        foreach (var (partId, part) in _body.GetBodyChildren(body))
        {
            if (_body.GetTargetBodyPart(part.PartType, part.Symmetry) is not { } target)
                continue;

            foreach (var surgery in _surgery.AllSurgeries)
            {
                if (!_surgery.WolfmedSurgeryValid(body, partId, surgery))
                    continue;

                result.Add(new AutodocProcedureEntry(surgery, target, IsKnown(ent, surgery)));
            }
        }

        return result;
    }

    #endregion

    #region Queue

    public bool TryQueue(Entity<AutodocComponent> ent, EntProtoId surgery, TargetBodyPart part)
    {
        if (GetOccupant(ent) is not { } body || ResolvePart(body, part) is not { } partEnt)
            return false;

        if (!IsKnown(ent, surgery))
        {
            Speak(ent, AutodocVoiceEvent.DiskMissing);
            return false;
        }

        if (!_surgery.WolfmedSurgeryValid(body, partEnt, surgery))
            return false;

        ent.Comp.Queue.Add(new AutodocQueued
        {
            Surgery = surgery,
            Part = part,
            Requirements = BuildRequirements(ent, surgery),
        });
        return true;
    }

    /// <summary>
    /// What the procedure will ask for, worked out once at queue time from the step components of the surgery
    /// and everything it requires.
    /// </summary>
    public List<AutodocRequirement> BuildRequirements(Entity<AutodocComponent> ent, EntProtoId surgery)
    {
        var result = new List<AutodocRequirement>();
        CollectRequirements(surgery, result, 0);

        var proc = _protos.TryIndex<AutodocProcedurePrototype>(surgery, out var procProto) ? procProto : null;
        var opens = OpensBody(surgery, 0);
        var anaesthetic = proc?.Anaesthetic ?? (opens ? ent.Comp.DefaultAnaesthetic : 0f);
        var antibiotic = proc?.Antibiotic ?? (opens ? ent.Comp.DefaultAntibiotic : 0f);

        if (anaesthetic > 0f)
            result.Insert(0, new AutodocRequirement { Kind = AutodocRequirementKind.Reagent, Reagent = nameof(AutodocReagentRole.Anaesthetic), Units = anaesthetic });

        if (antibiotic > 0f)
            result.Add(new AutodocRequirement { Kind = AutodocRequirementKind.Reagent, Reagent = nameof(AutodocReagentRole.Antibiotic), Units = antibiotic });

        return result;
    }

    private void CollectRequirements(EntProtoId surgery, List<AutodocRequirement> result, int depth)
    {
        if (depth > 8 ||
            _surgery.GetSingleton(surgery) is not { } surgeryEnt ||
            !TryComp(surgeryEnt, out SurgeryComponent? comp))
            return;

        if (comp.Requirement is { } requirement)
            CollectRequirements(requirement, result, depth + 1);

        foreach (var stepId in comp.Steps)
        {
            if (_surgery.GetSingleton(stepId) is not { } stepEnt)
                continue;

            if (HasComp<SurgeryAddPartStepComponent>(stepEnt) &&
                TryComp(surgeryEnt, out SurgeryPartRemovedConditionComponent? removed))
            {
                result.Add(new AutodocRequirement
                {
                    Kind = AutodocRequirementKind.Part,
                    PartType = removed.Part,
                    Symmetry = removed.Symmetry,
                });
            }
            else if (HasComp<SurgeryAddOrganStepComponent>(stepEnt) &&
                     TryComp(surgeryEnt, out SurgeryOrganConditionComponent? organ) &&
                     organ.Organ is { } registry &&
                     registry.Count > 0)
            {
                result.Add(new AutodocRequirement
                {
                    Kind = AutodocRequirementKind.Organ,
                    Component = registry.Keys.First(),
                });
            }
            else if (TryComp(stepEnt, out SurgeryStepCavityEffectComponent? cavity) && cavity.Action == "Insert")
            {
                result.Add(new AutodocRequirement { Kind = AutodocRequirementKind.Item });
            }
        }
    }

    /// <summary>True when the surgery opens the body, which is what buys the default anaesthetic dose.</summary>
    private bool OpensBody(EntProtoId surgery, int depth)
    {
        if (surgery == OpenIncision)
            return true;

        if (depth > 8 ||
            _surgery.GetSingleton(surgery) is not { } ent ||
            !TryComp(ent, out SurgeryComponent? comp) ||
            comp.Requirement is not { } requirement)
            return false;

        return OpensBody(requirement, depth + 1);
    }

    #endregion

    #region State machine

    public bool TryStart(Entity<AutodocComponent> ent, EntityUid? user)
    {
        if (ent.Comp.State is not (AutodocState.Idle or AutodocState.Complete or AutodocState.Paused))
            return false;

        if (GetOccupant(ent) is not { } body)
        {
            Speak(ent, AutodocVoiceEvent.NoOccupant);
            return false;
        }

        if (ent.Comp.Queue.Count == 0)
        {
            Speak(ent, AutodocVoiceEvent.QueueEmpty);
            return false;
        }

        if (!IsPowered(ent))
            return false;

        if (ent.Comp.State == AutodocState.Paused)
        {
            ent.Comp.State = AutodocState.Step;
            ent.Comp.PauseRequested = false;
            Speak(ent, AutodocVoiceEvent.Resumed);
            BeginStep(ent);
            return true;
        }

        ent.Comp.Operator = user == body ? null : user;
        ent.Comp.Locked = true;
        ent.Comp.AnaestheticGiven = false;
        ent.Comp.AbortRequested = false;
        ent.Comp.PauseRequested = false;
        ent.Comp.State = AutodocState.Preparing;
        Speak(ent, ent.Comp.SelfService ? AutodocVoiceEvent.SelfService : AutodocVoiceEvent.OperatorStart);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user)} started autodoc {ToPrettyString(ent.Owner)} on {ToPrettyString(body)} with {ent.Comp.Queue.Count} queued procedure(s)");

        UpdateAppearance(ent);
        UpdateUi(ent);
        return true;
    }

    /// <summary>Announce, push the anaesthetic if one is loaded, then start the first step.</summary>
    private void Prepare(Entity<AutodocComponent> ent)
    {
        if (GetOccupant(ent) is not { } body)
        {
            Reset(ent);
            return;
        }

        WarnAboutJunkReagents(ent);
        TryDefibrillateOccupant(ent, body);

        // A fresh procedure gets to announce each family of work once more.
        ent.Comp.SpokenFamilies.Clear();

        if (!ent.Comp.AnaestheticGiven)
        {
            ent.Comp.AnaestheticGiven = true;
            var dose = ent.Comp.Queue.Count > 0
                ? ent.Comp.Queue[0].Requirements
                    .Where(r => r.Kind == AutodocRequirementKind.Reagent && r.Reagent == nameof(AutodocReagentRole.Anaesthetic))
                    .Sum(r => r.Units)
                : 0f;

            if (dose > 0f && ent.Comp.Anaesthesia)
                Speak(ent, PushReagent(ent, body, AutodocReagentRole.Anaesthetic, dose)
                    ? AutodocVoiceEvent.Anaesthetic
                    : AutodocVoiceEvent.NoAnaesthetic);
        }

        ent.Comp.State = AutodocState.Step;
        BeginStep(ent);
    }

    private void BeginStep(Entity<AutodocComponent> ent)
    {
        if (GetOccupant(ent) is not { } body)
        {
            Reset(ent);
            return;
        }

        if (ent.Comp.AbortRequested)
        {
            Abort(ent);
            return;
        }

        if (ent.Comp.PauseRequested)
        {
            ent.Comp.State = AutodocState.Paused;
            Speak(ent, AutodocVoiceEvent.Paused);
            UpdateAppearance(ent);
            UpdateUi(ent);
            return;
        }

        if (ent.Comp.Queue.Count == 0)
        {
            FinishQueue(ent);
            return;
        }

        var queued = ent.Comp.Queue[0];
        if (ResolvePart(body, queued.Part) is not { } part ||
            _surgery.GetSingleton(queued.Surgery) is not { } surgeryEnt)
        {
            CompleteProcedure(ent, body);
            return;
        }

        if (_surgery.GetNextStep(body, part, surgeryEnt) is not { } next ||
            MetaData(next.Surgery.Owner).EntityPrototype?.ID is not { } owningSurgery)
        {
            CompleteProcedure(ent, body);
            return;
        }

        var stepId = next.Surgery.Comp.Steps[next.Step];
        if (_surgery.GetSingleton(stepId) is not { } stepEnt)
        {
            Fault(ent);
            return;
        }

        if (!_surgery.WolfmedCanPerformStep(ent.Owner, body, part, stepEnt, out var reason))
        {
            if (reason == StepInvalidReason.MissingTool)
            {
                EnterWaiting(ent, queued);
                return;
            }

            Fault(ent);
            return;
        }

        ent.Comp.CurrentStep = stepId;
        ent.Comp.CurrentSurgery = owningSurgery;
        ent.Comp.StepLength = MathF.Max(0.1f, _surgery.WolfmedStepDuration(stepEnt) * GetStepSpeed(ent));
        ent.Comp.StepRemaining = ent.Comp.StepLength;
        ent.Comp.State = AutodocState.Step;
        ent.Comp.Pending = null;

        PlayToolStart(ent, stepEnt);
        SpeakStep(ent, GetStepVoice(stepEnt, stepId));
        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    private void TickStep(Entity<AutodocComponent> ent, float frameTime)
    {
        if (GetOccupant(ent) is not { } body)
        {
            Reset(ent);
            return;
        }

        if (!IsPowered(ent))
            return;

        if (WatchOccupant(ent, body))
            return;

        if (ent.Comp.CurrentStep == null)
        {
            BeginStep(ent);
            return;
        }

        ent.Comp.StepRemaining -= frameTime;
        if (ent.Comp.StepRemaining > 0f)
            return;

        FinishStep(ent, body);
    }

    private void FinishStep(Entity<AutodocComponent> ent, EntityUid body)
    {
        var queued = ent.Comp.Queue.Count > 0 ? ent.Comp.Queue[0] : null;
        if (queued == null || ResolvePart(body, queued.Part) is not { } part)
        {
            ent.Comp.CurrentStep = null;
            BeginStep(ent);
            return;
        }

        if (ent.Comp.CurrentStep is { } stepId && ent.Comp.CurrentSurgery is { } surgeryId)
        {
            RollMalfunction(ent, part);
            var performed = _surgery.WolfmedPerformStep(ent.Owner, body, part, surgeryId, stepId);
            if (performed)
                queued.StepsDone++;

            // Two ways a procedure ends. Its own last step reads as done - the closing step usually removes
            // the incision its requirement opened, so GetNextStep would otherwise send the pod back to
            // re-open it forever. Or the surgery stops being valid at all, which is what a surgeon sees as
            // the entry leaving the menu once the fracture is mended.
            if (surgeryId == queued.Surgery && IsFinalStep(queued.Surgery, stepId) &&
                _surgery.WolfmedIsStepComplete(body, part, stepId, surgeryId) ||
                !performed && queued.StepsDone > 0)
            {
                ent.Comp.CurrentStep = null;
                CloseTray(ent);
                CompleteProcedure(ent, body);
                return;
            }

            if (!performed)
            {
                ent.Comp.CurrentStep = null;
                Fault(ent);
                return;
            }
        }

        ent.Comp.CurrentStep = null;
        CloseTray(ent);
        BeginStep(ent);
    }

    private bool IsFinalStep(EntProtoId surgery, EntProtoId step)
    {
        return _surgery.GetSingleton(surgery) is { } ent &&
               TryComp(ent, out SurgeryComponent? comp) &&
               comp.Steps.Count > 0 &&
               comp.Steps[^1] == step;
    }

    private void CompleteProcedure(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (ent.Comp.Queue.Count > 0)
        {
            var queued = ent.Comp.Queue[0];
            var antibiotic = queued.Requirements
                .Where(r => r.Kind == AutodocRequirementKind.Reagent && r.Reagent == nameof(AutodocReagentRole.Antibiotic))
                .Sum(r => r.Units);

            if (antibiotic > 0f)
                PushReagent(ent, body, AutodocReagentRole.Antibiotic, antibiotic);

            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"autodoc {ToPrettyString(ent.Owner)} finished {queued.Surgery} on {ToPrettyString(body)} for operator {ToPrettyString(ent.Comp.Operator)}");

            ent.Comp.Queue.RemoveAt(0);
        }

        Speak(ent, AutodocVoiceEvent.Complete);
        ent.Comp.AnaestheticGiven = false;

        if (ent.Comp.Queue.Count == 0)
        {
            FinishQueue(ent);
            return;
        }

        ent.Comp.State = AutodocState.Preparing;
        UpdateUi(ent);
    }

    /// <summary>
    /// BRAIN: with the module installed the pod shocks an occupant whose heart has stopped, on the same rule
    /// a medic's paddles follow. It happens before the first procedure and again once the queue is done.
    /// </summary>
    public bool TryDefibrillateOccupant(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (!_life.InArrest(body) && !_mobState.IsDead(body))
            return false;

        if (!HasDefibModule(ent))
        {
            Speak(ent, AutodocVoiceEvent.DefibMissing);
            return false;
        }

        Speak(ent, AutodocVoiceEvent.DefibCharge);
        var revived = _revival.TryDefibrillate(body, out _);
        Speak(ent, revived ? AutodocVoiceEvent.DefibSuccess : AutodocVoiceEvent.DefibFailure);
        return revived;
    }

    private void FinishQueue(Entity<AutodocComponent> ent)
    {
        // BRAIN: a patient who arrested on the table is the last thing the pod does something about.
        if (GetOccupant(ent) is { } patient)
            TryDefibrillateOccupant(ent, patient);

        Speak(ent, AutodocVoiceEvent.QueueComplete);
        ent.Comp.State = AutodocState.Complete;
        ent.Comp.Locked = IsEmagged(ent);
        ent.Comp.CurrentStep = null;
        CloseTray(ent);
        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    public void Abort(Entity<AutodocComponent> ent)
    {
        Speak(ent, AutodocVoiceEvent.Aborted);
        ent.Comp.Queue.Clear();
        ent.Comp.AbortRequested = false;
        ent.Comp.State = AutodocState.Idle;
        ent.Comp.CurrentStep = null;
        ent.Comp.Locked = IsEmagged(ent);
        CloseTray(ent);
        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    /// <summary>Pauses and calls for help when the patient crashes. True when the pod stopped.</summary>
    private bool WatchOccupant(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (_mobState.IsDead(body))
        {
            ent.Comp.State = AutodocState.Paused;
            ent.Comp.PauseRequested = true;
            Speak(ent, AutodocVoiceEvent.Critical);
            CallForOperator(ent);
            UpdateAppearance(ent);
            UpdateUi(ent);
            return true;
        }

        if (_mobState.IsCritical(body) && !ent.Comp.SaidUnconscious)
        {
            ent.Comp.SaidUnconscious = true;
            Speak(ent, AutodocVoiceEvent.Unconscious);
        }

        return false;
    }

    private void CallForOperator(Entity<AutodocComponent> ent)
    {
        var location = Transform(ent.Owner).GridUid is { } grid ? Name(grid) : Name(ent.Owner);
        _radio.SendRadioMessage(ent.Owner,
            Loc.GetString("wolfmed-autodoc-radio-critical", ("location", location)),
            "Medical",
            ent.Owner);
    }

    #endregion

    #region Requirements and the tray

    private void EnterWaiting(Entity<AutodocComponent> ent, AutodocQueued queued)
    {
        ent.Comp.State = AutodocState.Waiting;
        ent.Comp.CurrentStep = null;
        ent.Comp.Pending = queued.Requirements.FirstOrDefault(r => r.Kind != AutodocRequirementKind.Reagent && !r.Satisfied);
        _slots.SetLock(ent.Owner, AutodocComponent.TraySlotId, false);

        Speak(ent, ent.Comp.Pending?.Kind switch
        {
            AutodocRequirementKind.Part => AutodocVoiceEvent.RequirePart,
            AutodocRequirementKind.Organ => AutodocVoiceEvent.RequireOrgan,
            _ => AutodocVoiceEvent.RequireItem,
        }, ("item", DescribeRequirement(ent.Comp.Pending)));

        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    private void TickWaiting(Entity<AutodocComponent> ent)
    {
        if (GetOccupant(ent) == null)
        {
            Reset(ent);
            return;
        }

        if (!IsPowered(ent))
            return;

        if (ent.Comp.AbortRequested)
        {
            Abort(ent);
            return;
        }

        if (_slots.GetItemOrNull(ent.Owner, AutodocComponent.TraySlotId) is not { } item)
            return;

        if (!Matches(ent.Comp.Pending, item))
        {
            Speak(ent, AutodocVoiceEvent.WrongItem, ("item", DescribeRequirement(ent.Comp.Pending)));
            _slots.TryEject(ent.Owner, AutodocComponent.TraySlotId, null, out _);
            return;
        }

        if (ent.Comp.Pending is { } pending)
            pending.Satisfied = true;

        Speak(ent, AutodocVoiceEvent.ItemAccepted);
        ent.Comp.State = AutodocState.Step;
        BeginStep(ent);
    }

    /// <summary>The same checks the add-part and add-organ steps make when they look through the tools.</summary>
    public bool Matches(AutodocRequirement? requirement, EntityUid item)
    {
        if (requirement == null)
            return false;

        switch (requirement.Kind)
        {
            case AutodocRequirementKind.Part:
                return TryComp(item, out BodyPartComponent? part) &&
                       part.PartType == requirement.PartType &&
                       (requirement.Symmetry == null || part.Symmetry == requirement.Symmetry);
            case AutodocRequirementKind.Organ:
                return requirement.Component != null &&
                       EntityManager.ComponentFactory.TryGetRegistration(requirement.Component, out var registration) &&
                       HasComp(item, registration.Type);
            case AutodocRequirementKind.Item:
                return true;
            default:
                return false;
        }
    }

    public string DescribeRequirement(AutodocRequirement? requirement)
    {
        if (requirement == null)
            return Loc.GetString("wolfmed-autodoc-requirement-item");

        return requirement.Kind switch
        {
            AutodocRequirementKind.Part => Loc.GetString("wolfmed-autodoc-requirement-part",
                ("side", Loc.GetString($"wolfmed-autodoc-symmetry-{(requirement.Symmetry?.ToString() ?? "None").ToLowerInvariant()}")),
                ("part", Loc.GetString($"wolfmed-autodoc-parttype-{requirement.PartType.ToString().ToLowerInvariant()}"))),
            AutodocRequirementKind.Organ => Loc.GetString("wolfmed-autodoc-requirement-organ",
                ("organ", requirement.Component ?? string.Empty)),
            AutodocRequirementKind.Reagent => Loc.GetString("wolfmed-autodoc-requirement-reagent",
                ("role", Loc.GetString($"wolfmed-autodoc-reagent-{(requirement.Reagent ?? "fluid").ToLowerInvariant()}")),
                ("units", requirement.Units)),
            _ => Loc.GetString("wolfmed-autodoc-requirement-item"),
        };
    }

    private void CloseTray(Entity<AutodocComponent> ent)
    {
        ent.Comp.Pending = null;
        _slots.SetLock(ent.Owner, AutodocComponent.TraySlotId, true);
    }

    #endregion

    #region Reagents

    /// <summary>
    /// Draws units of a role out of the loaded beakers and pushes them into the occupant's bloodstream.
    /// Anything not on the pod's reagent list is left in the beaker.
    /// </summary>
    public bool PushReagent(Entity<AutodocComponent> ent, EntityUid body, AutodocReagentRole role, float units)
    {
        if (!_protos.TryIndex(ent.Comp.Reagents, out var list))
            return false;

        var allowed = list.Reagents
            .Where(entry => entry.AutodocAdministrable && entry.Role == role)
            .Select(entry => entry.Reagent.Id)
            .ToArray();

        if (allowed.Length == 0)
            return false;

        var pushed = false;
        var left = FixedPoint2.New(units);
        foreach (var slot in AutodocComponent.ReservoirSlotIds)
        {
            if (left <= FixedPoint2.Zero)
                break;

            if (_slots.GetItemOrNull(ent.Owner, slot) is not { } beaker ||
                !_solutions.TryGetFitsInDispenser(beaker, out var soln, out _))
                continue;

            var taken = _solutions.SplitSolutionPerReagentWithOnly(soln.Value, left, allowed);
            if (taken.Volume <= FixedPoint2.Zero)
                continue;

            left -= taken.Volume;
            _bloodstream.TryAddToChemicals(body, taken);
            pushed = true;
        }

        return pushed;
    }

    /// <summary>One line the first time a beaker holds something the pod refuses to use.</summary>
    private void WarnAboutJunkReagents(Entity<AutodocComponent> ent)
    {
        if (!_protos.TryIndex(ent.Comp.Reagents, out var list))
            return;

        var allowed = list.Reagents
            .Where(entry => entry.AutodocAdministrable)
            .Select(entry => entry.Reagent.Id)
            .ToHashSet();

        foreach (var slot in AutodocComponent.ReservoirSlotIds)
        {
            if (_slots.GetItemOrNull(ent.Owner, slot) is not { } beaker ||
                !_solutions.TryGetFitsInDispenser(beaker, out _, out var solution))
                continue;

            if (solution.Contents.Any(reagent => !allowed.Contains(reagent.Reagent.Prototype)))
            {
                Speak(ent, AutodocVoiceEvent.ReagentIgnored);
                return;
            }
        }
    }

    #endregion

    #region Step detail

    /// <summary>Base 1.25x of a surgeon's step time, down to 0.9x at the best manipulator tier.</summary>
    private float GetStepSpeed(Entity<AutodocComponent> ent)
    {
        var normalised = Math.Clamp((ent.Comp.PartRating - 1f) / 3f, 0f, 1f);
        return ent.Comp.StepTime + (ent.Comp.BestStepTime - ent.Comp.StepTime) * normalised;
    }

    private void PlayToolStart(Entity<AutodocComponent> ent, EntityUid stepEnt)
    {
        if (!TryComp(stepEnt, out SurgeryStepComponent? step) || step.Tool == null ||
            !_containers.TryGetContainer(ent, AutodocComponent.ToolContainerId, out var container))
            return;

        foreach (var registration in step.Tool.Values)
        {
            foreach (var tool in container.ContainedEntities)
            {
                if (!HasComp(tool, registration.Component.GetType()) ||
                    !TryComp(tool, out Content.Shared._Shitmed.Medical.Surgery.Tools.SurgeryToolComponent? toolComp) ||
                    toolComp.StartSound == null)
                    continue;

                _audio.PlayPvs(toolComp.StartSound, ent.Owner, DuckedParams(ent, toolComp.StartSound.Params));
                return;
            }
        }
    }

    /// <summary>The voice line family for a step, read off the step's own effect components and tool.</summary>
    private AutodocVoiceEvent GetStepVoice(EntityUid stepEnt, EntProtoId stepId)
    {
        if (StepVoiceOverrides.TryGetValue(stepId.Id, out var over))
            return over;

        if (HasComp<SurgeryAddPartStepComponent>(stepEnt) || HasComp<SurgeryAffixPartStepComponent>(stepEnt))
            return AutodocVoiceEvent.StepAttachPart;
        if (HasComp<SurgeryRemovePartStepComponent>(stepEnt))
            return AutodocVoiceEvent.StepAmputate;
        if (HasComp<SurgeryAddOrganStepComponent>(stepEnt) || HasComp<SurgeryAffixOrganStepComponent>(stepEnt))
            return AutodocVoiceEvent.StepInsertOrgan;
        if (HasComp<SurgeryRemoveOrganStepComponent>(stepEnt))
            return AutodocVoiceEvent.StepRemoveOrgan;
        if (HasComp<SurgeryTendWoundsEffectComponent>(stepEnt))
            return AutodocVoiceEvent.StepTend;
        if (HasComp<SurgeryStepCavityEffectComponent>(stepEnt))
            return AutodocVoiceEvent.StepCavity;

        if (!TryComp(stepEnt, out SurgeryStepComponent? step) || step.Tool == null)
            return AutodocVoiceEvent.StepGeneric;

        foreach (var name in step.Tool.Keys)
        {
            switch (name)
            {
                case "Scalpel": return AutodocVoiceEvent.StepIncision;
                case "Retractor": return AutodocVoiceEvent.StepRetract;
                case "Hemostat": return AutodocVoiceEvent.StepClamp;
                case "Cautery": return AutodocVoiceEvent.StepCauterise;
                case "BoneSaw": return AutodocVoiceEvent.StepSaw;
                case "Drill": return AutodocVoiceEvent.StepDrill;
                case "BoneSetter": return AutodocVoiceEvent.StepSetBone;
                case "BoneGel": return AutodocVoiceEvent.StepBoneGel;
                case "Tending": return AutodocVoiceEvent.StepSuture;
                case "WolfmedSkinGraft": return AutodocVoiceEvent.StepTend;
                case "WolfmedServoKit": return AutodocVoiceEvent.StepWire;
                case "WolfmedHullPlate": return AutodocVoiceEvent.StepWrench;
                case "WolfmedHullWeld": return AutodocVoiceEvent.StepWeld;
            }
        }

        return AutodocVoiceEvent.StepGeneric;
    }

    /// <summary>A slip: one small bleeding wound on the part being worked on, and an apology. Never more.</summary>
    private void RollMalfunction(Entity<AutodocComponent> ent, EntityUid part)
    {
        var damage = TryComp(ent, out DamageableComponent? damageable)
            ? damageable.TotalDamage.Float() / MathF.Max(1f, ent.Comp.FaultDamage)
            : 0f;

        var chance = Math.Clamp(ent.Comp.MalfunctionChance * (1f + damage * 4f), 0f, 1f);
        if (!ent.Comp.ForceMalfunction && !_random.Prob(chance))
            return;

        ent.Comp.ForceMalfunction = false;
        _wounds.CreateOrMergeWound(part, ent.Comp.SlipWound, FixedPoint2.New(ent.Comp.SlipSeverity));
        Speak(ent, AutodocVoiceEvent.Slip);
        Speak(ent, AutodocVoiceEvent.SlipFix);
    }

    #endregion

    #region Emag

    /// <summary>An emagged pod keeps queueing amputations on whatever limb is still attached.</summary>
    private void TickEmag(Entity<AutodocComponent> ent)
    {
        if (!IsEmagged(ent) || !IsPowered(ent) || ent.Comp.State is not (AutodocState.Idle or AutodocState.Complete))
            return;

        if (GetOccupant(ent) is not { } body)
            return;

        ent.Comp.Locked = true;
        var limbs = new List<TargetBodyPart>();
        foreach (var (partId, part) in _body.GetBodyChildren(body))
        {
            if (part.PartType is BodyPartType.Torso or BodyPartType.Head or BodyPartType.Other ||
                _body.GetTargetBodyPart(part.PartType, part.Symmetry) is not { } target ||
                !_surgery.WolfmedSurgeryValid(body, partId, "SurgeryRemovePart"))
                continue;

            limbs.Add(target);
        }

        if (limbs.Count == 0)
            return;

        ent.Comp.Queue.Clear();
        ent.Comp.Queue.Add(new AutodocQueued
        {
            Surgery = "SurgeryRemovePart",
            Part = _random.Pick(limbs),
            Requirements = new List<AutodocRequirement>(),
        });
        ent.Comp.Anaesthesia = false;
        ent.Comp.State = AutodocState.Preparing;
        Speak(ent, AutodocVoiceEvent.Emag);
        UpdateAppearance(ent);
    }

    #endregion
}
