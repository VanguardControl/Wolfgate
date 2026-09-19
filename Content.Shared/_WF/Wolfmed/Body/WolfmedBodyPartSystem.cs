namespace Content.Shared._WF.Wolfmed.Body;

/// <summary>Reads Wolfmed part data off Shitmed body parts, with a no-wounds default.</summary>
public sealed class WolfmedBodyPartSystem : EntitySystem
{
    private static readonly WolfmedBodyPartComponent None = new();

    /// <summary>Wolfmed data for a part; a zeroed default when the part has none.</summary>
    public WolfmedBodyPartComponent Get(EntityUid part) => CompOrNull<WolfmedBodyPartComponent>(part) ?? None;
}
