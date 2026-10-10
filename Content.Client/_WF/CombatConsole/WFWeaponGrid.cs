using System.Linq;
using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.CombatConsole;

/// <summary>Pages a battery into readable fixed-height rows while retaining every weapon button.</summary>
public sealed class WFWeaponGrid : BoxContainer
{
    private const float Gap = 4;
    private const float FooterHeight = 32;
    private readonly HashSet<Button> _unavailable = new();
    private BoxContainer? _footer;
    private Button? _previous;
    private Button? _next;
    private Label? _pageLabel;

    /// <summary>Preferred row height, bounded below so weapon names and supply remain readable.</summary>
    public float MinimumRowHeight { get; set; } = 52;
    /// <summary>Zero-based active page, retained across telemetry updates.</summary>
    public int PageIndex { get; private set; }
    /// <summary>Number of pages in the current available height.</summary>
    public int PageCount { get; private set; } = 1;
    /// <summary>Number of whole weapon rows that fit on the active page.</summary>
    public int ItemsPerPage { get; private set; } = 1;

    public WFWeaponGrid()
    {
        HorizontalExpand = VerticalExpand = true;
        MinHeight = 88;
        RectClipContent = true;
        EnsureFooter();
    }

    /// <summary>Excludes non-offensive launchers independently of page visibility.</summary>
    public void SetAvailable(Button button, bool available)
    {
        var changed = available ? _unavailable.Remove(button) : _unavailable.Add(button);
        if (!changed)
            return;
        if (!available)
            button.Visible = false;
        InvalidateMeasure();
        InvalidateArrange();
    }

    /// <summary>Changes pages without modifying any weapon selection or event handler.</summary>
    public void SetPage(int page)
    {
        var next = Math.Clamp(page, 0, PageCount - 1);
        if (PageIndex == next)
            return;
        PageIndex = next;
        InvalidateMeasure();
        InvalidateArrange();
    }

    protected override void ChildAdded(Control child)
    {
        base.ChildAdded(child);
        // Page visibility can leave measurement pending without invalidating the completed arrangement.
        InvalidateArrange();
    }

    protected override void ChildRemoved(Control child)
    {
        base.ChildRemoved(child);
        InvalidateArrange();
        if (child is Button button)
            _unavailable.Remove(button);
        if (child == _footer)
            _footer = null;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        EnsureFooter();
        // A vertical parent may measure with space reserved for later siblings still included.
        // Publish paging only from the final arranged rectangle.
        var cell = new Vector2(Math.Max(0, availableSize.X), RowHeight);
        foreach (var button in Children.OfType<Button>())
        {
            if (button.Visible && !_unavailable.Contains(button))
                button.Measure(cell);
        }
        if (_footer!.Visible)
            _footer.Measure(new Vector2(cell.X, FooterHeight));
        return Vector2.Zero;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        Layout(finalSize);
        return finalSize;
    }

    private float RowHeight => float.IsFinite(MinimumRowHeight) ? Math.Max(52, MinimumRowHeight) : 52;

    /// <summary>Restores the pager after upstream refreshes clear all children.</summary>
    private void EnsureFooter()
    {
        if (_footer != null)
            return;
        _previous = WFInstrumentTheme.Button("wf-weapon-page-previous");
        _next = WFInstrumentTheme.Button("wf-weapon-page-next");
        _previous.Name = "WfWeaponPrevious";
        _next.Name = "WfWeaponNext";
        _previous.ToolTip = Loc.GetString("wf-weapon-page-previous-help");
        _next.ToolTip = Loc.GetString("wf-weapon-page-next-help");
        foreach (var button in new[] { _previous, _next })
        {
            button.AddStyleClass("WfCompact");
            button.HorizontalExpand = false;
            button.SetWidth = 36;
            WFInstrumentTheme.Switch(button);
        }
        _previous.OnPressed += _ => SetPage(PageIndex - 1);
        _next.OnPressed += _ => SetPage(PageIndex + 1);
        _pageLabel = new Label { Text = Loc.GetString("wf-weapon-page", ("page", 1), ("pages", 1)) };
        WFInstrumentTheme.Apply(_pageLabel);
        _pageLabel.Name = "WfWeaponPage";
        _pageLabel.Align = Label.AlignMode.Center;
        _pageLabel.HorizontalExpand = true;
        _pageLabel.ClipText = true;
        _pageLabel.Margin = new Thickness(0);
        _footer = WFInstrumentTheme.Row(_previous, _pageLabel, _next);
        _footer.Name = "WfWeaponPager";
        _footer.SetHeight = FooterHeight;
        _footer.Visible = false;
        AddChild(_footer);
    }

    private void Layout(Vector2 available)
    {
        EnsureFooter();
        if (!float.IsFinite(available.X) || !float.IsFinite(available.Y))
            return;
        var buttons = Children.OfType<Button>().Where(button => !_unavailable.Contains(button)).ToArray();
        var height = RowHeight;
        var withoutFooter = Math.Max(1, (int) ((available.Y + Gap) / (height + Gap)));
        var paged = buttons.Length > withoutFooter;
        var contentHeight = Math.Max(0, available.Y - (paged ? FooterHeight + Gap : 0));
        ItemsPerPage = Math.Max(1, (int) ((contentHeight + Gap) / (height + Gap)));
        PageCount = Math.Max(1, (buttons.Length + ItemsPerPage - 1) / ItemsPerPage);
        PageIndex = Math.Clamp(PageIndex, 0, PageCount - 1);
        _footer!.Visible = paged;
        _previous!.Disabled = PageIndex == 0;
        _next!.Disabled = PageIndex == PageCount - 1;
        var caption = Loc.GetString("wf-weapon-page", ("page", PageIndex + 1), ("pages", PageCount));
        if (_pageLabel!.Text != caption)
            _pageLabel.Text = caption;
        var first = PageIndex * ItemsPerPage;
        for (var i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            button.Visible = i >= first && i < first + ItemsPerPage;
            if (!button.Visible)
                continue;
            var cell = new Vector2(Math.Max(0, available.X), height);
            button.Measure(cell);
            button.Arrange(UIBox2.FromDimensions(new Vector2(0, (i - first) * (height + Gap)), cell));
        }
        if (!paged)
            return;
        var footerSize = new Vector2(Math.Max(0, available.X), FooterHeight);
        _footer.Measure(footerSize);
        _footer.Arrange(UIBox2.FromDimensions(new Vector2(0, Math.Max(0, available.Y - FooterHeight)), footerSize));
    }
}
