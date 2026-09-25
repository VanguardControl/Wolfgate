# F9 implementation plan (architect output, revised, 2026-09-14)

Produced by the F9 design workflow after two adversarial review passes. Lands last. Notable decisions: the begin notice hangs off the Cracking edge of WFCrackStateChangedEvent with a per-cut latch cleared on the AnchorsLocked edge; the extraction notice off WFPlanetCrackedEvent; DispatchGlobalAnnouncement with a named sender, sound and colour; CVar wf.planet_cracker.announce; the necromorph hook is WFPlanetCrackedEvent, documented only.

F9 — SANCTION NOTICES. File-level implementation plan, REVISED against the eleven critiques. Re-verified against the worktree at C:/Users/jzo12/Documents/GitHub/Wolfgate/.claude/worktrees/modest-chaum-01364c at its REAL HEAD, de8a4a043a "F7 stage E console" (the prior revision's "5f774de8b5" was stale). `git status --porcelain -- Content.Server/_WF/PlanetCracker Content.Shared/_WF/PlanetCracker Content.IntegrationTests/Tests/_WF/PlanetCracker Resources/Locale/en-US/_WF/planet-cracker` is EMPTY: F7 is fully committed, nothing in the crack tree is half-written, and every symbol below is committed code. Three stages: core (opus), locale (sonnet, data), tests (opus).

=== 0. DECISIONS CLOSED (do not reopen) ===

D9-A. TWO HOOKS, BOTH BROADCAST, BOTH COMMITTED. The begin notice hangs off `WFCrackStateChangedEvent` (Content.Shared/_WF/PlanetCracker/Cracker/WFCrackEvents.cs:7, raised by WFCrackerSystem.SetState at Content.Server/_WF/PlanetCracker/Cracker/WFCrackerSystem.cs:117-118) filtered on `args.New == WFCrackState.Cracking`. The extraction notice hangs off `WFPlanetCrackedEvent` (Content.Shared/_WF/PlanetCracker/Chunk/WFChunkEvents.cs:27, raised by FlagPlanet at Content.Server/_WF/PlanetCracker/Chunk/WFPlanetChunkSystem.Extraction.cs:470-471). F9 adds NO event of its own; the necromorph hook IS WFPlanetCrackedEvent and is only documented, never widened (widening breaks GravityAnchorTest.cs:1183/:1213). F7's chain is COMMITTED and F9 coexists with it by filter, not by hope: WFCrackerReleasingEvent exists at WFCrackEvents.cs:26-27 and is never referenced here, and F9's positive `== Cracking` filter cannot be reached by Cracked -> Disconnecting -> Released -> Idle.

D9-B. SANCTIONED IS READ OFF THE BODY, NEVER OFF THE PROTOTYPE. `WFSectorPlanetComponent.Sanctioned` (Content.Shared/_WF/PlanetCracker/Planets/WFSectorPlanetComponent.cs:38), the copy F2 D-J declares canonical, whose single production writer is WFPlanetRegistrySystem.ApplySurface (Content.Server/_WF/PlanetCracker/Planets/WFPlanetRegistrySystem.cs:107-115, the write at :111). The prototype copy (WFPlanetSurfacePrototype.cs:53) is frozen at network-build time and is deliberately NOT used. F9 adds no yield work: F2 already ships the unsanctioned multiplier.

D9-C. THE PLANET IS RESOLVED BY THE ORBIT WALK, WITH NO GROUND FALLBACK. `Transform(args.Cracker).MapUid` -> `SharedWFCrackerSystem.TryGetPlanetFromOrbit` (Content.Shared/_WF/PlanetCracker/Cracker/SharedWFCrackerSystem.cs:106), exactly as IsPlanetCracked does at :124. The hull is always on the orbit layer when it enters Cracking. No resolution, no notice, no log spam.

D9-D (REWRITTEN — critique 1 upheld). THE BEGIN LATCH IS PER-CUT, NOT PER-PLANET-FOREVER. The old plan latched `AnnouncedCracking` permanently on the body, which silenced every LATER unsanctioned cut on that world — including the retry after TSF players force an abort, which is the exact situation D7's notice exists to summon them to. Verified: StartAbort (WFCrackerSystem.Crack.cs:330) runs from Cracking/Cracked, FinishAbort (:350) clears every timer and ends `SetState(ent, pending)` at :375 with pending = AnchorsPlaced or Surveying, and the planet is only permanently flagged at extraction (Extraction.cs:467), so the world stays crackable. Fix, and the invariant it rests on: TryBegin (WFCrackerSystem.Lock.cs:75) refuses unless the hull is in AnchorsLocked (ComputeBlockers :111-113), so EVERY legitimate begin — first cut or re-begin after an abort — passes through a `SetState(..., AnchorsLocked)` edge (StateMachine.cs:100 from OnDrillFinished, :177 from ReconcilePair). Therefore: the handler CLEARS AnnouncedCracking on the `args.New == WFCrackState.AnchorsLocked` edge, before anything else and before the CVar check. AnnouncedExtraction is never cleared — the planet is Cracked forever after extraction. Net behaviour: one notice per real cut attempt; an admin `wfcracker state cracking` from AnchorsPlaced (WFCrackerCommand.cs:139-153, SetState at :150), which skips AnchorsLocked, is absorbed by the latch. Chosen over the critique's own suggestion (clear on `Old == Cracking && New != Cracked`) because that edge is indistinguishable from the admin re-entry the critique also wants absorbed; AnchorsLocked separates the two cleanly and is testable through the same seam.

D9-E (JUSTIFICATION CORRECTED). GLOBAL ANNOUNCEMENT, ONE CALL, NO RADIO. A filtered announcement API DOES exist — ChatSystem.DispatchFilteredAnnouncement (Content.Server/Chat/Systems/ChatSystem.cs:388-406), which F7 already uses twice for in-hull lines (WFPlanetChunkSystem.Disconnect.cs:270-275, WFCrackerSystem.Disconnect.cs:327-332) — but nothing in the fork can build an "everyone in this sector" Filter, so F9 uses DispatchGlobalAnnouncement (ChatSystem.cs:359-376: ChatMessageToAll at :370 + Filter.Broadcast() at :373). DispatchStationAnnouncement is a silent no-op off-station, and every TSF-reading radio channel is non-longRange and telecomms-gated, so a radio echo from an orbit layer would be inaudible. Call shape copies WarLevelSystem.SetLevel (Content.Server/_Mono/AlertLevel/WarLevelSystem.cs:31-36): pre-formatted Loc string, own sender key, SoundPathSpecifier, colorOverride, ALL PASSED BY NAME (critique 4). The global overload resolves sound through `_audio.ResolveSound` (:373), so a SoundPathSpecifier is correct there.

D9-F. SHIP NAME = `Name(args.Cracker)`; PLANET NAME = `Name(planet.Owner)`. The cracker entity IS the grid and Wolfgate's TryAssignDeed sets the grid's entity name, so MetaData reads identically on a deeded hull. ShuttleDeedComponent is NOT touched ([Access]-gated, defaults to the literal "Unknown"). Planet name precedent: WFSurveyConsoleSystem.cs:162 reads `MetaData(body.Owner).EntityName` for the same bodies.

D9-G (DECIDED, no fallback — critique 11). The CVar is ONE appended block in Content.Shared/_WF/CCVar/PlanetCrackerCVars.cs (which today holds exactly one def, PlanetNetworks at :13-16): `wf.planet_cracker.announce`, default true, CVar.SERVERONLY. That file is _WF, so zero-upstream-edits holds; the orchestrator's "acceptable if it costs one line" allowance covers it. The [CVarDefs]-class-under-Sanction/ alternative is REJECTED outright and must not be revisited by the implementer. Checked BEFORE the announce path but AFTER the latch clear, so muting neither burns nor strands a latch.

D9-H (NEW — critiques 3 and 7). F9 SHIPS NO TEST PROTOTYPE. An unsanctioned test surface already exists: `WFTestBareSurface` (Content.IntegrationTests/Tests/_WF/PlanetCracker/SurveyConsoleTest.cs:43-49 — planetType PlanetThrascias, ground WFAsclepiuSurface, buildAtRoundStart false, `sanctioned: false` at :49). [TestPrototypes] fields are discovered assembly-wide and loaded into every pair (RobustToolbox/Robust.UnitTesting/Pool/PoolManager.cs:30/:310-328, TestPair.cs:90-91), so CrackSanctionTest can name it directly. The old plan's own prototype was both redundant and wrong: it took `planetType: PlanetTypeBarren`, already claimed by DeepVeinTest.cs:71-72's WFTestVeinSurface.

=== 1. Content.Server/_WF/PlanetCracker/Sanction/WFCrackNoticeComponent.cs (NEW) ===
```
namespace Content.Server._WF.PlanetCracker.Sanction;

/// <summary>Which sanction notices this planet has already raised; the latch that keeps one cut to one notice.</summary>
/// <remarks>
/// Server-only and not networked: nothing client-side reads it, and the body already networks Sanctioned and Cracked
/// (WFSectorPlanetComponent). UnsavedComponent for the same reason WFPlanetNetworkComponent.cs:10 and
/// WFSectorPlanetComponent.cs:9 carry it - this is round state, and a saved map must not come back pre-latched.
/// It holds no deadline, so it deliberately carries no TimeOffsetSerializer/AutoPausedField/AutoGenerateComponentPause;
/// the omission is not the F1/F3 pause trap.
/// </remarks>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFCrackNoticeComponent : Component
{
    /// <summary>Whether the begin-crack notice has gone out for the cut currently under way; cleared at AnchorsLocked.</summary>
    [DataField] public bool AnnouncedCracking;

    /// <summary>Whether the extraction notice has gone out for this world; never cleared, the cut cannot repeat.</summary>
    [DataField] public bool AnnouncedExtraction;
}
```

=== 2. Content.Server/_WF/PlanetCracker/Sanction/WFCrackSanctionSystem.cs (NEW) ===
`public sealed partial class WFCrackSanctionSystem : EntitySystem`, namespace Content.Server._WF.PlanetCracker.Sanction. One file.

Dependencies (house style, no `readonly`, matching WFSurveyConsoleSystem.cs:27-32):
```
[Dependency] private ChatSystem _chat = default!;
[Dependency] private IConfigurationManager _cfg = default!;
[Dependency] private SharedWFCrackerSystem _crackers = default!;
```
(SharedWFCrackerSystem is abstract but resolvable server-side: WFSurveyConsoleSystem.cs:31 injects it exactly this way.)

Consts:
```
/// <summary>Announcement sender for every sanction notice; NOT wf-crack-announce-sender, which is the hull's own "Crack control" (cracker.ftl:83).</summary>
private const string SenderKey = "wf-crack-sanction-sender";
private const string CrackingKey = "wf-crack-sanction-cracking";
private const string ExtractedKey = "wf-crack-sanction-extracted";

/// <summary>Alert tone for the begin notice; the extraction notice is text only.</summary>
private static readonly SoundSpecifier AlertSound = new SoundPathSpecifier("/Audio/Announcements/attention.ogg");

/// <summary>Notice colour, distinct from a routine Central Command line.</summary>
private static readonly Color NoticeColour = Color.FromHex("#D7443E");
```
(attention.ogg verified on disk under Resources/Audio/Announcements/.)

Test-visible counters (nothing in Content.IntegrationTests captures chat):
```
/// <summary>How many begin-crack notices have been dispatched; incremented only after the chat call returns.</summary>
public int CrackingNotices { get; private set; }

/// <summary>How many extraction notices have been dispatched.</summary>
public int ExtractionNotices { get; private set; }
```

Initialize:
```
SubscribeLocalEvent<WFCrackStateChangedEvent>(OnCrackStateChanged);
SubscribeLocalEvent<WFPlanetCrackedEvent>(OnPlanetCracked);
```

OnCrackStateChanged(ref WFCrackStateChangedEvent args):
 1. `if (args.New == WFCrackState.AnchorsLocked) { ClearCrackingLatch(args.Cracker); return; }` — D9-D, before every other check including the CVar.
 2. `if (args.New != WFCrackState.Cracking) return;` — positive filter only; never test args.Old.
 3. `if (!_cfg.GetCVar(PlanetCrackerCVars.Announce)) return;`
 4. `if (!TryGetOrbitedPlanet(args.Cracker, out var planet)) return;`
 5. `if (planet.Comp.Sanctioned) return;`
 6. `var notice = EnsureComp<WFCrackNoticeComponent>(planet.Owner); if (notice.AnnouncedCracking) return; notice.AnnouncedCracking = true;`
 7. `Announce(CrackingKey, planet.Owner, args.Cracker, sound: true); CrackingNotices++;` — counter AFTER the dispatch (critique 4).

OnPlanetCracked(ref WFPlanetCrackedEvent args):
 1. `if (!_cfg.GetCVar(PlanetCrackerCVars.Announce)) return;`
 2. `if (!TryComp<WFSectorPlanetComponent>(args.Planet, out var sector) || sector.Sanctioned) return;`
 3. latch on AnnouncedExtraction as above;
 4. `Announce(ExtractedKey, args.Planet, args.Cracker, sound: false); ExtractionNotices++;` — the cheap second notice D7 allows.

Helpers:
```
/// <summary>The sector body this hull is orbiting, or false when it is anywhere else; the only resolution F9 does.</summary>
private bool TryGetOrbitedPlanet(EntityUid cracker, out Entity<WFSectorPlanetComponent> planet)
{
    planet = default;
    return Transform(cracker).MapUid is { } mapUid && _crackers.TryGetPlanetFromOrbit(mapUid, out planet);
}

/// <summary>Re-arms the begin notice for the next cut. AnchorsLocked is the one state TryBegin demands (Lock.cs:75, ComputeBlockers:112), so every real re-begin - including one after an abort - passes through here.</summary>
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

private void Announce(string key, EntityUid planet, EntityUid cracker, bool sound)
{
    _chat.DispatchGlobalAnnouncement(
        BuildNotice(key, planet, cracker),
        sender: Loc.GetString(SenderKey),
        playSound: sound,
        announcementSound: sound ? AlertSound : null,
        colorOverride: NoticeColour);
}
```
Class doc must state: the necromorph/marker hook is WFPlanetCrackedEvent itself (WFChunkEvents.cs:27) and F9 adds nothing for it; D7's "no automated response" means there is deliberately no TSF NPC, no war-level change and no radio echo; the global reach is deliberate because no per-sector Filter exists (D9-E).

=== 3. SUBSCRIPTIONS AND GREP PROOF (RE-BASELINED — critiques 2, 5, 6) ===
F9 adds exactly TWO subscriptions, BOTH BROADCAST, ZERO directed pairs.
 - Census at the real HEAD: `grep -rn "SubscribeLocalEvent" Content.Server/_WF/PlanetCracker Content.Shared/_WF/PlanetCracker | wc -l` = 49 (the old plan's "42" came from F8's plan, written before F7 landed). F9 takes it to 51.
 - PROOF MUST USE `grep -rn`, NOT `git grep`: git grep searches TRACKED files only, so the new untracked files under Sanction/ contribute nothing and a git-grep gate can never observe them (this is why the old gate demanding "44" was unsatisfiable in two independent ways).
 - Full reference census at HEAD for both events (`grep -rn "WFCrackStateChangedEvent\|WFPlanetCrackedEvent" Content.Server Content.Shared Content.Client Content.IntegrationTests`): WFCrackEvents.cs:7 (decl); WFChunkEvents.cs:27 (decl); WFCrackerSystem.cs:117 (raise); WFPlanetChunkSystem.Extraction.cs:470 (raise); WFCrackConsoleSystem.cs:57 + :79 (broadcast subscriber + by-ref handler); **WFPlanetChunkSystem.Disconnect.cs:49 + :54 (F7's committed broadcast subscriber + by-ref handler — MISSING from the previous revision)**; GravityAnchorTest.cs:1168/:1183/:1205/:1213 (test recorder). So F9's handler is the FOURTH broadcast subscriber of WFCrackStateChangedEvent, and the FIRST production subscriber of WFPlanetCrackedEvent.
 - No collision with F7's subscriber: Disconnect.cs:56 acts on `New == Disconnecting` and :68 on `Old == Disconnecting`; F9 acts on `New == Cracking` and `New == AnchorsLocked`. Disjoint, both broadcast, neither a directed (component,event) pair, so the one-owner crash class is untouched.
 - Both events are `[ByRefEvent] readonly record struct`; BOTH handlers must be `private void OnX(ref TEvent args)`. A by-value handler throws at startup.
 - F9 does NOT extend WFAnchorTestEventSystem (GravityAnchorTest.cs:1141): it raises no event.

=== 4. Content.Shared/_WF/CCVar/PlanetCrackerCVars.cs (EDIT, one block) ===
Appended after PlanetNetworks (:13-16):
```
/// <summary>Whether an unsanctioned crack raises sector notices (F9 D7); false mutes both the begin and the extraction line.</summary>
public static readonly CVarDef<bool> Announce =
    CVarDef.Create("wf.planet_cracker.announce", true, CVar.SERVERONLY);
```

=== 5. Resources/Locale/en-US/_WF/planet-cracker/sanction.ftl (NEW) ===
A new file, not an edit to cracker.ftl (F7's locale surface, whose `## Disconnect protocol (F7)` block sits at :82-89). Exact content (LF, trailing newline):
```
## Unsanctioned crack notices (F9, D7). { $planet } is the world's own entity name, { $ship } the cracker hull's.

wf-crack-sanction-sender = TSF Sector Watch
wf-crack-sanction-cracking = Unsanctioned excavation in progress: the vessel { $ship } has begun a planetary crack on { $planet }. No cracking licence is on file for this world.
wf-crack-sanction-extracted = { $ship } has lifted a chunk clear of { $planet }. The unsanctioned crack is complete.
```
Placeholder style matches chunk.ftl:2 (`{ $planet }`). The sender is a NEW key: `wf-crack-announce-sender` (cracker.ftl:83) reads "Crack control", which is the hull's own voice, not a TSF watch.

=== 6. Content.IntegrationTests/Tests/_WF/PlanetCracker/CrackSanctionTest.cs (NEW) ===
`[TestFixture] [TestOf(typeof(WFCrackSanctionSystem))] public sealed class CrackSanctionTest`, `#nullable enable`, `using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;` (the CrackExtractionTest.cs:31 idiom). NO [TestPrototypes] block (D9-H).

Unsanctioned flip, through the one production writer:
`await server.WaitPost(() => ApplySurfaceTo(pair, site.Planet, "WFTestBareSurface"));` (fixture helper at PlanetCrackerFixture.cs:184; the surface is SurveyConsoleTest.cs:43-49). Then assert `GetComponent<WFSectorPlanetComponent>(site.Planet).Sanctioned` is false — which also turns a future edit of that shared surface into a loud failure rather than a silent degrade. Doc must say: this rewrites only the BODY copy, the copy F9 reads (D9-B); WFPlanetNetworkComponent.Surface stays Asclepiu, so terrain, veins and the built stack are untouched and no rebuild is needed.

Counters are read as DELTAS (`var before = sanction.CrackingNotices;`) because PoolManager reuses servers between tests.

Cleanup (critique 9): the fixture's `Teardown(pair, site)` (PlanetCrackerFixture.cs:243) deletes the network and the sector map only, and no test in this family ends without `pair.CleanReturnAsync()`. The class declares its own private `Cleanup(TestPair pair, CrackerSite site)` — a copy of CrackExtractionTest.cs:944-962: delete `FindChunk(entMan)` (public at PlanetCrackerFixture.cs:618, so no fixture edit), wait a tick, `await Teardown(pair, site)`, `await pair.CleanReturnAsync()`. Every test ends with `await Cleanup(pair, site);` — the ones that never cut a chunk free pay only a no-op FindChunk.

Tests (four):
 T1 `UnsanctionedCrackAnnouncesOnce` — BuildReadyToCut (:487) -> flip -> baseline -> BeginCut (:711) -> assert CrackingNotices delta 1, `HasComp<WFCrackNoticeComponent>(site.Planet)`, AnnouncedCracking true. Then, in one WaitPost through the admin seam, `crackers.SetState(cracker, AnchorsPlaced); crackers.SetState(cracker, Cracking);` and assert the delta is STILL 1 (the latch absorbs a re-entry that skipped AnchorsLocked). Then, in a second WaitPost, `SetState(cracker, AnchorsLocked); SetState(cracker, Cracking);` and assert the delta is now 2 — the abort-then-re-begin shape, which D7 requires to summon players again (D9-D).
 T2 `SanctionedCrackIsSilent` — BuildReadyToCut on stock Asclepiu -> baseline -> BeginCut -> CrackingNotices delta 0 and `HasComp<WFCrackNoticeComponent>(site.Planet)` false.
 T3 `NoticesNameThePlanetAndTheShip` — BuildReadyToCut, `SetEntityName` the body and the hull to two distinctive strings via `server.System<MetaDataSystem>()`, then assert `sanction.BuildNotice("wf-crack-sanction-cracking", ...)` and `...-extracted` both contain BOTH names and contain no `{`. Asserts the exact production builder, not chat plumbing.
 T4 `ExtractionAnnouncesForAnUnsanctionedPlanet` — BuildReadyToExtract (:526) -> flip -> baseline -> BeginCut -> CompleteCut (:542) -> ExtractionNotices delta 1, CrackingNotices delta 1, AnnouncedExtraction true. Ends with the chunk-first Cleanup.

=== 7. WHAT F9 DOES NOT DO ===
No radio send; no TSF NPC or war-level change; no widening of WFPlanetCrackedEvent; no marker vein; no yield work; no new test prototype; no edit to cracker.ftl, chunk.ftl, WFCrackEvents.cs, WFChunkEvents.cs, WFCrackerSystem.*, WFPlanetChunkSystem.*, PlanetCrackerFixture.cs or GravityAnchorTest.cs.

=== 8. REJECTED CRITIQUES ===
R-1. Critique 7's MECHANISM, rejected (its surface-choice observation is upheld and folded into D9-H; only the stated cause is wrong). It claims "WFPlanetRegistrySystem.Initialize will hit its duplicate branch on each server start" and that every pair gains a `Planet type "..." has two surfaces` warning. It cannot. That map is built once inside `Initialize` (WFPlanetRegistrySystem.cs:29-38) from `_proto.EnumeratePrototypes<WFPlanetSurfacePrototype>()`, and test prototypes are not present then: TestPair.InitializeAsync generates the server first and only afterwards calls `LoadPrototypes(Manager.TestPrototypes)` (RobustToolbox/Robust.UnitTesting/Pool/TestPair.cs:83-91), which goes through `ProtoMan.LoadString` + `ReloadPrototypes` (:197-204). The registry subscribes no prototype-reload hook (`grep -rn "PrototypesReloaded" Content.Server/_WF/PlanetCracker` is empty), so a test surface NEVER enters `_surfaces` and cannot collide there — which also means the previous plan's own PlanetTypeBarren rationale was moot rather than merely wrong. The same critique's citation `Content.IntegrationTests/PoolManager.Prototypes.cs:18-32` points at a `_testPrototypes` list that is written and never read anywhere in Content.IntegrationTests (`grep -rn "_testPrototypes" Content.IntegrationTests --include=*.cs` returns only its declaration at :11 and its Add at :31); the live path is RobustToolbox's. Its incidental claim that TestPair.cs:90 lowers FailureLogLevel for the CLIENT only IS correct (that line is inside `ClientOptions()`), which is a second reason a server-side Log.Warning would not have failed anything.
R-2. Critique 1's SUGGESTED CLEAR EDGE, rejected in favour of a stricter one (the defect it reports is upheld — see D9-D). Clearing on `args.Old == Cracking && args.New != Cracked` would also clear on the admin `SetState(AnchorsPlaced)` pull that the same critique's T1 expects the latch to absorb, so its own two test expectations cannot both hold under its own fix. `args.New == AnchorsLocked` is the edge every TryBegin-legal re-begin must cross (Lock.cs:75 + ComputeBlockers:112) and the admin pull from Cracking to AnchorsPlaced does not, so both expectations hold.
All other critiques (2, 3, 4, 5, 6, 8, 9, 10, 11) verified and folded in.

## FILES
- [create] Content.Server/_WF/PlanetCracker/Sanction/WFCrackNoticeComponent.cs — Server-only, non-networked, [UnsavedComponent] latch on the sector body: AnnouncedCracking (cleared on the AnchorsLocked edge) and AnnouncedExtraction (permanent). No deadline, so deliberately no AutoGenerateComponentPause.
- [create] Content.Server/_WF/PlanetCracker/Sanction/WFCrackSanctionSystem.cs — Two broadcast by-ref subscriptions (WFCrackStateChangedEvent handling both the AnchorsLocked latch clear and the Cracking notice, WFPlanetCrackedEvent for the extraction notice); resolves the planet by the orbit walk, reads WFSectorPlanetComponent.Sanctioned, latches, and dispatches the global announcement with named arguments. Exposes BuildNotice plus two post-dispatch counters for tests.
- [edit] Content.Shared/_WF/CCVar/PlanetCrackerCVars.cs — Adds wf.planet_cracker.announce (bool, default true, SERVERONLY) beside PlanetNetworks; one CVarDef block, _WF file, no upstream edit. Decided location, no fallback.
- [create] Resources/Locale/en-US/_WF/planet-cracker/sanction.ftl — wf-crack-sanction-sender / -cracking / -extracted with { $planet } and { $ship } placeholders. A new file so F9 never collides with cracker.ftl, and a new sender key because wf-crack-announce-sender (cracker.ftl:83) is the hull's own 'Crack control'.
- [create] Content.IntegrationTests/Tests/_WF/PlanetCracker/CrackSanctionTest.cs — Four tests, no new prototype (reuses SurveyConsoleTest's WFTestBareSurface): announce-once plus latch-clear-on-re-begin at Cracking, silence on a sanctioned world, the notice text carries planet and ship, and the extraction notice. Private chunk-first Cleanup wrapper.

## UPSTREAM HOOKS

## STAGES
### core
Depends on: (none)
Model: opus
Files: Content.Server/_WF/PlanetCracker/Sanction/WFCrackNoticeComponent.cs, Content.Server/_WF/PlanetCracker/Sanction/WFCrackSanctionSystem.cs, Content.Shared/_WF/CCVar/PlanetCrackerCVars.cs
Gate: `dotnet build Content.Server/Content.Server.csproj -c Debug` succeeds (Content.Shared with it). Then paste the output of `grep -rn "SubscribeLocalEvent" Content.Server/_WF/PlanetCracker Content.Shared/_WF/PlanetCracker | wc -l` - it must read 51, up from 49 at HEAD - and of `grep -rn "SubscribeLocalEvent" Content.Server/_WF/PlanetCracker/Sanction`, which must be exactly the two broadcast lines with no `<Component,` generic pair. USE grep -rn, NOT git grep: git grep skips untracked files and would report 49 and nothing under Sanction/. Confirm both handlers take `ref`, and paste the three-line body of OnCrackStateChanged's first branch to show the AnchorsLocked clear comes first.

Write the component and the system exactly as sections 1, 2 and 4 of the plan specify, and append the one CVar block to PlanetCrackerCVars.cs (after PlanetNetworks at :13-16).

Hard constraints:
- Wolfgate style: no license header; a /// <summary> one-liner on the class, every field, every method and the consts; dependencies as `[Dependency] private X _x = default!;` (no `readonly` - match Content.Server/_WF/PlanetCracker/Survey/WFSurveyConsoleSystem.cs:27-32); `public sealed partial class`; LF line endings.
- EXACTLY two subscriptions, both broadcast, both on [ByRefEvent] record structs, both handlers `private void OnX(ref TEvent args)`: SubscribeLocalEvent<WFCrackStateChangedEvent>(OnCrackStateChanged) and SubscribeLocalEvent<WFPlanetCrackedEvent>(OnPlanetCracked). ZERO directed (component,event) pairs. A by-value handler throws at startup.
- THE LATCH IS PER-CUT, NOT PERMANENT. OnCrackStateChanged's FIRST branch is `if (args.New == WFCrackState.AnchorsLocked) { ClearCrackingLatch(args.Cracker); return; }`, before the CVar check and before everything else. Rationale to put in the doc: TryBegin refuses unless the hull is in AnchorsLocked (Content.Server/_WF/PlanetCracker/Cracker/WFCrackerSystem.Lock.cs:75, ComputeBlockers:112), so every legitimate begin - including a re-begin after FinishAbort drops the hull back at WFCrackerSystem.Crack.cs:375 - crosses this edge; a permanent latch would silence exactly the retry D7's notice exists to summon players to. Never clear AnnouncedExtraction.
- Then `if (args.New != WFCrackState.Cracking) return;` - positive filter only, never test args.Old. Then CVar, then the orbit walk (Transform(args.Cracker).MapUid -> _crackers.TryGetPlanetFromOrbit, Content.Shared/_WF/PlanetCracker/Cracker/SharedWFCrackerSystem.cs:106), then `planet.Comp.Sanctioned` early-out, then EnsureComp latch, then Announce, then the counter. No ground-map fallback, no logging on an unresolved planet.
- COUNTERS ARE INCREMENTED AFTER the DispatchGlobalAnnouncement call returns, never before.
- The announcement is ONE _chat.DispatchGlobalAnnouncement call with EVERY optional argument passed BY NAME (sender:, playSound:, announcementSound:, colorOverride:), the way Content.Server/_Mono/AlertLevel/WarLevelSystem.cs:31-36 does, so a ChatSystem signature change breaks the build. SoundPathSpecifier("/Audio/Announcements/attention.ogg") on the begin notice only. No radio, no Filter, no DispatchStationAnnouncement.
- The component carries `[RegisterComponent, UnsavedComponent]` (precedents: Content.Server/_WF/PlanetCracker/Planets/WFPlanetNetworkComponent.cs:10, Content.Shared/_WF/PlanetCracker/Planets/WFSectorPlanetComponent.cs:9) and its remarks must say both why it is unsaved and why it carries no AutoGenerateComponentPause (it holds no deadline, so the omission is not the F1/F3 pause trap).
- Locale keys exactly wf-crack-sanction-sender / -cracking / -extracted, parameters named "planet" and "ship". Do NOT reuse wf-crack-announce-sender (cracker.ftl:83, "Crack control" - the hull's own voice). BuildNotice(string key, EntityUid planet, EntityUid cracker) is public and is the ONLY place the Loc string is built. Ship name is Name(cracker); do not read ShuttleDeedComponent.
- The class doc records that WFPlanetCrackedEvent is itself the necromorph hook and F9 adds nothing for it; that D7's 'no automated response' is deliberate; and that the announcement is global because ChatSystem.DispatchFilteredAnnouncement (ChatSystem.cs:388-406) exists but nothing in the fork can build an 'everyone in this sector' Filter.
- Do NOT edit any file outside this stage's list - in particular not WFCrackerSystem.*, WFPlanetChunkSystem.*, WFCrackEvents.cs, WFChunkEvents.cs, WFSectorPlanetComponent.cs, cracker.ftl or chunk.ftl.

### locale
Depends on: (none)
Model: sonnet
Files: Resources/Locale/en-US/_WF/planet-cracker/sanction.ftl
Gate: `grep -rn "wf-crack-sanction" Resources/Locale` returns only the three definitions in sanction.ftl (use grep -rn, not git grep - the file is untracked and git grep cannot see it). `git status --porcelain -- Resources/Locale/en-US/_WF/planet-cracker` shows one new untracked file and nothing modified.

Create Resources/Locale/en-US/_WF/planet-cracker/sanction.ftl with exactly the content in section 5 of the plan: a `## Unsanctioned crack notices (F9, D7)` comment line, then the three keys wf-crack-sanction-sender, wf-crack-sanction-cracking and wf-crack-sanction-extracted. Key names, placeholder names ({ $planet }, { $ship }) and their spelling must match the plan character for character - the core stage hard-codes them. LF line endings, one trailing newline, no BOM. Do not create or edit any other file; in particular do NOT touch cracker.ftl, chunk.ftl, planets.ftl or survey.ftl. Do not invent extra keys, and do not reuse wf-crack-announce-sender or a company prototype id for the sender.

### tests
Depends on: core, locale
Model: opus
Files: Content.IntegrationTests/Tests/_WF/PlanetCracker/CrackSanctionTest.cs
Gate: `dotnet test Content.IntegrationTests --filter FullyQualifiedName~CrackSanctionTest` passes with all four tests green, and re-running the whole `Tests._WF.PlanetCracker` namespace filter shows no regression in CrackExtractionTest, ChunkDropTest, SurveyConsoleTest or GravityAnchorTest. `git status --porcelain -- Content.IntegrationTests/Tests/_WF/PlanetCracker` lists exactly one new untracked file and no modifications (do not use `git diff --stat`: it cannot see untracked files, and a bare git diff is forbidden here).

Write Content.IntegrationTests/Tests/_WF/PlanetCracker/CrackSanctionTest.cs exactly as section 6 of the plan specifies: `#nullable enable`, `using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;` (the CrackExtractionTest.cs:31 idiom), `[TestFixture] [TestOf(typeof(WFCrackSanctionSystem))]`, a class doc explaining that nothing in Content.IntegrationTests captures chat, so the assertions run on the system's own post-dispatch counters and on its public BuildNotice.

- NO [TestPrototypes] BLOCK. Reuse the existing unsanctioned surface `WFTestBareSurface` (Content.IntegrationTests/Tests/_WF/PlanetCracker/SurveyConsoleTest.cs:43-49, `sanctioned: false` at :49); [TestPrototypes] fields are discovered assembly-wide and loaded into every pair (RobustToolbox/Robust.UnitTesting/Pool/PoolManager.cs:310-328, TestPair.cs:90-91), so it is available here. Do NOT add a surface on planetType PlanetTypeBarren - DeepVeinTest.cs:71-72 already claims it.
- The unsanctioned flip is `await server.WaitPost(() => ApplySurfaceTo(pair, site.Planet, "WFTestBareSurface"));` (PlanetCrackerFixture.cs:184) after the site is built and before BeginCut, with a comment saying it rewrites only the body copy of Sanctioned - exactly the copy F9 reads (F2 D-J) - while WFPlanetNetworkComponent.Surface stays Asclepiu, so no rebuild is needed. Assert the flip took (Sanctioned is false) before relying on it; that also fails loudly if the shared surface is ever changed.
- Build sites with PlanetCrackerFixture only: BuildReadyToCut (:487), BuildReadyToExtract (:526), BeginCut (:711), CompleteCut (:542), ApplySurfaceTo (:184), FindChunk (:618), Teardown (:243). Do not add helpers to PlanetCrackerFixture.cs, do not touch GravityAnchorTest.cs, do not edit any other test file.
- Declare a private `Cleanup(TestPair pair, CrackerSite site)` in this class, a copy of CrackExtractionTest.cs:944-962: delete FindChunk(entMan) if it resolves, wait a tick, `await Teardown(pair, site)`, `await pair.CleanReturnAsync()`. EVERY test ends with `await Cleanup(pair, site);` - plain Teardown alone leaves a live chunk over the stack and skips CleanReturnAsync, which no test in this family omits.
- Read the counters as DELTAS against a baseline captured immediately before the acting call; PoolManager reuses servers across tests.
- Four tests. UnsanctionedCrackAnnouncesOnce: flip -> BeginCut -> delta 1, WFCrackNoticeComponent present, AnnouncedCracking true; then in ONE WaitPost `crackers.SetState(cracker, WFCrackState.AnchorsPlaced); crackers.SetState(cracker, WFCrackState.Cracking);` and assert the delta is STILL 1 (a re-entry that skipped AnchorsLocked is absorbed); then in a SECOND WaitPost `SetState(cracker, WFCrackState.AnchorsLocked); SetState(cracker, WFCrackState.Cracking);` and assert the delta is now 2, with a comment that this is the abort-then-re-begin shape D7 requires to re-summon players. SanctionedCrackIsSilent: stock Asclepiu, delta 0 and no WFCrackNoticeComponent on the body. NoticesNameThePlanetAndTheShip: `server.System<MetaDataSystem>().SetEntityName` two distinctive names onto site.Planet and site.Cracker, then assert BuildNotice output for BOTH message keys contains both names and no '{'. ExtractionAnnouncesForAnUnsanctionedPlanet: BuildReadyToExtract -> flip -> BeginCut -> CompleteCut -> ExtractionNotices delta 1, CrackingNotices delta 1, AnnouncedExtraction true.
- Use `Assert.EnterMultipleScope()` with a message on every assertion, matching CrackExtractionTest.ExtractionFlagsThePlanet (:581-606).


## TESTS
- CrackSanctionTest.UnsanctionedCrackAnnouncesOnce - an unsanctioned body announces exactly once when the cut begins and latches AnnouncedCracking; a re-entry into Cracking that skips AnchorsLocked (the admin pull) adds no second notice; and an AnchorsLocked -> Cracking re-begin, the shape every abort-then-retry takes, announces again (delta 2).
- CrackSanctionTest.SanctionedCrackIsSilent - stock Asclepiu (sanctioned: true) produces zero notices at Cracking and no WFCrackNoticeComponent on the body.
- CrackSanctionTest.NoticesNameThePlanetAndTheShip - WFCrackSanctionSystem.BuildNotice, the exact builder production dispatches, returns strings containing both the renamed sector body and the renamed cracker hull for wf-crack-sanction-cracking and wf-crack-sanction-extracted, with no unresolved '{' placeholder.
- CrackSanctionTest.ExtractionAnnouncesForAnUnsanctionedPlanet - a completed cut on an unsanctioned body raises the second, sound-free extraction notice exactly once (ExtractionNotices delta 1, AnnouncedExtraction true) on top of the one begin notice.
- Regression: CrackExtractionTest, ChunkDropTest, SurveyConsoleTest and GravityAnchorTest still pass, proving the two new broadcast subscribers did not disturb WFCrackStateChangedEvent ordering against F7's committed subscriber at WFPlanetChunkSystem.Disconnect.cs:49, nor the recorder counts.

## OPEN RISKS
- No unsanctioned planet ships in Resources: Resources/Prototypes/_WF/PlanetCracker/planets.yml:20 is `sanctioned: true` on the only surface, and both WFPlanetSurfacePrototype.cs:53 and WFSectorPlanetComponent.cs:38 default true. F9's announcement path is unreachable in a live round until a second surface ships or the flag flips. Accepted - a content gap, not an F9 gap.
- CrackSanctionTest now depends on a [TestPrototypes] surface owned by another fixture (SurveyConsoleTest.cs:43-49). If that surface is deleted the test throws on Index; if its `sanctioned` flips the test fails on its own precondition assert rather than degrading silently. SurveyConsoleTest.cs:184 documents that it needs `sanctioned: false` too, so the flip risk is low.
- The begin latch clears on the AnchorsLocked edge, so an admin who drives a hull AnchorsLocked -> Cracking repeatedly can emit a notice per cycle. That is the debug seam (WFCrackerCommand.cs:139-153) and the CVar mutes it; no production path reaches AnchorsLocked without a real drill completing (StateMachine.cs:100/:177).
- D7 says 'sector announcement' but nothing in the fork can build an 'everyone in this sector' Filter, so DispatchGlobalAnnouncement (round-wide) is used even though ChatSystem.DispatchFilteredAnnouncement (ChatSystem.cs:388-406) exists and F7 uses it for in-hull lines. On a multi-sector shard the notice is louder than the design intends. Accepted, with wf.planet_cracker.announce as the escape hatch.
- The two public counters exist mainly because no integration test in the tree can observe chat; they are production state whose only consumer is the test suite. They are incremented after dispatch, so they prove the call was made, not what ChatSystem did with it - the named arguments are what protect the call shape.
- Counters are per-server and PoolManager reuses servers between tests, so every assertion must be a delta. A future test that forgets this will pass in isolation and fail in a full run.
- WFPlanetCrackedEvent never fires when FlagPlanet cannot resolve a body (WFPlanetChunkSystem.Extraction.cs:459-465), so the extraction notice is silently absent on a bodyless stack (PlanetCrackerFixture.BuildStandalone). Live rounds always have a body.
- The test flips Sanctioned on the body AFTER the network is built, so the body copy and the frozen WFPlanetNetworkComponent.Surface disagree for the rest of that test. F9 reads only the body copy, but a future test asserting vein yield on the same site would read the other source.
- The CVar block lands in Content.Shared/_WF/CCVar/PlanetCrackerCVars.cs, the one file F9 touches outside its own directories. Zero-upstream-edits still holds (that file is _WF) and D9-G closes the question; it is recorded here only so a reviewer sees it was decided, not overlooked.
