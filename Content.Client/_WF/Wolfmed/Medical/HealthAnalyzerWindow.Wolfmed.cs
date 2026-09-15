// HOOK 26 body. Kept out of the upstream window so that file carries only the marked lines PLAN4 authorises.
// The partial lives on the window class because the panel and the overview pane are private generated fields a
// standalone control could not reach.

using Content.Shared.MedicalScanner;

namespace Content.Client.HealthAnalyzer.UI;

public sealed partial class HealthAnalyzerWindow
{
    /// <summary>Feeds the Wolfmed detail pane, or collapses it outright for anything that is not a wound host (D2).</summary>
    private void PopulateWolfmed(HealthAnalyzerScannedUserMessage msg)
    {
        if (msg.WoundDiagnostics == null && msg.Organs == null)
        {
            HideWolfmed();
            return;
        }

        WolfmedPanel.Visible = true;
        // The overview pane keeps its natural width while the tabs take the rest of the window.
        WolfmedOverviewPane.HorizontalExpand = false;
        WolfmedPanel.Populate(msg);
    }

    /// <summary>Drops the detail pane and lets the overview fill the window, so no early return leaves stale findings on screen.</summary>
    private void HideWolfmed()
    {
        WolfmedPanel.Clear();
        WolfmedPanel.Visible = false;
        WolfmedOverviewPane.HorizontalExpand = true;
    }
}
