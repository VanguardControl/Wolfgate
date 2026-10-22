using Content.Shared._WF.SuitTank;
using Content.Shared.Clothing;
using Robust.Client.GameObjects;
using Robust.Shared.Serialization.Manager;

namespace Content.Client._WF.SuitTank;

/// <summary>
/// Draws a tank in a worn suit's slot on the wearer, using the tank's suit storage sprite.
/// </summary>
public sealed class WFSuitTankVisualsSystem : EntitySystem
{
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private const string TankSlot = "suitstorage";
    private const string LayerPrefix = "wf-suit-tank";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFSuitTankSlotComponent, EquipmentVisualsUpdatedEvent>(OnVisualsUpdated);
    }

    private void OnVisualsUpdated(EntityUid uid, WFSuitTankSlotComponent comp, EquipmentVisualsUpdatedEvent args)
    {
        if (comp.Slot.Item is not { } tank || !TryComp<SpriteComponent>(args.Equipee, out var sprite))
            return;

        var ev = new GetEquipmentVisualsEvent(args.Equipee, TankSlot);
        RaiseLocalEvent(tank, ev);

        var tankRsi = CompOrNull<SpriteComponent>(tank)?.BaseRSI;
        var i = 0;
        foreach (var (_, data) in ev.Layers)
        {
            // Own keys, so a tank in the suit storage slot as well doesn't collide.
            var key = $"{LayerPrefix}-{i++}";
            if (!args.RevealedLayers.Add(key))
                continue;

            var layer = _serialization.CreateCopy(data, notNullableOverride: true);
            layer.MapKeys = null;
            if (layer.RsiPath == null && layer.TexturePath == null && tankRsi != null)
                layer.RsiPath = tankRsi.Path.ToString();

            // Reserved at the top of the sprite, where suit storage draws.
            var index = _sprite.LayerMapReserve((args.Equipee, sprite), key);
            _sprite.LayerSetData((args.Equipee, sprite), index, layer);
        }
    }
}
