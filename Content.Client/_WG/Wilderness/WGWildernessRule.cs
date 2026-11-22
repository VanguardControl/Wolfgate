using System.Diagnostics.CodeAnalysis;
using Content.Client.Guidebook.Richtext;
using Content.Shared._WG.Wilderness;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Utility;

namespace Content.Client._WG.Wilderness;

/// <summary>
/// The guidebook's <c>&lt;WGWildernessRule/&gt;</c>: who may fight in a Wilderness, and how the mark works, from the
/// server's settings, so the rule says what the server does.
/// </summary>
[UsedImplicitly]
public sealed class WGWildernessRule : RichTextLabel, IDocumentTag
{
    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        var cfg = IoCManager.Resolve<IConfigurationManager>();
        var message = new FormattedMessage();
        message.AddMarkupPermissive(Loc.GetString("wg-wilderness-rule-who",
            ("open", cfg.GetCVar(WildernessCVars.OpenToAll) ? "yes" : "no")));
        message.PushNewline();
        message.AddMarkupPermissive(Loc.GetString("wg-wilderness-rule-mark",
            ("seconds", cfg.GetCVar(WildernessCVars.DwellSeconds)),
            ("minutes", cfg.GetCVar(WildernessCVars.SkullMinutes)),
            ("onfoot", cfg.GetCVar(WildernessCVars.SkullOnFoot) ? "yes" : "no")));
        message.PushNewline();
        message.AddMarkupPermissive(Loc.GetString("wg-wilderness-rule-pilot"));
        message.PushNewline();
        message.AddMarkupPermissive(Loc.GetString("wg-wilderness-rule-attack",
            ("minutes", cfg.GetCVar(WildernessCVars.SkullMinutes))));
        message.PushNewline();
        message.AddMarkupPermissive(Loc.GetString("wg-wilderness-rule-standing"));
        SetMessage(message);
        control = this;
        return true;
    }
}
