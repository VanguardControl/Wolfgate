using Content.Shared.Administration.Logs;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Administration.UI.CustomControls;

public sealed class AdminLogLabel : RichTextLabel
{
    public AdminLogLabel(ref SharedAdminLog log, HSeparator separator)
    {
        Log = log;
        Separator = separator;

        // WOLFGATE(LogInvestigator) START: each line names its type and is coloured by impact
        var wgMessage = new Robust.Shared.Utility.FormattedMessage();
        wgMessage.PushColor(Content.Client._WG.LogInvestigator.LogInvestigatorStyle.ImpactColor(log.Impact));
        wgMessage.AddText($"{log.Date:HH:mm:ss} [{log.Type}] ");
        wgMessage.Pop();
        wgMessage.AddText(log.Message);
        SetMessage(wgMessage);
        // SetMessage($"{log.Date:HH:mm:ss}: {log.Message}");
        // WOLFGATE END
        OnVisibilityChanged += VisibilityChanged;
    }

    public SharedAdminLog Log { get; }

    public HSeparator Separator { get; }

    private void VisibilityChanged(Control control)
    {
        Separator.Visible = Visible;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        OnVisibilityChanged -= VisibilityChanged;
    }
}
