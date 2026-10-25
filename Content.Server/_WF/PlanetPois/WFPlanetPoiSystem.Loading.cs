using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Utility;

namespace Content.Server._WF.PlanetPois;

/// <summary>Map files for sites, wrecks and camps, read and parsed off the main thread so only the entities cost a tick.</summary>
public sealed partial class WFPlanetPoiSystem
{
    [Dependency] private IResourceManager _resources = default!;

    /// <summary>Reads and parses a map file on the thread pool; null if it is missing or isn't one document.</summary>
    private Task<MappingDataNode?> ReadMapAsync(ResPath path)
    {
        var rooted = path.ToRootedPath();
        return Task.Run(() =>
        {
            TextReader reader;
            if (_resources.UserData.Exists(rooted))
                reader = _resources.UserData.OpenText(rooted);
            else if (_resources.TryContentFileRead(rooted, out var stream))
                reader = new StreamReader(stream);
            else
                return null;

            using (reader)
            {
                var documents = DataNodeParser.ParseYamlStream(reader).ToArray();
                return documents.Length == 1 ? (MappingDataNode?) documents[0].Root : null;
            }
        });
    }

    /// <summary>Loads a single-grid map file onto a map, as the loader's own TryLoadGrid does, with the parsing off the main thread.</summary>
    private async Task<Entity<MapGridComponent>?> LoadGridAsync(MapId map, ResPath path)
    {
        if (await ReadMapAsync(path) is not { } data)
        {
            Log.Error($"Cannot read map file {path}.");
            return null;
        }

        var options = new MapLoadOptions { MergeMap = map, ExpectedCategory = FileCategory.Grid };
        if (!_loader.TryLoadGeneric(data, path.ToString(), out var result, options))
            return null;

        if (result.Grids.Count == 1)
            return result.Grids.Single();

        _loader.Delete(result);
        return null;
    }
}
