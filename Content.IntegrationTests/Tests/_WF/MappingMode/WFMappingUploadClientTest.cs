#nullable enable
using System.IO;
using System.Linq;
using System.Text;
using Content.Client._WF.MappingMode;
using Content.Server._WF.MappingMode;
using Content.Shared._WF.MappingMode;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// The real client's upload path, from the bytes a picked file gives it to the version in its library. The picker
/// itself can't run headless, so this starts where it hands the file over.
/// </summary>
[TestFixture]
public sealed class WFMappingUploadClientTest
{
    [Test]
    public async Task ClientUploadLandsInLibrary()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var loader = entMan.System<MapLoaderSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var res = server.ResolveDependency<IResourceManager>();
        var upload = client.EntMan.System<WFMappingUploadClientSystem>();
        await Enable(pair);
        var player = pair.Player!;

        // A small map saved to text, as a file on the player's disk would hold it.
        var yaml = string.Empty;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(player, "Upload Source", out var source), Is.True);
            var mapId = entMan.GetComponent<MapComponent>(source!.RootMap).MapId;
            var grid = mapSys.CreateGridEntity(mapId);
            var plating = new Tile(tileDefs["Plating"].TileId);
            mapSys.SetTiles(grid, grid.Comp, Enumerable.Range(0, 4).Select(x => (new Vector2i(x, 0), plating)).ToList());
            using var writer = new StringWriter();
            Assert.That(loader.TrySaveMap(source.RootMap, writer), Is.True);
            yaml = writer.ToString();
        });
        await pair.RunTicksSync(5);

        var bytes = Encoding.UTF8.GetBytes(yaml);
        await client.WaitPost(() => upload.Start("Client Upload", bytes));
        for (var i = 0; i < 200 && upload.Upload is not { Stage: WFMappingUploadStage.Done }; i++)
        {
            await pair.RunTicksSync(1);
        }

        await client.WaitAssertion(() =>
        {
            Assert.That(upload.Upload, Is.Not.Null, "The upload never started on the client.");
            Assert.That(upload.Upload!.Stage, Is.EqualTo(WFMappingUploadStage.Done), "The upload did not finish.");
        });
        await server.WaitAssertion(() =>
        {
            var folder = WFMappingLibraryNames.MapFolder(player.UserId, "Client Upload");
            Assert.That(res.UserData.IsDir(folder), Is.True, "The library has no folder for the upload.");
            var versions = res.UserData.DirectoryEntries(folder).ToList();
            Assert.That(versions, Has.Count.EqualTo(1));
            Assert.That(versions[0], Does.EndWith("_upload"));
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
