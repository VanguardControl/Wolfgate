#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WG.Wolfmed.Scenarios;
using Content.Server._Mono.Temperature.Components;
using Content.Server._Mono.Temperature.Systems;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._WG.Warmth;
using Content.Shared._WG.WolfWeather;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Warmth;

/// <summary>TEMPORARY: traces the gauge test's scenario to find why a body at a bonfire sometimes never warms.</summary>
[TestFixture]
public sealed class WarmthGaugeDiagTest
{
    private const string OpenTile = "FloorPlanetGrass";
    private const float WinterNight = 257.15f;
    private const int Half = 12;

    [Test]
    public async Task Trace([Range(1, 12)] int run)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var timing = server.Resolve<Robust.Shared.Timing.IGameTiming>();
        var lookup = server.System<EntityLookupSystem>();
        var atmos = server.System<AtmosphereSystem>();
        var temps = server.System<TemperatureSystem>();
        var heaters = server.System<EntityRadiusHeaterSystem>();
        var bt = server.System<Content.Server._WG.Wolfmed.Life.WolfmedBodyTemperatureSystem>();
        EntityUid mob = default;
        EntityUid fire = default;
        var lines = new List<string>();

        await server.WaitPost(() =>
        {
            var air = WolfmedScenario.Air();
            air.Temperature = WinterNight;
            atmos.SetMapAtmosphere(map.MapUid, false, air);
            new WolfmedScenario(entMan).KeepGrid(map.Grid);
            var maps = server.System<SharedMapSystem>();
            var tile = new Tile(server.ResolveDependency<ITileDefinitionManager>()[OpenTile].TileId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -Half; x <= Half; x++)
            for (var y = -Half; y <= Half; y++)
                tiles.Add((new Vector2i(x, y), tile));
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, tiles);
            var weather = server.System<SharedWolfWeatherSystem>();
            weather.SetWeather(map.MapUid, new ProtoId<WolfWeatherPrototype>("WGBlizzard"), null, 1);
            var ws = entMan.GetComponent<WolfWeatherMapComponent>(map.MapUid).Weathers;
            ws.Clear();
            ws.Add(new WolfWeatherEntry { Weather = "WGBlizzard", Start = timing.CurTime - TimeSpan.FromHours(1), Seed = 1 });
            mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
        });

        string Snap(string label)
        {
            var xform = entMan.GetComponent<TransformComponent>(mob);
            var t = entMan.GetComponent<TemperatureComponent>(mob);
            var w = entMan.GetComponent<WGWarmthComponent>(mob);
            var air = atmos.GetContainingMixture((mob, xform));
            var dist = -1f;
            var inLookup = "-";
            var active = "-";
            if (fire.IsValid() && entMan.EntityExists(fire))
            {
                var fx = entMan.GetComponent<TransformComponent>(fire);
                xform.Coordinates.TryDistance(entMan, fx.Coordinates, out dist);
                var comp = entMan.GetComponent<EntityRadiusHeaterComponent>(fire);
                active = heaters.IsActive(fire, comp).ToString();
                var near = lookup.GetEntitiesInRange<TemperatureComponent>(fx.Coordinates, comp.Radius,
                    LookupFlags.Dynamic | LookupFlags.Sundries);
                inLookup = near.Any(e => e.Owner == mob).ToString();
            }

            return $"{label} tick {timing.CurTick.Value} pos {xform.LocalPosition} grid {xform.GridUid} surface {t.CurrentTemperature:F3} " +
                   $"core {bt.GetCore(mob):F3} raw {w.LastGauge:F4} gauge {w.Gauge} trend {w.Trend} near {w.NearWarmth} " +
                   $"wet {w.Wetness:F2} chill {w.WindChill:F2} here {w.HereCelsius} air {air?.Temperature:F2}K/{air?.TotalMoles:F1}mol/C{(air == null ? 0 : atmos.GetHeatCapacity(air, false)):F0} " +
                   $"bodyC {temps.GetHeatCapacity(mob, t):F0} dist {dist:F2} lookup {inLookup} active {active} feel {w.Feel} strain {w.Strain}";
        }

        await pair.RunTicksSync(pair.SecondsToTicks(240f));
        var coldCore = 0f;
        var coldRaw = 0f;
        var coldGauge = 0;
        await server.WaitPost(() =>
        {
            lines.Add($"run {run} mob {mob.Id} period {Content.Shared._WG.Performance.WGTickSpread.Period(timing, 1f)} rate {timing.TickRate}");
            lines.Add(Snap("cold"));
            var w = entMan.GetComponent<WGWarmthComponent>(mob);
            coldCore = bt.GetCore(mob) ?? 0f;
            coldRaw = w.LastGauge;
            coldGauge = w.Gauge;
            fire = entMan.SpawnEntity("Bonfire", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));
            lines.Add($"fire {fire.Id}");
        });

        var warmedAt = -1;
        for (var i = 1; i <= 48; i++)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(5f));
            var step = i;
            await server.WaitPost(() =>
            {
                var w = entMan.GetComponent<WGWarmthComponent>(mob);
                if (warmedAt < 0 && (w.Trend == WGWarmthTrend.Warming || w.Gauge > coldGauge || w.LastGauge > coldRaw
                                     || bt.GetCore(mob) > coldCore + 0.05f))
                    warmedAt = step * 5;
                if (step <= 4 || step % 6 == 0)
                    lines.Add(Snap($"t+{step * 5}s"));
            });
        }

        for (var i = 0; i < 31; i++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() => lines.Add(Snap("tick")));
        }

        lines.Insert(0, $"WARMED={(warmedAt >= 0)} at {warmedAt}s");
        TestContext.Out.WriteLine("WGDIAG\n" + string.Join("\n", lines));
        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }
}
