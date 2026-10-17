using Content.Server._Mono.Radar;
using Content.Shared._Mono.Radar;
using Content.Shared.Mind;
using Content.Shared.Mobs;

namespace Content.Server._WF.CorpseRadar;

/// <summary>Shows a player's body on ship radar from the moment it dies until it is revived or destroyed.</summary>
public sealed class WFCorpseRadarSystem : EntitySystem
{
    [Dependency] private SharedMindSystem _minds = default!;

    /// <summary>A small red diamond, drawn the same at every zoom so it reads as a marker rather than a hull.</summary>
    private static readonly BlipConfig CorpseBlip = new()
    {
        Bounds = new Box2(-0.3f, -0.3f, 0.3f, 0.3f),
        Color = Color.FromHex("#D23C3C"),
        Shape = RadarBlipShape.Diamond,
    };

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        var body = args.Target;
        if (args.NewMobState == MobState.Dead)
        {
            // Only a player's body; another blip already on it (a mapped marker, say) is left as it is.
            if (!_minds.TryGetMind(body, out _, out var mind) || mind.UserId == null || HasComp<RadarBlipComponent>(body))
                return;

            var blip = AddComp<RadarBlipComponent>(body);
            blip.Config = CorpseBlip;
            blip.VisibleFromOtherGrids = true;
            EnsureComp<WFCorpseRadarComponent>(body);
            return;
        }

        if (args.OldMobState == MobState.Dead && RemComp<WFCorpseRadarComponent>(body))
            RemComp<RadarBlipComponent>(body);
    }
}
