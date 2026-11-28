using Content.Client._WG.Mentors;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.MentorHelp;

/// <summary>The line above a player's ahelp: how many mentors are on duty, and a button to ask one.</summary>
public sealed class MentorHelpAHelpHeader : BoxContainer
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private readonly Label _count = new() { HorizontalExpand = true };
    private WGClientMentorSystem? _mentors;

    public MentorHelpAHelpHeader()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 6;
        Margin = new Thickness(0, 0, 0, 4);

        AddChild(_count);
        var ask = new Button { Text = Loc.GetString("wg-mhelp-ahelp-ask") };
        ask.OnPressed += _ => _ui.GetUIController<MentorHelpUIController>().Open();
        AddChild(ask);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _entities.EntitySysManager.TryGetEntitySystem(out _mentors);
        if (_mentors != null)
            _mentors.CountChanged += UpdateCount;

        UpdateCount();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        if (_mentors != null)
            _mentors.CountChanged -= UpdateCount;

        _mentors = null;
    }

    private void UpdateCount()
    {
        _count.Text = Loc.GetString("wg-mhelp-on-duty", ("count", _mentors?.OnDutyCount ?? 0));
    }
}
