using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Cockpit;

public sealed partial class WFCockpitView
{
    /// <summary>Centers the existing Wolfgate wordmark independently of the title and exit control.</summary>
    private static Control BuildHeader(Action exit)
    {
        var header = new Control { Name = "CockpitHeader", HorizontalExpand = true, SetHeight = 40 };
        var title = Label("wf-cockpit-title", Accent);
        title.Name = "CockpitTitle";
        title.HorizontalAlignment = HAlignment.Left;
        title.VerticalAlignment = VAlignment.Center;
        var texture = IoCManager.Resolve<IResourceCache>()
            .GetResource<TextureResource>("/Textures/_WF/Branding/Logo/logo.png").Texture;
        var logo = new TextureRect
        {
            Name = "CockpitLogo",
            // The source wordmark has transparent margins intended for the full-screen splash.
            Texture = new AtlasTexture(texture, new UIBox2(48, 67, 754, 199)),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            CanShrink = true,
            SetSize = new Vector2(214, 40),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };
        var exitButton = Button("wf-cockpit-exit");
        exitButton.Name = "CockpitExit";
        exitButton.HorizontalExpand = false;
        exitButton.HorizontalAlignment = HAlignment.Right;
        exitButton.VerticalAlignment = VAlignment.Center;
        exitButton.SetWidth = 150;
        exitButton.OnPressed += _ => exit();
        header.AddChild(title);
        header.AddChild(logo);
        header.AddChild(exitButton);
        return header;
    }
}
