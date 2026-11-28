using Content.Shared._WG.Mentors;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.Mentors;

/// <summary>Draws the tier's crown plate over an on-duty mentor's head, unless they turned it off.</summary>
public sealed class WGMentorIconSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    private static readonly ProtoId<SsdIconPrototype> Trainee = "WGMentorIconTrainee";
    private static readonly ProtoId<SsdIconPrototype> Mentor = "WGMentorIconMentor";
    private static readonly ProtoId<SsdIconPrototype> Senior = "WGMentorIconSenior";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGMentorMarkComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(Entity<WGMentorMarkComponent> ent, ref GetStatusIconsEvent args)
    {
        if (!ent.Comp.ShowCrown || _mobState.IsDead(ent))
            return;

        var id = ent.Comp.Tier switch
        {
            MentorTier.Senior => Senior,
            MentorTier.Mentor => Mentor,
            _ => Trainee,
        };

        if (_prototypes.TryIndex(id, out var icon))
            args.StatusIcons.Add(icon);
    }
}
