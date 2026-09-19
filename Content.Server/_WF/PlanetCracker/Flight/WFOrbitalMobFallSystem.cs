using System.Linq;
using Content.Server.Chat.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Content.Server._WF.PlanetCracker.Planets;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._CE.ZLevels.Damage;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._WF.PlanetCracker.Planets;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Random;

namespace Content.Server._WF.PlanetCracker.Flight;

/// <summary>Unprotected orbital falls sever one arm and one leg on surface impact.</summary>
public sealed partial class WFOrbitalMobFallSystem : EntitySystem
{
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    private static readonly SoundSpecifier Splat = new SoundPathSpecifier("/Audio/Effects/gib1.ogg");

    public override void Initialize()
    {
        SubscribeLocalEvent<MobStateComponent, CEZLevelFallMapEvent>(OnFall);
        SubscribeLocalEvent<WFOrbitalMobFallComponent, CEZFallingDamageCalculateEvent>(OnCalculate);
        SubscribeLocalEvent<WFOrbitalMobFallComponent, CEZLevelHitEvent>(OnHit, after: new[] { typeof(CEZLevelDamageSystem) });
    }

    private void OnFall(Entity<MobStateComponent> ent, ref CEZLevelFallMapEvent args)
    {
        if (ent.Comp.CurrentState == MobState.Dead || HasComp<WFOrbitalMobFallComponent>(ent))
            return;
        var map = Transform(ent).MapUid;
        if (map == null || !_zLevels.TryMapUp(map.Value, out var above)
            || !TryComp<WFOrbitLayerComponent>(above, out var orbit)
            || orbit.Network is not { } net || !TryGetEntity(net, out var network)
            || !TryComp<WFPlanetNetworkComponent>(network, out var planet))
            return;
        EnsureComp<WFOrbitalMobFallComponent>(ent).Ground = planet.GroundMap;
        _chat.TryEmoteWithChat(ent, "Scream");
    }

    private bool IsSurfaceImpact(EntityUid uid, WFOrbitalMobFallComponent fall)
    {
        return Transform(uid).MapUid == fall.Ground
            && TryComp<CEZPhysicsComponent>(uid, out var physics) && physics.Velocity < 0;
    }

    private void OnCalculate(Entity<WFOrbitalMobFallComponent> ent, ref CEZFallingDamageCalculateEvent args)
    {
        // Replace the lethal accumulated fall damage, rather than resurrecting a corpse afterwards.
        if (args.Fallen == ent.Owner && IsSurfaceImpact(ent, ent.Comp))
            args.DamageMultiplier = 0;
    }

    private void OnHit(Entity<WFOrbitalMobFallComponent> ent, ref CEZLevelHitEvent args)
    {
        var surface = IsSurfaceImpact(ent, ent.Comp);
        RemComp<WFOrbitalMobFallComponent>(ent);
        if (!surface || !TryComp<MobStateComponent>(ent, out var mob) || mob.CurrentState == MobState.Dead
            || !TryComp<DamageableComponent>(ent, out var damageable))
            return;

        _audio.PlayPvs(Splat, ent);
        SeverOne(ent, BodyPartType.Arm);
        SeverOne(ent, BodyPartType.Leg);
        if (_thresholds.TryGetThresholdForState(ent, MobState.Critical, out var critical))
        {
            var missing = critical.Value + 1 - damageable.TotalDamage;
            if (missing > 0)
            {
                var damage = new DamageSpecifier(damageable.Damage);
                damage.DamageDict.TryGetValue("Blunt", out var blunt);
                damage.DamageDict["Blunt"] = blunt + missing;
                _damage.SetDamage(ent, damageable, damage);
            }
        }
    }

    private void SeverOne(EntityUid uid, BodyPartType type)
    {
        var parts = _body.GetBodyChildrenOfType(uid, type).Where(p => p.Component.CanSever).ToArray();
        if (parts.Length == 0)
            return;
        var part = _random.Pick(parts).Id;
        var amputate = new AmputateAttemptEvent(part);
        RaiseLocalEvent(part, ref amputate);
    }
}

[RegisterComponent, UnsavedComponent]
public sealed partial class WFOrbitalMobFallComponent : Component
{
    public EntityUid Ground;
}
