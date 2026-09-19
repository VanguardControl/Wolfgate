using Robust.Shared.Prototypes;

namespace Content.Shared.StatusEffectNew;

/// <summary>RT 277 has no EntitySystem.ProtoMan; this supplies it so the vendored files stay verbatim.</summary>
public sealed partial class StatusEffectsSystem
{
    [Dependency] private IPrototypeManager ProtoMan = default!;
}
