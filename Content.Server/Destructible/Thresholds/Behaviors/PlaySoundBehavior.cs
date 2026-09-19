using Content.Shared.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server.Destructible.Thresholds.Behaviors
{
    [Serializable]
    [DataDefinition]
    public sealed partial class PlaySoundBehavior : IThresholdBehavior
    {
        /// <summary>
        ///     Sound played upon destruction.
        /// </summary>
        [DataField("sound", required: true)] public SoundSpecifier Sound { get; set; } = default!;

        public void Execute(EntityUid owner, DestructibleSystem system, EntityUid? cause = null)
        {
            // WOLFGATE: a hull grinding out a landing destroys every alarm, light and window aboard in the same tick,
            // and one PlayPvs each is one OpenAL source each on every client that can hear them.
            if (!system.WfDestructionSoundAllowed())
                return;

            var pos = system.EntityManager.GetComponent<TransformComponent>(owner).Coordinates;
            system.EntityManager.System<SharedAudioSystem>().PlayPvs(Sound, pos);
        }
    }
}
