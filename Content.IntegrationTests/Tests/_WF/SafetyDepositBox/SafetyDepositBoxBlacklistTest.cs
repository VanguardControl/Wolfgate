#nullable enable
using System.Linq;
using Content.Shared._WF.SafetyDepositBox.Components;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.SafetyDepositBox;

/// <summary>
/// Every component a safety deposit box blacklists exists, so no entry is silently skipped, and a box refuses a PDA.
/// </summary>
[TestFixture]
[TestOf(typeof(SafetyDepositBoxComponent))]
public sealed class SafetyDepositBoxBlacklistTest
{
    private const string PdaProto = "PassengerPDA";

    [Test]
    public async Task BlacklistNamesRealComponents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var factory = entMan.ComponentFactory;
        var storageSys = entMan.System<SharedStorageSystem>();

        await server.WaitAssertion(() =>
        {
            var boxes = server.ProtoMan.EnumeratePrototypes<EntityPrototype>()
                .Where(proto => !proto.Abstract && proto.TryGetComponent<SafetyDepositBoxComponent>(out _, factory))
                .ToList();
            Assert.That(boxes, Is.Not.Empty, "No safety deposit box prototypes were found.");

            Assert.Multiple(() =>
            {
                foreach (var proto in boxes)
                {
                    Assert.That(proto.TryGetComponent<StorageComponent>(out var storage, factory), Is.True,
                        $"{proto.ID} has no storage.");

                    foreach (var name in storage!.Blacklist?.Components ?? Array.Empty<string>())
                    {
                        Assert.That(factory.GetComponentAvailability(name), Is.Not.EqualTo(ComponentAvailability.Unknown),
                            $"{proto.ID} blacklists '{name}', which isn't a component, so the entry does nothing.");
                    }

                    var box = entMan.SpawnEntity(proto.ID, map.GridCoords);
                    var pda = entMan.SpawnEntity(PdaProto, map.GridCoords);
                    Assert.That(storageSys.CanInsert(box, pda, out _), Is.False, $"{proto.ID} takes a PDA.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
