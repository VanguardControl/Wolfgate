#nullable enable
using System.Linq;
using System.Numerics;
using Content.Client.Chat.Managers;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Pair;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.Mind;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WF.ChatFormatting;

/// <summary>
///     Custom chat emphasis delimiters convert to BBCode tags for speech, emotes, whispers, and subtle emotes.
/// </summary>
[TestFixture]
[TestOf(typeof(ChatSystem))]
public sealed class ChatEmphasisTest
{
    private const string MobProto = "MobHuman";
    private const int MaxWaitTicks = 120;

    [Test]
    public async Task SpeechEmphasisDelimiters()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var clientChat = pair.Client.ResolveDependency<IChatManager>();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid mob;

        var map = await pair.CreateTestMap();
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(mindSys.CreateMind(session.UserId).Owner);
            mob = entMan.SpawnEntity(MobProto, map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, mob);
        });
        await pair.RunTicksSync(5);

        // Plain speech
        await pair.Client.WaitPost(() => clientChat.SendMessage("hello world", ChatSelectChannel.Local));
        await WaitForSay(pair, chat, "hello world", "a plain speech message");

        // Italic: |text|
        await pair.Client.WaitPost(() => clientChat.SendMessage("|italic text|", ChatSelectChannel.Local));
        await WaitForSay(pair, chat, "[italic]italic text[/italic]", "an italic speech message");

        // Bold: +text+
        await pair.Client.WaitPost(() => clientChat.SendMessage("+bold text+", ChatSelectChannel.Local));
        await WaitForSay(pair, chat, "[bold]bold text[/bold]", "a bold speech message");

        // Small: ^text^
        await pair.Client.WaitPost(() => clientChat.SendMessage("^small text^", ChatSelectChannel.Local));
        await WaitForSay(pair, chat, "[font size=8]small text[/font]", "a small speech message");
    }

    [Test]
    public async Task EmoteEmphasisDelimiters()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var clientChat = pair.Client.ResolveDependency<IChatManager>();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid mob;

        var map = await pair.CreateTestMap();
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(mindSys.CreateMind(session.UserId).Owner);
            mob = entMan.SpawnEntity(MobProto, map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, mob);
        });
        await pair.RunTicksSync(5);

        await pair.Client.WaitPost(() => clientChat.SendMessage("|waves at you|", ChatSelectChannel.Emotes));
        await WaitForEmote(pair, chat, "[italic]waves at you[/italic]", "an italic emote message");

        await pair.Client.WaitPost(() => clientChat.SendMessage("+smiles warmly+", ChatSelectChannel.Emotes));
        await WaitForEmote(pair, chat, "[bold]smiles warmly[/bold]", "a bold emote message");

        await pair.Client.WaitPost(() => clientChat.SendMessage("^mumbles quietly^", ChatSelectChannel.Emotes));
        await WaitForEmote(pair, chat, "[font size=8]mumbles quietly[/font]", "a small emote message");
    }

    [Test]
    public async Task WhisperEmphasisDelimiters()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var clientChat = pair.Client.ResolveDependency<IChatManager>();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid mob;

        var map = await pair.CreateTestMap();
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(mindSys.CreateMind(session.UserId).Owner);
            mob = entMan.SpawnEntity(MobProto, map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, mob);
        });
        await pair.RunTicksSync(5);

        await pair.Client.WaitPost(() => clientChat.SendMessage("|secret whisper|", ChatSelectChannel.Whisper));
        await WaitForWhisper(pair, chat, "[italic]secret whisper[/italic]", "an italic whisper message");

        await pair.Client.WaitPost(() => clientChat.SendMessage("+hushed tone+", ChatSelectChannel.Whisper));
        await WaitForWhisper(pair, chat, "[bold]hushed tone[/bold]", "a bold whisper message");

        await pair.Client.WaitPost(() => clientChat.SendMessage("^barely audible^", ChatSelectChannel.Whisper));
        await WaitForWhisper(pair, chat, "[font size=8]barely audible[/font]", "a small whisper message");
    }

    [Test]
    public async Task SubtleEmoteEmphasisDelimiters()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var clientChat = pair.Client.ResolveDependency<IChatManager>();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid mob;

        var map = await pair.CreateTestMap();
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(mindSys.CreateMind(session.UserId).Owner);
            mob = entMan.SpawnEntity(MobProto, map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, mob);
        });
        await pair.RunTicksSync(5);

        await pair.Client.WaitPost(() => clientChat.SendMessage("|leans in|", ChatSelectChannel.Subtle));
        await WaitForSubtle(pair, chat, "[italic]leans in[/italic]", "an italic subtle emote message");

        await pair.Client.WaitPost(() => clientChat.SendMessage("+smiles warmly+", ChatSelectChannel.Subtle));
        await WaitForSubtle(pair, chat, "[bold]smiles warmly[/bold]", "a bold subtle emote message");

        await pair.Client.WaitPost(() => clientChat.SendMessage("^mumbles quietly^", ChatSelectChannel.Subtle));
        await WaitForSubtle(pair, chat, "[font size=8]mumbles quietly[/font]", "a small subtle emote message");
    }

    [Test]
    public async Task NestedEmphasisDelimiters()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var clientChat = pair.Client.ResolveDependency<IChatManager>();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid mob;

        var map = await pair.CreateTestMap();
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(mindSys.CreateMind(session.UserId).Owner);
            mob = entMan.SpawnEntity(MobProto, map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, mob);
        });
        await pair.RunTicksSync(5);

        // Nested: |hello +world+| → [italic]hello [bold]world[/bold][/italic]
        await pair.Client.WaitPost(() => clientChat.SendMessage("|hello +world+|", ChatSelectChannel.Local));
        await WaitForSay(pair, chat, "[italic]hello [bold]world[/bold][/italic]", "nested italic + bold speech");

        // Nested: +^small bold^+ → [bold][font size=8]small bold[/font][/bold]
        await pair.Client.WaitPost(() => clientChat.SendMessage("+^small bold^+", ChatSelectChannel.Local));
        await WaitForSay(pair, chat, "[bold][font size=8]small bold[/font][/bold]", "nested bold + small speech");
    }

    [Test]
    public async Task EscapedDelimiters()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var clientChat = pair.Client.ResolveDependency<IChatManager>();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid mob;

        var map = await pair.CreateTestMap();
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(mindSys.CreateMind(session.UserId).Owner);
            mob = entMan.SpawnEntity(MobProto, map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, mob);
        });
        await pair.RunTicksSync(5);

        // Escaped delimiters should be unescaped and passed through literally
        await pair.Client.WaitPost(() => clientChat.SendMessage("\\|not italic\\|", ChatSelectChannel.Local));
        await WaitForSay(pair, chat, "|not italic|", "escaped italic delimiters");

        await pair.Client.WaitPost(() => clientChat.SendMessage("\\+not bold\\+", ChatSelectChannel.Local));
        await WaitForSay(pair, chat, "+not bold+", "escaped bold delimiters");

        await pair.Client.WaitPost(() => clientChat.SendMessage("\\^not small\\^", ChatSelectChannel.Local));
        await WaitForSay(pair, chat, "^not small^", "escaped small delimiters");
    }

    [Test]
    public async Task UnclosedDelimitersIgnored()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var clientChat = pair.Client.ResolveDependency<IChatManager>();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid mob;

        var map = await pair.CreateTestMap();
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(mindSys.CreateMind(session.UserId).Owner);
            mob = entMan.SpawnEntity(MobProto, map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, mob);
        });
        await pair.RunTicksSync(5);

        // Unclosed delimiter: |hello → passed through as-is (no formatting)
        await pair.Client.WaitPost(() => clientChat.SendMessage("|hello", ChatSelectChannel.Local));
        await WaitForSay(pair, chat, "|hello", "unclosed italic delimiter ignored");

        // Mixed: |hello +world+ → [italic]hello [bold]world[/bold] (inner closes, outer ignored)
        await pair.Client.WaitPost(() => clientChat.SendMessage("|hello +world+", ChatSelectChannel.Local));
        await WaitForSay(pair, chat, "[italic]hello [bold]world[/bold]", "partial closing delimiters");
    }

    [Test]
    public async Task AdjacentDelimiters()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var clientChat = pair.Client.ResolveDependency<IChatManager>();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid mob;

        var map = await pair.CreateTestMap();
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(mindSys.CreateMind(session.UserId).Owner);
            mob = entMan.SpawnEntity(MobProto, map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, mob);
        });
        await pair.RunTicksSync(5);

        // Adjacent same delimiters: |a||b| → [italic]a[/italic][italic]b[/italic]
        await pair.Client.WaitPost(() => clientChat.SendMessage("|a||b|", ChatSelectChannel.Local));
        await WaitForSay(pair, chat, "[italic]a[/italic][italic]b[/italic]", "adjacent same delimiters");
    }

    private static async Task WaitForSay(TestPair pair, ChatUIController chat, string expectedContent, string reason)
    {
        var expected = $"[color=#7ebbff]{expectedContent}[/color]";
        bool Arrived() => chat.History.Any(h => h.Msg.Message.Contains(expected));

        for (var i = 0; i < MaxWaitTicks; i++)
        {
            if (Arrived())
                return;
            await pair.RunTicksSync(1);
        }

        Assert.That(Arrived(), Is.True, $"Timed out waiting for {reason}.");
    }

    private static async Task WaitForEmote(TestPair pair, ChatUIController chat, string expectedContent, string reason)
    {
        bool Arrived() => chat.History.Any(h => h.Msg.Channel == ChatChannel.Emotes && h.Msg.Message.Contains(expectedContent));

        for (var i = 0; i < MaxWaitTicks; i++)
        {
            if (Arrived())
                return;
            await pair.RunTicksSync(1);
        }

        Assert.That(Arrived(), Is.True, $"Timed out waiting for {reason}.");
    }

    private static async Task WaitForWhisper(TestPair pair, ChatUIController chat, string expectedContent, string reason)
    {
        bool Arrived() => chat.History.Any(h => h.Msg.Channel == ChatChannel.Whisper && h.Msg.Message.Contains(expectedContent));

        for (var i = 0; i < MaxWaitTicks; i++)
        {
            if (Arrived())
                return;
            await pair.RunTicksSync(1);
        }

        Assert.That(Arrived(), Is.True, $"Timed out waiting for {reason}.");
    }

    private static async Task WaitForSubtle(TestPair pair, ChatUIController chat, string expectedContent, string reason)
    {
        bool Arrived() => chat.History.Any(h => h.Msg.Channel == ChatChannel.Subtle && h.Msg.Message.Contains(expectedContent));

        for (var i = 0; i < MaxWaitTicks; i++)
        {
            if (Arrived())
                return;
            await pair.RunTicksSync(1);
        }

        Assert.That(Arrived(), Is.True, $"Timed out waiting for {reason}.");
    }
}
