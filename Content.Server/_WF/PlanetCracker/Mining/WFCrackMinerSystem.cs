using Content.Server.PowerCell;
using Content.Server.Stack;
using Content.Shared._WF.PlanetCracker.Mining;
using Content.Shared._WF.PlanetCracker.Survey;
using Content.Shared.Audio;
using Content.Shared.Construction.Components;
using Content.Shared.Destructible;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Repairable;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetCracker.Mining;

/// <summary>Server half of the crack miner: the production tick, cell drain, metered ore and its state.</summary>
public sealed partial class WFCrackMinerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private PowerCellSystem _cell = default!;
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedWFSurveySystem _survey = default!;
    [Dependency] private StackSystem _stack = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFCrackMinerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFCrackMinerComponent, AnchorAttemptEvent>(OnAnchorAttempt);
        SubscribeLocalEvent<WFCrackMinerComponent, AnchorStateChangedEvent>(OnAnchorStateChanged);
        SubscribeLocalEvent<WFCrackMinerComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFCrackMinerComponent, BreakageEventArgs>(OnBreakage);
        SubscribeLocalEvent<WFCrackMinerComponent, RepairedEvent>(OnRepaired);
    }

    /// <summary>Pushes the initial appearance and ambience so a mapped or spawned miner looks right on its first frame.</summary>
    private void OnMapInit(Entity<WFCrackMinerComponent> ent, ref MapInitEvent args)
    {
        _appearance.SetData(ent.Owner, WFCrackMinerVisuals.State, ent.Comp.State);
        _ambient.SetAmbience(ent.Owner, ent.Comp.State == WFCrackMinerState.Mining);

        ent.Comp.NextTick = _timing.CurTime + ent.Comp.Interval;
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WFCrackMinerComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            // The query doesn't skip paused entities, so a miner on a paused map would otherwise keep mining.
            if (Paused(uid))
                continue;

            if (_timing.CurTime < comp.NextTick)
                continue;

            comp.NextTick = _timing.CurTime + comp.Interval;

            Tick((uid, comp), xform);
        }
    }

    /// <summary>One production tick: the gate in order, then the cut, then the metered drop.</summary>
    private void Tick(Entity<WFCrackMinerComponent> ent, TransformComponent xform)
    {
        var comp = ent.Comp;

        if (comp.State == WFCrackMinerState.Broken)
            return;

        if (!xform.Anchored || !TryGetVein(xform, out var chunk, out var vein))
        {
            FlushOutput(ent, xform);
            SetState(ent, WFCrackMinerState.Idle);
            return;
        }

        // A falling chunk isn't sprayed with stacks on the way down.
        if (chunk.Comp.Dropped)
        {
            FlushOutput(ent, xform);
            SetState(ent, WFCrackMinerState.Idle);
            return;
        }

        if (vein.Comp.Remaining <= 0)
        {
            FlushOutput(ent, xform);
            SetState(ent, WFCrackMinerState.Exhausted);
            return;
        }

        // No user, so a background tick doesn't pop "insufficient power" at bystanders; the charge is all-or-nothing.
        if (!_cell.TryUseCharge(ent.Owner, comp.DrawRate))
        {
            FlushOutput(ent, xform);
            SetState(ent, WFCrackMinerState.Idle);
            return;
        }

        SetState(ent, WFCrackMinerState.Mining);

        // Rate is ore per minute; the carry keeps fractional ticks exact.
        comp.Carry += vein.Comp.Rate * (float) comp.Interval.TotalSeconds / 60f;

        var units = (int) comp.Carry;

        if (units <= 0)
            return;

        comp.Carry -= units;

        // Remaining isn't networked, so the vein isn't dirtied.
        units = Math.Min(units, vein.Comp.Remaining);
        vein.Comp.Remaining -= units;
        comp.Buffer += units;
        comp.BufferedOre = vein.Comp.Ore;

        if (comp.Buffer >= comp.BatchSize || vein.Comp.Remaining <= 0)
            FlushOutput(ent, xform);

        // Depleted, never deleted: players' survey records still point at the vein.
        if (vein.Comp.Remaining <= 0)
            SetState(ent, WFCrackMinerState.Exhausted);
    }

    /// <summary>Drops the buffered ore as one stack entity beside the miner and merges it into whatever is already there.</summary>
    private void FlushOutput(Entity<WFCrackMinerComponent> ent, TransformComponent xform)
    {
        var comp = ent.Comp;

        if (comp.Buffer <= 0 || comp.BufferedOre is not { } ore)
            return;

        // OreEntity is the single-count form; the "Full" parent would hand out fifty per entity.
        if (!_proto.TryIndex(ore, out var orePrototype) || orePrototype.OreEntity is not { } oreEntity)
        {
            Log.Error($"{ToPrettyString(ent.Owner)} buffered {comp.Buffer} units of {ore.Id}, which has no ore entity.");
            comp.Buffer = 0;
            comp.BufferedOre = null;
            return;
        }

        var coords = xform.Coordinates.Offset(_random.NextVector2(comp.OutputSpread));
        var spawned = _stack.SpawnMultiple(oreEntity.Id, comp.Buffer, coords);

        comp.Buffer = 0;
        comp.BufferedOre = null;

        foreach (var stack in spawned)
        {
            _stack.TryMergeToContacts(stack);
        }
    }

    /// <summary>The only writer of the miner's state; it also pushes the appearance key and the cutting ambience.</summary>
    private void SetState(Entity<WFCrackMinerComponent> ent, WFCrackMinerState state)
    {
        if (ent.Comp.State == state)
            return;

        ent.Comp.State = state;

        _appearance.SetData(ent.Owner, WFCrackMinerVisuals.State, state);
        _ambient.SetAmbience(ent.Owner, state == WFCrackMinerState.Mining);
    }

    /// <summary>Examine: what the miner is doing, and what the seam under it is worth.</summary>
    private void OnExamined(Entity<WFCrackMinerComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("wf-crack-miner-examine-state",
            ("state", Loc.GetString(GetStateLocId(ent.Comp.State)))));

        // No charge line here: PowerCellSystem gives any PowerCellSlot holder one of its own on the same examine.
        if (!TryGetVein(Transform(ent.Owner), out _, out var vein))
        {
            args.PushMarkup(Loc.GetString("wf-crack-miner-examine-no-vein"));
            return;
        }

        args.PushMarkup(Loc.GetString("wf-crack-miner-examine-vein",
            ("ore", _survey.GetOreName(vein.Comp.Ore))));

        var percent = vein.Comp.TotalYield > 0
            ? Math.Clamp(vein.Comp.Remaining * 100 / vein.Comp.TotalYield, 0, 100)
            : 0;

        args.PushMarkup(Loc.GetString("wf-crack-miner-examine-remaining",
            ("percent", percent),
            ("remaining", vein.Comp.Remaining)));

        args.PushMarkup(Loc.GetString("wf-crack-miner-examine-rate",
            ("rate", (int) vein.Comp.Rate)));
    }

    /// <summary>The Destructible Breakage threshold: drop what was cut and wait for a welder.</summary>
    private void OnBreakage(Entity<WFCrackMinerComponent> ent, ref BreakageEventArgs args)
    {
        FlushOutput(ent, Transform(ent.Owner));
        SetState(ent, WFCrackMinerState.Broken);
    }

    /// <summary>Repaired: back to Idle, and the next tick promotes it if it is still sitting on a live seam.</summary>
    private void OnRepaired(Entity<WFCrackMinerComponent> ent, ref RepairedEvent args)
    {
        if (ent.Comp.State != WFCrackMinerState.Broken)
            return;

        SetState(ent, WFCrackMinerState.Idle);
    }

    /// <summary>The examine word for a state.</summary>
    private static string GetStateLocId(WFCrackMinerState state)
    {
        return state switch
        {
            WFCrackMinerState.Idle => "wf-crack-miner-state-idle",
            WFCrackMinerState.Mining => "wf-crack-miner-state-mining",
            WFCrackMinerState.Exhausted => "wf-crack-miner-state-exhausted",
            _ => "wf-crack-miner-state-broken",
        };
    }
}
