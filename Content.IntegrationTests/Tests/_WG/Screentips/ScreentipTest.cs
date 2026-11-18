#nullable enable
using System.Reflection;
using Content.Client._WG.Screentips;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WG.Screentips;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._WG.Screentips;

/// <summary>
/// A screentip shows the name centred at the top of the view, and hides again once the cursor isn't over the view.
/// </summary>
[TestOf(typeof(ScreentipUIController))]
public sealed class ScreentipTest : InteractionTest
{
    [Test]
    public async Task ShowsAtTopOfViewThenHides()
    {
        await SpawnTarget("Crowbar");
        var cfg = Client.ResolveDependency<IConfigurationManager>();
        var tips = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ScreentipUIController>();
        var panelField = typeof(ScreentipUIController).GetField("_panel", BindingFlags.Instance | BindingFlags.NonPublic)!;

        try
        {
            await Client.WaitAssertion(() =>
            {
                cfg.SetCVar(ScreentipCVars.Mode, (int) ScreentipMode.TopOfView);
                var viewport = (Control) ((MainViewport) GetWidget<MainViewport>()).Viewport;

                tips.Show(CTarget!.Value, "a crowbar", ScreentipMode.TopOfView, viewport);
                Assert.That(tips.Shown, Is.EqualTo(CTarget));
                Assert.That(tips.Text, Is.EqualTo("a crowbar"));

                var panel = (PanelContainer) panelField.GetValue(tips)!;
                Assert.That(panel.Visible, Is.True);
                var centre = panel.GetValue<float>(LayoutContainer.MarginLeftProperty) + panel.DesiredSize.X / 2;
                Assert.That(centre, Is.EqualTo(viewport.GlobalPosition.X + viewport.Size.X / 2).Within(1f),
                    "The tip is centred over the view.");
            });

            // The headless client's cursor is never over the view, so the next frame hides the tip.
            await RunTicks(3);
            await Client.WaitAssertion(() => Assert.That(tips.Shown, Is.Null));
        }
        finally
        {
            await Client.WaitPost(() => cfg.SetCVar(ScreentipCVars.Mode, ScreentipCVars.Mode.DefaultValue));
        }
    }
}
