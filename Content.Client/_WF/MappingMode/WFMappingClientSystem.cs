using Content.Client._WF.MappingTools;
using Content.Client._WF.Spawning;
using Content.Client.Actions;
using Content.Client.Gameplay;
using Content.Client.Markers;
using Content.Client.SubFloor;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers.Implementations;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._WF.MappingMode;

/// <summary>The outcome of a mapping request as the player reads it.</summary>
public readonly record struct WFMappingResult(string LocKey, string Text, bool Success);

/// <summary>
/// Client side of mapping mode: the session, library and rejoin state the server sends, the mapping setup while the
/// local entity is a mapping observer, and the download writer.
/// </summary>
public sealed partial class WFMappingClientSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ILightManager _light = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlacementManager _placement = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IStateManager _state = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private ActionsSystem _actions = default!;
    [Dependency] private MappingToolsSystem _tools = default!;
    [Dependency] private MarkerSystem _markers = default!;
    [Dependency] private SubFloorHideSystem _subFloor = default!;

    private const string MappingActions = "/mapping_actions.yml";

    private readonly List<WFMappingRejoinInfo> _rejoinable = new();
    private bool _setUp;

    /// <summary>Whether the server runs in mapping mode.</summary>
    public bool ModeEnabled => _cfg.GetCVar(WFMappingModeCVars.Enabled);

    /// <summary>Whether the local entity is a mapping observer, so the player is mapping in a session.</summary>
    public bool InSession => _player.LocalEntity is { } local && HasComp<WFMappingObserverComponent>(local);

    /// <summary>The player's own session while they are present in it.</summary>
    public WFMappingSessionInfo? Session { get; private set; }

    /// <summary>Sessions the player can go back into from the lobby.</summary>
    public IReadOnlyList<WFMappingRejoinInfo> Rejoinable => _rejoinable;

    /// <summary>Whether the player is an admin observing through mapping mode, whom Return to lobby takes back.</summary>
    public bool AdminObserver { get; private set; }

    /// <summary>The last library listing, or null before the first.</summary>
    public WFMappingLibraryEvent? Library { get; private set; }

    /// <summary>Files the server offers as reference grids.</summary>
    public List<MappingMapFile> ReferenceFiles { get; private set; } = new();

    /// <summary>The outcome of the player's last request.</summary>
    public WFMappingResult? LastResult { get; private set; }

    public event Action? SessionUpdated;
    public event Action? LibraryUpdated;
    public event Action? ReferenceFilesUpdated;
    public event Action<WFMappingResult>? ResultReceived;

    /// <summary>Raised when the in-session setup is undone, after leaving or losing the observer.</summary>
    public event Action? SessionEnded;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFMappingSessionStateEvent>(OnSessionState);
        SubscribeNetworkEvent<WFMappingLibraryEvent>(OnLibrary);
        SubscribeNetworkEvent<WFMappingReferenceListEvent>(OnReferenceList);
        SubscribeNetworkEvent<WFMappingResultEvent>(OnResult);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnDetached);

        InitializeDownload();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Teardown();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // The observer's state and the gameplay state arrive in either order, so setup waits for both.
        var inSession = InSession;
        if (!_setUp && inSession && _state.CurrentState is GameplayState)
            Setup();
        else if (_setUp && !inSession)
            Teardown();
    }

    public void Send(EntityEventArgs ev)
    {
        RaiseNetworkEvent(ev);
    }

    private void OnSessionState(WFMappingSessionStateEvent ev)
    {
        Session = ev.Session;
        AdminObserver = ev.AdminObserver;
        _rejoinable.Clear();
        _rejoinable.AddRange(ev.Rejoinable);
        SessionUpdated?.Invoke();
    }

    private void OnLibrary(WFMappingLibraryEvent ev)
    {
        Library = ev;
        LibraryUpdated?.Invoke();
    }

    private void OnReferenceList(WFMappingReferenceListEvent ev)
    {
        ReferenceFiles = ev.Files;
        ReferenceFilesUpdated?.Invoke();
    }

    private void OnResult(WFMappingResultEvent ev)
    {
        Report(new WFMappingResult(ev.LocKey, Loc.GetString(ev.LocKey, ev.LocArgs()), ev.Success));
    }

    /// <summary>Records an outcome the client works out itself, such as a name it won't send.</summary>
    public void ReportLocal(string locKey, bool success, params (string, object)[] args)
    {
        Report(new WFMappingResult(locKey, Loc.GetString(locKey, args), success));
    }

    /// <summary>Records an outcome for the lobby panel and the Mapping window to show.</summary>
    private void Report(WFMappingResult result)
    {
        LastResult = result;
        ResultReceived?.Invoke(result);
    }

    private void OnDetached(LocalPlayerDetachedEvent ev)
    {
        Teardown();
    }

    /// <summary>What <c>mappingclientsidesetup</c> does, from code: markers, no lighting, subfloor, the mapping bar.</summary>
    private void Setup()
    {
        _setUp = true;
        _markers.MarkersVisible = true;
        _light.Enabled = false;
        _subFloor.ShowAll = true;

        try
        {
            _actions.LoadActionAssignments(MappingActions, false);
        }
        catch (Exception e)
        {
            Log.Error($"Loading {MappingActions} for a mapping session failed: {e}");
        }

        _tools.SetEnabled(true);
        _overlays.AddOverlay(new WFMappingBoundsOverlay(this));
    }

    /// <summary>Undoes <see cref="Setup"/> and closes the spawn and mapping windows.</summary>
    private void Teardown()
    {
        if (!_setUp)
            return;

        _setUp = false;
        _markers.MarkersVisible = false;
        _light.Enabled = true;
        _subFloor.ShowAll = false;
        _tools.SetEnabled(false);
        _placement.Clear();
        _ui.GetUIController<WolfgateSpawnUIController>().CloseWindow();
        _ui.GetUIController<TileSpawningUIController>().CloseWindow();
        _overlays.RemoveOverlay<WFMappingBoundsOverlay>();
        SessionEnded?.Invoke();
    }

    /// <summary>The side of the square every level's tiles must lie in.</summary>
    public int MapSize => Session is { Limits.MapSize: > 0 } session
        ? session.Limits.MapSize
        : _cfg.GetCVar(WFMappingModeCVars.MapSize);
}
