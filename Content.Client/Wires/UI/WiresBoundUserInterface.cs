using Content.Client._WG.AdminTech; // WOLFGATE(AdminTech)
using Content.Shared._WG.AdminTech; // WOLFGATE(AdminTech)
using Content.Shared.Wires;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client.Wires.UI
{
    public sealed class WiresBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private WiresMenu? _menu;

        private WGWireLegendWindow? _wgLegend; // WOLFGATE(AdminTech): what each wire does, for a viewer holding a legend tool

        public WiresBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();
            _menu = this.CreateWindow<WiresMenu>();
            _menu.OnAction += PerformAction;
            SendMessage(new WGWireLegendRequestMessage()); // WOLFGATE(AdminTech): answered only for a holder of a legend tool
        }

        // WOLFGATE(AdminTech) START: show the wire legend the server sends back
        protected override void ReceiveMessage(BoundUserInterfaceMessage message)
        {
            base.ReceiveMessage(message);
            if (message is not WGWireLegendMessage legend)
                return;

            _wgLegend ??= new WGWireLegendWindow();
            _wgLegend.Show(legend);
        }
        // WOLFGATE END

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);
            _menu?.Populate((WiresBoundUserInterfaceState) state);
        }

        public void PerformAction(int id, WiresAction action)
        {
            SendMessage(new WiresActionMessage(id, action));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
                return;

            _menu?.Dispose();
            _wgLegend?.Close(); // WOLFGATE(AdminTech)
        }
    }
}
