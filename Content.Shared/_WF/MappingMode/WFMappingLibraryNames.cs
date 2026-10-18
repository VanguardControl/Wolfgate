using System.Globalization;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WF.MappingMode;

/// <summary>How a library version was written.</summary>
public enum WFMappingVersionKind : byte
{
    Manual,
    Auto,
}

/// <summary>A library version as its folder name spells it: number, UTC time of the save and kind.</summary>
[Serializable, NetSerializable]
public readonly record struct WFMappingVersionName(int Number, DateTime Stamp, WFMappingVersionKind Kind)
{
    /// <summary>The folder name, <c>v0001_20261018-142501_manual</c>.</summary>
    public string Folder => WFMappingLibraryNames.FormatVersion(Number, Stamp, Kind);
}

/// <summary>
/// Names and paths of the per-player map library:
/// <c>/Mapping/Library/&lt;NetUserId&gt;/&lt;MapName&gt;/v&lt;NNNN&gt;_&lt;yyyyMMdd-HHmmss&gt;_&lt;kind&gt;/z&lt;D&gt;.yml</c>.
/// </summary>
public static class WFMappingLibraryNames
{
    /// <summary>The folder in the server's data directory holding every player's library.</summary>
    public static readonly ResPath Root = new("/Mapping/Library");

    /// <summary>The folder in the client's data directory downloads are written to.</summary>
    public static readonly ResPath DownloadRoot = new("/Mapping");

    private const string StampFormat = "yyyyMMdd-HHmmss";
    private const string ManualSuffix = "manual";
    private const string AutoSuffix = "auto";

    /// <summary>Names Windows refuses as file or folder names, whatever the case.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Trims a map name and checks it: 1 to 48 letters, digits, spaces, dashes or underscores, and not a name the
    /// file system reserves.
    /// </summary>
    public static bool TryNormalizeMapName(string raw, out string name)
    {
        name = raw.Length > WFMappingModeLimits.MaxNameLength * 2 ? string.Empty : raw.Trim();
        return IsValidMapName(name);
    }

    /// <summary>Whether an already trimmed name is a valid map name.</summary>
    public static bool IsValidMapName(string name)
    {
        return WFMappingModeLimits.IsValidName(name) && !Reserved.Contains(name);
    }

    public static string FormatVersion(int number, DateTime stamp, WFMappingVersionKind kind)
    {
        var suffix = kind == WFMappingVersionKind.Auto ? AutoSuffix : ManualSuffix;
        return $"v{number.ToString("D4", CultureInfo.InvariantCulture)}_{stamp.ToString(StampFormat, CultureInfo.InvariantCulture)}_{suffix}";
    }

    /// <summary>Reads a version folder name in the form <see cref="FormatVersion"/> writes.</summary>
    public static bool TryParseVersion(string folder, out WFMappingVersionName version)
    {
        version = default;
        var parts = folder.Split('_');
        if (parts.Length != 3 || parts[0].Length < 5 || parts[0].Length > 10 || parts[0][0] != 'v')
            return false;

        var digits = parts[0].AsSpan(1);
        foreach (var c in digits)
        {
            if (c is < '0' or > '9')
                return false;
        }

        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
            return false;

        if (!DateTime.TryParseExact(parts[1], StampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var stamp))
        {
            return false;
        }

        WFMappingVersionKind kind;
        switch (parts[2])
        {
            case ManualSuffix:
                kind = WFMappingVersionKind.Manual;
                break;
            case AutoSuffix:
                kind = WFMappingVersionKind.Auto;
                break;
            default:
                return false;
        }

        version = new WFMappingVersionName(number, stamp, kind);
        return version.Folder == folder;
    }

    /// <summary>The file a level is saved to inside a version folder: <c>z0.yml</c> for the ground.</summary>
    public static string LevelFile(int depth)
    {
        return $"z{depth.ToString(CultureInfo.InvariantCulture)}.yml";
    }

    /// <summary>Reads a level file name back to its depth.</summary>
    public static bool TryParseLevelFile(string fileName, out int depth)
    {
        depth = 0;
        if (fileName.Length < 6 || fileName.Length > 16 || fileName[0] != 'z' ||
            !fileName.EndsWith(".yml", StringComparison.Ordinal))
        {
            return false;
        }

        var digits = fileName.AsSpan(1, fileName.Length - 5);
        var start = digits[0] == '-' ? 1 : 0;
        if (digits.Length == start)
            return false;

        for (var i = start; i < digits.Length; i++)
        {
            if (digits[i] is < '0' or > '9')
                return false;
        }

        return int.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out depth) &&
               LevelFile(depth) == fileName;
    }

    public static ResPath UserFolder(NetUserId user)
    {
        return Root / user.ToString();
    }

    public static ResPath MapFolder(NetUserId user, string mapName)
    {
        return UserFolder(user) / mapName;
    }

    public static ResPath VersionFolder(NetUserId user, string mapName, string version)
    {
        return MapFolder(user, mapName) / version;
    }

    public static ResPath LevelPath(NetUserId user, string mapName, string version, int depth)
    {
        return VersionFolder(user, mapName, version) / LevelFile(depth);
    }

    /// <summary>Where the client writes a downloaded level: <c>/Mapping/&lt;MapName&gt;/&lt;version&gt;/z&lt;D&gt;.yml</c>.</summary>
    public static ResPath DownloadPath(string mapName, string version, string fileName)
    {
        return DownloadRoot / mapName / version / fileName;
    }
}
