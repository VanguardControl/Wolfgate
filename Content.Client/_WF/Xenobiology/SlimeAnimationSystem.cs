using System.Numerics;
using Content.Shared._WF.Xenobiology;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._WF.Xenobiology;

/// <summary>
/// Plays a short lunge towards the target when a slime bites.
/// </summary>
public sealed partial class SlimeAnimationSystem : EntitySystem
{
    [Dependency] private AnimationPlayerSystem _animation = default!;

    private const string BiteKey = "slime-bite";
    private const float Distance = 0.15f;
    private const float Length = 0.15f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<SlimeBiteAnimationMessage>(OnBite);
    }

    private void OnBite(SlimeBiteAnimationMessage args)
    {
        var uid = GetEntity(args.Entity);
        if (!Exists(uid))
            return;

        _animation.Stop(uid, BiteKey);
        _animation.Play(uid, BiteAnimation(args.Angle), BiteKey);
    }

    private static Animation BiteAnimation(Angle angle)
    {
        var end = angle.RotateVec(new Vector2(0f, -Distance));
        return new Animation
        {
            Length = TimeSpan.FromSeconds(Length),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, 0f),
                        new AnimationTrackProperty.KeyFrame(end, 0.05f),
                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, Length - 0.05f),
                    },
                },
            },
        };
    }
}
