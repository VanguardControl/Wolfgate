#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests._WG.AdminObserver;
using Content.Server._EinsteinEngines.Language;
using Content.Server.Administration.Commands;
using Content.Server.Atmos.Components;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._WG.AdminObserver;
using Content.Shared._WG.AdminTech;
using Content.Shared.Antag;
using Content.Shared.Damage.Components;
using Content.Shared.Interaction.Components;
using Content.Shared.Inventory;
using Content.Shared.Overlays;
using Content.Shared.Roles;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.AdminTech;

/// <summary>The technician's gear: it all exists, the outfits dress a body, and each piece's power comes and goes with it.</summary>
[TestFixture]
[TestOf(typeof(WGAdminGearSystem))]
public sealed class AdminTechGearTest
{
    private static readonly string[] Outfits = { "WGAdminTechBluespace", "WGAdminTechSubspace" };

    [Test]
    public async Task EverythingSpawnsAndBothOutfitsDress()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var inventory = server.System<InventorySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var ids = server.ProtoMan.EnumeratePrototypes<EntityPrototype>()
                .Where(p => p.ID.StartsWith("WGAdminTech") && !p.Abstract)
                .Select(p => p.ID)
                .ToList();
            Assert.That(ids, Has.Count.GreaterThan(40));
            foreach (var id in ids)
            {
                Assert.That(entMan.EntityExists(entMan.SpawnEntity(id, map.GridCoords)), id);
            }

            foreach (var outfit in Outfits)
            {
                var gear = server.ProtoMan.Index<StartingGearPrototype>(outfit);
                var body = entMan.SpawnEntity("MobHuman", map.GridCoords);
                Assert.That(SetOutfitCommand.SetOutfit(body, outfit, entMan));
                Assert.Multiple(() =>
                {
                    foreach (var (slot, item) in gear.Equipment)
                    {
                        Assert.That(inventory.TryGetSlotEntity(body, slot, out var worn), $"{outfit}: nothing in {slot}.");
                        Assert.That(entMan.GetComponent<MetaDataComponent>(worn!.Value).EntityPrototype?.ID, Is.EqualTo(item.Id));
                    }
                });
            }
        });

        // The client gets every one of them; a missing sprite state would be logged as an error and fail this.
        await pair.RunTicksSync(30);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EachPowerComesAndGoesWithItsPiece()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var inventory = server.System<InventorySystem>();
        var tags = server.System<TagSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(SetOutfitCommand.SetOutfit(body, Outfits[0], entMan));

            // slot -> what wearing it gives.
            var powers = new (string Slot, Func<bool> Has)[]
            {
                ("gloves", () => entMan.HasComponent<BypassInteractionChecksComponent>(body)
                                 && entMan.HasComponent<IgnoreUIRangeComponent>(body)
                                 && tags.HasTag(body, "BypassInteractionRangeChecks")
                                 && tags.HasTag(body, "InstantDoAfters")),
                ("neck", () => entMan.HasComponent<GodmodeComponent>(body)),
                ("mask", () => entMan.HasComponent<BreathingImmunityComponent>(body)
                               && entMan.HasComponent<PressureImmunityComponent>(body)),
                ("head", () => entMan.HasComponent<UniversalLanguageSpeakerComponent>(body)
                               && entMan.HasComponent<ShowAntagIconsComponent>(body)
                               && entMan.HasComponent<ShowSyndicateIconsComponent>(body)),
                ("jumpsuit", () => entMan.HasComponent<WGStaffMarkerComponent>(body)),
            };

            Assert.Multiple(() =>
            {
                foreach (var (slot, has) in powers)
                {
                    Assert.That(has(), $"Wearing the {slot} piece should give its power.");
                }
            });
            Assert.That(entMan.GetComponent<WGStaffMarkerComponent>(body).Label, Is.EqualTo("Staff"), "A body nobody is in is plain Staff.");

            // The uniform comes off last: taking it off drops what is worn over it.
            foreach (var (slot, has) in powers)
            {
                Assert.That(inventory.TryUnequip(body, slot, force: true), $"Could not take off the {slot} piece.");
                Assert.That(has(), Is.False, $"Taking off the {slot} piece should take its power away.");
            }

            // A power the body had of its own is not taken away with the piece.
            entMan.AddComponent<GodmodeComponent>(body);
            var projector = entMan.SpawnEntity("WGAdminTechShieldProjectorBluespace", map.GridCoords);
            Assert.That(inventory.TryEquip(body, projector, "neck", force: true));
            Assert.That(inventory.TryUnequip(body, "neck", force: true));
            Assert.That(entMan.HasComponent<GodmodeComponent>(body));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OnlyTheWearerOrAnAdminTakesItOff()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var inventory = server.System<InventorySystem>();
        var map = await pair.CreateTestMap();
        await AdminKitFixture.Admin(pair);
        var admin = await AdminKitFixture.Living(pair, map.GridCoords);

        await server.WaitAssertion(() =>
        {
            var wearer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var stranger = entMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(SetOutfitCommand.SetOutfit(wearer, Outfits[1], entMan));

            Assert.Multiple(() =>
            {
                Assert.That(inventory.CanUnequip(stranger, wearer, "gloves", out var reason), Is.False, "A player cannot strip staff gear.");
                Assert.That(reason, Is.EqualTo("wg-admin-gear-strip-blocked"));
                Assert.That(inventory.CanUnequip(wearer, wearer, "gloves", out _), "The wearer takes their own gear off.");
                Assert.That(inventory.CanUnequip(admin, wearer, "gloves", out _), "An admin can take it off them.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
