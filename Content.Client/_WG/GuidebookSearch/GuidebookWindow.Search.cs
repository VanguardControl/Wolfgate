using System.Linq;
using System.Text;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls.FancyTree;
using Content.Shared.Guidebook;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.Guidebook.Controls;

/// <summary>
/// A search bar over the guidebook's contents. Typing swaps the tree for a list of the pages whose title or text
/// match, titles first; rule pages, which the tree leaves out, are found too.
/// </summary>
public sealed partial class GuidebookWindow
{
    private const int WGMaxResults = 50;
    private const int WGSnippetBefore = 30;
    private const int WGSnippetLength = 110;

    private readonly Dictionary<ProtoId<GuideEntryPrototype>, string> _wgPageText = new();
    private readonly List<ProtoId<GuideEntryPrototype>> _wgShown = new();
    private BoxContainer _wgColumn = default!;
    private LineEdit _wgSearch = default!;
    private ScrollContainer _wgResultsScroll = default!;
    private BoxContainer _wgResults = default!;

    /// <summary>Puts the search bar above the contents tree.</summary>
    private void WGInitSearch()
    {
        _wgSearch = new LineEdit
        {
            PlaceHolder = Loc.GetString("wg-guidebook-search-placeholder"),
            Margin = new Thickness(0, 5, 5, 5),
        };
        _wgResults = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _wgResultsScroll = new ScrollContainer
        {
            HScrollEnabled = false,
            VerticalExpand = true,
            HorizontalExpand = true,
            Visible = false,
        };
        _wgResultsScroll.AddChild(_wgResults);
        _wgColumn = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var index = Tree.GetPositionInParent();
        TreeBox.RemoveChild(Tree);
        _wgColumn.AddChild(_wgSearch);
        _wgColumn.AddChild(_wgResultsScroll);
        _wgColumn.AddChild(Tree);
        TreeBox.AddChild(_wgColumn);
        _wgColumn.SetPositionInParent(index);

        _wgSearch.OnTextChanged += _ => WGRefreshSearch();
        _wgSearch.OnTextEntered += _ =>
        {
            if (_wgShown.Count > 0)
                WGOpen(_wgShown[0]);
        };
    }

    /// <summary>Shows the pages matching the search, or the tree again once it is empty.</summary>
    private void WGRefreshSearch()
    {
        _wgResults.DisposeAllChildren();
        _wgShown.Clear();

        var query = _wgSearch.Text.Trim();
        if (query.Length == 0)
        {
            _wgResultsScroll.Visible = false;
            Tree.Visible = true;
            _wgColumn.MinWidth = 0;
            return;
        }

        if (Tree.Visible)
        {
            _wgColumn.MinWidth = Tree.Width;
            Tree.Visible = false;
        }

        _wgResultsScroll.Visible = true;

        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = new List<(int Rank, string Name, ProtoId<GuideEntryPrototype> Id, string? Snippet)>();
        foreach (var (id, entry) in _entries)
        {
            var name = Loc.GetString(entry.Name);
            int rank;
            string? snippet = null;
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                rank = 0;
            else if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
                rank = 1;
            else if (words.All(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                rank = 2;
            else
            {
                var text = WGPageText(entry);
                if (!words.All(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)
                                    || text.Contains(w, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                rank = 3;
                snippet = WGSnippet(text, query, words);
            }

            matches.Add((rank, name, id, snippet));
        }

        if (matches.Count == 0)
        {
            _wgResults.AddChild(new Label
            {
                Text = Loc.GetString("wg-guidebook-search-none"),
                StyleClasses = { StyleBase.StyleClassLabelSubText },
                Margin = new Thickness(4, 2),
            });
            return;
        }

        foreach (var match in matches.OrderBy(m => m.Rank).ThenBy(m => m.Name).Take(WGMaxResults))
        {
            _wgResults.AddChild(WGResultRow(match.Name, match.Id, match.Snippet, _wgShown.Count));
            _wgShown.Add(match.Id);
        }
    }

    private Control WGResultRow(string name, ProtoId<GuideEntryPrototype> id, string? snippet, int row)
    {
        var button = new ContainerButton
        {
            StyleIdentifier = TreeItem.StyleIdentifierTreeButton,
            HorizontalExpand = true,
            ToolTip = snippet,
        };
        button.AddStyleClass(row % 2 == 0 ? TreeItem.StyleClassEvenRow : TreeItem.StyleClassOddRow);
        if (id == LastEntry)
            button.AddStyleClass(TreeItem.StyleClassSelected);

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(4, 2),
        };
        box.AddChild(new Label { Text = name, ClipText = true });
        if (snippet != null)
        {
            box.AddChild(new Label
            {
                Text = snippet,
                ClipText = true,
                StyleClasses = { StyleBase.StyleClassLabelSubText },
            });
        }

        button.AddChild(box);
        button.OnPressed += _ => WGOpen(id);
        return button;
    }

    /// <summary>Opens a result and moves the highlight to it, leaving the list in place.</summary>
    private void WGOpen(ProtoId<GuideEntryPrototype> id)
    {
        HandleClick(id.Id);
        for (var i = 0; i < _wgShown.Count && i < _wgResults.ChildCount; i++)
        {
            if (_wgResults.GetChild(i) is not ContainerButton row)
                continue;

            if (_wgShown[i] == LastEntry)
                row.AddStyleClass(TreeItem.StyleClassSelected);
            else
                row.RemoveStyleClass(TreeItem.StyleClassSelected);
        }
    }

    /// <summary>A page's text with markup and tags taken out, read once.</summary>
    private string WGPageText(GuideEntry entry)
    {
        if (_wgPageText.TryGetValue(entry.Id, out var cached))
            return cached;

        string raw;
        try
        {
            using var file = _resourceManager.ContentFileReadText(entry.Text);
            raw = file.ReadToEnd();
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Guidebook search couldn't read {entry.Id}: {e.Message}");
            raw = string.Empty;
        }

        var text = new StringBuilder(raw.Length);
        var depth = 0;
        var space = true;
        foreach (var c in raw)
        {
            if (c is '<' or '[')
            {
                depth++;
                continue;
            }

            if (c is '>' or ']')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth > 0 || c == '#')
                continue;

            if (char.IsWhiteSpace(c))
            {
                if (!space)
                    text.Append(' ');
                space = true;
                continue;
            }

            text.Append(c);
            space = false;
        }

        var stripped = text.ToString().Trim();
        _wgPageText[entry.Id] = stripped;
        return stripped;
    }

    /// <summary>A line of the page's text round the first place the search matches.</summary>
    private static string WGSnippet(string text, string query, string[] words)
    {
        var at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        for (var i = 0; at < 0 && i < words.Length; i++)
        {
            at = text.IndexOf(words[i], StringComparison.OrdinalIgnoreCase);
        }

        if (at < 0)
            return string.Empty;

        var start = Math.Max(0, at - WGSnippetBefore);
        var length = Math.Min(WGSnippetLength, text.Length - start);
        var snippet = text.Substring(start, length).Trim();
        if (start > 0)
            snippet = "…" + snippet;
        if (start + length < text.Length)
            snippet += "…";
        return snippet;
    }
}
