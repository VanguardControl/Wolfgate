using Robust.Shared.Serialization;

namespace Content.Shared._WF.AppearanceStudio;

/// <summary>What part of a character a tweak moves.</summary>
public enum StudioTarget : byte
{
    /// <summary>A body part layer, keyed by its HumanoidVisualLayers name. Its markings move with it.</summary>
    Body,

    /// <summary>The sprites of one marking, keyed by marking id.</summary>
    Marking,

    /// <summary>What is worn in an inventory slot, keyed by slot name.</summary>
    Worn,

    /// <summary>What is held in a hand, keyed by HandLocation name.</summary>
    Held,
}

/// <summary>
/// One part of a character placed by the player in the Appearance Studio: moved per facing, scaled, hidden or warped.
/// </summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class StudioTweak : IEquatable<StudioTweak>
{
    [DataField]
    public StudioTarget Target;

    [DataField]
    public string Key = string.Empty;

    /// <summary>Pixel offset for each facing, south, north, east and west; +Y is up on screen.</summary>
    [DataField]
    public Vector2i[] Offsets = new Vector2i[StudioRules.Facings];

    [DataField]
    public float Scale = 1f;

    /// <summary>Not drawn at all. Body parts and markings only.</summary>
    [DataField]
    public bool Hidden;

    /// <summary>Hash of a displacement map the player painted (custom marking art). Worn and held only.</summary>
    [DataField]
    public string? Warp;

    public StudioTweak()
    {
    }

    public StudioTweak(StudioTarget target, string key)
    {
        Target = target;
        Key = key;
    }

    public StudioTweak Clone()
    {
        return new StudioTweak(Target, Key)
        {
            Offsets = Offsets == null ? new Vector2i[StudioRules.Facings] : (Vector2i[]) Offsets.Clone(),
            Scale = Scale,
            Hidden = Hidden,
            Warp = Warp,
        };
    }

    public Vector2i GetOffset(int facing)
    {
        return Offsets != null && facing >= 0 && facing < Offsets.Length ? Offsets[facing] : Vector2i.Zero;
    }

    /// <summary>Whether this tweak changes nothing.</summary>
    public bool IsEmpty()
    {
        foreach (var offset in Offsets ?? Array.Empty<Vector2i>())
        {
            if (offset != Vector2i.Zero)
                return false;
        }

        return MathHelper.CloseTo(Scale, 1f) && !Hidden && Warp == null;
    }

    public bool Same(StudioTarget target, string key)
    {
        return Target == target && Key == key;
    }

    public bool Equals(StudioTweak? other)
    {
        if (other is null)
            return false;

        return Target == other.Target
               && Key == other.Key
               && Offsets.AsSpan().SequenceEqual(other.Offsets)
               && Scale.Equals(other.Scale)
               && Hidden == other.Hidden
               && Warp == other.Warp;
    }

    public override bool Equals(object? obj)
    {
        return obj is StudioTweak other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Target, Key, Scale, Hidden, Warp);
    }
}
