using System.Diagnostics.CodeAnalysis;
using System.Text;
using Content.Client.Guidebook;
using Content.Client.Guidebook.Richtext;
using Content.Shared._WG.Wilderness;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;

namespace Content.Client._WG.Wilderness;

/// <summary>
/// The guidebook's <c>&lt;WGWildernessRule/&gt;</c>: who may fight in a Wilderness, and how the mark works, from the
/// server's settings. The lines go through the guidebook's own parser, so they show as its indented bullets.
/// </summary>
[UsedImplicitly]
public sealed class WGWildernessRule : BoxContainer, IDocumentTag
{
    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;

        var cfg = IoCManager.Resolve<IConfigurationManager>();
        var minutes = cfg.GetCVar(WildernessCVars.SkullMinutes);
        var markup = new StringBuilder()
            .Append(Loc.GetString("wg-wilderness-rule-who")).Append('\n')
            .Append(Loc.GetString("wg-wilderness-rule-mark",
                ("seconds", cfg.GetCVar(WildernessCVars.DwellSeconds)),
                ("minutes", minutes),
                ("onfoot", cfg.GetCVar(WildernessCVars.SkullOnFoot) ? "yes" : "no"))).Append('\n')
            .Append(Loc.GetString("wg-wilderness-rule-pilot")).Append('\n')
            .Append(Loc.GetString("wg-wilderness-rule-attack", ("minutes", minutes))).Append('\n')
            .Append(Loc.GetString("wg-wilderness-rule-standing")).Append('\n')
            .Append(Loc.GetString("wg-wilderness-rule-permanent"));

        IoCManager.Resolve<DocumentParsingManager>().TryAddMarkup(this, markup.ToString());
        control = this;
        return true;
    }
}
