#nullable enable
using System.Linq;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Wires;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Cyborgs;

/// <summary>
/// A cyborg takes encryption keys once its lock and panel are open, and they add to its built-in channels.
/// </summary>
[TestFixture]
public sealed class BorgRadioKeysTest
{
    private const string Borg = "BorgChassisGeneric";
    private const string User = "MobHuman";
    private const string Key = "EncryptionKeyMedical";
    private static readonly ProtoId<RadioChannelPrototype> KeyChannel = "Medical";

    [Test]
    public async Task BorgKeysAddChannelsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid borg = default;
        EntityUid user = default;
        EntityUid key = default;
        await server.WaitPost(() =>
        {
            borg = entMan.SpawnEntity(Borg, map.GridCoords);
            user = entMan.SpawnEntity(User, map.GridCoords);
            key = entMan.SpawnEntity(Key, map.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var proto = server.ProtoMan.Index<EntityPrototype>(Borg);
            Assert.That(proto.TryGetComponent<IntrinsicRadioTransmitterComponent>(out var protoTransmitter, entMan.ComponentFactory));
            Assert.That(proto.TryGetComponent<ActiveRadioComponent>(out var protoReceiver, entMan.ComponentFactory));
            var builtInTransmit = protoTransmitter!.Channels.ToHashSet();
            var builtInReceive = protoReceiver!.Channels.ToHashSet();
            Assert.That(builtInTransmit, Is.Not.Empty);

            var transmitter = entMan.GetComponent<IntrinsicRadioTransmitterComponent>(borg);
            var receiver = entMan.GetComponent<ActiveRadioComponent>(borg);
            var holder = entMan.GetComponent<EncryptionKeyHolderComponent>(borg);
            var interaction = entMan.System<SharedInteractionSystem>();
            var coords = entMan.GetComponent<TransformComponent>(borg).Coordinates;

            // The empty key slots leave the built-in channels alone.
            Assert.That(transmitter.Channels, Is.EquivalentTo(builtInTransmit), "The empty key slots wiped the cyborg's channels.");
            Assert.That(receiver.Channels, Is.EquivalentTo(builtInReceive));

            // Locked, the slots refuse a key.
            interaction.InteractUsing(user, key, borg, coords, checkCanInteract: false, checkCanUse: false);
            Assert.That(holder.KeyContainer.Contains(key), Is.False, "A locked cyborg took a key.");

            entMan.System<LockSystem>().Unlock(borg, null);
            Assert.That(entMan.System<SharedWiresSystem>().TogglePanel(borg, entMan.GetComponent<WiresPanelComponent>(borg), true));

            interaction.InteractUsing(user, key, borg, coords, checkCanInteract: false, checkCanUse: false);
            Assert.That(holder.KeyContainer.Contains(key), "An unlocked cyborg with its panel open refused a key.");

            Assert.That(transmitter.Channels, Is.EquivalentTo(builtInTransmit.Append(KeyChannel).ToHashSet()),
                "The key's channel didn't add to the built-in ones.");
            Assert.That(receiver.Channels, Is.EquivalentTo(builtInReceive.Append(KeyChannel).ToHashSet()));

            // Taking the key out returns the cyborg to its built-in channels.
            entMan.System<SharedContainerSystem>().Remove(key, holder.KeyContainer);
            Assert.That(transmitter.Channels, Is.EquivalentTo(builtInTransmit));
            Assert.That(receiver.Channels, Is.EquivalentTo(builtInReceive));
        });

        await pair.CleanReturnAsync();
    }
}
