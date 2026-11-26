using Content.Client.Eui;
using Content.Shared._WG.LogInvestigator;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._WG.LogInvestigator;

/// <summary>Client end of the log investigator: relays the window's requests and the server's results.</summary>
[UsedImplicitly]
public sealed class LogInvestigatorEui : BaseEui
{
    private readonly LogInvestigatorWindow _window;

    public LogInvestigatorEui()
    {
        _window = new LogInvestigatorWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.QueryRequested += (query, after) => SendMessage(new LogInvestigatorQueryMsg(query, after));
        _window.RoundRequested += round => SendMessage(new LogInvestigatorRoundMsg(round));
        _window.ReportRequested += (kind, query, point, radius) => SendMessage(new LogInvestigatorReportRequest(kind, query, point, radius));
    }

    public override void Opened()
    {
        base.Opened();
        _window.OpenCentered();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is LogInvestigatorEuiState investigator)
            _window.SetState(investigator);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        switch (msg)
        {
            case LogInvestigatorPageMsg page:
                _window.AddPage(page);
                break;
            case LogInvestigatorPrefillMsg prefill:
                _window.Prefill(prefill.Query, prefill.Report);
                break;
            case LogInvestigatorReportMsg report:
                _window.ShowReport(report.Report);
                break;
        }
    }

    public override void Closed()
    {
        base.Closed();
        _window.Close();
        _window.Dispose();
    }
}
