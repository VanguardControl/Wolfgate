#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.Biotype;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Biotype;

/// <summary>Traits, breather loadouts and the zombie roles follow the effective biotype, not the species id.</summary>
[TestFixture]
[TestOf(typeof(WFBiotypeGates))]
[TestOf(typeof(WFBiotypeLoadoutEffect))]
[TestOf(typeof(WFBiotypeRequirement))]
public sealed class BiotypeGatesTest
{
    private static HumanoidCharacterProfile Profile(string species, WFBiotype biotype) =>
        HumanoidCharacterProfile.DefaultWithSpecies(species).WithBiotype(biotype);

    /// <summary>No trait still keys its body on a machine species id, and a biotype list never holds Default.</summary>
    [Test]
    public async Task TraitListsSweepTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                var machines = new ProtoId<SpeciesPrototype>[] { "IPC", "Synth" };

                foreach (var trait in proto.EnumeratePrototypes<TraitPrototype>())
                {
                    foreach (var species in machines)
                    {
                        Assert.That(trait.SpeciesBlacklist, Does.Not.Contain(species),
                            $"{trait.ID} lists {species} in speciesBlacklist; use biotypeBlacklist.");
                    }

                    Assert.That(trait.BiotypeBlacklist, Does.Not.Contain(WFBiotype.Default),
                        $"{trait.ID} lists Default in biotypeBlacklist.");
                }

                var listed = proto.EnumeratePrototypes<TraitPrototype>().Where(t => t.BiotypeBlacklist.Count > 0).ToList();
                Assert.That(listed, Has.Count.GreaterThanOrEqualTo(14), "the machine-only traits lost their biotypeBlacklist");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TraitFollowsEffectiveBiotypeTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var hemophilia = proto.Index<TraitPrototype>("Hemophilia");
            var voracious = proto.Index<TraitPrototype>("Voracious");

            Assert.Multiple(() =>
            {
                Assert.That(WFBiotypeGates.TraitBlocked(hemophilia, Profile("Human", WFBiotype.Synthetic), proto), Is.True,
                    "a synthetic Human is blocked");
                Assert.That(WFBiotypeGates.TraitBlocked(hemophilia, Profile("IPC", WFBiotype.Default), proto), Is.True,
                    "a native IPC is blocked");
                Assert.That(WFBiotypeGates.TraitBlocked(hemophilia, Profile("IPC", WFBiotype.Organic), proto), Is.False,
                    "an organic IPC is allowed");
                Assert.That(WFBiotypeGates.TraitBlocked(hemophilia, Profile("Human", WFBiotype.Default), proto), Is.False);
                Assert.That(WFBiotypeGates.TraitBlocked(voracious, Profile("Human", WFBiotype.Synthetic), proto), Is.True);

                // The server drops a blocked trait when it validates a profile's list.
                var traits = new List<ProtoId<TraitPrototype>> { hemophilia.ID, voracious.ID };
                Assert.That(Profile("Human", WFBiotype.Synthetic).GetValidTraits(traits, proto), Is.Empty);
                Assert.That(Profile("IPC", WFBiotype.Organic).GetValidTraits(traits, proto), Has.Count.EqualTo(2));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The breather boxes are issued to organic bodies only, whatever the species.</summary>
    [Test]
    public async Task BreatherLoadoutsFollowBiotypeTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var collection = server.ResolveDependency<IDependencyCollection>();

        await server.WaitAssertion(() =>
        {
            var oxygen = Breathers(proto, "OxygenBreatherNF");
            var nitrogen = Breathers(proto, "NitrogenBreatherNF");
            Assert.That(oxygen, Is.Not.Empty);
            Assert.That(nitrogen, Is.Not.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(Issued(proto, Profile("Human", WFBiotype.Synthetic), oxygen), Is.Empty,
                    "a synthetic Human is issued an oxygen box");
                Assert.That(Issued(proto, Profile("IPC", WFBiotype.Default), oxygen), Is.Empty,
                    "a native IPC is issued an oxygen box");
                Assert.That(Issued(proto, Profile("IPC", WFBiotype.Organic), oxygen), Does.Contain("EmergencyOxygen"),
                    "an organic IPC is issued no oxygen box");
                Assert.That(Issued(proto, Profile("Human", WFBiotype.Default), oxygen), Does.Contain("EmergencyOxygen"));

                Assert.That(Issued(proto, Profile("Vox", WFBiotype.Synthetic), nitrogen), Is.Empty,
                    "a synthetic Vox is issued a nitrogen box");
                Assert.That(Issued(proto, Profile("Vox", WFBiotype.Default), nitrogen), Does.Contain("EmergencyNitrogen"));
                Assert.That(Issued(proto, Profile("IPC", WFBiotype.Organic), nitrogen), Is.Empty,
                    "an organic IPC breathes oxygen");

                // The effect itself, not only the defaults: nothing behind a breather group validates for a synthetic body.
                var loadout = new RoleLoadout("JobContractor");
                foreach (var id in oxygen.Concat(nitrogen))
                {
                    Assert.That(loadout.IsValid(Profile("Human", WFBiotype.Synthetic), null, id, collection, out _), Is.False, $"{id}");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Loadouts that sit behind the given breather effect group.</summary>
    private static HashSet<ProtoId<LoadoutPrototype>> Breathers(IPrototypeManager proto, string group)
    {
        return proto.EnumeratePrototypes<LoadoutPrototype>()
            .Where(l => l.Effects.OfType<GroupLoadoutEffect>().Any(e => e.Proto == group))
            .Select(l => (ProtoId<LoadoutPrototype>) l.ID)
            .ToHashSet();
    }

    /// <summary>The breather loadouts that role defaults hand the profile, over every role.</summary>
    private static HashSet<string> Issued(IPrototypeManager proto, HumanoidCharacterProfile profile, HashSet<ProtoId<LoadoutPrototype>> breathers)
    {
        var issued = new HashSet<string>();

        foreach (var role in proto.EnumeratePrototypes<RoleLoadoutPrototype>())
        {
            var loadout = new RoleLoadout(role.ID);
            loadout.SetDefault(profile, null, proto);

            foreach (var pick in loadout.SelectedLoadouts.Values.SelectMany(l => l))
            {
                if (breathers.Contains(pick.Prototype))
                    issued.Add(pick.Prototype);
            }
        }

        return issued;
    }

    [Test]
    public async Task ZombieRequirementFollowsBiotypeTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var entMan = server.ResolveDependency<IEntityManager>();
        var roles = entMan.System<SharedRoleSystem>();

        await server.WaitAssertion(() =>
        {
            var empty = new Dictionary<string, TimeSpan>();

            Assert.Multiple(() =>
            {
                foreach (var id in new[] { "InitialInfected", "Zombie" })
                {
                    var requirements = roles.GetAntagRequirement(id) ?? new HashSet<JobRequirement>();
                    Assert.That(requirements, Is.Not.Empty, id);
                    Assert.That(requirements.OfType<WFBiotypeRequirement>().Count(), Is.EqualTo(1),
                        $"{id} needs the biotype requirement");

                    bool Met(HumanoidCharacterProfile? profile) =>
                        requirements.All(r => r.Check(entMan, proto, profile, empty, out _));

                    Assert.That(Met(Profile("IPC", WFBiotype.Organic)), Is.True, $"{id}: organic IPC");
                    Assert.That(Met(Profile("Human", WFBiotype.Default)), Is.True, $"{id}: Human");
                    Assert.That(Met(Profile("Human", WFBiotype.Synthetic)), Is.False, $"{id}: synthetic Human");
                    Assert.That(Met(Profile("IPC", WFBiotype.Default)), Is.False, $"{id}: IPC");
                    Assert.That(Met(null), Is.True, $"{id}: no profile");
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
