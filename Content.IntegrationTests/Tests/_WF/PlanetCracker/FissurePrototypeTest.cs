#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Queries;
using Content.Shared._WF.PlanetCracker;
using Content.Shared._WF.PlanetCracker.Fissures;
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

/// <summary>Fissure data a loaded server must check: decals, burst effect, NPC targeting and factions.</summary>
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

    /// <summary>The stock window crack sheet all four stages borrow.</summary>
    private const string FissureRsi = "/Textures/Structures/Windows/cracks.rsi";

    /// <summary>The longest a fissure burst may live, in seconds.</summary>
    private const float MaxBurstLifetime = 2f;

    /// <summary>The crackable world whose faction table the fissures roll from.</summary>
    private const string Surface = "WFSurfaceAsclepiu";

    /// <summary>The threats' anchor-scoring query; a string, as the linter's client pass lacks the kind.</summary>
    private const string TargetQuery = "WFFissureTargets";

    /// <summary>The compound every stamped threat is re-rooted onto; a string, as for TargetQuery.</summary>
    private const string ThreatCompound = "WFFissureThreatCompound";

    /// <summary>Every fissure decal resolves and names a real state of the crack sheet.</summary>
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

                    // Snapping would discard the ring tangent the stamp computes.
                    Assert.That(decal.SnapCardinals, Is.False,
                        $"{id} snaps to cardinals, which cancels the per-instance ring rotation.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The burst the spawner component names spawns and times itself out; read in one callback.</summary>
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

    /// <summary>The targeting query and compound the site threats are re-rooted onto both resolve.</summary>
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

    /// <summary>Asclepiu names both factions, and every crack site's factions and the mobs they can roll resolve.</summary>
    [Test]
    public async Task TheSurfaceFactionsAndTheirMobsResolve()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(WFCrackSitePrototype.TryGet(protoMan, Surface, out var asclepiu), Is.True,
                "The crackable world has no crack site, so its fissures would spawn nothing.");

            var ids = new List<ProtoId<SalvageFactionPrototype>?>();
            foreach (var site in protoMan.EnumeratePrototypes<WFCrackSitePrototype>())
            {
                ids.Add(site.Faction);
                ids.Add(site.UnsanctionedFaction);
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(asclepiu!.Faction, Is.Not.Null, "The crackable world names no sanctioned faction at all.");
                Assert.That(asclepiu.UnsanctionedFaction, Is.Not.Null, "The crackable world names no unsanctioned faction.");

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
