using Content.Server._WF.ChatFont;
using Content.Shared._Shitmed.Humanoid.Events;
using Content.Shared._WF.Traits;

namespace Content.Server._WF.Clown;

/// <summary>Keeps a clown's speech in the trait's font, whatever font their profile picks.</summary>
public sealed partial class ClownFontSystem : EntitySystem
{
    [Dependency] private WFChatFontSystem _chatFont = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClownComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ClownComponent, ProfileLoadFinishedEvent>(OnProfileLoaded, after: [typeof(WFChatFontSystem)]);
    }

    private void OnStartup(Entity<ClownComponent> ent, ref ComponentStartup args)
        => _chatFont.SetFont(ent, ent.Comp.Font);

    private void OnProfileLoaded(Entity<ClownComponent> ent, ref ProfileLoadFinishedEvent args)
        => _chatFont.SetFont(ent, ent.Comp.Font);
}
