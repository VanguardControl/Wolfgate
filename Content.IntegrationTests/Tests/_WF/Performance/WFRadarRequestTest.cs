#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.Radar;
using Content.Shared._WF.Performance;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WF.Performance;

/// <summary>A radar request walks only the blips near it and is answered no faster than the server allows.</summary>
[TestFixture]
public sealed class WFRadarRequestTest
{
    [Test]
    public async Task BlipsInReachMatchAFullWalk()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var radar = entMan.System<RadarBlipSystem>();
        var xform = entMan.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        var other = await pair.CreateTestMap();
        var sources = new List<EntityUid>();
        var spawned = new List<EntityUid>();
        var seen = new List<EntityUid>();
        var unseen = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            sources.Add(entMan.SpawnEntity(null, new MapCoordinates(0, 0, map.MapId)));
            sources.Add(entMan.SpawnEntity(null, new MapCoordinates(-3000, 500, map.MapId)));
            spawned.AddRange(sources);

            seen.Add(Blip(new Vector2(10, 10), 50, map.MapId));
            unseen.Add(Blip(new Vector2(100, 0), 50, map.MapId));
            seen.Add(Blip(new Vector2(5000, 0), 6000, map.MapId));
            unseen.Add(Blip(new Vector2(5000, 0), 100, map.MapId));
            seen.Add(Blip(new Vector2(-2990, 500), 20, map.MapId));
            unseen.Add(Blip(new Vector2(-1500.5f, -1500.5f), 10, map.MapId));
            seen.Add(Blip(new Vector2(1023.9f, -0.1f), 1100, map.MapId));
            seen.Add(Blip(new Vector2(-1024.1f, 2047.9f), 2400, map.MapId));
            unseen.Add(Blip(new Vector2(20000, 20000), 2048, map.MapId));
            unseen.Add(Blip(new Vector2(10, 10), 50, other.MapId));
        });
        await pair.RunTicksSync(1);

        await server.WaitAssertion(() =>
        {
            var positions = sources.Select(s => xform.GetWorldPosition(s)).ToList();
            var fullWalk = radar.WFBlipsOnMap(map.MapId)
                .Where(b => positions.Any(p => (p - b.Position).LengthSquared() < b.Blip.MaxDistance * b.Blip.MaxDistance))
                .Select(b => b.Uid)
                .ToList();
            var inReach = radar.WFBlipsInReach(map.MapId, sources).Select(b => b.Uid).ToList();

            Assert.That(inReach, Is.EquivalentTo(fullWalk), "Skipping squares must not change which blips a radar gets.");
            Assert.That(inReach, Is.SupersetOf(seen));
            Assert.That(inReach.Intersect(unseen), Is.Empty);

            foreach (var uid in spawned)
            {
                entMan.DeleteEntity(uid);
            }
        });

        await pair.CleanReturnAsync();

        EntityUid Blip(Vector2 position, float range, MapId on)
        {
            var uid = entMan.SpawnEntity(null, new MapCoordinates(position, on));
            entMan.AddComponent<PhysicsComponent>(uid);
            var blip = entMan.AddComponent<RadarBlipComponent>(uid);
            blip.Enabled = true;
            blip.MaxDistance = range;
            spawned.Add(uid);
            return uid;
        }
    }

    [Test]
    public async Task RequestsOverTheCapAreNotAnswered()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var radar = server.EntMan.System<RadarBlipSystem>();
        var before = server.CfgMan.GetCVar(WFRadarCVars.MaxRequestHz);

        await server.WaitAssertion(() =>
        {
            var session = pair.Player!;
            var tick = server.Timing.TickPeriod;
            // Made-up times in the past, so nothing this leaves behind delays a real request.
            var start = server.Timing.RealTime - TimeSpan.FromMinutes(5);

            server.CfgMan.SetCVar(WFRadarCVars.MaxRequestHz, 10f);
            Assert.That(Flood(start), Is.InRange(100, 102), "A flood for 10 s at a cap of 10 is answered about 100 times.");

            server.CfgMan.SetCVar(WFRadarCVars.MaxRequestHz, 30f);
            Assert.That(Flood(start + TimeSpan.FromSeconds(60)), Is.InRange(295, 303), "A cap at or over the tick rate still holds.");

            // A console at 8 a second under a cap of 10, each request handled on the tick after it arrives.
            server.CfgMan.SetCVar(WFRadarCVars.MaxRequestHz, 10f);
            var answered = 0;
            for (var i = 0; i < 80; i++)
            {
                var ticks = Math.Ceiling(TimeSpan.FromMilliseconds(125 * i) / tick);
                if (radar.WFTakeRequest(session, start + TimeSpan.FromSeconds(120) + tick * ticks))
                    answered++;
            }

            Assert.That(answered, Is.EqualTo(80), "A console under the cap loses nothing to tick timing.");
            server.CfgMan.SetCVar(WFRadarCVars.MaxRequestHz, before);

            // One request every 10 ms for 10 s.
            int Flood(TimeSpan from)
            {
                var taken = 0;
                for (var i = 0; i < 1000; i++)
                {
                    if (radar.WFTakeRequest(session, from + TimeSpan.FromMilliseconds(10 * i)))
                        taken++;
                }

                return taken;
            }
        });

        await pair.CleanReturnAsync();
    }
}
