using System.Collections.Generic;
using System.Linq;
using Content.Client.Humanoid;
using Content.Server.Database;
using Content.Shared._WF.MismatchedParts;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Client.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.MismatchedParts;

/// <summary>
/// Mismatched parts: the hair and facial hair each playable species may pick with the option off and on, validation,
/// the saved column, random profiles, and the bodies and creator dolls of every playable species.
/// </summary>
[TestFixture]
[TestOf(typeof(MismatchedPartsRules))]
public sealed class MismatchedPartsTest
{
    private static readonly MarkingCategories[] Categories = { MarkingCategories.Hair, MarkingCategories.FacialHair };

    private static readonly Color HairColor = Color.FromHex("#3355FF");
    private static readonly Color BeardColor = Color.FromHex("#FF5533");

    private static List<SpeciesPrototype> Playable(IPrototypeManager proto)
    {
        return proto.EnumeratePrototypes<SpeciesPrototype>().Where(s => s.RoundStart).OrderBy(s => s.ID).ToList();
    }

    private static Sex SexFor(SpeciesPrototype species)
    {
        return species.Sexes.Contains(Sex.Male) ? Sex.Male : species.Sexes.First();
    }

    /// <summary>A profile wearing the first style of each category the option allows, with the option set.</summary>
    private static HumanoidCharacterProfile Profile(
        SpeciesPrototype species,
        bool mismatchedParts,
        MarkingManager markings,
        IPrototypeManager proto,
        out string hair,
        out string beard)
    {
        hair = MismatchedPartsRules.Styles(MarkingCategories.Hair, species.ID, true, markings, proto).Keys.First();
        beard = MismatchedPartsRules.Styles(MarkingCategories.FacialHair, species.ID, true, markings, proto).Keys.First();
        var profile = HumanoidCharacterProfile.DefaultWithSpecies(species.ID).WithSex(SexFor(species));
        return profile
            .WithCharacterAppearance(profile.Appearance
                .WithHairStyleName(hair)
                .WithHairColor(HairColor)
                .WithFacialHairStyleName(beard)
                .WithFacialHairColor(BeardColor))
            .WithMismatchedParts(mismatchedParts);
    }

    private static string[] Worn(MarkingSet set, MarkingCategories category)
    {
        return set.TryGetCategory(category, out var list) ? list.Select(m => m.MarkingId).ToArray() : Array.Empty<string>();
    }

    /// <summary>
    /// With the option on every playable species has hair and facial hair to pick; with it off a species is only offered
    /// a category it wears on its own, and the option never changes a species' own styles.
    /// </summary>
    [Test]
    public async Task RulesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var markings = server.ResolveDependency<MarkingManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var species in Playable(proto))
                {
                    foreach (var category in Categories)
                    {
                        var off = MismatchedPartsRules.Styles(category, species.ID, false, markings, proto);
                        var on = MismatchedPartsRules.Styles(category, species.ID, true, markings, proto);

                        Assert.That(on, Is.Not.Empty, $"{species.ID} has no {category} with the option on.");
                        if (MismatchedPartsRules.IsNative(category, species.ID, markings, proto))
                            Assert.That(on.Keys, Is.EquivalentTo(off.Keys), $"The option changed {species.ID}'s own {category}.");
                        else
                            Assert.That(off, Is.Empty, $"{species.ID} is offered {category} it can't wear.");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Validation keeps unlocked hair only with the option on, copies and the database keep the option, and random
    /// profiles leave it off.
    /// </summary>
    [Test]
    public async Task ProfileTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var markings = server.ResolveDependency<MarkingManager>();
        var db = GetDb(server);
        var user = new NetUserId(Guid.NewGuid());

        HumanoidCharacterProfile on = default!;
        string hair = default!;
        string beard = default!;
        await server.WaitAssertion(() =>
        {
            var species = Playable(proto).FirstOrDefault(s =>
                !MismatchedPartsRules.IsNative(MarkingCategories.Hair, s.ID, markings, proto)
                && !MismatchedPartsRules.IsNative(MarkingCategories.FacialHair, s.ID, markings, proto));
            Assert.That(species, Is.Not.Null, "No playable species lacks both hair and facial hair.");

            var profile = Profile(species!, true, markings, proto, out hair, out beard);
            on = (HumanoidCharacterProfile) profile.Validated(pair.Player!, IoCManager.Instance!);
            var off = (HumanoidCharacterProfile) profile.WithMismatchedParts(false).Validated(pair.Player!, IoCManager.Instance!);

            Assert.Multiple(() =>
            {
                Assert.That(on.MismatchedParts, Is.True);
                Assert.That(on.Appearance.HairStyleId, Is.EqualTo(hair));
                Assert.That(on.Appearance.FacialHairStyleId, Is.EqualTo(beard));
                Assert.That(off.Appearance.HairStyleId, Is.EqualTo(HairStyles.DefaultHairStyle), "Validation kept hair the option no longer allows.");
                Assert.That(off.Appearance.FacialHairStyleId, Is.EqualTo(HairStyles.DefaultFacialHairStyle));
                Assert.That(on.Clone().MismatchedParts, Is.True, "Copies drop the option.");
                Assert.That(on.MemberwiseEquals(on.WithMismatchedParts(false)), Is.False);

                foreach (var playable in Playable(proto))
                {
                    Assert.That(HumanoidCharacterProfile.RandomWithSpecies(playable.ID).MismatchedParts, Is.False,
                        $"A random {playable.ID} has the option on.");
                }
            });
        });

        await db.InitPrefsAsync(user, on);
        var prefs = await db.GetPlayerPreferencesAsync(user);
        var loaded = (HumanoidCharacterProfile) prefs!.Characters.Single().Value;
        Assert.Multiple(() =>
        {
            Assert.That(loaded.MismatchedParts, Is.True, "The option was not saved.");
            Assert.That(loaded.Appearance.HairStyleId, Is.EqualTo(hair));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A body of every playable species wears the hair and facial hair of a validated profile with the option on, and one
    /// with it off wears none it can't have.
    /// </summary>
    [Test]
    public async Task BodiesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var markings = server.ResolveDependency<MarkingManager>();
        var humanoids = entMan.System<SharedHumanoidAppearanceSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var species in Playable(proto))
                {
                    foreach (var enabled in new[] { true, false })
                    {
                        var profile = Profile(species, enabled, markings, proto, out var hair, out var beard);
                        profile = profile.WithCharacterAppearance(
                            HumanoidCharacterAppearance.EnsureValid(profile.Appearance, species.ID, profile.Sex, enabled));
                        var mob = entMan.Spawn(species.Prototype, map.MapCoords);
                        humanoids.LoadProfile(mob, profile);
                        var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(mob);

                        Assert.That(humanoid.MismatchedParts, Is.EqualTo(enabled), $"{species.ID}: option not copied.");
                        foreach (var (category, style) in new[] { (MarkingCategories.Hair, hair), (MarkingCategories.FacialHair, beard) })
                        {
                            var expected = enabled || MismatchedPartsRules.IsNative(category, species.ID, markings, proto)
                                ? new[] { style }
                                : Array.Empty<string>();
                            Assert.That(Worn(humanoid.MarkingSet, category), Is.EqualTo(expected),
                                $"{species.ID} (option {(enabled ? "on" : "off")}) wears the wrong {category}.");
                        }

                        entMan.DeleteEntity(mob);
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The creator doll of every playable species draws hair and facial hair in the profile's colours with the option
    /// on, and draws none its species can't wear with it off.
    /// </summary>
    [Test]
    public async Task DollsTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var entMan = client.EntMan;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var markings = client.ResolveDependency<MarkingManager>();

        await client.WaitAssertion(() =>
        {
            var humanoids = entMan.System<HumanoidAppearanceSystem>();
            var sprites = entMan.System<SpriteSystem>();

            Assert.Multiple(() =>
            {
                foreach (var species in Playable(proto))
                {
                    foreach (var enabled in new[] { true, false })
                    {
                        var profile = Profile(species, enabled, markings, proto, out var hair, out var beard);
                        var doll = entMan.SpawnEntity(species.DollPrototype, MapCoordinates.Nullspace);
                        humanoids.LoadProfile(doll, profile);
                        var sprite = entMan.GetComponent<SpriteComponent>(doll);

                        foreach (var (category, style, color) in new[]
                                 {
                                     (MarkingCategories.Hair, hair, HairColor),
                                     (MarkingCategories.FacialHair, beard, BeardColor),
                                 })
                        {
                            var native = MismatchedPartsRules.IsNative(category, species.ID, markings, proto);
                            var where = $"{species.ID} doll, option {(enabled ? "on" : "off")}, {style}";
                            var keys = LayerKeys(markings.Markings[style]).ToList();
                            foreach (var key in keys)
                            {
                                var found = sprites.TryGetLayer((doll, sprite), key, out var layer, false);
                                if (!enabled && !native)
                                {
                                    Assert.That(found && layer!.Visible, Is.False, $"{where}: drawn without the option.");
                                    continue;
                                }

                                Assert.That(found, Is.True, $"{where}: no {key} layer.");
                                if (!found)
                                    continue;

                                Assert.That(layer!.Visible, Is.True, $"{where}: {key} is hidden.");
                                if (!native && key == keys[0])
                                    Assert.That(layer.Color, Is.EqualTo(color), $"{where}: {key} lost the profile colour.");
                            }
                        }

                        entMan.DeleteEntity(doll);
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    private static IEnumerable<string> LayerKeys(MarkingPrototype marking)
    {
        foreach (var sprite in marking.Sprites)
        {
            if (sprite is SpriteSpecifier.Rsi rsi)
                yield return $"{marking.ID}-{rsi.RsiState}";
        }
    }

    private static ServerDbSqlite GetDb(RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var opsLog = server.ResolveDependency<ILogManager>().GetSawmill("db.ops");
        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        builder.UseSqlite(conn);
        return new ServerDbSqlite(() => builder.Options, true, cfg, true, opsLog);
    }
}
