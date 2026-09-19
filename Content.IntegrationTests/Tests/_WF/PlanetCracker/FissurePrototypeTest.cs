#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Queries;
using Content.Shared._WF.PlanetCracker.Fissures;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Decals;
using Content.Shared.Salvage.Expeditions;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.PlanetCracker;

/// <summary>
/// Everything F8's data promises that only a loaded server can check: that the four growth-stage decals index and name
/// real states of the crack sheet they borrow, that the burst effect the spawner component actually names is a
/// spawnable one-shot, that the NPC targeting pair indexes, and that Asclepiu's two salvage factions and every mob
/// they can roll resolve.
/// A mistyped decal id here is not a load failure: DecalSystem.SetDecalId THROWS ArgumentOutOfRangeException on an
/// unknown prototype (Content.Server/Decals/DecalSystem.cs:427-430), so the growth promoter would surface it as a
/// server exception in the middle of a drill instead. That is why all four are asserted.
/// </summary>
[TestFixture]
public sealed class FissurePrototypeTest
{
    /// <summary>The four growth stages, in the order WFFissureSpawnerSystem.Rings.cs promotes them.</summary>
    private static readonly string[] FissureDecals =
    {
        "WFFissure1",
        "WFFissure2",
        "WFFissure3",
        "WFFissure4",
    };

    /// <summary>
    /// The sheet all four stages borrow: the stock window damage overlays, which are the only escalating ground-crack
    /// art the tree ships. See Resources/Prototypes/_WF/PlanetCracker/fissures.yml.
    /// </summary>
    private const string FissureRsi = "/Textures/Structures/Windows/cracks.rsi";

    /// <summary>The longest a fissure burst may live; the stock spark effect it now names is half a second.</summary>
    private const float MaxBurstLifetime = 2f;

    /// <summary>The crackable world whose faction table the fissures roll from.</summary>
    private const string Surface = "WFSurfaceAsclepiu";

    /// <summary>
    /// The utility query the stamped threats score the anchor with. A const rather than a literal for RA0033, and
    /// deliberately not a static ProtoId: UtilityQueryPrototype is server-only, and Content.YAMLLinter's
    /// ValidateStaticFields runs over this assembly on the CLIENT instance too, where that kind does not exist.
    /// </summary>
    private const string TargetQuery = "WFFissureTargets";

    /// <summary>The compound every stamped threat is re-rooted onto; a const for the same two reasons as TargetQuery.</summary>
    private const string ThreatCompound = "WFFissureThreatCompound";

    /// <summary>
    /// A decal prototype names its state as a bare SpriteSpecifier and DecalOverlay only ever draws frame zero, so a
    /// renamed or mistyped state is a silently missing fissure and nothing else in the tree would notice.
    /// </summary>
    [Test]
    public async Task FissureDecalsResolve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var protoMan = client.ResolveDependency<IPrototypeManager>();
        var cache = client.ResolveDependency<IResourceCache>();

        await client.WaitAssertion(() =>
        {
            var rsi = cache.GetResource<RSIResource>(new ResPath(FissureRsi)).RSI;

            using (Assert.EnterMultipleScope())
            {
                foreach (var id in FissureDecals)
                {
                    Assert.That(protoMan.TryIndex<DecalPrototype>(id, out var decal), Is.True,
                        $"{id} is not a decal prototype at all, so the growth promoter would throw on it.");
                    Assert.That(decal!.Sprite, Is.InstanceOf<SpriteSpecifier.Rsi>(),
                        $"{id} does not name an RSI state, so the fissure has nothing to draw.");

                    var state = ((SpriteSpecifier.Rsi)decal.Sprite).RsiState;

                    Assert.That(rsi.TryGetState(state, out _), Is.True,
                        $"{id} names state '{state}', which the crack sheet does not have.");

                    // True makes DecalOverlay snap the decal to the eye's cardinal and SUBTRACT that from the
                    // per-decal Angle (Content.Client/Decals/Overlays/DecalOverlay.cs:103-109), which would throw
                    // away the ring tangent the stamp computes.
                    Assert.That(decal.SnapCardinals, Is.False,
                        $"{id} snaps to cardinals, which cancels the per-instance ring rotation.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The burst the ring stamp spawns per tile, read off the COMPONENT rather than off a literal: the id now points
    /// at an existing stock effect, and the only thing that keeps the spawner honest is that whatever it names still
    /// spawns and still takes itself away. Spawned and read INSIDE one callback, because a one-shot effect is gone
    /// within a handful of ticks and anything that runs ticks first is reading a corpse.
    /// There is deliberately no emerge effect any more: a mob simply appears on its fissure tile.
    /// </summary>
    [Test]
    public async Task TheBurstEffectIsASpawnableOneShot()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var proto = new WFFissureSpawnerComponent().BurstEffect;
            var uid = entMan.SpawnEntity(proto, new MapCoordinates(Vector2.Zero, map.MapId));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID, Is.EqualTo(proto.Id),
                    $"{proto} spawned as something else.");
                Assert.That(entMan.TryGetComponent(uid, out TimedDespawnComponent? despawn), Is.True,
                    $"{proto} is not a one-shot effect at all; every fissure would leave one on the ground forever.");
                Assert.That(despawn!.Lifetime, Is.GreaterThan(0f).And.LessThan(MaxBurstLifetime),
                    $"{proto} lingers far longer than the moment a fissure opens.");
            }

            entMan.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The targeting pair the site threats are re-rooted onto. A missing compound would leave every stamped mob with a
    /// RootTask pointing at nothing, which surfaces as an HTN plan failure per mob rather than as a load error.
    /// </summary>
    [Test]
    public async Task FissureTargetingPrototypesResolve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(protoMan.HasIndex<UtilityQueryPrototype>(TargetQuery), Is.True,
                    "WFFissureTargets is not a utility query, so the threats have nothing to pick the anchor with.");
                Assert.That(protoMan.HasIndex<HTNCompoundPrototype>(ThreatCompound), Is.True,
                    "WFFissureThreatCompound is not an HTN compound, so every stamped mob's root task dangles.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Asclepiu's two faction ids and everything they can roll. A faction whose entries do not resolve is not a load
    /// error either: the roll simply comes back with a prototype CreateEntityUninitialized then throws on, mid-drill.
    /// </summary>
    [Test]
    public async Task TheSurfaceFactionsAndTheirMobsResolve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var surface = protoMan.Index<WFPlanetSurfacePrototype>(Surface);
            var ids = new List<ProtoId<SalvageFactionPrototype>?> { surface.Faction, surface.UnsanctionedFaction };

            using (Assert.EnterMultipleScope())
            {
                Assert.That(surface.Faction, Is.Not.Null, "The crackable world names no sanctioned faction at all.");
                Assert.That(surface.UnsanctionedFaction, Is.Not.Null, "The crackable world names no unsanctioned faction.");

                foreach (var id in ids)
                {
                    if (id is not { } value)
                        continue;

                    Assert.That(protoMan.TryIndex(value, out var faction), Is.True,
                        $"{value} is not a salvage faction, so the fissures would spawn nothing at all.");

                    foreach (var group in faction!.MobGroups)
                    foreach (var entry in group.Entries)
                    {
                        Assert.That(entry.PrototypeId, Is.Not.Null,
                            $"{value} has a mob entry with no prototype id.");
                        Assert.That(protoMan.HasIndex<EntityPrototype>(entry.PrototypeId!.Value), Is.True,
                            $"{value} can roll '{entry.PrototypeId}', which is not an entity prototype.");
                    }
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
