using System.Numerics;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Shuttles.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.PlanetCracker.Flight;

/// <summary>
/// The ground-out of a hard landing. The scrape itself is CE's ground friction, which already stops a hull in finite
/// time; what this adds is the price of arriving fast - the leading edge grinds itself off, anything under the hull is
/// crushed, and the obstacles CE would have bounced off are flattened instead (CEZLevelsSystem.WFFlight.cs).
/// </summary>
public sealed partial class WFFlightSystem
{
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;

    /// <summary>Speed (m/s) under which the hull has stopped and the skid is over.</summary>
    public const float SkidStopSpeed = 0.5f;

    /// <summary>Reference rock-impact speed (m/s) for one point of contact damage.</summary>
    public const float SkidRamSpeed = 4f;

    /// <summary>Damage a leading-edge tile takes per second, per metre per second of hull speed.</summary>
    public const float SkidTileDamageRate = 0.15f;

    /// <summary>Accumulated damage at which a leading-edge tile is torn off the hull.</summary>
    public const float SkidTileThreshold = 40f;

    /// <summary>
    /// Damage every tile of the hull takes at touchdown, at the speed a free fall would have arrived at. Under
    /// <see cref="SkidTileThreshold"/> on its own: a hard landing rattles the whole hull but tears nothing off it.
    /// </summary>
    public const float ImpactTileDamage = 12f;

    /// <summary>
    /// Extra damage the leading-edge tiles take at touchdown when the hull came in with planar speed. With the
    /// hull-wide share this passes <see cref="SkidTileThreshold"/> on a fast hard landing and not on a gentle one, so
    /// the expected price of arriving badly is the machinery along the nose.
    /// </summary>
    public const float ImpactEdgeDamage = 45f;

    /// <summary>How wide the leading edge is, in tiles of projection behind the foremost one.</summary>
    private const float SkidEdgeDepth = 1.5f;

    /// <summary>Blast left where a leading-edge tile tears off; small, and silent so one skid is not a hundred bangs.</summary>
    private const float SkidTileIntensity = 2f;

    private const float SkidTileSlope = 2f;
    private const float SkidTileMaxIntensity = 2f;

    /// <summary>
    /// How often the leading edge is chewed on. The footprint walk is not a per-tick job, and neither is anything else
    /// a skid does that makes a noise: the crush and the plough both run on this interval so a capital hull grinding
    /// across a populated deck is one round of damage sounds every quarter second rather than one per victim per tick.
    /// </summary>
    public static readonly TimeSpan SkidBiteInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>
    /// Tiles a hull is never ground below. A grid with nothing left is not a hull that can be repaired, and a massless
    /// grid is a physics body whose solve divides by its own mass.
    /// </summary>
    private const int SkidMinTiles = 4;

    /// <summary>
    /// Tiles one bite may tear off. Everything over the budget keeps the damage it has and comes off at the next bite,
    /// so a capital hull does not queue a hundred craters into one frame the way a crash used to (CrashAudioTest).
    /// </summary>
    private const int SkidMaxBiteTiles = 24;

    private readonly List<Vector2i> _skidEdge = new();

    /// <summary>Grinds every skidding hull down, and lets go of the ones that have come to rest.</summary>
    private void UpdateSkids(float frameTime)
    {
        _skidScan.Clear();

        var query = EntityQueryEnumerator<WFSkidComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            _skidScan.Add(uid);
        }

        foreach (var grid in _skidScan)
        {
            if (TerminatingOrDeleted(grid) || !TryComp<WFSkidComponent>(grid, out var skid))
                continue;

            if (!_zLevels.WfHasSkidGround(grid)
                || !TryComp<PhysicsComponent>(grid, out var body) || !TryComp<MapGridComponent>(grid, out var gridComp))
            {
                EndSkid(grid, skid);
                continue;
            }

            // A non-finite velocity defeats every comparison it appears in - NaN is neither over nor under a
            // threshold - so it would slide past the stop test, be divided into a NaN heading and written straight
            // back into the hull, and from there into every child transform and every sound played on it. The hull
            // is stopped instead, which is what a skid ends in anyway.
            TryHeading(body.LinearVelocity, out var heading, out var speed);
            if (!float.IsFinite(body.LinearVelocity.LengthSquared()))
                _physics.SetLinearVelocity(grid, Vector2.Zero, body: body);
            if (!float.IsFinite(body.AngularVelocity))
                _physics.SetAngularVelocity(grid, 0f, body: body);
            var rotatingSpeed = MathF.Abs(body.AngularVelocity) * gridComp.LocalAABB.Size.Length() * 0.5f;
            if (speed <= SkidStopSpeed && rotatingSpeed <= SkidStopSpeed)
            {
                EndSkid(grid, skid);
                continue;
            }
            if (speed <= SkidStopSpeed)
            {
                heading = _transform.GetWorldRotation(grid).ToWorldVec();
                speed = rotatingSpeed;
            }

            EnsureGroundGrind((grid, gridComp), skid);

            if (_timing.CurTime < skid.NextBite)
                continue;

            var elapsed = (float) SkidBiteInterval.TotalSeconds;
            skid.NextBite = _timing.CurTime + SkidBiteInterval;

            // Anything standing where the hull is going gets the same treatment an FTL arrival gives it. On the bite
            // interval rather than every tick: the crush gibs and deletes everything under the footprint, each of
            // which is its own networked sound, and a capital hull's footprint is a lot of them.
            _zLevels.WfClearLandingObstacles(grid, reportImpacts: true);
            ScarSkidGround((grid, gridComp), skid);
            WearSlidingHull((grid, gridComp), skid, Math.Clamp(speed * SkidTileDamageRate * elapsed, 0f, 0.5f), heading);
            if (skid.Debris)
                continue; // Every section wears; only the main hull runs the occupant crush pass.
            _shuttle.Smimsh(grid);


        }
    }

    /// <summary>
    /// A touchdown the hull walks away from: the thud, the scrape, and the tiles it pays for arriving at the speed it
    /// did. <paramref name="severity"/> is the touchdown speed over the speed a free fall would have brought it in at,
    /// so a hull that nearly held itself up barely marks the deck and one that nearly crashed loses its nose.
    /// </summary>
    public void HardLanding(Entity<MapGridComponent> grid, float severity)
    {
        if (!ImpactCrew(grid.Owner, severity))
            return;
        BeginSkid(grid.Owner);

        if (!TryComp<WFSkidComponent>(grid.Owner, out var skid))
            return;

        // Math.Clamp hands a NaN back out unchanged, and a NaN damage is over every threshold there is: it would tear
        // the whole footprint off in one frame on a landing nobody could have measured.
        severity = float.IsFinite(severity) ? Math.Clamp(severity, 0f, 1f) : 0f;

        // The whole hull takes the landing; nothing comes off from this alone.
        BiteTiles(grid, skid, ImpactTileDamage * severity, null);

        if (!TryComp<PhysicsComponent>(grid.Owner, out var body))
            return;

        // Straight down onto its own footprint has no leading edge to concentrate the impact on.
        if (!TryHeading(body.LinearVelocity, out var heading, out var speed) || speed <= SkidStopSpeed)
            return;

        BiteTiles(grid, skid, ImpactEdgeDamage * severity, heading);
    }

    /// <summary>
    /// A hull's direction of travel and how fast it is going, or false when it has no direction to have. The one place
    /// a velocity is turned into a heading: a zero vector normalises to NaN and a non-finite one stays non-finite, and
    /// either of those written back into a grid is a NaN world position for everything aboard it.
    /// </summary>
    private static bool TryHeading(Vector2 velocity, out Vector2 heading, out float speed)
    {
        heading = Vector2.Zero;
        speed = 0f;

        var lengthSquared = velocity.LengthSquared();

        if (!float.IsFinite(lengthSquared) || lengthSquared <= float.Epsilon)
            return false;

        speed = MathF.Sqrt(lengthSquared);
        heading = velocity / speed;
        return true;
    }

    /// <summary>
    /// Puts damage on a hull's own tiles and tears off the ones that have had enough, leaving a small silent blast
    /// where each one was. With a world heading only the leading edge is touched; without one the whole footprint is,
    /// which is what an impact straight down does. Returns how many tiles went.
    /// </summary>
    private int BiteTiles(Entity<MapGridComponent> grid, WFSkidComponent skid, float damage, Vector2? heading)
    {
        // A NaN is under no threshold and over every one at the same time; it is not a landing this hull took.
        if (!float.IsFinite(damage) || damage <= 0f)
            return 0;

        // The hull's own frame: the footprint is indexed in it, and the hull may be sliding sideways or spinning.
        var local = heading is { } world ? (-_transform.GetWorldRotation(grid.Owner)).RotateVec(world) : Vector2.Zero;

        _skidEdge.Clear();
        var best = float.MinValue;

        var tiles = _map.GetAllTilesEnumerator(grid.Owner, grid.Comp);
        while (tiles.MoveNext(out var tileRef))
        {
            var indices = tileRef.Value.GridIndices;

            if (heading is not null)
                best = MathF.Max(best, Projection(indices, local));

            _skidEdge.Add(indices);
        }

        var lost = 0;

        // What this bite is allowed to take: never past the floor the hull has to keep, and never more of it in one
        // frame than the explosion queue and the client's audio can carry. Damage over the budget stays on the tile.
        var budget = Math.Min(SkidMaxBiteTiles, _skidEdge.Count - SkidMinTiles);

        foreach (var indices in _skidEdge)
        {
            if (heading is not null && Projection(indices, local) < best - SkidEdgeDepth)
                continue;

            var total = skid.TileDamage.GetValueOrDefault(indices) + damage;

            if (total < SkidTileThreshold || lost >= budget)
            {
                skid.TileDamage[indices] = total;
                continue;
            }

            skid.TileDamage.Remove(indices);
            lost++;

            var coords = _map.GridTileToLocal(grid.Owner, grid.Comp, indices);
            _map.SetTile(grid.Owner, grid.Comp, indices, Tile.Empty);

            _explosion.QueueExplosion(coords,
                ExplosionSystem.DefaultExplosionPrototypeId,
                SkidTileIntensity,
                SkidTileSlope,
                SkidTileMaxIntensity,
                cause: grid.Owner,
                addLog: false,
                silent: true);
        }

        return lost;
    }

    /// <summary>How far along the direction of travel a tile sits; the leading edge is the highest of these.</summary>
    private static float Projection(Vector2i indices, Vector2 heading)
    {
        return Vector2.Dot(new Vector2(indices.X + 0.5f, indices.Y + 0.5f), heading);
    }

    /// <summary>Stops the scrape and lets the hull be an ordinary grid again.</summary>
    private void EndSkid(EntityUid grid, WFSkidComponent skid)
    {
        StopGroundSounds(skid);
        RemComp<WFSkidComponent>(grid);
    }
}
