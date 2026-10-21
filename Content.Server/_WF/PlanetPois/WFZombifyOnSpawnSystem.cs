using Content.Server.Zombies;

namespace Content.Server._WF.PlanetPois;

/// <summary>A body that rises as a zombie as soon as it exists, such as a wrecked ship's crew.</summary>
[RegisterComponent]
public sealed partial class WFZombifyOnSpawnComponent : Component;

/// <summary>Turns <see cref="WFZombifyOnSpawnComponent"/> bodies a tick after map init, once their gear is on.</summary>
public sealed class WFZombifyOnSpawnSystem : EntitySystem
{
    [Dependency] private ZombieSystem _zombies = default!;

    private readonly List<EntityUid> _pending = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFZombifyOnSpawnComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<WFZombifyOnSpawnComponent> ent, ref MapInitEvent args)
    {
        _pending.Add(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_pending.Count == 0)
            return;

        foreach (var uid in _pending.ToArray())
        {
            if (TerminatingOrDeleted(uid) || !RemComp<WFZombifyOnSpawnComponent>(uid))
                continue;
            _zombies.ZombifyEntity(uid);
        }
        _pending.Clear();
    }
}
