using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingMode.Export;
using Content.Shared._WF.MappingMode.Issues;

namespace Content.Client._WF.MappingMode;

public sealed partial class WFMappingClientSystem
{
    /// <summary>The last issues reply, for the export wizard.</summary>
    public WFMappingIssuesEvent? Issues { get; private set; }

    public event Action<WFMappingIssuesEvent>? IssuesReceived;

    private void InitializeExport()
    {
        SubscribeNetworkEvent<WFMappingIssuesEvent>(OnIssues);
    }

    private void OnIssues(WFMappingIssuesEvent ev)
    {
        Issues = ev;
        IssuesReceived?.Invoke(ev);
    }

    /// <summary>Asks the server to check the session for a scope; the reply comes as <see cref="IssuesReceived"/>.</summary>
    public void RequestIssues(WFMappingIssueScope scope)
    {
        RaiseNetworkEvent(new WFMappingIssuesRequest(scope));
    }

    /// <summary>Moves the observer to an issue's entity, or its coordinates.</summary>
    public void GoToIssue(WFMappingIssue issue)
    {
        if (issue.Entity == null && issue.Where == null)
            return;

        RaiseNetworkEvent(new WFMappingGoToIssueRequest(issue.Entity, issue.Where));
    }

    /// <summary>
    /// Asks for a library version packaged as one zip, as a ship with the given details or as a map; null is the newest
    /// version. The zip arrives through the download path as <c>&lt;Map&gt;.zip</c>.
    /// </summary>
    public void RequestExport(string mapName, string? version, WFMappingExportKind kind, WFMappingShipDetails? details)
    {
        if (RefuseWhileSaving())
            return;

        _requestedShip = false;
        _requestedZip = true;
        RaiseNetworkEvent(new WFMappingExportRequest(mapName, version, kind, details));
    }

    /// <summary>The ship details the library last listed for a map, if any.</summary>
    public WFMappingShipDetails? ShipDetailsOf(string mapName)
    {
        if (Library == null)
            return null;

        foreach (var map in Library.Maps)
        {
            if (string.Equals(map.Name, mapName, StringComparison.OrdinalIgnoreCase))
                return map.Ship;
        }

        return null;
    }

    /// <summary>Whether the library lists a version of a map as a ship; null for the newest version.</summary>
    public bool? IsShipVersion(string mapName, string? version)
    {
        if (Library == null)
            return null;

        foreach (var map in Library.Maps)
        {
            if (!string.Equals(map.Name, mapName, StringComparison.OrdinalIgnoreCase) || map.Versions.Count == 0)
                continue;

            if (version == null)
                return map.Versions[0].Ship;

            foreach (var found in map.Versions)
            {
                if (found.Name.Folder == version)
                    return found.Ship;
            }
        }

        return null;
    }
}
