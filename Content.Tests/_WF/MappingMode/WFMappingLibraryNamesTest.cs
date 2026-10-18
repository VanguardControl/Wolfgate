using System;
using Content.Shared._WF.MappingMode;
using NUnit.Framework;
using Robust.Shared.Network;

namespace Content.Tests._WF.MappingMode;

[TestFixture]
public sealed class WFMappingLibraryNamesTest
{
    private static readonly DateTime Stamp = new(2026, 10, 18, 14, 25, 1, DateTimeKind.Utc);

    [TestCase("  My Map_1-2 ", "My Map_1-2")]
    [TestCase("a", "a")]
    public void ValidMapNamesAreTrimmed(string raw, string expected)
    {
        Assert.That(WFMappingLibraryNames.TryNormalizeMapName(raw, out var name), Is.True);
        Assert.That(name, Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("a/b")]
    [TestCase("..")]
    [TestCase("map.yml")]
    [TestCase("con")]
    [TestCase("LPT1")]
    [TestCase("Nul")]
    public void InvalidMapNamesAreRefused(string raw)
    {
        Assert.That(WFMappingLibraryNames.TryNormalizeMapName(raw, out _), Is.False);
    }

    [Test]
    public void MapNameLength()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WFMappingLibraryNames.IsValidMapName(new string('a', WFMappingModeLimits.MaxNameLength)), Is.True);
            Assert.That(WFMappingLibraryNames.IsValidMapName(new string('a', WFMappingModeLimits.MaxNameLength + 1)), Is.False);
            Assert.That(WFMappingLibraryNames.TryNormalizeMapName(new string(' ', 500) + "a", out _), Is.False,
                "Very long input is refused before trimming.");
        });
    }

    [TestCase(1, WFMappingVersionKind.Manual, "v0001_20261018-142501_manual")]
    [TestCase(42, WFMappingVersionKind.Auto, "v0042_20261018-142501_auto")]
    [TestCase(12345, WFMappingVersionKind.Manual, "v12345_20261018-142501_manual")]
    public void VersionFoldersRoundTrip(int number, WFMappingVersionKind kind, string folder)
    {
        var version = new WFMappingVersionName(number, Stamp, kind);
        Assert.That(version.Folder, Is.EqualTo(folder));
        Assert.That(WFMappingLibraryNames.TryParseVersion(folder, out var parsed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.EqualTo(version));
            Assert.That(parsed.Stamp.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [TestCase("v001_20261018-142501_manual")]
    [TestCase("v0000_20261018-142501_manual")]
    [TestCase("x0001_20261018-142501_manual")]
    [TestCase("v0001_20261018-142501_other")]
    [TestCase("v0001_20261318-142501_manual")]
    [TestCase("v0001_20261018-142501")]
    [TestCase("v0001_20261018-142501_manual_x")]
    [TestCase("v00a1_20261018-142501_manual")]
    [TestCase("v+001_20261018-142501_manual")]
    [TestCase("")]
    public void BadVersionFoldersAreIgnored(string folder)
    {
        Assert.That(WFMappingLibraryNames.TryParseVersion(folder, out _), Is.False);
    }

    [TestCase(-3)]
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(2)]
    [TestCase(10)]
    public void LevelFilesRoundTrip(int depth)
    {
        var file = WFMappingLibraryNames.LevelFile(depth);
        Assert.That(file, Is.EqualTo($"z{depth}.yml"));
        Assert.That(WFMappingLibraryNames.TryParseLevelFile(file, out var parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(depth));
    }

    [TestCase("z.yml")]
    [TestCase("z01.yml")]
    [TestCase("z-0.yml")]
    [TestCase("z+1.yml")]
    [TestCase("z1.yaml")]
    [TestCase("zz.yml")]
    [TestCase("../z0.yml")]
    [TestCase("z0.yml/..")]
    [TestCase("Z0.yml")]
    public void BadLevelFilesAreRefused(string file)
    {
        Assert.That(WFMappingLibraryNames.TryParseLevelFile(file, out _), Is.False);
    }

    [Test]
    public void Paths()
    {
        var user = new NetUserId(new Guid("01234567-89ab-cdef-0123-456789abcdef"));
        var version = new WFMappingVersionName(3, Stamp, WFMappingVersionKind.Auto).Folder;
        Assert.Multiple(() =>
        {
            Assert.That(WFMappingLibraryNames.LevelPath(user, "Outpost", version, -1).ToString(),
                Is.EqualTo($"/Mapping/Library/{user}/Outpost/v0003_20261018-142501_auto/z-1.yml"));
            Assert.That(WFMappingLibraryNames.DownloadPath("Outpost", version, "z0.yml").ToString(),
                Is.EqualTo("/Mapping/Outpost/v0003_20261018-142501_auto/z0.yml"));
        });
    }
}
