using Content.Shared.Examine;

namespace Content.Shared._WF.Xenobiology;

/// <summary>
/// Shows whether a slime extract still reacts.
/// </summary>
public sealed partial class SlimeExtractSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlimeExtractComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<SlimeExtractComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString(ent.Comp.RemainingUses <= 0
            ? "slime-extract-exhausted"
            : "slime-extract-not-exhausted"));
    }
}
