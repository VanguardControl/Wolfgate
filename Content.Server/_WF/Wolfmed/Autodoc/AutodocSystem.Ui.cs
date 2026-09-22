using System.Linq;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared.Chemistry.EntitySystems;
using Content.Server.Construction;
using Content.Shared.Construction;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;

namespace Content.Server._WF.Wolfmed.Autodoc;

public sealed partial class AutodocSystem
{
    private void InitializeUi()
    {
        SubscribeLocalEvent<AutodocComponent, RefreshPartsEvent>(OnRefreshParts);
        SubscribeLocalEvent<AutodocComponent, AutodocPryDoAfterEvent>(OnPried);
        SubscribeLocalEvent<AutodocComponent, InteractHandEvent>(OnInteractHand);

        Subs.BuiEvents<AutodocComponent>(AutodocUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<AutodocQueueAddMessage>(OnQueueAdd);
            subs.Event<AutodocQueueRemoveMessage>(OnQueueRemove);
            subs.Event<AutodocQueueMoveMessage>(OnQueueMove);
            subs.Event<AutodocControlMessage>(OnControl);
            subs.Event<AutodocAnaesthesiaMessage>(OnAnaesthesia);
        });
    }

    private void OnRefreshParts(EntityUid uid, AutodocComponent comp, RefreshPartsEvent args)
    {
        if (args.PartRatings.TryGetValue(comp.MachinePartManipulator, out var manipulator))
            comp.PartRating = manipulator;

        if (args.PartRatings.TryGetValue(comp.MachinePartMatterBin, out var bin))
            comp.ReservoirSize = comp.BaseReservoirSize * bin;
    }

    /// <summary>A pod with no power answers a hand with a stutter and nothing else.</summary>
    private void OnInteractHand(Entity<AutodocComponent> ent, ref InteractHandEvent args)
    {
        if (!IsPowered(ent))
            Speak(ent, AutodocVoiceEvent.Offline);
    }

    private void OnPried(Entity<AutodocComponent> ent, ref AutodocPryDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        ent.Comp.Locked = false;
        Speak(ent, AutodocVoiceEvent.LidForced);
        Abort(ent);
        TryEject(ent, force: true);
    }

    private void OnUiOpened(Entity<AutodocComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnQueueAdd(Entity<AutodocComponent> ent, ref AutodocQueueAddMessage args)
    {
        // Self-service picks exactly one procedure and has no queue at all.
        if (ent.Comp.SelfService && ent.Comp.Queue.Count > 0)
            ent.Comp.Queue.Clear();

        TryQueue(ent, args.Surgery, args.Part);
        UpdateUi(ent);
    }

    private void OnQueueRemove(Entity<AutodocComponent> ent, ref AutodocQueueRemoveMessage args)
    {
        if (ent.Comp.State is AutodocState.Idle or AutodocState.Complete &&
            args.Index >= 0 && args.Index < ent.Comp.Queue.Count)
            ent.Comp.Queue.RemoveAt(args.Index);

        UpdateUi(ent);
    }

    private void OnQueueMove(Entity<AutodocComponent> ent, ref AutodocQueueMoveMessage args)
    {
        var target = args.Up ? args.Index - 1 : args.Index + 1;
        if (ent.Comp.State is AutodocState.Idle or AutodocState.Complete &&
            args.Index >= 0 && args.Index < ent.Comp.Queue.Count &&
            target >= 0 && target < ent.Comp.Queue.Count)
        {
            (ent.Comp.Queue[args.Index], ent.Comp.Queue[target]) = (ent.Comp.Queue[target], ent.Comp.Queue[args.Index]);
        }

        UpdateUi(ent);
    }

    private void OnControl(Entity<AutodocComponent> ent, ref AutodocControlMessage args)
    {
        Control(ent, args.Control, args.Actor);
    }

    /// <summary>One press of one button on the terminal. Public so a test drives the same path a player does.</summary>
    public void Control(Entity<AutodocComponent> ent, AutodocControl control, EntityUid? actor)
    {
        switch (control)
        {
            case AutodocControl.Start:
                TryStart(ent, actor);
                break;
            case AutodocControl.Pause:
                // Only at a step boundary: the pod never stops with a scalpel in the wound.
                ent.Comp.PauseRequested = ent.Comp.State != AutodocState.Paused;
                if (ent.Comp.State == AutodocState.Paused)
                    TryStart(ent, actor);
                break;
            case AutodocControl.Abort:
                ent.Comp.AbortRequested = true;
                if (ent.Comp.State is AutodocState.Paused or AutodocState.Waiting or AutodocState.Complete)
                    Abort(ent);
                break;
            case AutodocControl.Eject:
                // An eject with a patient open on the table is an emergency; one from an idle or finished
                // pod is somebody getting out, and gets a door held for them instead of an alarm.
                if (IsRunning(ent))
                {
                    Speak(ent, AutodocVoiceEvent.EmergencyEject);
                    Abort(ent);
                }
                else
                {
                    Speak(ent, AutodocVoiceEvent.Goodbye);
                }

                TryEject(ent, force: !ent.Comp.EmagRevealed);
                break;
            case AutodocControl.Plan:
                // Self-service has no queue to look at, so PLAN and START are one button there.
                if (TryPlan(ent) > 0)
                {
                    Speak(ent, AutodocVoiceEvent.Plan);
                    if (ent.Comp.SelfService)
                        TryStart(ent, actor);
                }
                else
                {
                    Speak(ent, AutodocVoiceEvent.AutoNothing);
                }

                break;
            case AutodocControl.Auto:
                SetAuto(ent, !ent.Comp.Auto);
                break;
        }

        UpdateUi(ent);
    }

    private void OnAnaesthesia(Entity<AutodocComponent> ent, ref AutodocAnaesthesiaMessage args)
    {
        ent.Comp.Anaesthesia = args.Enabled;
        UpdateUi(ent);
    }

    public void UpdateUi(Entity<AutodocComponent> ent)
    {
        if (!_ui.HasUi(ent.Owner, AutodocUiKey.Key))
            return;

        var occupant = GetOccupant(ent);
        var state = new AutodocBuiState
        {
            State = ent.Comp.State,
            Status = Loc.GetString($"wolfmed-autodoc-status-{ent.Comp.State.ToString().ToLowerInvariant()}"),
            CurrentStep = ent.Comp.CurrentStep?.Id,
            Progress = ent.Comp.StepLength > 0f
                ? Math.Clamp(1f - ent.Comp.StepRemaining / ent.Comp.StepLength, 0f, 1f)
                : 0f,
            StepEnds = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(0f, ent.Comp.StepRemaining)),
            StepLength = ent.Comp.StepLength,
            SelfService = ent.Comp.SelfService,
            Anaesthesia = ent.Comp.Anaesthesia,
            Occupied = occupant != null,
            DefibModule = HasDefibModule(ent),
            AutofixModule = HasAutofixModule(ent),
            Auto = ent.Comp.Auto,
            DiskProgram = CurrentDisk(ent) is { } disk && _protos.TryIndex(disk.Program, out var program)
                ? Loc.GetString(program.Name)
                : null,
            TrayItem = _slots.GetItemOrNull(ent.Owner, AutodocComponent.TraySlotId) is { } tray ? Name(tray) : null,
            Reservoir = BuildReservoir(ent),
            LastLine = ent.Comp.LastLine,
        };

        if (occupant is { } body)
        {
            state.Diagnostics = _analyzer.WolfmedBuildScanMessage(body);
            state.Available = GetAvailable(ent, body);
        }

        foreach (var queued in ent.Comp.Queue)
        {
            var lines = queued.Requirements
                .Select(req => Loc.GetString("wolfmed-autodoc-requirement-line",
                    ("what", DescribeRequirement(req)),
                    ("status", Loc.GetString(req.Satisfied
                        ? "wolfmed-autodoc-requirement-loaded"
                        : "wolfmed-autodoc-requirement-waiting"))))
                .ToList();

            state.Queue.Add(new AutodocQueueEntry(queued.Surgery, queued.Part, lines));
        }

        _ui.SetUiState(ent.Owner, AutodocUiKey.Key, state);
    }

    private List<AutodocReservoirEntry> BuildReservoir(Entity<AutodocComponent> ent)
    {
        var result = new List<AutodocReservoirEntry>();
        if (!_protos.TryIndex(ent.Comp.Reagents, out var list))
            return result;

        var allowed = list.Reagents
            .Where(entry => entry.AutodocAdministrable)
            .Select(entry => entry.Reagent.Id)
            .ToHashSet();

        foreach (var slot in AutodocComponent.ReservoirSlotIds)
        {
            if (_slots.GetItemOrNull(ent.Owner, slot) is not { } beaker ||
                !_solutions.TryGetFitsInDispenser(beaker, out _, out var solution))
            {
                result.Add(new AutodocReservoirEntry(Loc.GetString("wolfmed-autodoc-reservoir-empty"), 0f, ent.Comp.ReservoirSize, false));
                continue;
            }

            var usable = solution.Contents.Any(reagent => allowed.Contains(reagent.Reagent.Prototype));
            result.Add(new AutodocReservoirEntry(Name(beaker), solution.Volume.Float(), ent.Comp.ReservoirSize, usable));
        }

        return result;
    }
}
