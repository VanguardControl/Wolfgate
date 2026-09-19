#nullable enable
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Content.Client._WF.ShipPa;
using Content.Shared._WF.Audio.InternetSound;
using Content.Shared._WF.ShipPa;
using Robust.Client;
using Robust.Client.Graphics;
using Robust.Client.Replays.Loading;
using Robust.Client.Replays.Playback;
using Robust.Client.ResourceManagement;
using Robust.Shared;
using Robust.Shared.Audio;
using Robust.Shared.ContentPack;
using Robust.Shared.Configuration;
using Robust.Shared.Replays;
using Robust.Shared.Log;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Robust.Shared.Replays.ReplayConstants;

namespace Content.IntegrationTests.Tests._WF.ShipPa;

[TestFixture]
// StopReplay reloads all prototypes; serialize these cases to limit peak memory in CI.
[NonParallelizable]
public sealed class ShipPaReplayTest
{
    [TestCase(10, 30)]
    [TestCase(0, 1)] // Force incremental seeking after a slow initial timebase.
    public async Task RecordedAssetsSurviveReleaseAndReplaySeeking(int scrubBudgetMs, int initialTickrate)
    {
        // StopReplay resets the client's prototype manager, including the pool's test-only prototypes.
        // Ordinary dirty recycling does not reload them, so this pair must never be reused.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Destructive = true });
        var server = pair.Server;
        var client = pair.Client;
        // Reproduce the slow tickrate seen on a reused CI pair and advance its clock.
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetTickrate, initialTickrate));
        await pair.RunTicksSync(60);
        // The 20-tick seek below must land inside the short clip. A recycled pair can
        // retain a different tickrate, so establish the recording's rate explicitly.
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetTickrate, 30));
        var map = await pair.CreateTestMap();
        var recording = server.ResolveDependency<IReplayRecordingManager>();
        var directory = new VirtualWritableDirProvider();
        ReplayRecordingFinished? finished = null;
        byte[] audio = [];
        const int id = 9201;
        var path = InternetSoundResources.PathFor(id);

        await server.WaitAssertion(() =>
        {
            var resources = server.ResolveDependency<IResourceManager>();
            using var source = resources.ContentFileRead("/Audio/Announcements/attention.ogg");
            using var stream = new MemoryStream();
            source.CopyTo(stream);
            audio = stream.ToArray();
            InternetSoundResources.For(resources).Store(id, audio);
            var speaker = server.EntMan.SpawnEntity("WallmountShipPaSpeaker", map.GridCoords);
            server.EntMan.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(speaker).NeedsPower = false;
            server.System<Content.Server._WF.ShipPa.ShipPaSystem>().StartTrack(map.Grid.Owner, "replay", new SoundPathSpecifier(path));
            server.CfgMan.SetCVar(CVars.ReplayServerRecordingEnabled, true);
            recording.RecordingFinished += result => finished = result;
            Assert.That(recording.TryStartRecording(directory, "pa-test"), Is.True);
        });
        await pair.RunTicksSync(20);
        await server.WaitPost(() => InternetSoundResources.For(server.ResolveDependency<IResourceManager>()).Store(id + 1, audio));
        await pair.RunTicksSync(20);
        await server.WaitPost(() => server.System<Content.Server._WF.ShipPa.ShipPaSystem>().StopAlarm(map.Grid.Owner, "replay"));
        await pair.RunTicksSync(20);
        await server.WaitPost(recording.StopRecording);
        await recording.WaitWriteTasks();
        Assert.That(finished, Is.Not.Null);

        // Load a real serialized recording into a fresh single-player client with no transferred audio.
        Task<ReplayData>? loading = null;
        await client.WaitPost(() =>
        {
            client.ResolveDependency<IBaseClient>().StartSinglePlayer();
            // VirtualWritableDirProvider reports stream Length as its current position; ZipArchive
            // needs a stable Length while seeking, so read through a normal memory stream.
            using var recorded = directory.OpenRead(finished!.Path);
            var archive = new MemoryStream();
            recorded.CopyTo(archive);
            archive.Position = 0;
            var zip = new ZipArchive(archive, ZipArchiveMode.Read);
            var timing = client.ResolveDependency<IGameTiming>();
            loading = ReadTestReplay(new ReplayFileReaderZip(zip, ReplayZipFolder),
                (ReplayLoadManager) client.ResolveDependency<IReplayLoadManager>(),
                client.ResolveDependency<IRobustSerializer>(), timing, client.CfgMan);
        });
        var data = await loading!;
        Assert.That(data.InitialMessages!.Messages.OfType<InternetSoundReplayAsset>().Any(a => a.Id == id && a.Audio.SequenceEqual(audio)), Is.True);
        Assert.That(data.Messages.SelectMany(m => m.Messages).OfType<InternetSoundReplayAsset>().Any(a => a.Id == id + 1), Is.True);

        Task? starting = null;
        await client.WaitPost(() => starting = client.ResolveDependency<IReplayLoadManager>().StartReplayAsync(data, (_, _, _, _) => Task.CompletedTask));
        await starting!;
        await client.WaitAssertion(() =>
        {
            var em = client.EntMan;
            var playback = client.ResolveDependency<IReplayPlaybackManager>();
            var resources = InternetSoundResources.For(client.ResolveDependency<IResourceCache>());
            var pa = em.System<ShipPaMeshSystem>();
            var internet = em.System<Content.Client._WF.Audio.InternetSound.InternetSoundSystem>();
            client.CfgMan.SetCVar(CVars.ReplayMaxScrubTime, scrubBudgetMs);
            client.ResolveDependency<IEyeManager>().CurrentEye = new FixedEye { Position = map.MapCoords };
            SeekTo(playback, 20);
            internet.FrameUpdate(0.3f);
            pa.FrameUpdate(0.3f);
            Assert.That(resources.Has(id), Is.True);
            Assert.That(resources.Has(id + 1), Is.False, "Unused assets stay compressed in the recording.");
            Assert.That(em.EntityQuery<ShipPaAudioComponent>().Count(), Is.EqualTo(1));

            SeekTo(playback, data.States.Count - 1);
            internet.FrameUpdate(0.3f);
            pa.FrameUpdate(0.3f);
            Assert.That(resources.Has(id), Is.False, "Seeking past the song frees its decoded audio.");
            Assert.That(em.EntityQuery<ShipPaAudioComponent>(), Is.Empty);

            SeekTo(playback, 20);
            internet.FrameUpdate(0.3f);
            pa.FrameUpdate(0.3f);
            Assert.That(resources.Has(id), Is.True, "Rewind restores the song after release.");
            Assert.That(em.EntityQuery<ShipPaAudioComponent>().Count(), Is.EqualTo(1));
            // The engine reset warns about re-registering the deliberately client-ignored map kind.
            // This is unrelated to audio; keep errors visible while suppressing that known reset warning.
            var prototypes = client.ResolveDependency<ILogManager>().GetSawmill("proto");
            var oldLevel = prototypes.Level;
            try
            {
                prototypes.Level = LogLevel.Error;
                playback.StopReplay();
            }
            finally
            {
                prototypes.Level = oldLevel;
            }
            Assert.That(resources.Has(id), Is.False);
            if (client.ResolveDependency<IBaseClient>().RunLevel == ClientRunLevel.SinglePlayerGame)
                client.ResolveDependency<IBaseClient>().StopSinglePlayer();
        });
        await pair.CleanReturnAsync();
    }
    private static void SeekTo(IReplayPlaybackManager playback, int index)
    {
        var replay = playback.Replay!;
        // SetIndex is time-budgeted, including checkpoint restoration. Continue the pending seek
        // as playback frames normally would, rather than asserting against a partially applied state.
        // Even a zero budget advances at least one state per call, so the recording bounds this loop.
        for (var attempt = 0; attempt < replay.States.Count; attempt++)
        {
            playback.SetIndex(index);
            if (replay.CurrentIndex == index && playback.ScrubbingTarget == null)
                return;
        }

        Assert.Fail($"Replay seek did not finish: expected index {index}, reached {replay.CurrentIndex}.");
    }

    // The integration serializer deliberately has no mapped-string package and rejects SetPackage.
    // Read its raw-string recording directly, then use the real checkpoint generator and playback.
    private static async Task<ReplayData> ReadTestReplay(IReplayFileReader reader, ReplayLoadManager loader,
        IRobustSerializer serializer, IGameTiming timing, IConfigurationManager configuration)
    {
        using (reader)
        {
            var metadata = loader.LoadYamlMetadata(reader)!;
            using var init = reader.Open(FileInit);
            serializer.DeserializeDirect(init, out ReplayMessage initial);
            var states = new List<GameState>();
            var messages = new List<ReplayMessage>();
            for (var i = 0; reader.Exists(new ResPath($"{DataFilePrefix}{i}.{Ext}")); i++)
            {
                using var input = reader.Open(new ResPath($"{DataFilePrefix}{i}.{Ext}"));
                var length = new byte[4];
                input.ReadExactly(length);
                using var compressed = new ZStdDecompressStream(input, false);
                using var decoded = new MemoryStream();
                compressed.CopyTo(decoded);
                decoded.Position = 0;
                while (decoded.Position < decoded.Length)
                {
                    serializer.DeserializeDirect(decoded, out GameState state);
                    serializer.DeserializeDirect(decoded, out ReplayMessage message);
                    states.Add(state);
                    messages.Add(message);
                }
            }
            using var cvarFile = reader.Open(FileCvars);
            var cvars = configuration.LoadFromTomlStream(cvarFile);
            // Match the real loader: tickrate callbacks change TimeBase, so restore the
            // recording's clock only AFTER loading its CVars, including on reused clients.
            timing.CurTick = states[0].ToSequence;
            timing.TimeBase = (
                TimeSpan.FromTicks(long.Parse(((ValueDataNode) metadata[MetaKeyBaseTime]).Value)),
                new GameTick(uint.Parse(((ValueDataNode) metadata[MetaKeyBaseTick]).Value)));
            var (checkpoints, times) = await loader.GenerateCheckpointsAsync(initial, cvars,
                states, messages, (_, _, _, _) => Task.CompletedTask);
            return new ReplayData(states, messages, times, states[0].ToSequence, timing.CurTime, null,
                checkpoints, initial, false, metadata);
        }
    }

}
