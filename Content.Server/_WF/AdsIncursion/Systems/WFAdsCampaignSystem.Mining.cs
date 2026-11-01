using System.Linq;
using Content.Server._WF.AdsIncursion.Components;
using Content.Server._WF.Encounters.Components;
using Content.Shared._WF.AdsIncursion;
using Content.Shared._WF.AsteroidMining;

namespace Content.Server._WF.AdsIncursion.Systems;

public sealed partial class WFAdsCampaignSystem
{
    /// <summary>One pass of the mining watch, for tests and the admin.</summary>
    public void CheckMining(TimeSpan elapsed)
    {
        if (Campaign is { } campaign && State() is { } state)
            TickMining(campaign, state, Crewed(), elapsed);
    }

    /// <summary>
    /// Warns each crewed ship mining a rock in held space, and declares one that goes on past the grace period a threat
    /// to every ADS ship, with a sweep sent after it. Stopping leaves a ship alone; its mining time is kept a while, so
    /// short bursts don't reset the warning.
    /// </summary>
    private void TickMining(WFAdsCampaignPrototype campaign, WFAdsCampaignComponent state, HashSet<EntityUid> ships, TimeSpan elapsed)
    {
        var now = _timing.CurTime;
        var mining = MiningShips(campaign, ships);

        foreach (var (ship, watch) in state.Miners.ToList())
        {
            if (TerminatingOrDeleted(ship) || !mining.Contains(ship) && now - watch.LastMined >= campaign.MiningForget)
                state.Miners.Remove(ship);
        }

        foreach (var (ship, until) in state.MiningGrudges.ToList())
        {
            if (now >= until || TerminatingOrDeleted(ship))
                state.MiningGrudges.Remove(ship);
        }

        foreach (var ship in mining)
        {
            if (state.MiningGrudges.ContainsKey(ship))
            {
                state.MiningGrudges[ship] = now + campaign.MiningGrudge;
                continue;
            }

            if (!state.Miners.TryGetValue(ship, out var watch))
            {
                state.Miners[ship] = new WFAdsMinerWatch { LastMined = now };
                TellMiner(campaign, ship, "wf-ads-mining-warning");
                Log.Info($"ADS warns {ToPrettyString(ship)} off mining in held space.");
                continue;
            }

            watch.LastMined = now;
            watch.MinedFor += elapsed;
            if (watch.MinedFor < campaign.MiningGrace)
                continue;

            state.Miners.Remove(ship);
            state.MiningGrudges[ship] = now + campaign.MiningGrudge;
            TellMiner(campaign, ship, "wf-ads-mining-declared");
            Log.Info($"ADS declares {ToPrettyString(ship)} a threat for mining on after its warning.");
            DeclareMiners(state);
            TryInterdict(ship);
        }

        DeclareMiners(state);
    }

    /// <summary>The crewed ships with a mining beam on a rock in held space.</summary>
    private HashSet<EntityUid> MiningShips(WFAdsCampaignPrototype campaign, HashSet<EntityUid> ships)
    {
        var mining = new HashSet<EntityUid>();
        var map = _territory.SectorMap;
        var beams = EntityQueryEnumerator<WFMiningBeamEmitterComponent, TransformComponent>();
        while (beams.MoveNext(out var beam, out var xform))
        {
            if (!beam.Active || beam.Target is not { } rock || TerminatingOrDeleted(rock)
                || xform.GridUid is not { } ship || !ships.Contains(ship) || _scheduler.IsSecretPlace(ship))
                continue;

            if (_territory.Owner(map, _territory.CellOf(_transform.GetMapCoordinates(rock))) == campaign.Faction)
                mining.Add(ship);
        }

        return mining;
    }

    /// <summary>Puts every ship under a grudge on the declared list of each ADS ship, those launched since included.</summary>
    private void DeclareMiners(WFAdsCampaignComponent state)
    {
        if (state.MiningGrudges.Count == 0)
            return;

        var encounters = EntityQueryEnumerator<WFEncounterComponent>();
        while (encounters.MoveNext(out var uid, out var encounter))
        {
            if (encounter.Resolution != null || !IsCampaignEncounter(uid))
                continue;

            foreach (var ship in encounter.Ships.Values)
            {
                ship.Declared.UnionWith(state.MiningGrudges.Keys);
            }
        }
    }

    private void TellMiner(WFAdsCampaignPrototype campaign, EntityUid ship, string key)
    {
        _notices.Tell(ship, Loc.GetString(key, ("ship", Name(ship))), Loc.GetString("wf-ads-watch"), campaign.RisingColor, true);
    }
}
