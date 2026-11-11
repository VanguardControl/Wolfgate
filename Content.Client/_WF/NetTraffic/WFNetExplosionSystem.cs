using Content.Shared._WF.NetTraffic;
using Content.Shared.Explosion;
using Content.Shared.Explosion.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._WF.NetTraffic;

/// <summary>
/// Builds the overlay entity for an explosion the server sent as a message, steps it out one iteration a tick as the
/// server would, and plays the sound this player hears.
/// </summary>
public sealed class WFNetExplosionSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private readonly List<Blast> _blasts = new();

    /// <summary>How many explosion overlays are showing.</summary>
    public int Count => _blasts.Count;

    private sealed class Blast
    {
        public EntityUid Entity;
        public int Iterations;
        public int Progress = 1;
        public TimeSpan NextStep;
        public TimeSpan? Until;
        public float Persistence;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFExplosionEvent>(OnExplosion);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        foreach (var blast in _blasts)
        {
            QueueDel(blast.Entity);
        }

        _blasts.Clear();
    }

    private void OnExplosion(WFExplosionEvent ev)
    {
        PlaySound(ev);

        var entity = Spawn(null, MapCoordinates.Nullspace);
        var appearance = EnsureComp<AppearanceComponent>(entity);
        _appearance.SetData(entity, ExplosionAppearanceData.Progress, 1, appearance);

        var visuals = Factory.GetComponent<ExplosionVisualsComponent>();
        visuals.Epicenter = ev.Epicenter;
        visuals.ExplosionType = ev.ExplosionType;
        visuals.Intensity = ev.Intensity;
        visuals.SpaceTiles = ev.SpaceTiles;
        visuals.SpaceMatrix = ev.SpaceMatrix;
        visuals.SpaceTileSize = ev.SpaceTileSize;
        foreach (var (grid, tiles) in ev.Tiles)
        {
            if (TryGetEntity(grid, out var uid))
                visuals.Tiles[uid.Value] = tiles;
        }

        AddComp(entity, visuals);
        _blasts.Add(new Blast
        {
            Entity = entity,
            Iterations = ev.Intensity.Count,
            NextStep = _timing.RealTime + _timing.TickPeriod,
            Persistence = ev.Persistence,
        });
    }

    private void PlaySound(WFExplosionEvent ev)
    {
        if (ev.Sound is not { } sound)
            return;

        if (ev.FarSound)
        {
            _audio.PlayGlobal(sound, Filter.Local(), false, sound.Params);
            return;
        }

        if (TryGetCoordinates(ev.SoundAt) is { } at)
            _audio.PlayStatic(sound, Filter.Local(), at, false, sound.Params);
    }

    private EntityCoordinates? TryGetCoordinates(NetCoordinates coords)
    {
        return TryGetEntity(coords.NetEntity, out var parent) ? new EntityCoordinates(parent.Value, coords.Position) : null;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var now = _timing.RealTime;
        for (var i = _blasts.Count - 1; i >= 0; i--)
        {
            var blast = _blasts[i];
            if (TerminatingOrDeleted(blast.Entity) || blast.Until is { } until && now >= until)
            {
                QueueDel(blast.Entity);
                _blasts.RemoveAt(i);
                continue;
            }

            if (blast.Until != null || now < blast.NextStep)
                continue;

            blast.NextStep = now + _timing.TickPeriod;
            blast.Progress++;
            if (blast.Progress > blast.Iterations)
            {
                // Finished: the whole area shows, then fades after the server's persistence.
                _appearance.SetData(blast.Entity, ExplosionAppearanceData.Progress, int.MaxValue);
                blast.Until = now + TimeSpan.FromSeconds(blast.Persistence);
                continue;
            }

            _appearance.SetData(blast.Entity, ExplosionAppearanceData.Progress, blast.Progress);
        }
    }
}
