using Content.Shared.Examine;
using Robust.Shared.Utility;

namespace Content.Shared._WG.Mentors;

/// <summary>The examine line on an on-duty mentor, and the paths to the crown plates.</summary>
public sealed class WGMentorMarkSystem : EntitySystem
{
    /// <summary>The crown plates, one state per tier (<c>trainee</c>, <c>mentor</c>, <c>senior</c>).</summary>
    public static readonly ResPath MarkRsi = new("/Textures/_WG/Mentors/mentor_mark.rsi");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGMentorMarkComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>The crown plate state for a tier.</summary>
    public static string State(MentorTier tier)
    {
        return tier switch
        {
            MentorTier.Senior => "senior",
            MentorTier.Mentor => "mentor",
            _ => "trainee",
        };
    }

    private void OnExamined(Entity<WGMentorMarkComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wg-mentor-examine"), -1);
    }
}
