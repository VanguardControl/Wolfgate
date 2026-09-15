namespace Content.Shared.Damage.Systems;

/// <summary>Raised after modifiers and before damage is written. Clearing the dict suppresses the write.</summary>
[ByRefEvent]
public record struct DamageDealtEvent(
    DamageSpecifier Damage,
    EntityUid? Origin,
    bool InterruptsDoAfters);
