using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
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

    /// <summary>
    /// Loads a single-grid map file onto a map, as the loader's own TryLoadGrid does, with the parsing off the main thread.
    /// A <paramref name="whole"/> grid is kept in one piece however many islands it has.
    /// </summary>
    private async Task<Entity<MapGridComponent>?> LoadGridAsync(MapId map, ResPath path, bool whole = false)
    {
        if (await ReadMapAsync(path) is not { } data)
        {
            Log.Error($"Cannot read map file {path}.");
            return null;
        }

        if (whole)
            KeepWhole(data);

        var options = new MapLoadOptions { MergeMap = map, ExpectedCategory = FileCategory.Grid };
        if (!_loader.TryLoadGeneric(data, path.ToString(), out var result, options))
            return null;

        if (result.Grids.Count == 1)
            return result.Grids.Single();

        _loader.Delete(result);
        return null;
    }

    /// <summary>
    /// Stops every grid in a map file splitting as it loads. With grid splitting on, as it is outside a dev build, a grid
    /// loads in pieces wherever its tiles don't touch, and only the piece that keeps the grid would be merged.
    /// </summary>
    private static void KeepWhole(MappingDataNode data)
    {
        if (!data.TryGet<SequenceDataNode>("entities", out var groups))
            return;

        foreach (var group in groups)
        {
            if (!((MappingDataNode) group).TryGet<SequenceDataNode>("entities", out var entities))
                continue;

            foreach (var entity in entities)
            {
                if (!((MappingDataNode) entity).TryGet<SequenceDataNode>("components", out var components))
                    continue;

                foreach (var component in components)
                {
                    var node = (MappingDataNode) component;
                    if (node.Get<ValueDataNode>("type").Value != "MapGrid")
                        continue;

                    node.Remove("canSplit");
                    node.Add("canSplit", new ValueDataNode("false"));
                }
            }
        }
    }
}
