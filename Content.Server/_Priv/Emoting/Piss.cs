using Content.Server._Priv.Medical;
using Content.Server.Chat.Systems;
using Content.Shared.Emoting;
using Content.Shared._Priv.Emoting;
using Robust.Shared.Timing;

namespace Content.Server._Priv.Emoting;

public sealed partial class PissEmoteSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PissSystem _pissSystem = default!;
    private Dictionary<EntityUid, TimeSpan> _pissCooldown = default!;

    public override void Initialize()
    {
        base.Initialize();
        _pissCooldown = new();

        SubscribeLocalEvent<EmotingComponent, EmoteEvent>(OnEmote);
        SubscribeLocalEvent<EmotingComponent, BeforeEmoteEvent>(OnEmoteEvent);
    }


    private void OnEmote(EntityUid uid, EmotingComponent component, ref EmoteEvent args)
    {
        if (args.Handled)
            return;

        if (args.Emote.ID != "Piss")
            return;

        args.Handled = true;
        _pissSystem.Piss(uid);
    }

    private void OnEmoteEvent(Entity<EmotingComponent> entity, ref BeforeEmoteEvent args)
    {
        var uid = (EntityUid) entity;
        if (args.Emote.ID != "Piss")
            return;

        if (_pissCooldown.ContainsKey(uid) && _pissCooldown[uid] > _timing.CurTime)
        {
            args.Cancel();
            return;
        }

        _pissCooldown[uid] = _timing.CurTime.Add(TimeSpan.FromSeconds(5));
    }
}
