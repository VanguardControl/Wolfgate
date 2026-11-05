using Content.Shared._WF.Lattice;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._WF.Lattice;

/// <summary>
/// Shows the hallucinations the server sends this player: figures spawned only on this client, radar contacts that
/// aren't there, and a warped screen that grows with Drift.
/// </summary>
public sealed partial class WFLatticeHallucinationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>What a glimpse or phantom looks like.</summary>
    private const string FigurePrototype = "WFRemadeGlimpse";

    /// <summary>A phantom goes when the player comes this close, in metres.</summary>
    private const float VanishRange = 3f;

    private readonly List<(EntityUid Figure, TimeSpan Ends, bool Vanishes)> _figures = new();

    /// <summary>Radar contacts that aren't there: where, until when, and a seed for their garbled label.</summary>
    public readonly List<(MapCoordinates Position, TimeSpan Ends, int Seed)> Blips = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFLatticeHallucinationEvent>(OnHallucination);
        _overlays.AddOverlay(new WFDriftOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay<WFDriftOverlay>();
        Clear();
    }

    /// <summary>The local player's Drift level: their ship's, or their own off a ship.</summary>
    public int LocalLevel()
    {
        if (_player.LocalEntity is not { } mob)
            return 0;

        if (Transform(mob).GridUid is { } grid && TryComp<WFDriftComponent>(grid, out var ship))
            return ship.Level;

        return TryComp<WFDriftComponent>(mob, out var own) ? own.Level : 0;
    }

    private void OnHallucination(WFLatticeHallucinationEvent ev)
    {
        var coordinates = GetCoordinates(ev.Coordinates);
        var ends = _timing.CurTime + TimeSpan.FromSeconds(ev.Seconds);
        switch (ev.Kind)
        {
            case WFLatticeHallucinationKind.Blip:
                Blips.Add((_transform.ToMapCoordinates(coordinates), ends, ev.Seed));
                break;
            case WFLatticeHallucinationKind.Glimpse:
            case WFLatticeHallucinationKind.Phantom:
                if (!coordinates.IsValid(EntityManager))
                    return;

                var figure = Spawn(FigurePrototype, coordinates);
                _figures.Add((figure, ends, ev.Kind == WFLatticeHallucinationKind.Phantom));
                break;
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = _timing.CurTime;
        Blips.RemoveAll(blip => blip.Ends <= now);
        var viewer = _player.LocalEntity;
        for (var i = _figures.Count - 1; i >= 0; i--)
        {
            var (figure, ends, vanishes) = _figures[i];
            var gone = TerminatingOrDeleted(figure);
            var near = vanishes
                       && !gone
                       && viewer is { } mob
                       && _transform.InRange(Transform(figure).Coordinates, Transform(mob).Coordinates, VanishRange);
            if (now < ends && !near && !gone)
                continue;

            if (!gone)
                Del(figure);

            _figures.RemoveAt(i);
        }
    }

    private void Clear()
    {
        foreach (var (figure, _, _) in _figures)
        {
            if (!TerminatingOrDeleted(figure))
                Del(figure);
        }

        _figures.Clear();
        Blips.Clear();
    }
}
