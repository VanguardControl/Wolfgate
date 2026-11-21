using System.Diagnostics.CodeAnalysis;
using Content.Client.UserInterface.Systems.Guidebook;
using Content.Shared.Guidebook;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WG.MentorHelp;

/// <summary>
/// <c>[wgguide=ENTRYID/]</c>: a clickable guidebook page name that opens the page. Mentor Help turns the
/// <c>{guide:ENTRYID}</c> tokens mentors send into these (<see cref="MentorGuideLinks"/>).
/// </summary>
public sealed partial class WGGuideLinkTag : IMarkupTagHandler
{
    public const string TagName = "wgguide";

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    public string Name => TagName;

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;
        if (!node.Value.TryGetString(out var id) || !_prototypes.TryIndex<GuideEntryPrototype>(id, out var entry))
            return false;

        var label = new Label
        {
            Text = Loc.GetString(entry.Name),
            FontColorOverride = Color.SkyBlue,
            MouseFilter = Control.MouseFilterMode.Stop,
            DefaultCursorShape = Control.CursorShape.Hand,
        };
        label.OnMouseEntered += _ => label.FontColorOverride = Color.LightSkyBlue;
        label.OnMouseExited += _ => label.FontColorOverride = Color.SkyBlue;
        label.OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            args.Handle();
            _ui.GetUIController<GuidebookUIController>().OpenGuidebook(selected: new ProtoId<GuideEntryPrototype>(entry.ID));
        };

        control = label;
        return true;
    }
}
