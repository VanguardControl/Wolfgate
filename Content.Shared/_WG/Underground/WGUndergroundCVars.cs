using Robust.Shared.Configuration;

namespace Content.Shared._WG.Underground;

/// <summary>Underground faction settings: Heat, warrants, bail, the treasury and the safehouse.</summary>
[CVarDefs]
public sealed class WGUndergroundCVars
{
    /// <summary>Heat at which the Underground is WATCHED.</summary>
    public static readonly CVarDef<float> HeatWatched =
        CVarDef.Create("wg.underground.heat_watched", 3f, CVar.SERVERONLY);

    /// <summary>Heat at which the Underground is WANTED.</summary>
    public static readonly CVarDef<float> HeatWanted =
        CVarDef.Create("wg.underground.heat_wanted", 6f, CVar.SERVERONLY);

    /// <summary>Heat at which a RAID window opens.</summary>
    public static readonly CVarDef<float> HeatRaid =
        CVarDef.Create("wg.underground.heat_raid", 10f, CVar.SERVERONLY);

    /// <summary>Seconds after the last job before Heat starts to cool.</summary>
    public static readonly CVarDef<float> HeatGrace =
        CVarDef.Create("wg.underground.heat_grace", 300f, CVar.SERVERONLY);

    /// <summary>Heat lost a minute while cooling.</summary>
    public static readonly CVarDef<float> HeatCooling =
        CVarDef.Create("wg.underground.heat_cooling", 0.2f, CVar.SERVERONLY);

    /// <summary>How many times faster Heat cools while the market is closed.</summary>
    public static readonly CVarDef<float> HeatClosedFactor =
        CVarDef.Create("wg.underground.heat_closed_factor", 2f, CVar.SERVERONLY);

    /// <summary>Heat a smuggling crate sold at the drop adds.</summary>
    public static readonly CVarDef<float> HeatCrate =
        CVarDef.Create("wg.underground.heat_crate", 1f, CVar.SERVERONLY);

    /// <summary>Heat a load of sealed freight sold to the fence adds.</summary>
    public static readonly CVarDef<float> HeatFence =
        CVarDef.Create("wg.underground.heat_fence", 1f, CVar.SERVERONLY);

    /// <summary>Heat an Underground supply crate sold at a cargo pallet adds.</summary>
    public static readonly CVarDef<float> HeatSupply =
        CVarDef.Create("wg.underground.heat_supply", 2f, CVar.SERVERONLY);

    /// <summary>Share of a job's Heat that lands when someone outside the Underground makes the sale, from 0 to 1.</summary>
    public static readonly CVarDef<float> HeatOutsider =
        CVarDef.Create("wg.underground.heat_outsider", 0f, CVar.SERVERONLY);

    /// <summary>Heat a TSF or PDV player killed by an Underground member adds.</summary>
    public static readonly CVarDef<float> HeatKill =
        CVarDef.Create("wg.underground.heat_kill", 3f, CVar.SERVERONLY);

    /// <summary>Heat a member handed in to the TSF takes off.</summary>
    public static readonly CVarDef<float> HeatCapture =
        CVarDef.Create("wg.underground.heat_capture", 2f, CVar.SERVERONLY);

    /// <summary>Minutes a RAID window stays open.</summary>
    public static readonly CVarDef<float> RaidMinutes =
        CVarDef.Create("wg.underground.raid_minutes", 20f, CVar.SERVERONLY);

    /// <summary>Minutes after a RAID window before another can open.</summary>
    public static readonly CVarDef<float> RaidLockMinutes =
        CVarDef.Create("wg.underground.raid_lock_minutes", 45f, CVar.SERVERONLY);

    /// <summary>Minutes between the bearings security is given while the Underground is WANTED.</summary>
    public static readonly CVarDef<float> BearingMinutes =
        CVarDef.Create("wg.underground.bearing_minutes", 10f, CVar.SERVERONLY);

    /// <summary>Military credits a member's warrant grows by for each job they sell.</summary>
    public static readonly CVarDef<int> WarrantJob =
        CVarDef.Create("wg.underground.warrant_job", 25, CVar.SERVERONLY);

    /// <summary>Minutes a member can't be handed in again after a capture or bail.</summary>
    public static readonly CVarDef<float> ServedMinutes =
        CVarDef.Create("wg.underground.served_minutes", 60f, CVar.SERVERONLY);

    /// <summary>Minutes a pallet holds a captured member while the Underground can pay bail.</summary>
    public static readonly CVarDef<float> BailMinutes =
        CVarDef.Create("wg.underground.bail_minutes", 5f, CVar.SERVERONLY);

    /// <summary>Thalers of bail for each military credit of warrant.</summary>
    public static readonly CVarDef<int> BailRate =
        CVarDef.Create("wg.underground.bail_rate", 200, CVar.SERVERONLY);

    /// <summary>Thalers the treasury starts a round with.</summary>
    public static readonly CVarDef<int> TreasuryStart =
        CVarDef.Create("wg.underground.treasury_start", 50000, CVar.SERVERONLY);

    /// <summary>Thalers the treasury takes for each job sold.</summary>
    public static readonly CVarDef<int> TreasuryJob =
        CVarDef.Create("wg.underground.treasury_job", 5000, CVar.SERVERONLY);

    /// <summary>Thalers each member gets when the Godfather pays the crew.</summary>
    public static readonly CVarDef<int> PayrollEach =
        CVarDef.Create("wg.underground.payroll_each", 10000, CVar.SERVERONLY);

    /// <summary>Players in the round for each extra Fugitive slot.</summary>
    public static readonly CVarDef<int> SlotPlayers =
        CVarDef.Create("wg.underground.slot_players", 15, CVar.SERVERONLY);

    /// <summary>The most extra Fugitive slots the population can add.</summary>
    public static readonly CVarDef<int> SlotBonusMax =
        CVarDef.Create("wg.underground.slot_bonus_max", 5, CVar.SERVERONLY);

    /// <summary>Nearest the safehouse ship is parked to the station, in metres. Zero turns the safehouse off.</summary>
    public static readonly CVarDef<float> SafehouseMin =
        CVarDef.Create("wg.underground.safehouse_min", 6000f, CVar.SERVERONLY);

    /// <summary>Farthest the safehouse ship is parked from the station, in metres.</summary>
    public static readonly CVarDef<float> SafehouseMax =
        CVarDef.Create("wg.underground.safehouse_max", 9000f, CVar.SERVERONLY);

    /// <summary>Seconds of warning everyone aboard gets before the station's turrets come up.</summary>
    public static readonly CVarDef<float> TurretWarning =
        CVarDef.Create("wg.underground.turret_warning", 30f, CVar.SERVERONLY);

    /// <summary>Minutes between the station's jumps. Zero turns jumping off.</summary>
    public static readonly CVarDef<float> HopMinutes =
        CVarDef.Create("wg.underground.hop_minutes", 60f, CVar.SERVERONLY);

    /// <summary>Seconds of warning before the station jumps.</summary>
    public static readonly CVarDef<float> HopSpool =
        CVarDef.Create("wg.underground.hop_spool", 60f, CVar.SERVERONLY);

    /// <summary>Nearest the station jumps to the middle of the sector, in metres.</summary>
    public static readonly CVarDef<float> HopMinRange =
        CVarDef.Create("wg.underground.hop_min_range", 18000f, CVar.SERVERONLY);

    /// <summary>Farthest the station jumps from the middle of the sector, in metres.</summary>
    public static readonly CVarDef<float> HopMaxRange =
        CVarDef.Create("wg.underground.hop_max_range", 25000f, CVar.SERVERONLY);

    /// <summary>The least distance a jump moves the station, in metres.</summary>
    public static readonly CVarDef<float> HopMove =
        CVarDef.Create("wg.underground.hop_move", 10000f, CVar.SERVERONLY);

    /// <summary>Minutes a declared vendetta lasts.</summary>
    public static readonly CVarDef<float> VendettaMinutes =
        CVarDef.Create("wg.underground.vendetta_minutes", 30f, CVar.SERVERONLY);

    /// <summary>Seconds between market passes issued at the terminal.</summary>
    public static readonly CVarDef<float> PassCooldown =
        CVarDef.Create("wg.underground.pass_cooldown", 120f, CVar.SERVERONLY);
}
