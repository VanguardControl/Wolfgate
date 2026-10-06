using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client._WF.CustomMarkings;

/// <summary>
/// A content root holding fetched custom marking art as one RSI per hash, so a sprite layer can draw it, facings
/// and all, like any shipped marking.
/// </summary>
/// <remarks>
/// Roots can only ever be added to <see cref="IResourceManager"/>, never removed, so this mounts once per resource
/// manager and outlives the entity systems, which are rebuilt on reconnect. The manager owns the mounted root; the
/// static registry only holds weak references.
/// </remarks>
public sealed class CustomMarkingResources : IContentRoot, IDisposable
{
    /// <summary>Where the root is mounted. Nothing ships under this path, so it can't shadow a real resource.</summary>
    public static readonly ResPath Prefix = ResPath.Root / "WFCustomMarkings";

    /// <summary>The one state of every art RSI.</summary>
    public const string State = "art";

    private static readonly byte[] Meta = Encoding.UTF8.GetBytes(
        """{"version":1,"license":"CC-BY-SA-3.0","copyright":"Drawn by a player","size":{"x":32,"y":32},"states":[{"name":"art","directions":4}]}""");

    private static readonly List<WeakReference<CustomMarkingResources>> Mounted = new();

    private readonly WeakReference<IResourceManager> _owner;

    private readonly MemoryContentRoot _root = new();

    private CustomMarkingResources(IResourceManager resources)
    {
        _owner = new WeakReference<IResourceManager>(resources);
        resources.AddRoot(Prefix, this);
    }

    /// <summary>The root for this resource manager, mounting it on first use.</summary>
    public static CustomMarkingResources For(IResourceManager resources)
    {
        lock (Mounted)
        {
            for (var i = Mounted.Count - 1; i >= 0; i--)
            {
                if (!Mounted[i].TryGetTarget(out var existing) || !existing._owner.TryGetTarget(out var owner))
                {
                    Mounted.RemoveAt(i);
                    continue;
                }

                if (ReferenceEquals(owner, resources))
                    return existing;
            }

            var created = new CustomMarkingResources(resources);
            Mounted.Add(new WeakReference<CustomMarkingResources>(created));
            return created;
        }
    }

    void IContentRoot.Mount() => _root.Mount();
    bool IContentRoot.TryGetFile(ResPath path, [NotNullWhen(true)] out Stream? stream) => _root.TryGetFile(path, out stream);
    bool IContentRoot.FileExists(ResPath path) => _root.FileExists(path);
    IEnumerable<ResPath> IContentRoot.FindFiles(ResPath path) => _root.FindFiles(path);
    IEnumerable<string> IContentRoot.GetRelativeFilePaths() => _root.GetRelativeFilePaths();
    public void Dispose() => _root.Dispose();

    /// <summary>Full path of the RSI holding the art with this hash.</summary>
    public static ResPath PathFor(string hash)
    {
        return Prefix / Relative(hash);
    }

    private static ResPath Relative(string hash)
    {
        return new ResPath($"{hash}.rsi");
    }

    /// <summary>Puts art, a PNG sheet of the four facings, under its hash.</summary>
    public void Store(string hash, byte[] png)
    {
        var rsi = Relative(hash);
        _root.AddOrUpdateFile(rsi / $"{State}.png", png);
        // The manifest goes in last: it is what makes the RSI loadable.
        _root.AddOrUpdateFile(rsi / "meta.json", Meta);
    }

    /// <summary>Forgets art. The resource cache may still hold its RSI, so check <see cref="Has"/> before loading.</summary>
    public void Remove(string hash)
    {
        var rsi = Relative(hash);
        _root.RemoveFile(rsi / "meta.json");
        _root.RemoveFile(rsi / $"{State}.png");
    }

    public bool Has(string hash)
    {
        return _root.FileExists(Relative(hash) / "meta.json");
    }

    /// <summary>The stored PNG for a hash, for the editor to open or export.</summary>
    public bool TryGetPng(string hash, [NotNullWhen(true)] out byte[]? png)
    {
        png = null;
        if (!_root.TryGetFile(Relative(hash) / $"{State}.png", out var stream))
            return false;

        using (stream)
        {
            png = stream.CopyToArray();
        }

        return true;
    }
}
