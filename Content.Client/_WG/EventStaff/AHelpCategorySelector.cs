using Content.Shared._WG.EventStaff;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.EventStaff;

/// <summary>
/// Picks whether a player's ahelp is a gameplay issue or a player issue, or a how-to question that goes to Mentor Help
/// instead.
/// </summary>
public sealed class AHelpCategorySelector : BoxContainer
{
    /// <summary>The id of the how-to entry; not an <see cref="AHelpCategory"/>.</summary>
    private const int HowToId = 100;

    private readonly OptionButton _options;

    /// <summary>The category the player picked; Player for the how-to entry.</summary>
    public AHelpCategory Category => HowTo ? AHelpCategory.Player : (AHelpCategory) _options.SelectedId;

    /// <summary>Whether the player picked the how-to entry, so their text goes to Mentor Help.</summary>
    public bool HowTo => _options.SelectedId == HowToId;

    public AHelpCategorySelector()
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 6;
        Margin = new Thickness(0, 0, 0, 4);

        AddChild(new Label { Text = Loc.GetString("wg-bwoink-category-label") });

        _options = new OptionButton
        {
            HorizontalExpand = true,
            ToolTip = Loc.GetString("wg-bwoink-category-hint"),
        };
        _options.AddItem(Loc.GetString("wg-bwoink-category-option-player"), (int) AHelpCategory.Player);
        _options.AddItem(Loc.GetString("wg-bwoink-category-option-gameplay"), (int) AHelpCategory.Gameplay);
        _options.AddItem(Loc.GetString("wg-bwoink-category-option-howto"), HowToId);
        _options.OnItemSelected += args => _options.SelectId(args.Id);
        AddChild(_options);
    }

    /// <summary>Goes back to the first entry, after a how-to question was handed to Mentor Help.</summary>
    public void Reset()
    {
        _options.SelectId((int) AHelpCategory.Player);
    }
}
