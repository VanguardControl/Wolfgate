using System.Numerics;
using Content.Server._WF.PlanetCracker.Cracker;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Power.EntitySystems;
using Content.Shared._WF.PlanetCracker.Cracker;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetCracker.Testing;

/// <summary>Builds the two code-only planet cracker test hulls.</summary>
public sealed partial class WFTestGridFactory : EntitySystem
{
    [Dependency] private IMapManager _mapMan = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPowerReceiverSystem _receiver = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private WFCrackerOwnershipSystem _ownership = default!;

    /// <summary>Deck plating both test hulls are floored with.</summary>
    private const string HullTile = "FloorSteel";

    /// <summary>Width and height of the tiny cracker hull, in tiles.</summary>
    private const int CrackerSize = 15;

    /// <summary>Where the transport is built relative to the cracker before it is docked on.</summary>
    private static readonly Vector2 TransportPark = new(CrackerSize + 12f, 0f);

    /// <summary>Width of the micro anchor transport hull, in tiles.</summary>
    private const int TransportWidth = 7;

    /// <summary>Height of the micro anchor transport hull, in tiles.</summary>
    private const int TransportHeight = 9;

    /// <summary>Builds the tiny cracker hull in code and returns its grid.</summary>
    public EntityUid BuildCracker(MapId map, Vector2 offset)
    {
        var grid = CreateHull(map, offset, CrackerSize, CrackerSize);

        // Before the berth marker: a code-built grid isn't map-initialised, so the marker's map-init links the berth.
        EnsureComp<WFPlanetCrackerComponent>(grid.Owner);

        // The tests assert every one of these fifteen.
        SpawnOnHull(grid, "ComputerShuttle", 2, 2);
        SpawnOnHull(grid, "WFCrackConsole", 4, 2);
        SpawnOnHull(grid, "DebugGyroscope", 7, 2);
        // A hull has no FTL range without a powered drive.
        SpawnOnHull(grid, "MachineFTLDrive", 11, 2);
        SpawnOnHull(grid, "WFCentrifuge", 7, 7);
        SpawnOnHull(grid, "WFGravityProjector", 5, 14, 180);
        SpawnOnHull(grid, "WFGravityProjector", 9, 14, 180);
        var berth = SpawnOnHull(grid, "WFChunkBerthMarker", 7, 14, 180);
        SpawnOnHull(grid, "AirlockShuttle", 0, 7, 270);
        SpawnOnHull(grid, "WFAnchorCrate", 2, 11);
        SpawnOnHull(grid, "WFAnchorCrate", 5, 11);

        // Thruster facing picks the LinearThrust index: South 0, East 1, North 2, West 3.
        SpawnOnHull(grid, "DebugThruster", 1, 1);
        SpawnOnHull(grid, "DebugThruster", 13, 1, 90);
        SpawnOnHull(grid, "DebugThruster", 1, 13, 180);
        SpawnOnHull(grid, "DebugThruster", 13, 13, 270);

        FinishHull(grid);

        // The 48x48 / 26 defaults belong to the real hull; this one is tiny, so its berth is too.
        if (TryComp<WFChunkBerthComponent>(berth, out var berthComp))
        {
            berthComp.Size = new Vector2i(12, 12);
            berthComp.Distance = 8f;
            Dirty(berth, berthComp);
        }

        // The crates only exist once the hull does, so ownership is stamped last.
        _ownership.BindAboard(grid.Owner);

        return grid.Owner;
    }

    /// <summary>Builds the tiny cracker with its transport docked, as a bought one arrives; returns both.</summary>
    public (EntityUid Cracker, EntityUid Transport) BuildCrackerWithTransport(MapId map, Vector2 offset)
    {
        var cracker = BuildCracker(map, offset);
        var transport = BuildTransport(map, offset + TransportPark);

        // Same routine the shipyard purchase uses, so both paths dock and stamp ownership identically.
        _ownership.DockTransport(cracker, transport);

        return (cracker, transport);
    }

    /// <summary>Builds the micro anchor transport in code and returns its grid.</summary>
    public EntityUid BuildTransport(MapId map, Vector2 offset)
    {
        var grid = CreateHull(map, offset, TransportWidth, TransportHeight);

        SpawnOnHull(grid, "ComputerShuttle", 3, 1);
        // Landing thrusters, no gravgen; tests that need anchor capacity spawn one themselves.
        SpawnOnHull(grid, "WFThrusterLanding", 2, 4);
        SpawnOnHull(grid, "WFThrusterLanding", 4, 4);
        // Without one the hull can strafe but never turn.
        SpawnOnHull(grid, "DebugGyroscope", 3, 4);
        SpawnOnHull(grid, "AirlockShuttle", 3, 0);
        SpawnOnHull(grid, "WFAnchorCrate", 3, 7);

        SpawnOnHull(grid, "DebugThruster", 0, 1);
        SpawnOnHull(grid, "DebugThruster", 6, 1, 90);
        SpawnOnHull(grid, "DebugThruster", 0, 7, 180);
        SpawnOnHull(grid, "DebugThruster", 6, 7, 270);

        FinishHull(grid);

        return grid.Owner;
    }

    /// <summary>Creates the grid and floors it in one SetTiles call, because every tile change re-floods the CE connectors.</summary>
    private Entity<MapGridComponent> CreateHull(MapId map, Vector2 offset, int width, int height)
    {
        var grid = _mapMan.CreateGridEntity(map);
        _transform.SetLocalPosition(grid.Owner, offset);

        var floor = new Tile(_tileDefs[HullTile].TileId);
        var tiles = new List<(Vector2i GridIndices, Tile Tile)>(width * height);

        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            tiles.Add((new Vector2i(x, y), floor));
        }

        _map.SetTiles(grid.Owner, grid.Comp, tiles);

        return grid;
    }

    /// <summary>Spawns one prototype on the centre of a hull tile, facing the given angle.</summary>
    private EntityUid SpawnOnHull(Entity<MapGridComponent> grid, EntProtoId proto, int x, int y, double degrees = 0)
    {
        // Tile centres are not applied for us; a bare (x, y) would sit on the tile corner.
        var uid = SpawnAtPosition(proto, new EntityCoordinates(grid.Owner, new Vector2(x + 0.5f, y + 0.5f)));

        if (degrees != 0)
            _transform.SetLocalRotation(uid, Angle.FromDegrees(degrees));

        // No cabling on a test hull: PowerNetSystem short-circuits on !NeedsPower, so machines just run.
        _receiver.SetNeedsPower(uid, false);

        return uid;
    }

    /// <summary>Turns the finished hull into a flyable shuttle.</summary>
    private void FinishHull(Entity<MapGridComponent> grid)
    {
        EnsureComp<ShuttleComponent>(grid.Owner);
        _shuttle.Enable(grid.Owner, force: true);

        // Code-built grids aren't map-initialised, so later components (the ForceAnchor lock) get no MapInit.
        EntityManager.RunMapInit(grid.Owner, MetaData(grid.Owner));
    }
}
