using Content.Shared._Onyx.Wounds;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;

namespace Content.Client.UserInterface.Systems.DamageOverlays.Overlays;

public sealed partial class DamageOverlay
{
    /// <summary>Overrides the brute vignette with Onyx's pain level on wound hosts.</summary>
    private void TryApplyWolfmedPain()
    {
        // Onyx replaces the brute-damage vignette with a pain vignette
        // (ONYX Content.Shared/DamageOverlay/SharedDamageOverlaySystem.cs:93-97). Wolfgate has no shared damage
        // overlay and RT 277 has no [SubscribeLocalEvent], so this reads the networked PainComponent directly -
        // PainSystem.GetPain is a pure read of AutoNetworkedFields. The system is resolved here, not in a
        // constructor: the overlay is built from DamageOverlayUiController.Initialize(), which can run before
        // entity systems exist.
        if (State == MobState.Dead ||
            _playerManager.LocalEntity is not { } local ||
            !_entityManager.TryGetComponent(local, out PainComponent? pain) ||
            pain.SoftPainCap <= FixedPoint2.Zero)
            return;

        var painLevel = FixedPoint2
            .Min(1f, _entityManager.System<PainSystem>().GetPain((local, pain)) / pain.SoftPainCap)
            .Float();
        BruteLevel = painLevel < 0.05f ? 0f : painLevel;
    }
}
