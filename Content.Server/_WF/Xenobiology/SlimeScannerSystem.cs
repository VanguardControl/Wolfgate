using Content.Server.Chat.Managers;
using Content.Shared._WF.Xenobiology;
using Content.Shared.Chat;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Server._WF.Xenobiology;

/// <summary>
/// Reports a slime's name, hunger and mutation chance to the scanning player's chat.
/// </summary>
public sealed partial class SlimeScannerSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private HungerSystem _hunger = default!;

    private static readonly SoundSpecifier ScanSound = new SoundPathSpecifier("/Audio/Items/Medical/healthscanner.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlimeScannerComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnAfterInteract(Entity<SlimeScannerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || !TryComp<SlimeComponent>(args.Target, out var slime))
            return;

        args.Handled = true;
        if (SendInfo(args.User, (args.Target.Value, slime)))
            _audio.PlayEntity(ScanSound, Filter.Entities(args.User), ent, true);
    }

    /// <summary>
    /// Sends the slime's readout to the user's chat.
    /// </summary>
    public bool SendInfo(EntityUid user, Entity<SlimeComponent> slime)
    {
        if (!TryComp<ActorComponent>(user, out var actor))
            return false;

        var nutrition = TryComp<HungerComponent>(slime, out var hunger)
            ? FixedPoint2.New(_hunger.GetHunger(hunger))
            : FixedPoint2.Zero;
        var message = Loc.GetString("slime-scanner-info",
            ("name", Name(slime)), ("nutrition", nutrition), ("chance", slime.Comp.MutationChance * 100));
        _chat.ChatMessageToOne(ChatChannel.Local, message, message, EntityUid.Invalid, false, actor.PlayerSession.Channel);
        return true;
    }
}
