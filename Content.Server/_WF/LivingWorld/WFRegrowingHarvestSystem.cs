using Content.Shared._WF.LivingWorld;
using Content.Shared.DoAfter;
using Content.Shared.EntityTable;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.LivingWorld;

/// <summary>Runs <see cref="WFRegrowingHarvestComponent"/>: picking a plant for its loot, and regrowing it.</summary>
public sealed class WFRegrowingHarvestSystem : EntitySystem
{
    [Dependency] private EntityTableSystem _tables = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const float RegrowCheckSeconds = 1f;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRegrowingHarvestComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFRegrowingHarvestComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<WFRegrowingHarvestComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WFRegrowingHarvestComponent, WFHarvestDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<WFRegrowingHarvestComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(Entity<WFRegrowingHarvestComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Harvested)
            ent.Comp.RegrowAt = _timing.CurTime + ent.Comp.RegrowTime;
        _appearance.SetData(ent, WFRegrowingHarvestVisuals.Harvested, ent.Comp.Harvested);
    }

    private void OnInteractHand(Entity<WFRegrowingHarvestComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Tool is { } tool)
        {
            // Only tell them when there's something to pick.
            if (ent.Comp.Harvested)
                return;
            var toolName = Loc.GetString(_proto.Index(tool).ToolName);
            _popup.PopupEntity(Loc.GetString("wf-harvest-needs-tool", ("tool", toolName)), ent, args.User);
            args.Handled = true;
            return;
        }

        args.Handled = TryStartHarvest(ent, args.User);
    }

    private void OnInteractUsing(Entity<WFRegrowingHarvestComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.Tool is not { } tool || !_tool.HasQuality(args.Used, tool))
            return;

        if (ent.Comp.Harvested)
        {
            _popup.PopupEntity(Loc.GetString("wf-harvest-bare", ("plant", ent.Owner)), ent, args.User);
            args.Handled = true;
            return;
        }

        args.Handled = _tool.UseTool(args.Used, args.User, ent, (float) ent.Comp.HarvestTime.TotalSeconds, tool.Id,
            new WFHarvestDoAfterEvent());
    }

    /// <summary>Starts picking the plant by hand; false if there's nothing to pick.</summary>
    public bool TryStartHarvest(Entity<WFRegrowingHarvestComponent> ent, EntityUid user)
    {
        if (ent.Comp.Harvested)
        {
            _popup.PopupEntity(Loc.GetString("wf-harvest-bare", ("plant", ent.Owner)), ent, user);
            return true;
        }

        if (ent.Comp.HarvestTime <= TimeSpan.Zero)
        {
            Harvest(ent, user);
            return true;
        }

        var args = new DoAfterArgs(EntityManager, user, ent.Comp.HarvestTime, new WFHarvestDoAfterEvent(), ent, target: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };
        return _doAfter.TryStartDoAfter(args);
    }

    private void OnDoAfter(Entity<WFRegrowingHarvestComponent> ent, ref WFHarvestDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;
        args.Handled = true;
        Harvest(ent, args.User);
    }

    /// <summary>Spawns the loot, the first piece into the picker's hand, and leaves the plant bare until it regrows.</summary>
    public void Harvest(Entity<WFRegrowingHarvestComponent> ent, EntityUid? user)
    {
        if (ent.Comp.Harvested)
            return;

        ent.Comp.Harvested = true;
        ent.Comp.RegrowAt = _timing.CurTime + ent.Comp.RegrowTime;
        _appearance.SetData(ent, WFRegrowingHarvestVisuals.Harvested, true);
        _audio.PlayPvs(ent.Comp.HarvestSound, ent);

        var coords = _transform.GetMapCoordinates(ent);
        var spawns = new List<string>();
        if (ent.Comp.Loot is { } loot)
            spawns.AddRange(_proto.Index(loot).GetSpawns(_random));
        if (ent.Comp.Table is { } table)
        {
            foreach (var id in _tables.GetSpawns(table))
                spawns.Add(id.Id);
        }

        var handed = false;
        foreach (var id in spawns)
        {
            var item = Spawn(id, coords.Offset(_random.NextVector2(0.3f)));
            if (!handed && user is { } picker && HasComp<ItemComponent>(item))
                handed = _hands.TryPickupAnyHand(picker, item);
        }
    }

    private void OnExamined(Entity<WFRegrowingHarvestComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;
        args.PushMarkup(Loc.GetString(ent.Comp.Harvested ? "wf-harvest-examine-bare" : "wf-harvest-examine-ripe"));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _accumulator += frameTime;
        if (_accumulator < RegrowCheckSeconds)
            return;
        _accumulator = 0f;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFRegrowingHarvestComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Harvested || now < comp.RegrowAt)
                continue;
            comp.Harvested = false;
            _appearance.SetData(uid, WFRegrowingHarvestVisuals.Harvested, false);
        }
    }
}
