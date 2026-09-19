using Content.Server.Chat.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Planets;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;

namespace Content.Server._WF.PlanetCracker.Sanction;

/// <summary>
/// Raises the two unsanctioned-crack notices: one as a hull enters Cracking over a world with no licence on file, and
/// one as that world's chunk is finally lifted clear. Both hang off broadcast events the cracker and chunk systems
/// already raise; F9 adds no event of its own. The necromorph/marker hook is WFPlanetCrackedEvent itself
/// (Content.Shared/_WF/PlanetCracker/Chunk/WFChunkEvents.cs), and F9 adds nothing for it beyond this note - D7's
/// "no automated response" is deliberate, so there is no TSF NPC, no war-level change and no radio echo. The notice is
/// global rather than sector-scoped because ChatSystem.DispatchFilteredAnnouncement (Content.Server/Chat/Systems/
/// ChatSystem.cs) exists but nothing in the fork can build an "everyone in this sector" Filter; wf.planet_cracker.announce
/// is the escape hatch.
/// </summary>
public sealed partial class WFCrackSanctionSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedWFCrackerSystem _crackers = default!;

    /// <summary>Announcement sender for every sanction notice; NOT wf-crack-announce-sender, which is the hull's own "Crack control".</summary>
    private const string SenderKey = "wf-crack-sanction-sender";

    /// <summary>Locale key of the begin-crack notice.</summary>
    private const string CrackingKey = "wf-crack-sanction-cracking";

    /// <summary>Locale key of the extraction notice.</summary>
    private const string ExtractedKey = "wf-crack-sanction-extracted";

    /// <summary>Alert tone for the begin notice; the extraction notice is text only.</summary>
    private static readonly SoundSpecifier AlertSound = new SoundPathSpecifier("/Audio/Announcements/attention.ogg");

    /// <summary>Notice colour, distinct from a routine Central Command line.</summary>
    private static readonly Color NoticeColour = Color.FromHex("#D7443E");

    /// <summary>How many begin-crack notices have been dispatched; incremented only after the chat call returns.</summary>
    public int CrackingNotices { get; private set; }

    /// <summary>How many extraction notices have been dispatched.</summary>
    public int ExtractionNotices { get; private set; }

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFCrackStateChangedEvent>(OnCrackStateChanged);
        SubscribeLocalEvent<WFPlanetCrackedEvent>(OnPlanetCracked);
    }

    /// <summary>Re-arms the latch at AnchorsLocked, then raises the begin notice on the Cracking edge of an unsanctioned world.</summary>
    private void OnCrackStateChanged(ref WFCrackStateChangedEvent args)
    {
        // D9-D: the latch is per-cut, not per-planet-forever, and the clear comes before every other check including
        // the CVar so muting neither burns nor strands a latch. TryBegin refuses unless the hull is in AnchorsLocked
        // (WFCrackerSystem.Lock.cs, ComputeBlockers), so every legitimate begin - including a re-begin after
        // FinishAbort drops the hull back at WFCrackerSystem.Crack.cs - crosses this edge. A permanent latch would
        // silence exactly the retry D7's notice exists to summon players to.
        if (args.New == WFCrackState.AnchorsLocked)
        {
            ClearCrackingLatch(args.Cracker);
            return;
        }

        // Positive filter only: never test args.Old, so F7's Cracked -> Disconnecting -> Released -> Idle chain
        // cannot reach this path.
        if (args.New != WFCrackState.Cracking)
            return;

        if (!_cfg.GetCVar(PlanetCrackerCVars.Announce))
            return;

        if (!TryGetOrbitedPlanet(args.Cracker, out var planet))
            return;

        if (planet.Comp.Sanctioned)
            return;

        var notice = EnsureComp<WFCrackNoticeComponent>(planet.Owner);
        if (notice.AnnouncedCracking)
            return;

        notice.AnnouncedCracking = true;

        Announce(CrackingKey, planet.Owner, args.Cracker, sound: true);
        CrackingNotices++;
    }

    /// <summary>Raises the second, sound-free notice as an unsanctioned world's chunk is flagged clear.</summary>
    private void OnPlanetCracked(ref WFPlanetCrackedEvent args)
    {
        if (!_cfg.GetCVar(PlanetCrackerCVars.Announce))
            return;

        if (!TryComp<WFSectorPlanetComponent>(args.Planet, out var sector) || sector.Sanctioned)
            return;

        var notice = EnsureComp<WFCrackNoticeComponent>(args.Planet);
        if (notice.AnnouncedExtraction)
            return;

        // Never cleared: the planet is Cracked forever after extraction, so the cut cannot repeat.
        notice.AnnouncedExtraction = true;

        Announce(ExtractedKey, args.Planet, args.Cracker, sound: false);
        ExtractionNotices++;
    }

    /// <summary>The sector body this hull is orbiting, or false when it is anywhere else; the only resolution F9 does.</summary>
    private bool TryGetOrbitedPlanet(EntityUid cracker, out Entity<WFSectorPlanetComponent> planet)
    {
        planet = default;
        return Transform(cracker).MapUid is { } mapUid && _crackers.TryGetPlanetFromOrbit(mapUid, out planet);
    }

    /// <summary>Re-arms the begin notice for the next cut. AnchorsLocked is the one state TryBegin demands, so every real re-begin - including one after an abort - passes through here.</summary>
    private void ClearCrackingLatch(EntityUid cracker)
    {
        if (TryGetOrbitedPlanet(cracker, out var planet)
            && TryComp<WFCrackNoticeComponent>(planet.Owner, out var notice))
        {
            notice.AnnouncedCracking = false;
        }
    }

    /// <summary>The notice text as production sends it; public so a test can assert the text without capturing chat.</summary>
    public string BuildNotice(string key, EntityUid planet, EntityUid cracker)
        => Loc.GetString(key, ("planet", Name(planet)), ("ship", Name(cracker)));

    /// <summary>Dispatches one notice round-wide, every optional argument by name so a ChatSystem signature change breaks the build.</summary>
    private void Announce(string key, EntityUid planet, EntityUid cracker, bool sound)
    {
        _chat.DispatchGlobalAnnouncement(
            BuildNotice(key, planet, cracker),
            sender: Loc.GetString(SenderKey),
            playSound: sound,
            announcementSound: sound ? AlertSound : null,
            colorOverride: NoticeColour);
    }
}
