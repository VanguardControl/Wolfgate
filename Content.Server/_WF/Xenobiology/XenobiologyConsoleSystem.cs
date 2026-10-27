using System.Diagnostics.CodeAnalysis;
using Content.Server.Actions;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Shared._WF.Xenobiology;
using Content.Shared._WF.Xenobiology.Potions;
using Content.Shared.Construction;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Xenobiology;

/// <summary>
/// The xenobiology console: stores cubes and potions, and opens a remote eye on a leash for its actions.
/// </summary>
public sealed partial class XenobiologyConsoleSystem : EntitySystem
{
    [Dependency] private ActionsSystem _actions = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private EyeSystem _eye = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SlimePotionSystem _potions = default!;
    [Dependency] private SlimeScannerSystem _scanner = default!;
    [Dependency] private TagSystem _tag = default!;

    private static readonly EntProtoId MonkeyCube = "MonkeyCube";

    /// <summary>
    /// Radius around the eye that its actions reach.
    /// </summary>
    private const float EyeReach = 0.5f;

    private readonly HashSet<Entity<SlimeComponent>> _slimes = new();
    private readonly List<EntityUid> _toClose = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<XenobiologyConsoleComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<XenobiologyConsoleComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<XenobiologyConsoleComponent, AfterInteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<XenobiologyConsoleComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<XenobiologyConsoleComponent, MachineDeconstructedEvent>(OnDeconstructed);
        SubscribeLocalEvent<XenobiologyConsoleComponent, ComponentShutdown>(OnConsoleShutdown);

        SubscribeLocalEvent<XenobiologyConsoleUserComponent, PlayerDetachedEvent>(OnUserDetached);
        SubscribeLocalEvent<XenobiologyConsoleUserComponent, XenobioConsoleExitEvent>(OnExit);
        SubscribeLocalEvent<XenobiologyConsoleUserComponent, XenobioConsoleScanSlimeEvent>(OnScanSlime);
        SubscribeLocalEvent<XenobiologyConsoleUserComponent, XenobioConsoleGrabSlimeEvent>(OnGrabSlime);
        SubscribeLocalEvent<XenobiologyConsoleUserComponent, XenobioConsolePlaceSlimeEvent>(OnPlaceSlime);
        SubscribeLocalEvent<XenobiologyConsoleUserComponent, XenobioConsolePlaceMonkeyEvent>(OnPlaceMonkey);
        SubscribeLocalEvent<XenobiologyConsoleUserComponent, XenobioConsoleRecycleMonkeyEvent>(OnRecycleMonkey);
        SubscribeLocalEvent<XenobiologyConsoleUserComponent, XenobioConsoleMutationPotionEvent>(OnMutationPotion);
        SubscribeLocalEvent<XenobiologyConsoleUserComponent, XenobioConsoleStabilizerPotionEvent>(OnStabilizerPotion);
    }

    private void OnInit(Entity<XenobiologyConsoleComponent> ent, ref ComponentInit args)
    {
        _container.EnsureContainer<ContainerSlot>(ent, XenobiologyConsoleComponent.SlimeContainerName);
    }

    #region Console

    private void OnActivate(Entity<XenobiologyConsoleComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        if (!_power.IsPowered(ent))
            return;

        if (ent.Comp.User != null)
        {
            _popup.PopupEntity(Loc.GetString("xenobiology-console-in-use"), ent, args.User);
            return;
        }

        if (HasComp<XenobiologyConsoleUserComponent>(args.User))
            return;

        OpenEye(ent, args.User);
    }

    private void OnInteractUsing(Entity<XenobiologyConsoleComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        string message;
        if (_tag.HasTag(args.Used, ent.Comp.MonkeyCubeTag))
        {
            ent.Comp.MonkeyCubes += 1;
            message = Loc.GetString("xenobiology-console-monkey-cube-inserted", ("cubes", ent.Comp.MonkeyCubes));
        }
        else if (TryComp<SlimeMutationPotionComponent>(args.Used, out var potion))
        {
            if (potion.Amount > 0)
            {
                ent.Comp.MutationPotions++;
                message = Loc.GetString("xenobiology-console-mutation-potion-inserted", ("potions", ent.Comp.MutationPotions));
            }
            else
            {
                ent.Comp.StabilizerPotions++;
                message = Loc.GetString("xenobiology-console-stabilizer-potion-inserted", ("potions", ent.Comp.StabilizerPotions));
            }
        }
        else
        {
            return;
        }

        args.Handled = true;
        Dirty(ent);
        QueueDel(args.Used);
        _popup.PopupEntity(message, ent, args.User);
    }

    private void OnPowerChanged(Entity<XenobiologyConsoleComponent> ent, ref PowerChangedEvent args)
    {
        if (!args.Powered && ent.Comp.User is { } user)
            CloseEye(user);
    }

    private void OnDeconstructed(Entity<XenobiologyConsoleComponent> ent, ref MachineDeconstructedEvent args)
    {
        for (var i = 0; i < (int) ent.Comp.MonkeyCubes.Float(); i++)
        {
            SpawnNextToOrDrop(MonkeyCube, ent);
        }

        for (var i = 0; i < ent.Comp.MutationPotions; i++)
        {
            SpawnNextToOrDrop("SlimeMutationPotion", ent);
        }

        for (var i = 0; i < ent.Comp.StabilizerPotions; i++)
        {
            SpawnNextToOrDrop("SlimeStabilizerPotion", ent);
        }
    }

    private void OnConsoleShutdown(Entity<XenobiologyConsoleComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.User is { } user)
            CloseEye(user);
    }

    #endregion

    #region Eye

    /// <summary>
    /// Spawns the console's eye and hands the user its view, movement and actions.
    /// </summary>
    public void OpenEye(Entity<XenobiologyConsoleComponent> console, EntityUid user)
    {
        var eye = SpawnAtPosition(console.Comp.EyeProto, Transform(console).Coordinates);
        console.Comp.User = user;
        console.Comp.Eye = eye;

        var comp = EnsureComp<XenobiologyConsoleUserComponent>(user);
        comp.Console = console;
        comp.Eye = eye;
        foreach (var proto in console.Comp.Actions)
        {
            if (_actions.AddAction(user, proto) is { } action)
                comp.GrantedActions.Add(action);
        }

        Dirty(user, comp);

        _eye.SetTarget(user, eye);
        _mover.SetRelay(user, eye);
        if (TryComp<InputMoverComponent>(eye, out var mover))
        {
            mover.CanMove = true;
            Dirty(eye, mover);
        }
    }

    /// <summary>
    /// Ends a user's remote view and removes the eye and actions.
    /// </summary>
    public void CloseEye(EntityUid user)
    {
        if (!TryComp<XenobiologyConsoleUserComponent>(user, out var comp))
            return;

        foreach (var action in comp.GrantedActions)
        {
            _actions.RemoveAction(user, action);
        }

        RemComp<RelayInputMoverComponent>(user);
        _eye.SetTarget(user, null);

        if (TryComp<XenobiologyConsoleComponent>(comp.Console, out var console))
        {
            console.User = null;
            console.Eye = null;
        }

        if (comp.Eye is { } eye)
            QueueDel(eye);

        RemComp<XenobiologyConsoleUserComponent>(user);
    }

    private void OnUserDetached(Entity<XenobiologyConsoleUserComponent> ent, ref PlayerDetachedEvent args)
    {
        CloseEye(ent);
    }

    private void OnExit(Entity<XenobiologyConsoleUserComponent> ent, ref XenobioConsoleExitEvent args)
    {
        args.Handled = true;
        CloseEye(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _toClose.Clear();
        var query = EntityQueryEnumerator<XenobiologyConsoleUserComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var user, out var userXform))
        {
            if (!TryComp<XenobiologyConsoleComponent>(user.Console, out var console)
                || user.Eye is not { } eye
                || TerminatingOrDeleted(eye)
                || _mobState.IsIncapacitated(uid))
            {
                _toClose.Add(uid);
                continue;
            }

            var consoleXform = Transform(user.Console.Value);
            if (!_transform.InRange(userXform.Coordinates, consoleXform.Coordinates, console.UserRange))
            {
                _toClose.Add(uid);
                continue;
            }

            Leash(eye, consoleXform, console.EyeRange);
        }

        foreach (var uid in _toClose)
        {
            CloseEye(uid);
        }
    }

    /// <summary>
    /// Pulls the eye back inside its range of the console.
    /// </summary>
    private void Leash(EntityUid eye, TransformComponent console, float range)
    {
        var eyeXform = Transform(eye);
        if (eyeXform.MapID != console.MapID)
        {
            _transform.SetCoordinates(eye, console.Coordinates);
            return;
        }

        var origin = _transform.GetWorldPosition(console);
        var offset = _transform.GetWorldPosition(eyeXform) - origin;
        if (offset.LengthSquared() <= range * range)
            return;

        _transform.SetWorldPosition(eye, origin + offset.Normalized() * range);
    }

    #endregion

    #region Actions

    private bool TryGetEye(Entity<XenobiologyConsoleUserComponent> user,
        [NotNullWhen(true)] out Entity<XenobiologyConsoleComponent>? console, out EntityUid eye)
    {
        console = null;
        eye = default;
        if (user.Comp.Eye is not { } eyeUid || !TryComp<XenobiologyConsoleComponent>(user.Comp.Console, out var consoleComp))
            return false;

        console = (user.Comp.Console.Value, consoleComp);
        eye = eyeUid;
        return true;
    }

    private Entity<SlimeComponent>? SlimeUnderEye(EntityUid eye)
    {
        _slimes.Clear();
        _lookup.GetEntitiesInRange(Transform(eye).Coordinates, EyeReach, _slimes);
        foreach (var slime in _slimes)
        {
            if (!_container.IsEntityOrParentInContainer(slime))
                return slime;
        }

        return null;
    }

    private void Popup(EntityUid eye, EntityUid user, string message)
    {
        _popup.PopupEntity(message, eye, user, PopupType.Small);
    }

    private void OnScanSlime(Entity<XenobiologyConsoleUserComponent> ent, ref XenobioConsoleScanSlimeEvent args)
    {
        if (!TryGetEye(ent, out _, out var eye))
            return;

        args.Handled = true;
        if (SlimeUnderEye(eye) is { } slime)
            _scanner.SendInfo(ent, slime);
        else
            Popup(eye, ent, Loc.GetString("xenobiology-console-slime-picked-up-fail-none-found"));
    }

    private void OnGrabSlime(Entity<XenobiologyConsoleUserComponent> ent, ref XenobioConsoleGrabSlimeEvent args)
    {
        if (!TryGetEye(ent, out var console, out var eye))
            return;

        args.Handled = true;
        if (SlimeUnderEye(eye) is not { } slime)
        {
            Popup(eye, ent, Loc.GetString("xenobiology-console-slime-picked-up-fail-none-found"));
            return;
        }

        var slot = _container.EnsureContainer<ContainerSlot>(console.Value, XenobiologyConsoleComponent.SlimeContainerName);
        var key = _container.Insert(slime.Owner, slot)
            ? "xenobiology-console-slime-picked-up"
            : "xenobiology-console-slime-picked-up-fail-full";
        Popup(eye, ent, Loc.GetString(key, ("name", slime.Owner)));
    }

    private void OnPlaceSlime(Entity<XenobiologyConsoleUserComponent> ent, ref XenobioConsolePlaceSlimeEvent args)
    {
        if (!TryGetEye(ent, out var console, out var eye))
            return;

        args.Handled = true;
        var slot = _container.EnsureContainer<ContainerSlot>(console.Value, XenobiologyConsoleComponent.SlimeContainerName);
        if (slot.ContainedEntity is not { } slime)
        {
            Popup(eye, ent, Loc.GetString("xenobiology-console-slime-placed-down-fail-none-stored"));
            return;
        }

        _container.Remove(slime, slot, destination: Transform(eye).Coordinates);
        Popup(eye, ent, Loc.GetString("xenobiology-console-slime-placed-down", ("name", slime)));
    }

    private void OnPlaceMonkey(Entity<XenobiologyConsoleUserComponent> ent, ref XenobioConsolePlaceMonkeyEvent args)
    {
        if (!TryGetEye(ent, out var console, out var eye))
            return;

        args.Handled = true;
        var comp = console.Value.Comp;
        if (comp.MonkeyCubes < 1)
        {
            Popup(eye, ent, Loc.GetString("xenobiology-console-monkey-placed-fail-empty", ("cubes", comp.MonkeyCubes)));
            return;
        }

        SpawnAtPosition(comp.MonkeyProtoId, Transform(eye).Coordinates);
        comp.MonkeyCubes -= 1;
        Dirty(console.Value);
        Popup(eye, ent, Loc.GetString("xenobiology-console-monkey-placed", ("cubes", comp.MonkeyCubes)));
    }

    private void OnRecycleMonkey(Entity<XenobiologyConsoleUserComponent> ent, ref XenobioConsoleRecycleMonkeyEvent args)
    {
        if (!TryGetEye(ent, out var console, out var eye))
            return;

        args.Handled = true;
        var comp = console.Value.Comp;
        var recycled = 0;
        foreach (var target in _lookup.GetEntitiesInRange(eye, EyeReach))
        {
            if (!_tag.HasTag(target, comp.MonkeyTag)
                || !TryComp<DamageableComponent>(target, out var damage)
                || damage.TotalDamage < comp.RecycleDamageThreshold
                || HasComp<ActorComponent>(target))
                continue;

            QueueDel(target);
            comp.MonkeyCubes += 0.5;
            recycled++;
        }

        if (recycled == 0)
        {
            Popup(eye, ent, Loc.GetString("xenobiology-console-monkey-recycled-failed-none"));
            return;
        }

        Dirty(console.Value);
        Popup(eye, ent, Loc.GetString("xenobiology-console-monkey-recycled", ("monkeys", recycled), ("cubes", comp.MonkeyCubes)));
    }

    private void OnMutationPotion(Entity<XenobiologyConsoleUserComponent> ent, ref XenobioConsoleMutationPotionEvent args)
    {
        args.Handled = true;
        ApplyStoredPotion(ent, true);
    }

    private void OnStabilizerPotion(Entity<XenobiologyConsoleUserComponent> ent, ref XenobioConsoleStabilizerPotionEvent args)
    {
        args.Handled = true;
        ApplyStoredPotion(ent, false);
    }

    private void ApplyStoredPotion(Entity<XenobiologyConsoleUserComponent> ent, bool mutation)
    {
        if (!TryGetEye(ent, out var console, out var eye))
            return;

        var comp = console.Value.Comp;
        var stored = mutation ? comp.MutationPotions : comp.StabilizerPotions;
        if (stored < 1)
        {
            Popup(eye, ent, Loc.GetString(mutation
                ? "xenobiology-console-mutation-potion-applied-failed-empty"
                : "xenobiology-console-stabilizer-potion-applied-failed-empty"));
            return;
        }

        if (SlimeUnderEye(eye) is not { } slime)
        {
            Popup(eye, ent, Loc.GetString("xenobiology-console-slime-picked-up-fail-none-found"));
            return;
        }

        var proto = mutation ? "SlimeMutationPotion" : "SlimeStabilizerPotion";
        var amount = _potions.PotionAmount(proto);
        if (!_potions.TryApplyMutation(slime, amount, out var message))
        {
            Popup(eye, ent, message);
            return;
        }

        if (mutation)
            comp.MutationPotions--;
        else
            comp.StabilizerPotions--;

        Dirty(console.Value);
        Popup(eye, ent, message);
    }

    #endregion
}
