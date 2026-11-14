using System.Linq;
using Content.Shared._WF.Hideouts;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Hideouts;

/// <summary>
/// The outpost console's Hideout tab: the home world, the hangar with the owner's ship and its insurance, the safes'
/// storage points, and the buildings on sale, listed by category.
/// </summary>
public sealed class WFHideoutPanel : BoxContainer
{
    [Dependency] private IPrototypeManager _proto = default!;

    private static readonly Color GoodColor = Color.FromHex("#6FCF6F");
    private static readonly Color BadColor = Color.FromHex("#E05A4F");

    /// <summary>The owner buys a module.</summary>
    public event Action<string>? OnBuy;

    public event Action? OnRegister;
    public event Action? OnUnregister;
    public event Action? OnRecall;
    public event Action? OnInsure;
    public event Action? OnClaim;

    /// <summary>The owner moves the outpost's home world.</summary>
    public event Action<string>? OnMoveHome;

    private WFHideoutView? _view;
    private bool _isOwner;

    /// <summary>The category whose buildings are listed; null lists them all.</summary>
    private WFHideoutModuleCategory? _category;

    public WFHideoutPanel()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 8;
        Margin = new Thickness(8);
    }

    /// <summary>The list's scroll container and body, kept across updates so the list stays where it was scrolled to.</summary>
    private ScrollContainer? _scroll;
    private BoxContainer? _body;

    /// <summary>What the list was last built from; the console sends its state every second, and a rebuild is only for a change.</summary>
    private string? _built;

    /// <summary>Everything the list shows, as one string to compare.</summary>
    private string Shown(WFHideoutView view, bool isOwner)
    {
        // Built piece by piece: one Join over this many values compiles to an inline array, which the client sandbox refuses.
        var shown = new System.Text.StringBuilder();
        void Add(object? value) => shown.Append(value).Append('|');

        Add(isOwner);
        Add(_category);
        Add(view.Balance);
        Add(view.Company);
        Add(view.HasShipwright);
        Add(view.StashUsed);
        Add(view.StashBudget);
        Add(view.HomeWorld);
        Add(view.MoveFee);
        foreach (var world in view.Worlds)
        {
            Add(world.Id);
        }

        var hangar = view.Hangar;
        Add(hangar.HasPad);
        Add(hangar.Status);
        Add(hangar.ShipName);
        Add(hangar.Value);
        Add(hangar.InsuredValue);
        Add(hangar.Premium);
        Add(hangar.Deductible);
        Add(hangar.Unregistered);
        Add(hangar.Busy);
        return shown.ToString();
    }

    public void UpdateState(WFHideoutView? view, bool isOwner)
    {
        _view = view;
        _isOwner = isOwner;
        if (view == null)
        {
            _built = null;
            _scroll = null;
            _body = null;
            RemoveAllChildren();
            return;
        }

        var shown = Shown(view, isOwner);
        if (shown == _built && _body != null)
            return;

        _built = shown;
        if (_scroll == null || _body == null)
        {
            RemoveAllChildren();
            _scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
            _body = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 8 };
            _scroll.AddChild(_body);
            AddChild(_scroll);
        }

        // Only the body is rebuilt, inside the same scroll container, so a change doesn't jump the list back to its top.
        var body = _body;
        body.RemoveAllChildren();

        var top = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8 };
        var home = Card(Home(view, isOwner));
        var storage = Card(Storage(view));
        home.SizeFlagsStretchRatio = 3;
        storage.SizeFlagsStretchRatio = 2;
        top.AddChild(home);
        top.AddChild(storage);
        body.AddChild(top);
        body.AddChild(Card(Hangar(view.Hangar, view.Balance, isOwner)));
        body.AddChild(Shop(view));

        foreach (var module in _proto.EnumeratePrototypes<WFHideoutModulePrototype>()
                     .Where(module => _category == null || module.Category == _category)
                     .OrderBy(module => module.Category).ThenBy(module => module.Order).ThenBy(module => module.ID))
        {
            body.AddChild(Card(Module(module, view, isOwner)));
        }
    }

    /// <summary>The storage points the outpost's safes take, and what the rule is.</summary>
    private Control Storage(WFHideoutView view)
    {
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4 };
        box.AddChild(Heading(Loc.GetString("wf-hideout-ui-storage")));
        box.AddChild(new Label
        {
            Text = Loc.GetString("wf-hideout-ui-storage-points", ("used", view.StashUsed), ("budget", view.StashBudget)),
            FontColorOverride = view.StashUsed > view.StashBudget ? BadColor : null,
        });
        box.AddChild(new ProgressBar
        {
            MinValue = 0, MaxValue = 1, MinHeight = 10, HorizontalExpand = true,
            Value = view.StashBudget <= 0 ? 1 : Math.Clamp(view.StashUsed / (float) view.StashBudget, 0f, 1f),
        });
        box.AddChild(Help(Loc.GetString("wf-hideout-ui-storage-help")));
        return box;
    }

    /// <summary>The heading over the buildings on sale: the balance, and a button for each category.</summary>
    private Control Shop(WFHideoutView view)
    {
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4 };
        var title = Heading(Loc.GetString("wf-hideout-ui-buildings"));
        title.HorizontalExpand = true;
        box.AddChild(Row(title, new Label { Text = Loc.GetString("wf-hideout-ui-balance", ("balance", view.Balance)) }));
        box.AddChild(Help(Loc.GetString("wf-hideout-ui-buildings-help")));

        var filters = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        filters.AddChild(Filter(Loc.GetString("wf-hideout-category-all"), null));
        foreach (var category in Enum.GetValues<WFHideoutModuleCategory>())
        {
            filters.AddChild(Filter(Loc.GetString($"wf-hideout-category-{category.ToString().ToLowerInvariant()}"), category));
        }

        box.AddChild(filters);
        return box;
    }

    private Button Filter(string text, WFHideoutModuleCategory? category)
    {
        var button = new Button { Text = text, ToggleMode = true, Pressed = _category == category };
        button.OnPressed += _ =>
        {
            _category = category;
            UpdateState(_view, _isOwner);
        };
        return button;
    }

    /// <summary>The home world the outpost lands on each round, and moving it for the fee.</summary>
    private Control Home(WFHideoutView view, bool isOwner)
    {
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4 };
        box.AddChild(Heading(Loc.GetString("wf-hideout-ui-home")));
        var current = view.Worlds.FirstOrDefault(w => w.Id == view.HomeWorld)?.Name ?? view.HomeWorld ?? Loc.GetString("wf-hideout-ui-home-unset");
        box.AddChild(new Label { Text = Loc.GetString("wf-hideout-ui-home-world", ("world", current)) });
        box.AddChild(Help(Loc.GetString("wf-hideout-ui-home-help")));

        var picker = new OptionButton { HorizontalExpand = true, Disabled = !isOwner };
        var others = view.Worlds.Where(w => w.Id != view.HomeWorld).ToList();
        for (var i = 0; i < others.Count; i++)
            picker.AddItem(others[i].Name, i);
        picker.OnItemSelected += args => picker.SelectId(args.Id);
        var move = Button(Loc.GetString("wf-hideout-ui-home-move", ("price", view.MoveFee)),
            !isOwner || others.Count == 0 || view.MoveFee > view.Balance,
            () =>
            {
                if (picker.SelectedId >= 0 && picker.SelectedId < others.Count)
                    OnMoveHome?.Invoke(others[picker.SelectedId].Id);
            });
        box.AddChild(Row(picker, move));
        return box;
    }

    private Control Hangar(WFHideoutHangarView hangar, int balance, bool isOwner)
    {
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4 };
        box.AddChild(Heading(Loc.GetString("wf-hideout-ui-hangar")));
        if (!hangar.HasPad)
            box.AddChild(Secondary(Loc.GetString("wf-hideout-ui-no-hangar")));

        var status = Loc.GetString($"wf-hideout-ui-ship-{hangar.Status.ToString().ToLowerInvariant()}");
        box.AddChild(hangar.ShipName is { } name
            ? new Label { Text = Loc.GetString("wf-hideout-ui-ship", ("name", name), ("status", status), ("value", hangar.Value)) }
            : Secondary(status));
        box.AddChild(Secondary(hangar.InsuredValue > 0
            ? Loc.GetString("wf-hideout-ui-insured", ("value", hangar.InsuredValue))
            : Loc.GetString("wf-hideout-ui-uninsured")));

        var buttons = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 6 };
        var locked = !isOwner || hangar.Busy;
        if (hangar.Status == WFHideoutShipStatus.None)
        {
            buttons.AddChild(Button(hangar.Unregistered is { } parked
                    ? Loc.GetString("wf-hideout-ui-register", ("name", parked))
                    : Loc.GetString("wf-hideout-ui-register-none"),
                locked || hangar.Unregistered == null, () => OnRegister?.Invoke()));
        }
        else
        {
            if (hangar.Status == WFHideoutShipStatus.Stored)
                buttons.AddChild(Button(Loc.GetString("wf-hideout-ui-recall"), locked || !hangar.HasPad, () => OnRecall?.Invoke()));
            if (hangar.Status == WFHideoutShipStatus.Parked)
            {
                buttons.AddChild(Button(Loc.GetString("wf-hideout-ui-insure", ("price", hangar.Premium)),
                    locked || hangar.Premium > balance, () => OnInsure?.Invoke()));
            }

            if (hangar.InsuredValue > 0)
            {
                buttons.AddChild(Button(Loc.GetString("wf-hideout-ui-claim", ("price", hangar.Deductible)),
                    locked || !hangar.HasPad || hangar.Deductible > balance, () => OnClaim?.Invoke()));
            }

            buttons.AddChild(Button(Loc.GetString("wf-hideout-ui-unregister"), locked, () => OnUnregister?.Invoke()));
        }

        box.AddChild(buttons);
        return box;
    }

    private Control Module(WFHideoutModulePrototype module, WFHideoutView view, bool isOwner)
    {
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
        var title = Heading(Loc.GetString(module.Name));
        title.HorizontalExpand = true;
        var id = module.ID;

        // A hangar that already stands isn't sold twice.
        var stands = module.Bay == WFHideoutBay.Faction && view.Hangar.HasPad || module.Bay == WFHideoutBay.Shipwright && view.HasShipwright;
        var locked = module.Company && !view.Company;
        var buy = Button(Loc.GetString(stands ? "wf-hideout-ui-stands" : "wf-hideout-ui-buy", ("price", module.Price)),
            !isOwner || stands || locked || module.Price > view.Balance, () => OnBuy?.Invoke(id));
        buy.MinWidth = 170;
        box.AddChild(Row(title, buy));
        box.AddChild(Help(Loc.GetString(module.Description)));

        var gives = module.Prefab == null
            ? Loc.GetString("wf-hideout-ui-gives-kit", ("count", module.Grants.Count))
            : Loc.GetString("wf-hideout-ui-gives-stake", ("layouts", Math.Max(1, module.Layouts.Count)));
        box.AddChild(new Label { Text = gives, FontColorOverride = GoodColor });
        if (locked)
            box.AddChild(new Label { Text = Loc.GetString("wf-hideout-ui-company-only"), FontColorOverride = BadColor });
        return box;
    }

    /// <summary>A line of explanation that wraps to the card's width.</summary>
    private static RichTextLabel Help(string text)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(text, defaultColor: Color.FromHex("#A5A8B4"));
        return label;
    }

    private static Button Button(string text, bool disabled, Action pressed)
    {
        var button = new Button { Text = text, Disabled = disabled };
        button.OnPressed += _ => pressed();
        return button;
    }

    private static PanelContainer Card(Control content)
    {
        var card = new PanelContainer { HorizontalExpand = true };
        card.AddStyleClass("AngleRect");
        content.Margin = new Thickness(10, 6);
        card.AddChild(content);
        return card;
    }

    private static Label Heading(string text)
    {
        var label = new Label { Text = text };
        label.AddStyleClass("LabelHeading");
        return label;
    }

    private static Label Secondary(string text)
    {
        var label = new Label { Text = text, ClipText = true };
        label.AddStyleClass("LabelSecondaryColor");
        return label;
    }

    private static BoxContainer Row(params Control[] children)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8 };
        foreach (var child in children)
        {
            row.AddChild(child);
        }

        return row;
    }
}
