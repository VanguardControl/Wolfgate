using System.Diagnostics.CodeAnalysis;
using Content.Server.Wires;
using Content.Shared._WG.AdminTech;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Wires;
using Robust.Server.GameObjects;

namespace Content.Server._WG.AdminTech;

/// <summary>
/// The wire legend: a viewer of a wires panel who holds a tool with <see cref="WGWireLegendComponent"/> is told
/// what each wire does. The answer goes to that viewer alone; the panel's shared state is untouched.
/// </summary>
public sealed partial class WGWireLegendSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WiresComponent, WGWireLegendRequestMessage>(OnRequest);
    }

    private void OnRequest(Entity<WiresComponent> ent, ref WGWireLegendRequestMessage args)
    {
        if (TryGetLegend(ent, args.Actor, out var legend))
            _ui.ServerSendUiMessage(ent.Owner, WiresUiKey.Key, new WGWireLegendMessage(legend), args.Actor);
    }

    /// <summary>What each wire of a panel does, for a viewer holding a legend tool. False for anyone else.</summary>
    public bool TryGetLegend(Entity<WiresComponent> ent, EntityUid viewer, [NotNullWhen(true)] out List<WGWireLegendEntry>? legend)
    {
        legend = null;
        var holds = false;
        foreach (var held in _hands.EnumerateHeld(viewer))
        {
            holds |= HasComp<WGWireLegendComponent>(held);
        }

        if (!holds)
            return false;

        legend = new List<WGWireLegendEntry>();
        foreach (var wire in ent.Comp.WiresList)
        {
            // A wire with no action does nothing; it is left out, as it has no name to show.
            if (wire.Action is BaseWireAction action)
                legend.Add(new WGWireLegendEntry(wire.Color, wire.Letter, Loc.GetString(action.Name)));
        }

        return true;
    }
}
