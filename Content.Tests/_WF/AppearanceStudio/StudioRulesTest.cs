using System.Collections.Generic;
using Content.Shared._WF.AppearanceStudio;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.AppearanceStudio;

[TestFixture]
[TestOf(typeof(StudioRules))]
public sealed class StudioRulesTest
{
    private static readonly string Hash = new('a', 64);
    private static readonly StudioRules.Limits Limits = new(16, 0.5f, 2f, true);

    [Test]
    public void CleanHoldsToLimitsTest()
    {
        var tweaks = new List<StudioTweak>
        {
            null,
            new(StudioTarget.Worn, "head")
            {
                Offsets = new[] { new Vector2i(40, -40), new Vector2i(3, 4) },
                Scale = 9f,
                Hidden = true,
                Warp = Hash,
            },
            new(StudioTarget.Body, "Head") { Hidden = true, Warp = Hash, Scale = 0.123f },
            new(StudioTarget.Marking, "bad key!") { Hidden = true },
            new(StudioTarget.Held, "Left"),
        };

        var clean = StudioRules.Clean(tweaks, Limits);

        Assert.That(clean, Has.Count.EqualTo(2), "bad keys, nulls and empty tweaks go");
        var worn = clean[0];
        Assert.Multiple(() =>
        {
            Assert.That(worn.Offsets, Has.Length.EqualTo(StudioRules.Facings), "short offset lists are padded");
            Assert.That(worn.Offsets[0], Is.EqualTo(new Vector2i(16, -16)));
            Assert.That(worn.Offsets[1], Is.EqualTo(new Vector2i(3, 4)));
            Assert.That(worn.Scale, Is.EqualTo(2f));
            Assert.That(worn.Hidden, Is.False, "worn items can't be hidden");
            Assert.That(worn.Warp, Is.EqualTo(Hash));
        });

        var body = clean[1];
        Assert.Multiple(() =>
        {
            Assert.That(body.Hidden, Is.True);
            Assert.That(body.Warp, Is.Null, "body parts take no warp");
            Assert.That(body.Scale, Is.EqualTo(0.5f));
        });
    }

    [Test]
    public void WarpsOffDropsWarpsTest()
    {
        var tweaks = new List<StudioTweak> { new(StudioTarget.Worn, "head") { Warp = Hash } };

        Assert.That(StudioRules.Clean(tweaks, Limits with { Warps = false }), Is.Empty);
    }

    [Test]
    public void LastTweakOfAPartWinsTest()
    {
        var tweaks = new List<StudioTweak>
        {
            new(StudioTarget.Worn, "head") { Scale = 1.5f },
            new(StudioTarget.Worn, "head") { Scale = 0.75f },
        };

        var clean = StudioRules.Clean(tweaks, Limits);

        Assert.That(clean, Has.Count.EqualTo(1));
        Assert.That(clean[0].Scale, Is.EqualTo(0.75f));
    }

    [Test]
    public void StoredRoundTripTest()
    {
        var tweaks = StudioRules.Clean(new List<StudioTweak>
        {
            new(StudioTarget.Worn, "outerClothing")
            {
                Offsets = new[] { new Vector2i(-3, 12), new Vector2i(0, -1), new Vector2i(5, 5), new Vector2i(-5, 5) },
                Scale = 1.25f,
                Warp = Hash,
            },
            new(StudioTarget.Body, "LArm") { Hidden = true },
            new(StudioTarget.Marking, "VulpEar-1") { Offsets = new[] { Vector2i.Zero, Vector2i.Zero, new Vector2i(1, 0), Vector2i.Zero } },
        }, Limits);

        var stored = StudioRules.ToStored(tweaks);
        var read = StudioRules.FromStored(stored);

        Assert.That(StudioRules.SameList(read, tweaks), Is.True, stored);
    }

    [Test]
    public void FromStoredSkipsJunkTest()
    {
        var read = StudioRules.FromStored("junk;2|head|1,2,3,4,5,6,7,8|100|0|;9|x|0,0|100|0|;2|neck|1,2,3,4,5,6,7,x|100|0|");

        Assert.That(read, Has.Count.EqualTo(1));
        Assert.That(read[0].Key, Is.EqualTo("head"));
        Assert.That(read[0].Offsets[3], Is.EqualTo(new Vector2i(7, 8)));
    }

    [Test]
    public void WithReplacesAndRemovesTest()
    {
        var tweaks = new List<StudioTweak> { new(StudioTarget.Worn, "head") { Scale = 1.5f } };

        var changed = StudioRules.With(tweaks, new StudioTweak(StudioTarget.Worn, "head") { Scale = 2f });
        Assert.That(changed[0].Scale, Is.EqualTo(2f));

        var removed = StudioRules.With(tweaks, new StudioTweak(StudioTarget.Worn, "head"));
        Assert.That(removed, Is.Empty, "an empty tweak takes the part out");
        Assert.That(tweaks, Has.Count.EqualTo(1), "the list given is left alone");
    }
}
