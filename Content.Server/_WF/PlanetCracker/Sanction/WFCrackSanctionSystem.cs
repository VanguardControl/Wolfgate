using Content.Server.Chat.Systems;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.PlanetCracker.Chunk;
using Content.Shared._WF.PlanetCracker.Cracker;
using Content.Shared._WF.PlanetCracker.Planets;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;

namespace Content.Server._WF.PlanetCracker.Sanction;

/// <summary>Round-wide notices when a hull starts cracking an unsanctioned world and when its chunk lifts.</summary>
public sealed partial class WFCrackSanctionSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedWFCrackerSystem _crackers = default!;

    /// <summary>Sender for every sanction notice, distinct from the hull's own crack-control sender.</summary>
    private const string SenderKey = "wf-crack-sanction-sender";

    private const string CrackingKey = "wf-crack-sanction-cracking";

    private const string ExtractedKey = "wf-crack-sanction-extracted";

    /// <summary>Alert tone for the begin notice; the extraction notice is text only.</summary>
    private static readonly SoundSpecifier AlertSound = new SoundPathSpecifier("/Audio/Announcements/attention.ogg");

    /// <summary>Notice colour, distinct from a routine Central Command line.</summary>
    private static readonly Color NoticeColour = Color.FromHex("#D7443E");

    /// <summary>How many begin-crack notices have been dispatched.</summary>
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
        // The latch is per cut and cleared before the CVar check, so a re-begin after an abort is announced too.
        if (args.New == WFCrackState.AnchorsLocked)
        {
            ClearCrackingLatch(args.Cracker);
            return;
        }

        // Positive filter only, so the post-cut state chain can't reach this path.
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

    /// <summary>The sector body this hull is orbiting, or false when it is anywhere else.</summary>
    private bool TryGetOrbitedPlanet(EntityUid cracker, out Entity<WFSectorPlanetComponent> planet)
    {
        planet = default;
        return Transform(cracker).MapUid is { } mapUid && _crackers.TryGetPlanetFromOrbit(mapUid, out planet);
    }

    /// <summary>Re-arms the begin notice for the next cut; every real begin passes through AnchorsLocked.</summary>
    private void ClearCrackingLatch(EntityUid cracker)
    {
        if (TryGetOrbitedPlanet(cracker, out var planet)
            && TryComp<WFCrackNoticeComponent>(planet.Owner, out var notice))
        {
            notice.AnnouncedCracking = false;
        }
    }

    /// <summary>The notice text as it is sent.</summary>
    public string BuildNotice(string key, EntityUid planet, EntityUid cracker)
        => Loc.GetString(key, ("planet", Name(planet)), ("ship", Name(cracker)));

    /// <summary>Dispatches one notice round-wide.</summary>
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
