using System.Linq;
using Content.Server.Zombies;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.PlanetPois;

/// <summary>A body that rises as a zombie as soon as it exists, such as a wrecked ship's crew.</summary>
[RegisterComponent]
public sealed partial class WFZombifyOnSpawnComponent : Component
{
    /// <summary>
    /// The sides it takes once risen, in place of the zombies': the wildlife it shares its wreck or tunnel with, so the
    /// two hunt the living rather than each other.
    /// </summary>
    [DataField]
    public List<ProtoId<NpcFactionPrototype>> Factions = new() { "SimpleHostile" };
}

/// <summary>Turns <see cref="WFZombifyOnSpawnComponent"/> bodies a tick after map init, once their gear is on.</summary>
public sealed class WFZombifyOnSpawnSystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _factions = default!;
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
            if (TerminatingOrDeleted(uid) || !TryComp(uid, out WFZombifyOnSpawnComponent? comp))
                continue;
            var factions = comp.Factions;
            RemComp(uid, comp);
            _zombies.ZombifyEntity(uid);
            _factions.ClearFactions(uid, dirty: false);
            _factions.AddFactions(uid, factions.ToHashSet());
        }
        _pending.Clear();
    }
}
