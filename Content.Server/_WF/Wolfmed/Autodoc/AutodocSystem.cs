using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Medical;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Server.Radio.EntitySystems;
using Content.Server._Shitmed.Medical.Surgery;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Climbing.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.DragDrop;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Rotation;
using Content.Shared.Standing;
using Content.Shared.StatusEffect;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using SharedToolSystem = Content.Shared.Tools.Systems.SharedToolSystem;

namespace Content.Server._WF.Wolfmed.Autodoc;

/// <summary>
/// S.A.M., the surgical pod. Holds an occupant, offers the surgeries their body currently allows, and runs
/// them one real Shitmed step at a time with an internal toolset, so every wound, condition and side effect
/// of a hand-performed surgery still applies. It can only perform surgeries and push reagents from its
/// reservoir; it never bandages, heals by fiat or applies a topical.
/// </summary>
public sealed partial class AutodocSystem : EntitySystem
{
    /// <summary>The status effect key every sleep chem in the game uses, so the pod stacks with them.</summary>
    private const string SleepKey = "ForcedSleep";

    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private Life.WolfmedLifeSystem _life = default!; // BRAIN
    [Dependency] private Life.WolfmedRevivalSystem _revival = default!; // BRAIN
    [Dependency] private Consciousness.WolfmedConsciousnessSystem _consciousness = default!; // M1a: faints
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _protos = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly ClimbSystem _climb = default!;
    [Dependency] private readonly EmagSystem _emag = default!;
    [Dependency] private readonly HealthAnalyzerSystem _analyzer = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly StatusEffectsSystem _status = default!;
    [Dependency] private readonly WolfmedPainReliefSystem _relief = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SurgerySystem _surgery = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly WoundFractureSystem _fractures = default!;
    [Dependency] private readonly WolfmedEmbeddedObjectSystem _embedded = default!;
    [Dependency] private readonly WolfmedWoundDamageSyncSystem _damageSync = default!;
    [Dependency] private readonly WoundSystem _wounds = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AutodocComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AutodocComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<AutodocComponent, DragDropTargetEvent>(OnDragDrop);
        SubscribeLocalEvent<AutodocComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<AutodocComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<AutodocComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<AutodocComponent, GotEmaggedEvent>(OnEmagged);
        SubscribeLocalEvent<AutodocComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<AutodocComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<AutodocComponent, WolfmedSurgeryToolsEvent>(OnGetTools);
        SubscribeLocalEvent<AutodocComponent, DamageChangedEvent>(OnDamaged);

        InitializeUi();
        InitializeTriage();
    }

    private void OnMapInit(Entity<AutodocComponent> ent, ref MapInitEvent args)
    {
        var tools = _containers.EnsureContainer<Container>(ent, AutodocComponent.ToolContainerId);
        foreach (var proto in ent.Comp.Tools)
        {
            var tool = Spawn(proto, Transform(ent).Coordinates);
            if (!_containers.Insert(tool, tools))
                QueueDel(tool);
        }

        // Nothing in the tray until a step asks for something.
        _slots.SetLock(ent.Owner, AutodocComponent.TraySlotId, true);
        UpdateAppearance(ent);
    }

    #region Occupancy

    public EntityUid? GetOccupant(Entity<AutodocComponent> ent) =>
        _containers.TryGetContainer(ent, AutodocComponent.BodyContainerId, out var container) &&
        container.ContainedEntities.Count > 0
            ? container.ContainedEntities[0]
            : null;

    public bool TryInsert(Entity<AutodocComponent> ent, EntityUid body)
    {
        if (GetOccupant(ent) != null ||
            !HasComp<BodyComponent>(body) ||
            !_containers.TryGetContainer(ent, AutodocComponent.BodyContainerId, out var container) ||
            !_containers.Insert(body, container))
            return false;

        _audio.PlayPvs(ent.Comp.LidCloseSound, ent);
        return true;
    }

    public bool TryEject(Entity<AutodocComponent> ent, bool force = false)
    {
        if (ent.Comp.Locked && !force)
            return false;

        if (GetOccupant(ent) is not { } body ||
            !_containers.TryGetContainer(ent, AutodocComponent.BodyContainerId, out var container))
            return false;

        _containers.Remove(body, container);
        _climb.ForciblySetClimbing(body, ent);
        _audio.PlayPvs(ent.Comp.LidOpenSound, ent);
        return true;
    }

    private void OnGetVerbs(Entity<AutodocComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        var occupant = GetOccupant(ent);

        if (occupant == null && HasComp<BodyComponent>(user))
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Act = () => EnterPod(ent, user),
                Text = Loc.GetString("wolfmed-autodoc-verb-enter"),
                Priority = 2,
            });
        }

        if (occupant != null && !ent.Comp.Locked)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Act = () => TryEject(ent),
                Text = Loc.GetString("wolfmed-autodoc-verb-eject"),
                Priority = 1,
            });
        }
    }

    private void EnterPod(Entity<AutodocComponent> ent, EntityUid user)
    {
        // An unconscious body cannot climb in by itself; the action blocker has already refused the verb for
        // one. Downed is deliberately allowed, which is what WolfmedDownedReachable on the pod buys.
        if (!TryInsert(ent, user))
            return;

        ent.Comp.SelfService = true;
        Speak(ent, AutodocVoiceEvent.Greeting);
    }

    private void OnDragDrop(Entity<AutodocComponent> ent, ref DragDropTargetEvent args)
    {
        if (args.Handled || !TryInsert(ent, args.Dragged))
            return;

        ent.Comp.SelfService = args.Dragged == args.User;
        Speak(ent, AutodocVoiceEvent.Greeting);
        args.Handled = true;
    }

    private void OnInserted(Entity<AutodocComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == AutodocComponent.DiskSlotId)
            Speak(ent, AutodocVoiceEvent.DiskInserted);

        if (args.Container.ID == AutodocComponent.BodyContainerId)
        {
            SetOccupantLying(args.Entity, true);
            ent.Comp.DefibWarned = false;
            ent.Comp.DefibAttempt = 0;
            ent.Comp.DefibBlocked = null;
            ent.Comp.DefibNext = TimeSpan.Zero;
            ent.Comp.AutoSaidNothing = false;
            ent.Comp.AutoNextPlan = TimeSpan.Zero;

            // Everything the pod learned about the last patient goes with them.
            ent.Comp.FailedProcedures.Clear();
            ent.Comp.AutoSignature = null;
            ent.Comp.AutoReplans = 0;
            ent.Comp.PreProcedureWounds.Clear();
            ent.Comp.OccupantWasDead = false;
        }

        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    private void OnRemoved(Entity<AutodocComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (args.Container.ID == AutodocComponent.DiskSlotId)
            Speak(ent, AutodocVoiceEvent.DiskRemoved);

        if (args.Container.ID == AutodocComponent.BodyContainerId)
        {
            SetOccupantLying(args.Entity, false);
            WakeOccupant(ent, args.Entity);
            ent.Comp.FailedProcedures.Clear();
            ent.Comp.AutoSignature = null;
            ent.Comp.AutoReplans = 0;
            ent.Comp.PreProcedureWounds.Clear();
            ent.Comp.OccupantWasDead = false;
            Reset(ent);
            _ui.CloseUis(ent.Owner);
        }

        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    /// <summary>The lid can be forced open with a prying tool while it is locked.</summary>
    private void OnInteractUsing(Entity<AutodocComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !ent.Comp.Locked)
            return;

        args.Handled = _tool.UseTool(args.Used, args.User, ent.Owner, 3f, "Prying", new AutodocPryDoAfterEvent());
    }

    private void OnExamined(Entity<AutodocComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(AutodocComponent)))
        {
            args.PushMarkup(Loc.GetString($"wolfmed-autodoc-examine-{ent.Comp.State.ToString().ToLowerInvariant()}"));
            if (ent.Comp.EmagRevealed)
                args.PushMarkup(Loc.GetString("wolfmed-autodoc-examine-emagged"));
        }
    }

    private void OnDamaged(Entity<AutodocComponent> ent, ref DamageChangedEvent args)
    {
        if (ent.Comp.State is AutodocState.Idle or AutodocState.Faulted ||
            !TryComp(ent, out DamageableComponent? damage) ||
            damage.TotalDamage.Float() < ent.Comp.FaultDamage)
            return;

        Fault(ent);
    }

    #endregion

    #region Power and emag

    public bool IsPowered(EntityUid uid) => _power.IsPowered(uid);

    private void OnPowerChanged(Entity<AutodocComponent> ent, ref PowerChangedEvent args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (!args.Powered)
        {
            if (ent.Comp.State is AutodocState.Preparing or AutodocState.Step or AutodocState.Waiting)
            {
                ent.Comp.State = AutodocState.Paused;
                ent.Comp.PowerPaused = true;
                Speak(ent, AutodocVoiceEvent.PowerLost);
            }
        }
        else
        {
            Speak(ent, ent.Comp.PowerPaused ? AutodocVoiceEvent.PowerRestored : AutodocVoiceEvent.Boot);
            if (ent.Comp.PowerPaused)
            {
                ent.Comp.PowerPaused = false;
                ent.Comp.State = AutodocState.Step;
            }
        }

        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    private void OnEmagged(Entity<AutodocComponent> ent, ref GotEmaggedEvent args)
    {
        if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
            return;

        args.Handled = true;
        ent.Comp.Queue.Clear();
        ent.Comp.Locked = true;
        ent.Comp.State = AutodocState.Idle;
        Speak(ent, AutodocVoiceEvent.Emag);
        ent.Comp.EmagRevealed = true;
        UpdateUi(ent);
    }

    public bool IsEmagged(EntityUid uid) => _emag.CheckFlag(uid, EmagType.Interaction);

    #endregion

    private void OnGetTools(Entity<AutodocComponent> ent, ref WolfmedSurgeryToolsEvent args)
    {
        var tools = new List<EntityUid>();
        if (_containers.TryGetContainer(ent, AutodocComponent.ToolContainerId, out var container))
            tools.AddRange(container.ContainedEntities);

        // The tray is part of the toolset: add-part and add-organ steps take the item out of it.
        if (_slots.GetItemOrNull(ent.Owner, AutodocComponent.TraySlotId) is { } tray)
            tools.Add(tray);

        args.Tools = tools;
    }

    /// <summary>
    /// The occupant lies on the bed rather than standing on it. Same appearance key standing up and lying
    /// down use, so leaving the pod hands the sprite straight back to whatever state the body is in.
    /// </summary>
    private void SetOccupantLying(EntityUid body, bool lying)
    {
        if (TerminatingOrDeleted(body) || !HasComp<RotationVisualsComponent>(body))
            return;

        _appearance.SetData(body, RotationVisuals.RotationState,
            lying || _standing.IsDown(body) ? RotationState.Horizontal : RotationState.Vertical);
    }

    private void UpdateAppearance(Entity<AutodocComponent> ent)
    {
        var state = !IsPowered(ent) ? AutodocVisualState.Unpowered
            : ent.Comp.State is AutodocState.Preparing or AutodocState.Step ? AutodocVisualState.Operating
            : GetOccupant(ent) != null ? AutodocVisualState.Closed
            : AutodocVisualState.Open;

        _appearance.SetData(ent, AutodocVisuals.State, state);
        SetOccupantShown(ent, state is AutodocVisualState.Open or AutodocVisualState.Unpowered);
    }

    /// <summary>
    /// The lid is opaque. With it open the occupant lies on the bed and is drawn; with it closed or working
    /// they are inside the machine and nothing of them shows.
    /// </summary>
    private void SetOccupantShown(Entity<AutodocComponent> ent, bool shown)
    {
        if (!_containers.TryGetContainer(ent, AutodocComponent.BodyContainerId, out var container) ||
            container.ShowContents == shown)
            return;

        container.ShowContents = shown;
        if (TryComp(ent, out ContainerManagerComponent? manager))
            Dirty(ent.Owner, manager);
    }

    /// <summary>Puts the pod back to Idle without touching the occupant.</summary>
    private void Reset(Entity<AutodocComponent> ent)
    {
        ent.Comp.State = AutodocState.Idle;
        ent.Comp.Queue.Clear();
        ent.Comp.CurrentStep = null;
        ent.Comp.Pending = null;
        ent.Comp.Operator = null;
        ent.Comp.SelfService = false;
        ent.Comp.AnaestheticGiven = false;
        ent.Comp.PowerPaused = false;
        ent.Comp.Locked = IsEmagged(ent);
        ent.Comp.SpokenFamilies.Clear();
        ent.Comp.StallStep = null;
        ent.Comp.StallSignature = null;
        ent.Comp.StallCount = 0;
        ent.Comp.StepRuns.Clear();
        ent.Comp.BlockedReason = null;
        ent.Comp.CutClothingRequested = false;
        ent.Comp.Transfusing = false;
        ent.Comp.PreProcedureWounds.Clear();
        _slots.SetLock(ent.Owner, AutodocComponent.TraySlotId, true);
    }

    /// <summary>True while a procedure is under way, which is what makes an eject an emergency one.</summary>
    public bool IsRunning(Entity<AutodocComponent> ent) =>
        ent.Comp.State is AutodocState.Preparing or AutodocState.Step or AutodocState.Waiting or AutodocState.Paused;

    private void Fault(Entity<AutodocComponent> ent)
    {
        ent.Comp.State = AutodocState.Faulted;
        ent.Comp.Locked = false;
        ent.Comp.CurrentStep = null;
        _slots.SetLock(ent.Owner, AutodocComponent.TraySlotId, true);
        UpdateAppearance(ent);
        UpdateUi(ent);
    }

    /// <summary>The body part entity behind one doll slot, or null when the body has not got it.</summary>
    public EntityUid? ResolvePart(EntityUid body, TargetBodyPart target)
    {
        foreach (var (id, part) in _body.GetBodyChildren(body))
        {
            if (_body.GetTargetBodyPart(part.PartType, part.Symmetry) == target)
                return id;
        }

        return null;
    }
}
