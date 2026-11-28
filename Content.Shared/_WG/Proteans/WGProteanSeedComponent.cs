namespace Content.Shared._WG.Proteans;

/// <summary>
/// The seed, a Protean's one organ. The charge of its <c>BatteryComponent</c> is the Protean's nanomass, so the mass
/// stays with the seed. <see cref="WGProteanSystem"/> cancels EMPs on it, or a pulse would drain the battery in one
/// go; the body takes its own, smaller EMP burn instead.
/// </summary>
[RegisterComponent]
public sealed partial class WGProteanSeedComponent : Component;
