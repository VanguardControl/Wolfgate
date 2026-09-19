using Content.Shared._WF.PlanetCracker.Anchors;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.PlanetCracker.Anchors;

/// <summary>
/// Pries an anchor crate open on a planet ground layer and leaves one gravity anchor standing where the crate was.
/// </summary>
public sealed partial class WFAnchorCrateSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private WFGravityAnchorSystem _anchors = default!;

    /// <summary>Played as the crate comes apart.</summary>
    private static readonly SoundSpecifier UnpackSound = new SoundPathSpecifier("/Audio/Effects/unwrap.ogg");

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFAnchorCrateComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WFAnchorCrateComponent, WFAnchorUncrateDoAfterEvent>(OnUncrate);
        SubscribeLocalEvent<WFAnchorCrateComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>Starts the prying do-after, once every placement rule already holds.</summary>
    private void OnInteractUsing(Entity<WFAnchorCrateComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_tool.HasQuality(args.Used, ent.Comp.Tool))
            return;

        if (!CanUnpack(ent, args.User, out _, out _))
            return;

        args.Handled = _tool.UseTool(
            args.Used,
            args.User,
            ent.Owner,
            ent.Comp.Delay,
            ent.Comp.Tool,
            new WFAnchorUncrateDoAfterEvent());
    }

    /// <summary>Re-checks everything against the resolved grid, then swaps the crate for the anchor.</summary>
    private void OnUncrate(Entity<WFAnchorCrateComponent> ent, ref WFAnchorUncrateDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (!CanUnpack(ent, args.User, out var ground, out var origin))
            return;

        var coords = new EntityCoordinates(ground.Owner, _map.TileCenterToVector(ground, origin));
        var anchor = SpawnAtPosition(ent.Comp.Contents, coords);

        if (ent.Comp.Cracker is { } cracker && TryComp<WFGravityAnchorComponent>(anchor, out var anchorComp))
        {
            anchorComp.Cracker = cracker;
            Dirty(anchor, anchorComp);
        }

        _audio.PlayPvs(UnpackSound, coords);
        QueueDel(ent.Owner);

        args.Handled = true;
    }

    /// <summary>Examine: what opens it and where it may be opened.</summary>
    private void OnExamined(Entity<WFAnchorCrateComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-anchor-crate-examine"));
    }

    /// <summary>Every rule the crate must satisfy, popped at the user and re-run on do-after completion.</summary>
    private bool CanUnpack(
        Entity<WFAnchorCrateComponent> ent,
        EntityUid user,
        out Entity<MapGridComponent> ground,
        out Vector2i origin)
    {
        ground = default;
        origin = default;

        if (_container.IsEntityInContainer(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("wf-anchor-crate-in-container"), ent.Owner, user);
            return false;
        }

        var xform = Transform(ent.Owner);

        if (!_anchors.TryGetPlanetGround(xform, out ground))
        {
            _popup.PopupEntity(Loc.GetString("wf-anchor-crate-not-ground"), ent.Owner, user);
            return false;
        }

        origin = _map.TileIndicesFor(ground, xform.Coordinates);

        if (!TryComp<PhysicsComponent>(ent.Owner, out var body) ||
            !_anchors.FootprintFree(ground, origin, ent.Comp.FootprintRadius, body))
        {
            _popup.PopupEntity(Loc.GetString("wf-anchor-crate-no-room"), ent.Owner, user);
            return false;
        }

        return true;
    }
}
