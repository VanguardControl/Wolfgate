# F6 implementation plan (architect output, revision 2, 2026-09-14)

Produced by the F6 design workflow after two adversarial review passes. Lands after F2 and F7. Notable decisions: zero upstream edits; do not reuse ItemMiner; cell-only power drained per unit of work via PowerCellSystem.TryUseCharge; output metered into 25-ore batches of the vein's single-count ore entity; veins deplete but are never deleted; the miner is a Dynamic body so it can be dragged.

F6 — CRACK MINER: FILE-LEVEL IMPLEMENTATION PLAN, REVISION 2 (architect output after two adversarial reviews; verified against .claude/worktrees/modest-chaum-01364c)

=== 0. HEADLINE DECISIONS AND THE EVIDENCE ===

E-A. ZERO UPSTREAM EDITS. Every file F6 touches is new or _WF. The in-place edits are WFCrackerCommand.cs, cracker.ftl, boards.yml, plus two doc-comment-only corrections in F2's own shared files (WFDeepVeinComponent.cs:33, WFVeinTablePrototype.cs:36). No `// WOLFGATE` marker anywhere; no upstream file opened. Greenfield proof: `grep -rn "CrackMiner" Content.Server Content.Shared Content.Client Content.IntegrationTests Resources` returns ZERO hits.

E-B. DO NOT REUSE ItemMiner (design PLANET_CRACKER_DESIGN.md:222 is stale here). ItemMinerComponent.Proto (Content.Shared/_Goobstation/ItemMiner/ItemMinerComponent.cs:24) is ONE fixed EntProtoId and ItemMinerSystem spawns exactly one entity of it per interval (Content.Server/_Goobstation/ItemMiner/Systems/ItemMinerSystem.cs:73); F6 spawns the vein's rolled OrePrototype.OreEntity with a per-tick COUNT, has depletion, four visual states and no APC. F6 takes only the SHAPE: a NextTick/Interval accumulator advanced by TimeSpan, gate-then-slide ordering, and TryMergeToContacts.

E-C. THE GATE IS THE INVERSE OF WFGravityAnchorSystem.TryGetPlanetGround. CITATION CORRECTED (reviewer issue 7, valid): TryGetPlanetGround is at Content.Server/_WF/PlanetCracker/Anchors/WFGravityAnchorSystem.cs:343-362 — it demands `xform.MapUid == grid` (:348). The chunk is the opposite: a GRID ON a map, never the map itself (pinned by CrackExtractionTest.TheChunkNeverCarriesPlanetLayer). So F6's helper tests `xform.GridUid is {} grid && HasComp<WFPlanetChunkComponent>(grid) && TryComp<MapGridComponent>(grid, ...)`. No existing grid->bool chunk helper: WFPlanetChunkSystem.TryGetChunk (Content.Server/_WF/PlanetCracker/Chunk/WFPlanetChunkSystem.cs:251) goes cracker->chunk. F6 writes it. The snap-cell enumerator form to copy is TileFreeIgnoring at WFGravityAnchorSystem.cs:403-423 (`while (enumerator.MoveNext(out var other))`), NOT :216-221.

E-D. TWO REFUSALS, NOT THREE. "Another miner already on this tile" is redundant: AnchorableSystem.OnAnchorComplete calls `TileFree(xform.Coordinates, anchorBody)` and pops `anchorable-occupied` (Content.Shared/Construction/EntitySystems/AnchorableSystem.cs:142-148), and the miner keeps the hard MachineMask/MachineLayer fixture inherited from BaseMachineIndestructible (base_structuremachines.yml:10-20). TileFree tests CanCollide && Hard, never BodyType, so the Dynamic-body change in E-N does not weaken this.

E-E. NO POST-HOC UNANCHOR. WFGravityAnchorSystem re-verifies in OnAnchorStateChanged and Unanchors on failure (WFGravityAnchorSystem.cs:128-147) because its NINE-tile footprint is not what the engine validates and its 8 s do-after gives the world time to change. Neither applies: the miner's gate is exactly the one tile the engine registers, a vein cannot move, a grid cannot stop carrying WFPlanetChunkComponent. F6's OnAnchorStateChanged only re-evaluates state and flushes; it never Unanchors. A mapper may leave a miner anchored on the hull as scenery — the Update gate refuses to mine there.

E-F. CELL-ONLY POWER (D15), DRAINED PER UNIT OF WORK, NOT VIA PowerCellDraw. ApcPowerReceiver is dead weight: ParkChunk (WFPlanetChunkSystem.Extraction.cs:423-452) creates gravity and cleanup immunity and nothing else; no APC, no HV, no NodeContainer on a chunk. `needsPower: false` is not "APC optional": PowerNetSystem.IsPoweredCalculate short-circuits on `!NeedsPower` (Content.Server/Power/EntitySystems/PowerNetSystem.cs:330), which is why WFTestGridFactory.cs:133 uses it to fake a cableless hull. PowerCellDrawComponent is rejected: its loop (Content.Server/PowerCell/PowerCellSystem.Draw.cs:13-36) drains whether or not work happens. F6 calls `PowerCellSystem.TryUseCharge(uid, DrawRate)` (Content.Server/PowerCell/PowerCellSystem.cs:178) once per production tick, all-or-nothing through BatterySystem.TryUseCharge (Content.Server/Power/EntitySystems/BatterySystem.cs:248), so an empty cell stops output on the same tick it empties and an exhausted vein burns nothing.

E-G. THE COMPONENT IS SHARED BUT NOT NETWORKED. Nothing on the client reads WFCrackMinerComponent: the sprite is declarative (Appearance + GenericVisualizer) and examine is server-side. So `[RegisterComponent, AutoGenerateComponentPause]`, no [NetworkedComponent], no AutoGenerateComponentState. It lives in Content.Shared/_WF/PlanetCracker/Mining/ so the client registers the type and sits beside the two enums the GenericVisualizer table parses. AutoGenerateComponentPause is legal without networking (ItemMinerComponent.cs:11, MagnetPickupComponent.cs:9).

E-H. Rate IS ORE PER MINUTE. Design §5 (PLANET_CRACKER_DESIGN.md:271) says 150 ore/min with 10-27 min per vein; both committed comments (WFDeepVeinComponent.cs:33, WFVeinTablePrototype.cs:36) say "units per extraction tick", which at a 1 s tick empties a 4,000-unit vein in 27 SECONDS. Stage A rewrites exactly those two comment lines. No field, attribute, default or shape change.

E-I. Remaining IS ALREADY SEEDED BY F2. WFDeepVeinSystem.OnMapInit writes `Remaining = TotalYield` (Content.Server/_WF/PlanetCracker/Survey/WFDeepVeinSystem.cs:81) alongside Rate = table.Rate (:80) and the UnsanctionedMultiplier into TotalYield (:79). F6 seeds nothing.

E-J. DEPLETION, NOT DELETION. Drive Remaining to 0 and switch to Exhausted; the vein entity stays alive. Deleting it would dangle NetEntity ids in every player's WFSurveyedComponent.Revealed set (WFSurveyedComponent.cs:24) which the client overlay and WFDeepVeinVisualsSystem read (Content.Client/_WF/PlanetCracker/Survey/WFDeepVeinVisualsSystem.cs:48).

E-K. THE OUTPUT IS METERED BY A BUFFER. StackSystem.SpawnMultiple (Content.Server/Stack/StackSystem.cs:102) would happily make 40 entities for a whole vein. F6 accumulates into `Buffer` and flushes at BatchSize (25) — one call, one entity, then one TryMergeToContacts — so at most 6 spawns a minute. Always spawn OrePrototype.OreEntity (the single-count XOre1 form, Resources/Prototypes/ore.yml:7/:13/:19/:26), never the "Full" parent: StackComponent.Count defaults to 50 in this fork (Content.Shared/Stacks/StackComponent.cs:19). Never call SpawnMultiple with amount <= 0 — it Log.Errors (StackSystem.cs:104-109).

E-L. THE MINER DIES WITH THE CHUNK, AND STOPS BEFORE IT. F7 E-K deletes the grid via LinkedLifecycleGridSystem.UnparentPlayersFromGrid(deleteGrid: true) 10 s after the crash (F7_IMPLEMENTATION_PLAN.md:58-60); miners, cells and any ore go with it. F6's obligation is to stop cleanly: the tick gates on `chunk.Comp.Dropped` (WFPlanetChunkComponent.cs:41), which flips in DropChunk (WFPlanetChunkSystem.cs:225) before the 60 s evacuation. No WFChunkDroppedEvent subscription: the 1 s tick catches it within a second and the same path kills the ambience.

E-M. NO CLIENT STAGE. Zero files under Content.Client. The sprite table is pure YAML, and the "% charge" examine line comes free from PowerCellSystem's own <PowerCellSlotComponent, ExaminedEvent> handler (Content.Server/PowerCell/PowerCellSystem.cs:49, :234-248).

E-N (NEW, reviewer issue 4, VALID — the miner must be a DYNAMIC body or it cannot be moved). BaseStructure declares `- type: Physics / bodyType: Static` (Resources/Prototypes/Entities/Structures/base_structure.yml:14-15) and BaseMachine inherits it unchanged (base_structuremachines.yml:32-58). Nothing flips BodyType on anchor/unanchor: the only engine files mentioning AnchorStateChangedEvent are TransformComponent.cs, CollideOnAnchorSystem.cs and SharedTransformSystem.Component.cs, none of which touch BodyType. PullingSystem refuses a static body outright (Content.Shared/Movement/Pulling/Systems/PullingSystem.cs:384-387 `if (physics.BodyType == BodyType.Static) return false;`) even though BaseStructure already carries `- type: Pullable` (base_structure.yml:29). So a Static miner, once unwrenched, is welded to its tile forever, and D15's "unwrench it and move it to the next vein" is unreachable in play. F6 therefore does what the sibling planet-cracker machine already does: WFGravityAnchor is `parent: BaseStructureDynamic` (anchors.yml:5) with explicit `- type: Transform / anchored: false / noRot: true` and `- type: Physics / bodyType: Dynamic` (anchors.yml:47-51) and a description that says "just about draggable" (:7). WFCrackMiner keeps `parent: [ BaseMachine, ConstructibleMachine ]` for Damageable/Anchorable/Machine and overrides Transform and Physics in the same two blocks. The hard fixture is untouched, so E-D still holds. Construction is unaffected: ConstructionSystem.Graph.cs:369 copies `newTransform.Anchored = transform.Anchored`, so a miner built out of an anchored machine frame still arrives anchored (and bypasses AnchorAttemptEvent — the Update gate is authoritative, E-E).

E-O (NEW, reviewer issue 9, VALID — the prototype must say `anchored: false`). BaseStructure sets `- type: Transform / anchored: true` (base_structure.yml:9-10) and BaseMachineIndestructible overrides only `noRot` (base_structuremachines.yml:7-9), so a miner with no Transform block spawns ALREADY ANCHORED: every mapped, admin-spawned and `wfcracker mine`-spawned miner would skip the AnchorAttemptEvent gate, and stage E's "spawn the miner unanchored" would be false. The sibling prototypes spell their stance out either way (machines.yml:133-135 WFGravityProjector `anchored: true`; anchors.yml:47-49 WFGravityAnchor `anchored: false`). F6 takes `anchored: false`, which is also what E-N needs.

E-P (NEW, reviewer issue 2, VALID — the Update loop must check Paused). AutoGenerateComponentPause does NOT stop work. ComponentPauseGenerator emits only `SubscribeLocalEvent<X, EntityUnpausedEvent>` adding `args.PausedTime` to the field (RobustToolbox/Robust.Serialization.Generator/ComponentPauseGenerator.cs:160-200); EntityQueryEnumerator does not filter paused entities and IGameTiming.CurTime keeps advancing. A miner on a paused map would keep draining its cell, decrementing Remaining and spawning ore for the whole pause, then stall for exactly the pause length on unpause. Systems that care check explicitly (Content.Server/Power/EntitySystems/PowerNetSystem.cs:395 `if (Paused(uid, metadata))`; Content.Server/Light/EntitySystems/HandheldLightSystem.cs:168). The cited ItemMinerSystem precedent (ItemMinerSystem.cs:30-35) does not, and neither does WFPlanetChunkSystem's watchdog — for the watchdog it is harmless because the comparison is one-way, for a miner it is not. F6 adds `if (Paused(uid)) continue;` before the NextTick comparison; AutoPausedField's job is only to repair the timer across the pause.

E-Q (NEW, reviewer issue 5, VALID — self-recharging cells are an infinite power source and must be refused at the slot). DrawRate is 1 J/s, set by the capacities (a PowerCellHigh must be worth roughly one vein). PowerCellMicroreactor carries `- type: BatterySelfRecharger / autoRecharge: true / autoRechargeRate: 12` (Resources/Prototypes/Entities/Objects/Power/powercells.yml:236-238, in-file comment "takes 1 minute to charge itself back to full" on maxCharge 720, confirming joules per second), i.e. +11 J/s net while mining — the cell can never empty. PowerCellAntique is worse (maxCharge 1200, autoRechargeRate 40, powercells.yml:274-277). Raising DrawRate above 12 is not an option: at 13 J/s a PowerCellHigh lasts 83 seconds and the whole section-4 table collapses. The fix is at the slot, not the tick: ItemSlot carries a Blacklist (Content.Shared/Containers/ItemSlot/ItemSlotsComponent.cs:75) which CanInsertWhitelist honours (ItemSlotsSystem.cs:345-351 `IsBlacklistPass(slot.Blacklist, usedUid)`), and ItemSlot.WhitelistFailPopup (:199) supplies the refusal line. F6's cell_slot gains `blacklist: components: [ BatterySelfRecharger ]` (EntityWhitelist.Components is a plain `string[]`, Content.Shared/Whitelist/EntityWhitelist.cs:32) plus a popup key. Only cells in the slot are read by TryUseCharge, so the slot is the complete gate; stage E pins it with a test.

E-R (NEW, reviewer issues 3 and 8, both VALID and both narrow). (a) WFCrackMiner declares `startingItem: PowerCellHigh`, which ItemSlotsSystem spawns and inserts on MapInit (ItemSlotsSystem.cs:68-80), so TryInsert into cell_slot on a default miner returns false (the slot is occupied — ItemSlotsSystem.cs:325-326). Every cell-behaviour test therefore spawns WFCrackMinerEmpty, and the fixture's SeatCell ejects first and asserts the insert. (b) The "no ContainerContainer entry is needed" mechanism is real — ItemSlotsSystem.Oninitialize calls `_containers.EnsureContainer<ContainerSlot>(uid, id)` per slot (ItemSlotsSystem.cs:84-91) — but PowerCellRecharger is NOT the precedent: its parent is BaseItemRecharger (chargers.yml:71-73), which declares the container explicitly (chargers.yml:64-68), and it is PowerCageRecharger at :109-111 that parents off ConstructibleMachine. The justification comment cites only ItemSlotsSystem.cs:84-91.

=== 0b. REJECTED CRITIQUES ===

R-1. REJECTED (claimed blocker): "OnAnchorStateChanged flushes on the detaching unanchor raised during grid deletion, so SpawnMultiple parents to a terminating grid; every test teardown trips a server error." The detaching AnchorStateChangedEvent is NOT raised when the grid is being deleted. EntityManager.DeleteEntity calls RecursiveFlagEntityTermination FIRST on the whole subtree (RobustToolbox/Robust.Shared/GameObjects/EntityManager.cs:566, :597-628 — the parent is set Terminating at :599 and every child recursed at :627) and only then RecursiveDeleteEntity/DetachEntity (:594, :644). DetachEntityInternal's anchored branch is guarded by `gridMeta.EntityLifeStage <= EntityLifeStage.MapInitialized` (SharedTransformSystem.Component.cs:1598-1600); Terminating is 4 and MapInitialized is 3 (RobustToolbox/Robust.Shared/GameObjects/EntityLifeStage.cs:26-31), so the guard fails and the event at :1605-1606 never fires. The F7 grid delete, the map deletion in PlanetCrackerFixture teardown and pair.CleanReturnAsync all take exactly that path. The one case where Detaching=true DOES reach a miner is the miner being deleted individually while its grid is alive — and there the spawn target (the grid) is not terminating, so SharedTransformSystem.Component.cs:506/:552 are never reached. The predicted cleanup failure does not exist. ADOPTED ANYWAY AS HARDENING, at one line: `if (args.Detaching) return;` as the first line of OnAnchorStateChanged, matching the in-content precedent at Content.Server/Xenoarchaeology/XenoArtifacts/Triggers/Systems/ArtifactAnchorTriggerSystem.cs:17 (the only other content handler that tests the flag; Content.Server/Power/EntitySystems/CableSystem.cs:64 forwards it). The buffered ore is then lost on a mid-batch delete, which is the same call the plan already made for ComponentShutdown, and OnBreakage (Destructible threshold 200) flushes before Destruction (400) in the normal path. No TerminatingOrDeleted guard and no "delete the grid mid-batch" test is added: there is no code path to assert against.

R-2. PARTIALLY REJECTED (claimed minor, issue 7's second half): the assertion "TryGetPlanetGround is at :154 in the tree today" was wrong and is removed — it is at :343-362, exactly as the reviewer says. The :216-221 cite for the enumerator loop was also wrong; the real loop is TileFreeIgnoring at :403-423. Both corrected in E-C and in stage B. The rest of the plan's WFGravityAnchorSystem cites check out: subscriptions :54-63, OnMapInit :67-72, OnAnchorAttempt :75-99 (popup-before-cancel comment at :84), OnAnchorStateChanged :111-152 with the post-hoc Unanchor at :128-147, OnExamined :204-207.

=== 1. SHARED VOCABULARY (STAGE A) ===

1.1 [create] Content.Shared/_WF/PlanetCracker/Mining/WFCrackMinerState.cs — `[Serializable, NetSerializable] public enum WFCrackMinerState : byte { Idle, Mining, Exhausted, Broken }`, shaped after Content.Shared/_WF/PlanetCracker/Anchors/WFAnchorState.cs. No Off member: crack_miner.rsi ships no `off` state and `idle` doubles as off, contradicting ASSET_REQUIREMENTS.md:26 and recorded as such.

1.2 [create] Content.Shared/_WF/PlanetCracker/Mining/WFCrackMinerVisuals.cs — two byte enums in one file, mirroring WFAnchorVisuals.cs:7/:19: `WFCrackMinerVisuals { State }` and `WFCrackMinerVisualLayers { Base, Glow }`.

1.3 [create] Content.Shared/_WF/PlanetCracker/Mining/WFCrackMinerComponent.cs — `[RegisterComponent, AutoGenerateComponentPause]`, fields State, Interval (1 s), NextTick (TimeOffsetSerializer + AutoPausedField), DrawRate (1 J), BatchSize (25), OutputSpread (0.35), Carry, Buffer, BufferedOre. The AutoPausedField remark says what it actually does (repairs the timer across a pause) — the Update loop's `Paused` check is what stops the work (E-P).

1.4 [edit, comment only] WFDeepVeinComponent.cs:33 and WFVeinTablePrototype.cs:36 — "Units per extraction tick" -> "Ore units per minute" (E-H).

=== 2. THE SERVER SYSTEM (STAGE B) ===

2.1 Content.Server/_WF/PlanetCracker/Mining/WFCrackMinerSystem.cs — `public sealed partial class WFCrackMinerSystem : EntitySystem`, dependencies in the house form (no readonly, WFGravityAnchorSystem.cs:30-38): IGameTiming, IPrototypeManager, IRobustRandom, PowerCellSystem, SharedAmbientSoundSystem, SharedAppearanceSystem, SharedMapSystem, SharedPopupSystem, SharedTransformSystem, SharedWFSurveySystem, StackSystem.

SUBSCRIPTIONS — six directed pairs, all on the new component, all grep-proven zero today: MapInitEvent, AnchorAttemptEvent, AnchorStateChangedEvent, ExaminedEvent, BreakageEventArgs, RepairedEvent. All handlers `(Entity<WFCrackMinerComponent> ent, ref TEvent args)`, the form WFGravityAnchorSystem.cs:67/:75/:111/:204 already uses for these same types. NO subscription on WFDeepVeinComponent (<MapInitEvent> WFDeepVeinSystem.cs:28, <ExaminedEvent> SharedWFSurveySystem.cs:33, <ComponentStartup> the client's WFDeepVeinVisualsSystem.cs:48). NO PowerCellChangedEvent / PowerCellSlotEmptyEvent: the 1 s tick re-evaluates within a second of any swap.

UPDATE: `EntityQueryEnumerator<WFCrackMinerComponent, TransformComponent>`; `if (Paused(uid)) continue;` (E-P); `if (_timing.CurTime < comp.NextTick) continue;`; set NextTick = CurTime + Interval; Tick.

TICK, in order: (1) Broken -> return. (2) not anchored or no vein -> Flush, Idle, return. (3) chunk.Comp.Dropped -> Flush, Idle, return. (4) Remaining <= 0 -> Flush, Exhausted, return. (5) !TryUseCharge(uid, DrawRate) -> Flush, Idle, return (no user arg: no popup on a background tick). (6) SetState(Mining). (7) `Carry += Rate * Interval.TotalSeconds / 60f; units = (int)Carry; if (units <= 0) return; Carry -= units;`. (8) clamp to Remaining, decrement Remaining, add to Buffer, record BufferedOre. (9) flush at BatchSize or on exhaustion. (10) Exhausted when Remaining hits 0. The vein is re-resolved every tick and never cached.

RATE MATHS: Rate 150/min at a 1 s Interval is 2.5 units/tick; Carry emits 2,3,2,3… so exactly 150 land per minute for any Rate. Buffer reaches 25 every 10 s.

FLUSH: guard Buffer/BufferedOre, index the OrePrototype, take OreEntity (Log.Error and drop the buffer if either fails), offset by _random.NextVector2(OutputSpread) (MiningSystem.cs:72), SpawnMultiple, zero the buffer, TryMergeToContacts on the single spawned entity (SharedStackSystem.cs:228). Called from the tick, from OnAnchorStateChanged on a real (non-detaching) unanchor, and from OnBreakage. Not from ComponentShutdown, and not on a detaching unanchor (R-1).

SetState: early-return when unchanged, write the field, SetData(WFCrackMinerVisuals.State, state), SetAmbience(state == Mining) — declarative AmbientSound, the anchors.yml:97-102 recipe.

EXAMINE: state word; then vein ore, remaining percent and raw units, and rate; or the no-vein line. No charge line (PowerCellSystem.cs:234-248 supplies it).

2.2 Content.Server/_WF/PlanetCracker/Mining/WFCrackMinerSystem.Placement.cs — TryGetChunk / TryGetVeinAt / TryGetVein (public, as TryGetPlanetGround is, because the command and the tests both use them), OnAnchorAttempt (two popups, each before its Cancel), OnAnchorStateChanged (detaching guard, then flush and state only, never Unanchor).

2.3 Content.Server/_WF/PlanetCracker/Commands/WFCrackerCommand.cs [edit] — `wfcracker veins` and `wfcracker mine`, both arity 1, following ExecuteDrop (:243-258). THREE dependency additions, not two (reviewer issue 6, valid): `_miners`, `_map` and `_survey` — the existing block at :25-29 holds only _transform, _crackers, _anchors, _chunks, _factory, and GetOreName lives on SharedWFSurveySystem (Content.Shared/_WF/PlanetCracker/Survey/SharedWFSurveySystem.cs:92), so without the third the stage would not compile.

=== 3. PROTOTYPES AND LOCALE (STAGE D) ===

3.1 [create] Resources/Prototypes/_WF/PlanetCracker/mining.yml — `WFCrackMiner`, `parent: [ BaseMachine, ConstructibleMachine ]`, with explicit `- type: Transform / anchored: false / noRot: true` and `- type: Physics / bodyType: Dynamic` (E-N, E-O). BaseMachinePowered is deliberately NOT used (E-F). Cell slot declared inline (E-R b). Sprite + Appearance + GenericVisualizer on the two layers. Fixture left at the inherited 1x1 despite the 64x64 art (machines.yml:103-106 reasoning). PowerCellSlot cellSlotId cell_slot, `fitsInCharger: false`. ItemSlots cell_slot: startingItem PowerCellHigh, ejectOnInteract, whitelist tags [ PowerCell, PowerCellSmall ] (BOTH — PowerCellSmall redeclares the Tag list with only its own tag, powercells.yml:82-84, while Medium/High/Hyper inherit BasePowerCell's, :30-32), blacklist components [ BatterySelfRecharger ] and whitelistFailPopup (E-Q). AmbientSound enabled:false. Repairable. Destructible with Breakage at 200 and Destruction at 400 so the `broken` state is reachable. Machine board. WFCrackMiner. Plus `WFCrackMinerEmpty` (suffix Empty, no startingItem) — the prototype every cell test uses (E-R a).
3.2 [edit] boards.yml — append WFCrackMinerCircuitboard on the WFGravityProjectorCircuitboard shape (:19-33).
3.3 [create] Resources/Locale/en-US/_WF/planet-cracker/mining.ftl.
3.4 [edit] cracker.ftl — two lines (:87 help, :98 hint).

=== 4. NUMBERS (the row design §5 is missing) ===
| Tick interval | 1 s |
| Rate, read off the vein | 150 ore/min = 2.5 units/tick, Carry-exact |
| Output batch | 25 units -> one stack entity every 10 s, merged |
| Cell draw | 1 J/tick = 1 J/s |
| PowerCellSmall 360 J | 6 min, 900 ore |
| PowerCellMedium 720 J | 12 min, 1,800 ore |
| PowerCellHigh 1,080 J (startingItem) | 18 min, 2,700 ore — about one average vein |
| PowerCellHyper 1,800 J | 30 min, 4,500 ore |
| PowerCellMicroreactor / PowerCellAntique | REFUSED by the slot blacklist: +12 J/s and +40 J/s self-recharge against a 1 J/s draw would be infinite power (E-Q) |
| Vein 1,500-4,000 units | 10-27 min per vein per miner — matches design §5:271 |
Capacities from powercells.yml:80/:121/:159/:197/:235.

=== 5. STALE DESIGN CLAIMS FOUND ===
(a) PLANET_CRACKER_DESIGN.md:222 "ItemMiner is a good starting point" — false as a base (E-B).
(b) :222 "the existing ore economy prices it" — true only indirectly: OrePrototype (Content.Shared/Mining/OrePrototype.cs:10-26) has no price field; ore entities are priced by PhysicalComposition (Resources/Prototypes/Entities/Objects/Materials/ore.yml:37-39). WFVeinEntry.Value is a SURVEY-RATING input, never a payout multiplier.
(c) :226 names the component CrackMinerComponent; every committed _WF type carries the WF prefix.
(d) :224 "miners refuse to run anywhere except on a chunk over a deep vein" — no helper exists for the chunk half; F6 writes it (E-C). A vein is client-invisible until surveyed (veins.yml:19-22), so F6's answer to "how does a player know where to wrench" is the wf-crack-miner-no-vein popup.
(e) §5:269 "Deep veins per circle = 2 + floor(d/8)" is already recorded stale by F2 D-M (uniform area density, veins.yml:30-37).
(f) §5 and §4 have no crack-miner power row; section 4 above is the missing row.
(g) ASSET_REQUIREMENTS.md:26 "most machines need an off state" — crack_miner.rsi ships none; idle doubles as off.
(h) ASSET_REQUIREMENTS.md:57-73 lists no crack-miner sound; F6 borrows the anchor's circular_saw loop and the table needs a row or a note.
(i) D15's "no cabling across the tether gap" reads as if a tether existed; F5/F7 as built have none. The cell requirement stands because ParkChunk creates no power infrastructure.
(j) NEW: the design's "unwrench it and move it to the next vein" (:222) is only reachable with a dynamic body (E-N); the machine bases in this tree are all static and unpullable, and the only machine in Structures/Machines that overrides bodyType is nuke.yml.

=== 6. STAGE GRAPH ===
Level 1: A (shared vocabulary, sonnet).
Level 2: B (server, opus) and D (prototypes + locale, sonnet) in parallel — disjoint: B touches only Content.Server, D only Resources.
Level 3: E (tests, opus).
Parallel stages run in separate worktrees and are merged. Every shared file has exactly one owner: cracker.ftl and boards.yml -> D; WFCrackerCommand.cs -> B; PlanetCrackerFixture.cs and PlanetCrackerPrototypeTest.cs -> E. Per-stage gates are builds only; the orchestrator's final gate runs lint, a headless server start, the full suite and SandboxTest.

## FILES
- [create] Content.Shared/_WF/PlanetCracker/Mining/WFCrackMinerState.cs — [Serializable, NetSerializable] byte enum { Idle, Mining, Exhausted, Broken } - the miner's one state axis, mirroring Content.Shared/_WF/PlanetCracker/Anchors/WFAnchorState.cs:54. No Off member: crack_miner.rsi has no off state.
- [create] Content.Shared/_WF/PlanetCracker/Mining/WFCrackMinerVisuals.cs — Appearance key enum WFCrackMinerVisuals { State } and sprite layer enum WFCrackMinerVisualLayers { Base, Glow }, both [Serializable, NetSerializable] byte enums, mirroring WFAnchorVisuals.cs:7 and :19. These are the names mining.yml's GenericVisualizer table parses.
- [create] Content.Shared/_WF/PlanetCracker/Mining/WFCrackMinerComponent.cs — [RegisterComponent, AutoGenerateComponentPause], NOT networked (E-G): State, Interval, NextTick (TimeOffsetSerializer + AutoPausedField), DrawRate, BatchSize, OutputSpread, Carry, Buffer, BufferedOre. The AutoPausedField remark states what the generator actually does (repairs the timer on unpause only) - the Update loop's Paused check is what stops the work (E-P).
- [edit] Content.Shared/_WF/PlanetCracker/Survey/WFDeepVeinComponent.cs — Line 33 doc comment ONLY: 'Units per extraction tick, copied from the table. F6's; unused by F2.' -> ore units per MINUTE. No field, attribute, default or shape change (E-H).
- [edit] Content.Shared/_WF/PlanetCracker/Survey/WFVeinTablePrototype.cs — Line 36 doc comment ONLY: 'Units per extraction tick F6 reads off a vein' -> ore units per MINUTE. No other change.
- [create] Content.Server/_WF/PlanetCracker/Mining/WFCrackMinerSystem.cs — Partial system: Initialize (six directed subscriptions), OnMapInit (first-frame appearance + ambience push, NextTick seed), Update (Paused skip + NextTick gate) / Tick (gate order, Carry rate maths, cell drain, Remaining decrement, exhaustion), FlushOutput (SpawnMultiple + TryMergeToContacts), SetState (appearance + ambience), OnExamined, OnBreakage, OnRepaired.
- [create] Content.Server/_WF/PlanetCracker/Mining/WFCrackMinerSystem.Placement.cs — The placement gate and the three public lookups: TryGetChunk (grid carries WFPlanetChunkComponent), TryGetVeinAt (snap-cell enumeration at a tile index), TryGetVein; OnAnchorAttempt (two popups then Cancel), OnAnchorStateChanged (detaching guard, then flush + state only, never Unanchor).
- [edit] Content.Server/_WF/PlanetCracker/Commands/WFCrackerCommand.cs — Two arity-1 admin subcommands: 'wfcracker veins' and 'wfcracker mine'. Two consts, two Subcommands entries, two switch cases, two Execute methods, and THREE [Dependency] additions (_miners, _map, _survey - the block at :25-29 has none of them, and GetOreName lives on SharedWFSurveySystem).
- [create] Resources/Prototypes/_WF/PlanetCracker/mining.yml — WFCrackMiner (parent [ BaseMachine, ConstructibleMachine ] with explicit Transform anchored:false/noRot and Physics bodyType: Dynamic per E-N/E-O, crack_miner.rsi Base+Glow layers, Appearance + GenericVisualizer state table, inline PowerCellSlot + ItemSlots with PowerCellHigh and a BatterySelfRecharger blacklist, AmbientSound, Repairable, Destructible Breakage 200 / Destruction 400, Machine board, WFCrackMiner) and WFCrackMinerEmpty.
- [edit] Resources/Prototypes/_WF/PlanetCracker/boards.yml — Append WFCrackMinerCircuitboard following WFGravityProjectorCircuitboard (:19-33).
- [create] Resources/Locale/en-US/_WF/planet-cracker/mining.ftl — The two anchor-refusal popups, the cell-rejected popup, the examine lines, the four state words and the wfcracker veins/mine command strings.
- [edit] Resources/Locale/en-US/_WF/planet-cracker/cracker.ftl — Two lines: cmd-wfcracker-help (:87) usage string and cmd-wfcracker-hint-sub (:98) gain 'veins' and 'mine'.
- [create] Content.IntegrationTests/Tests/_WF/PlanetCracker/CrackMinerTest.cs — The whole F6 behaviour suite: placement refusals, rate and output, Remaining and exhaustion, cell drain/stop/resume, the self-recharger refusal, the pause case, pull-to-move, moving to a second vein, visual state keys, RSI state existence, the real ride-up-then-mine integration case, and the chunk-dropped stop.
- [edit] Content.IntegrationTests/Tests/_WF/PlanetCracker/PlanetCrackerFixture.cs — Add MinerProto/MinerEmptyProto/CellProto/VeinProto consts and three helpers: BuildMinerSite, SeatCell (eject-then-insert with an assertion, then SetCharge) and OreOnGrid. CONCURRENTLY EDITED BY F2 STAGE E - re-read before editing.
- [edit] Content.IntegrationTests/Tests/_WF/PlanetCracker/PlanetCrackerPrototypeTest.cs — Add "WFCrackMiner" and "WFCrackMinerEmpty" to the Prototypes array (:38-53). CONCURRENTLY EDITED BY F2 STAGE E - re-read before editing.

## UPSTREAM HOOKS

## STAGES
### A-shared
Depends on: (none)
Model: sonnet
Files: Content.Shared/_WF/PlanetCracker/Mining/WFCrackMinerState.cs, Content.Shared/_WF/PlanetCracker/Mining/WFCrackMinerVisuals.cs, Content.Shared/_WF/PlanetCracker/Mining/WFCrackMinerComponent.cs, Content.Shared/_WF/PlanetCracker/Survey/WFDeepVeinComponent.cs, Content.Shared/_WF/PlanetCracker/Survey/WFVeinTablePrototype.cs
Gate: dotnet build Content.Shared/Content.Shared.csproj -c DebugOpt succeeds with no new warnings (the AutoPausedField/AutoGenerateComponentPause source generator must produce no diagnostics).

Create three files under Content.Shared/_WF/PlanetCracker/Mining/, namespace Content.Shared._WF.PlanetCracker.Mining. No license headers. Every public member gets a one-line /// <summary>. LF line endings. Follow Content.Shared/_WF/PlanetCracker/Anchors/WFAnchorState.cs and WFAnchorVisuals.cs verbatim for file shape.

1) WFCrackMinerState.cs:
  using Robust.Shared.Serialization;
  /// <summary>What a crack miner is doing; the one axis its sprite is driven from.</summary>
  /// <remarks>There is no Off member on purpose: crack_miner.rsi ships idle/mining/exhausted/broken and no off state, so idle doubles as off (ASSET_REQUIREMENTS.md:26 says otherwise and is stale).</remarks>
  [Serializable, NetSerializable]
  public enum WFCrackMinerState : byte { Idle, Mining, Exhausted, Broken }

2) WFCrackMinerVisuals.cs - two enums in one file:
  /// <summary>Appearance keys the crack miner's GenericVisualizer table is written against.</summary>
  [Serializable, NetSerializable] public enum WFCrackMinerVisuals : byte { State }
  /// <summary>Sprite layers mining.yml maps, in draw order.</summary>
  [Serializable, NetSerializable] public enum WFCrackMinerVisualLayers : byte { Base, Glow }

3) WFCrackMinerComponent.cs:
  using Content.Shared.Mining; using Robust.Shared.Prototypes; using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
  Class doc: /// <summary>A machine that turns the deep vein anchored under its own tile into ore stacks while its cell holds out. F6.</summary>
  Add a /// <remarks> with TWO points: (a) deliberately NOT [NetworkedComponent] - nothing on the client reads this component (the sprite is Appearance + GenericVisualizer and examine is server-side), so networking it would be dead weight; WFGravityAnchorComponent networks its State only because the client's crack-circle overlay reads it. (b) AutoGenerateComponentPause only repairs NextTick across a pause - the generator emits nothing but an EntityUnpausedEvent handler that adds args.PausedTime (RobustToolbox/Robust.Serialization.Generator/ComponentPauseGenerator.cs:160-200) - so it is WFCrackMinerSystem.Update's own Paused(uid) check that actually stops a paused miner working.
  Attribute line exactly: [RegisterComponent, AutoGenerateComponentPause]
  public sealed partial class WFCrackMinerComponent : Component
  Fields, in this order, each [DataField] unless stated:
   - public WFCrackMinerState State = WFCrackMinerState.Idle;   // summary: current state; the appearance key and the ambience are written from it.
   - public TimeSpan Interval = TimeSpan.FromSeconds(1);        // summary: how often the miner draws charge and cuts rock.
   - [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextTick;  // summary: next production tick; carried across a map pause so an unpaused chunk does not owe a burst of ticks.
   - public float DrawRate = 1f;   // summary: joules taken from the cell per Interval. 1 J/s puts a PowerCellHigh (1080 J) at 18 minutes, roughly one average vein.
   - public int BatchSize = 25;    // summary: ore units buffered before a stack is dropped; 25 at 150/min is one drop every ten seconds and one entity per drop.
   - public float OutputSpread = 0.35f;  // summary: random scatter radius for the dropped stack, so a pile does not stack on one pixel.
   - public float Carry;    // summary: fractional ore carried between ticks, so any Rate lands exactly over a minute.
   - public int Buffer;     // summary: whole ore units cut but not yet dropped.
   - public ProtoId<OrePrototype>? BufferedOre;  // summary: what the buffer is made of, so a flush after the miner leaves its vein still knows what to spawn.

4) Two comment-only edits. In Content.Shared/_WF/PlanetCracker/Survey/WFDeepVeinComponent.cs, replace the summary on line 33 - currently '/// <summary>Units per extraction tick, copied from the table. F6's; unused by F2.</summary>' - with '/// <summary>Ore units per minute this vein gives up, copied from the table. Read by F6's crack miner.</summary>'. In Content.Shared/_WF/PlanetCracker/Survey/WFVeinTablePrototype.cs, replace the summary on line 36 - currently '/// <summary>Units per extraction tick F6 reads off a vein; carried here so one document tunes a world. Unused by F2.</summary>' - with '/// <summary>Ore units per minute a crack miner pulls out of a vein; carried here so one document tunes a world.</summary>'. CHANGE NOTHING ELSE IN EITHER FILE - no fields, no attributes, no defaults, no using lines. The reason is that design PLANET_CRACKER_DESIGN.md:271 says 150 ore/min and 10-27 minutes per vein, which agree with each other, while 'per tick' would empty a 4,000-unit vein in 27 seconds.

### B-server
Depends on: A-shared
Model: opus
Files: Content.Server/_WF/PlanetCracker/Mining/WFCrackMinerSystem.cs, Content.Server/_WF/PlanetCracker/Mining/WFCrackMinerSystem.Placement.cs, Content.Server/_WF/PlanetCracker/Commands/WFCrackerCommand.cs
Gate: dotnet build Content.Server/Content.Server.csproj -c DebugOpt succeeds with no new warnings.

Write the server half. Namespace Content.Server._WF.PlanetCracker.Mining. No license headers, one-line /// <summary> on every member, '[Dependency] private X _x = default!;' with NO readonly (match Content.Server/_WF/PlanetCracker/Anchors/WFGravityAnchorSystem.cs:30-38), LF endings, partial system split across two files.

FILE 1 - WFCrackMinerSystem.cs, 'public sealed partial class WFCrackMinerSystem : EntitySystem'.
Dependencies: IGameTiming _timing, IPrototypeManager _proto, IRobustRandom _random, PowerCellSystem _cell (Content.Server.PowerCell), SharedAmbientSoundSystem _ambient, SharedAppearanceSystem _appearance, SharedMapSystem _map, SharedPopupSystem _popup, SharedTransformSystem _transform, SharedWFSurveySystem _survey, StackSystem _stack (Content.Server.Stack).
Initialize - exactly six directed subscriptions, ALL on WFCrackMinerComponent, all handlers taking (Entity<WFCrackMinerComponent> ent, ref TEvent args) which is the form WFGravityAnchorSystem.cs:54-63 already registers and :67/:75/:111/:204 already implements for these same event types: MapInitEvent -> OnMapInit; AnchorAttemptEvent -> OnAnchorAttempt (Placement file); AnchorStateChangedEvent -> OnAnchorStateChanged (Placement file); ExaminedEvent -> OnExamined; BreakageEventArgs -> OnBreakage; RepairedEvent -> OnRepaired. DO NOT subscribe anything on WFDeepVeinComponent: WFDeepVeinSystem.cs:28 owns <MapInitEvent>, SharedWFSurveySystem.cs:33 owns <ExaminedEvent> and Content.Client/_WF/PlanetCracker/Survey/WFDeepVeinVisualsSystem.cs:48 owns <ComponentStartup>; a second directed subscription on any of those pairs crashes the server at start. DO NOT subscribe PowerCellChangedEvent or PowerCellSlotEmptyEvent - the one-second tick re-evaluates state within a second of any cell swap.
OnMapInit: push _appearance.SetData(uid, WFCrackMinerVisuals.State, comp.State) and _ambient.SetAmbience(uid, comp.State == Mining), then comp.NextTick = _timing.CurTime + comp.Interval. Comment it as the WFGravityAnchorSystem.cs:67-72 first-frame push.
Update(float frameTime): EntityQueryEnumerator<WFCrackMinerComponent, TransformComponent>; FIRST 'if (Paused(uid)) continue;' THEN 'if (_timing.CurTime < comp.NextTick) continue;'; set comp.NextTick = _timing.CurTime + comp.Interval; call Tick. The Paused check is load-bearing and must carry a comment: AutoGenerateComponentPause only emits an EntityUnpausedEvent handler that adds args.PausedTime to the field (RobustToolbox/Robust.Serialization.Generator/ComponentPauseGenerator.cs:160-200); EntityQueryEnumerator does not filter paused entities and IGameTiming.CurTime keeps advancing, so without this check a miner on a paused map would drain its cell, decrement Remaining and spawn ore for the whole pause and then stall for exactly the pause length afterwards. The house precedent for the explicit check is Content.Server/Power/EntitySystems/PowerNetSystem.cs:395 and Content.Server/Light/EntitySystems/HandheldLightSystem.cs:168 (ItemMinerSystem.cs:30-35 omits it and is wrong for the same reason). No system-level sweep accumulator and no frameTime maths.
Tick((uid, comp), xform), in exactly this order:
 1. if (comp.State == WFCrackMinerState.Broken) return;
 2. if (!xform.Anchored || !TryGetVein(xform, out var chunk, out var vein)) { FlushOutput(ent, xform); SetState(ent, Idle); return; }
 3. if (chunk.Comp.Dropped) { FlushOutput; SetState(Idle); return; }  // comment: Dropped flips in DropChunk (WFPlanetChunkSystem.cs:225) before the 60 s evacuation, and F7 deletes the grid after the crash, so a falling chunk must not be sprayed with stacks.
 4. if (vein.Comp.Remaining <= 0) { FlushOutput; SetState(Exhausted); return; }
 5. if (!_cell.TryUseCharge(uid, comp.DrawRate)) { FlushOutput; SetState(Idle); return; }   // no user argument: a background tick must not pop 'power-cell-insufficient' at anyone. TryUseCharge is all-or-nothing through BatterySystem.TryUseCharge (Content.Server/Power/EntitySystems/BatterySystem.cs:248).
 6. SetState(ent, Mining);
 7. comp.Carry += vein.Comp.Rate * (float)comp.Interval.TotalSeconds / 60f; var units = (int)comp.Carry; if (units <= 0) return; comp.Carry -= units;   // comment: Rate is ore per MINUTE (design section 5:271); the carry makes 150/min land as 2,3,2,3 over a one-second tick, exact for any rate.
 8. units = Math.Min(units, vein.Comp.Remaining); vein.Comp.Remaining -= units; comp.Buffer += units; comp.BufferedOre = vein.Comp.Ore;   // do NOT Dirty the vein: Remaining is a plain [DataField] and deliberately not networked (WFDeepVeinComponent.cs:36-39).
 9. if (comp.Buffer >= comp.BatchSize || vein.Comp.Remaining <= 0) FlushOutput(ent, xform);
10. if (vein.Comp.Remaining <= 0) SetState(ent, Exhausted);   // depletion, never deletion: deleting the vein would leave dangling NetEntity ids in every player's WFSurveyedComponent.Revealed set.
FlushOutput(Entity<WFCrackMinerComponent> ent, TransformComponent xform): return when Buffer <= 0 or BufferedOre is null; _proto.TryIndex the OrePrototype and take OreEntity - if either fails, Log.Error naming the ore id, zero the buffer and return; var coords = xform.Coordinates.Offset(_random.NextVector2(comp.OutputSpread)) (Content.Server/Mining/MiningSystem.cs:72 precedent); var spawned = _stack.SpawnMultiple(oreEntity.Value.Id, comp.Buffer, coords) (Content.Server/Stack/StackSystem.cs:102 - never call it with amount <= 0, it Log.Errors at :104); zero Buffer and BufferedOre; then _stack.TryMergeToContacts on the single spawned entity (SharedStackSystem.cs:228). Comment that OreEntity is always the single-count XOre1 form (Resources/Prototypes/ore.yml:7 etc) and that spawning the 'Full' parent would give 50 units because StackComponent.Count defaults to 50 in this fork (Content.Shared/Stacks/StackComponent.cs:19).
SetState(Entity<WFCrackMinerComponent> ent, WFCrackMinerState state): early-return when unchanged; write the field; _appearance.SetData(WFCrackMinerVisuals.State, state); _ambient.SetAmbience(ent.Owner, state == Mining). The ONLY writer of State and the only ambience toggle.
OnBreakage: FlushOutput then SetState(Broken). OnRepaired: if State == Broken, SetState(Idle) and let the next tick promote it.
OnExamined: if (!args.IsInDetailsRange) return; then one PushMarkup per fact: 'wf-crack-miner-examine-state' with ('state', Loc.GetString(GetStateLocId(comp.State))) where GetStateLocId is a private static switch returning wf-crack-miner-state-idle/-mining/-exhausted/-broken (the WFGravityAnchorSystem.cs:204-207 pattern); then, if TryGetVein resolves, 'wf-crack-miner-examine-vein' with ('ore', _survey.GetOreName(vein.Comp.Ore)) (Content.Shared/_WF/PlanetCracker/Survey/SharedWFSurveySystem.cs:92), 'wf-crack-miner-examine-remaining' with ('percent', clamped int of Remaining*100/TotalYield) and ('remaining', vein.Comp.Remaining), and 'wf-crack-miner-examine-rate' with ('rate', (int)vein.Comp.Rate); else 'wf-crack-miner-examine-no-vein'. Do NOT push a charge line: PowerCellSystem already gives any PowerCellSlot holder one (Content.Server/PowerCell/PowerCellSystem.cs:49, :234-248).

FILE 2 - WFCrackMinerSystem.Placement.cs, same partial class.
public bool TryGetChunk(TransformComponent xform, out Entity<WFPlanetChunkComponent> chunk, out Entity<MapGridComponent> grid): require xform.GridUid is {} gridUid, TryComp<WFPlanetChunkComponent>, TryComp<MapGridComponent>. Comment that this is the INVERSE of WFGravityAnchorSystem.TryGetPlanetGround (WFGravityAnchorSystem.cs:343-362, whose :348 demands xform.MapUid == grid): a chunk is a grid ON a map and never the map itself, which CrackExtractionTest.TheChunkNeverCarriesPlanetLayer pins, so there is deliberately no MapUid test here.
public bool TryGetVeinAt(Entity<MapGridComponent> grid, Vector2i idx, out Entity<WFDeepVeinComponent> vein): walk _map.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, idx) and return the first WFDeepVeinComponent. The loop form to copy is TileFreeIgnoring at WFGravityAnchorSystem.cs:403-423 ('while (enumerator.MoveNext(out var other))'). Comment that the lookup must be tile-index based, not an area query: F5 re-anchors the vein at the SAME tile index on the chunk grid (Extraction.cs:303-308) and the vein carries no fixture, so only snap-cell membership finds it.
public bool TryGetVein(TransformComponent xform, out Entity<WFPlanetChunkComponent> chunk, out Entity<WFDeepVeinComponent> vein): TryGetChunk, then _map.TileIndicesFor(grid.Owner, grid.Comp, xform.Coordinates), then TryGetVeinAt. Public because the admin command and the tests both use it, the same reason TryGetPlanetGround is public.
OnAnchorAttempt: if (args.Cancelled) return; var xform = Transform(ent); if (!TryGetChunk(xform, out _, out var grid)) { _popup.PopupEntity(Loc.GetString("wf-crack-miner-not-chunk"), ent.Owner, args.User); args.Cancel(); return; } var idx = _map.TileIndicesFor(grid.Owner, grid.Comp, xform.Coordinates); if (!TryGetVeinAt(grid, idx, out _)) { _popup.PopupEntity(Loc.GetString("wf-crack-miner-no-vein"), ent.Owner, args.User); args.Cancel(); }. Comment that the popup must precede args.Cancel() because AnchorAttemptEvent carries no reason field (WFGravityAnchorSystem.cs:84), and that there is deliberately no third 'a miner is already here' refusal: AnchorableSystem.OnAnchorComplete already calls TileFree and pops 'anchorable-occupied' (Content.Shared/Construction/EntitySystems/AnchorableSystem.cs:142-148), and TileFree tests CanCollide && Hard rather than BodyType, so the dynamic body this prototype now uses does not weaken it.
OnAnchorStateChanged, in this exact order:
  (a) 'if (args.Detaching) return;' FIRST, with a comment: Detaching means the entity is being sent to null-space as part of its own deletion (RobustToolbox/Robust.Shared/GameObjects/Systems/SharedTransformSystem.Component.cs:1598-1606), and spawning ore out of a terminating machine is the same call already made for ComponentShutdown - the buffer is deliberately lost. In-content precedent for the flag test: Content.Server/Xenoarchaeology/XenoArtifacts/Triggers/Systems/ArtifactAnchorTriggerSystem.cs:17. Note for the reader that this is NOT what happens when the chunk grid is deleted - EntityManager flags the whole subtree Terminating before detaching (EntityManager.cs:566, :597-628) and DetachEntityInternal's anchored branch is gated on the GRID being at most MapInitialized (SharedTransformSystem.Component.cs:1598-1600), so a grid delete raises no anchor event at all.
  (b) if (!args.Anchored) { FlushOutput(ent, Transform(ent)); if (ent.Comp.State != Broken) SetState(ent, Idle); } and nothing else.
  Comment WHY there is no post-hoc re-verify-and-Unanchor here, unlike WFGravityAnchorSystem.cs:128-147: that exists because the anchor's nine-tile footprint is not what the engine validates and because its eight-second do-after gives the world time to change; the miner's gate is exactly the one tile the engine registers, a vein cannot move and a grid cannot stop carrying WFPlanetChunkComponent, so the Update gate is authoritative and a mapper may leave a miner anchored on a hull as scenery.

FILE 3 - WFCrackerCommand.cs [edit]. Add THREE dependencies to the block at :25-29 (which currently holds only _transform, _crackers, _anchors, _chunks, _factory): '[Dependency] private WFCrackMinerSystem _miners = default!;', '[Dependency] private SharedMapSystem _map = default!;' and '[Dependency] private SharedWFSurveySystem _survey = default!;' - the third is required because GetOreName lives on SharedWFSurveySystem (Content.Shared/_WF/PlanetCracker/Survey/SharedWFSurveySystem.cs:92); add 'using Content.Shared._WF.PlanetCracker.Survey;' and 'using Content.Server._WF.PlanetCracker.Mining;' alongside the existing using block. Add 'private const string SubVeins = "veins";' and 'private const string SubMine = "mine";' to the block at :31-37 and both to the Subcommands array at :45-46. Add two cases to the arity switch at :72-98, both 'when args.Length == 1'. No GetCompletion branch is needed (neither takes an argument). ExecuteVeins(shell): TryGetCracker then _chunks.TryGetChunk (WFPlanetChunkSystem.cs:251), error cmd-wfcracker-no-chunk; enumerate the chunk grid's transform children for WFDeepVeinComponent; write cmd-wfcracker-veins-header with ('count') and ('grid', ToPrettyString) then one cmd-wfcracker-veins-row per vein with ('vein', ToPrettyString), ('ore', _survey.GetOreName(...)), ('remaining'), ('total') and ('miner', whether that vein's tile also holds a WFCrackMinerComponent - resolve it by walking the same snap cell with _map.GetAnchoredEntitiesEnumerator); cmd-wfcracker-veins-none when there are none. ExecuteMine(shell): same resolution, pick the first vein with Remaining > 0 whose tile holds no miner, SpawnAtPosition("WFCrackMiner", the tile centre on the chunk grid), _transform.AnchorEntity at that index, write cmd-wfcracker-mine-placed; if no such vein, cmd-wfcracker-mine-no-vein. Note in a comment that the prototype now spawns UNANCHORED (Transform anchored: false), so the explicit AnchorEntity call is doing real work rather than re-anchoring. Both follow ExecuteDrop's shape at :243-258 (resolve, act, one locale line).

### D-data
Depends on: A-shared
Model: sonnet
Files: Resources/Prototypes/_WF/PlanetCracker/mining.yml, Resources/Prototypes/_WF/PlanetCracker/boards.yml, Resources/Locale/en-US/_WF/planet-cracker/mining.ftl, Resources/Locale/en-US/_WF/planet-cracker/cracker.ftl
Gate: dotnet build -c DebugOpt succeeds (this stage changes no C#; prototype and locale loading is verified by the orchestrator's headless-server gate).

Data only. LF endings, two-space YAML indent, '#' comments in the house voice (see Resources/Prototypes/_WF/PlanetCracker/machines.yml and anchors.yml). Do not touch any C# file.

1) CREATE Resources/Prototypes/_WF/PlanetCracker/mining.yml. First line comment: '# The crack miner: the machine that turns a chunk's deep veins into ore. F6.'
Document one:
- type: entity
  id: WFCrackMiner
  parent: [ BaseMachine, ConstructibleMachine ]
  name: crack miner
  description: A drill head on a squat frame with a battery bay, heavy but draggable. It only bites on cut crust.
  # Comment block, mandatory, four points: (a) BaseMachine and NOT BaseMachinePowered - the chunk has no APC, no HV and no cable (ParkChunk at Content.Server/_WF/PlanetCracker/Chunk/WFPlanetChunkSystem.Extraction.cs:423-452 creates gravity and cleanup immunity and nothing else), so the miner runs on an internal cell per design D15. (b) The Transform and Physics overrides below are load-bearing, not tidiness: BaseStructure sets 'anchored: true' (Resources/Prototypes/Entities/Structures/base_structure.yml:9-10) and 'bodyType: Static' (:14-15), and BaseMachineIndestructible overrides only noRot (base_structuremachines.yml:7-9). Left inherited, the miner would spawn already anchored - skipping the AnchorAttemptEvent gate entirely - and, once unwrenched, would be immovable forever, because PullingSystem refuses a static body (Content.Shared/Movement/Pulling/Systems/PullingSystem.cs:384-387) and nothing in the engine flips BodyType on anchor or unanchor. Design PLANET_CRACKER_DESIGN.md:222 requires that the miner be unwrenched and moved to the next vein, so it takes the same stance WFGravityAnchor does (anchors.yml:47-51). The hard MachineMask/MachineLayer fixture inherited from BaseMachineIndestructible is untouched, so AnchorableSystem.TileFree still keeps two miners off one tile (it tests CanCollide and Hard, never BodyType). (c) The cell slot is declared inline rather than by multi-parenting PowerCellSlotHighItem, because ItemSlots ensures its own ContainerSlot at ComponentInit (ItemSlotsSystem.Oninitialize calls EnsureContainer per slot, Content.Shared/Containers/ItemSlot/ItemSlotsSystem.cs:84-91) so no ContainerContainer entry is needed at all. Cite only that line - do NOT cite PowerCellRecharger, which parents off BaseItemRecharger (chargers.yml:71-73) and declares its container explicitly (chargers.yml:64-68). (d) The placeholder art is 64x64 (2x2) but the fixture is left at the inherited 1x1, for the reason spelled out at machines.yml:103-106 - an even-sided AABB centred on a tile centre straddles four tiles by half-tiles and cannot align to the snap grid, and AnchorEntity claims one tile either way. Keep mapped miners at least two tiles apart.
  components:
  - type: Sprite
    sprite: _WF/PlanetCracker/Structures/crack_miner.rsi
    noRot: true
    layers:
    - map: [ "enum.WFCrackMinerVisualLayers.Base" ]
      state: idle
    - map: [ "enum.WFCrackMinerVisualLayers.Glow" ]
      state: mining-unshaded
      shader: unshaded
      visible: false
  - type: Transform
    anchored: false
    noRot: true
  - type: Physics
    bodyType: Dynamic
  - type: Appearance
  - type: GenericVisualizer
    visuals:
      enum.WFCrackMinerVisuals.State:
        enum.WFCrackMinerVisualLayers.Base:
          Idle:      { state: idle }
          Mining:    { state: mining }
          Exhausted: { state: exhausted }
          Broken:    { state: broken }
        enum.WFCrackMinerVisualLayers.Glow:
          Idle:      { visible: false }
          Mining:    { visible: true }
          Exhausted: { visible: false }
          Broken:    { visible: false }
  # add a comment on the Base table: there is no `off` state in crack_miner.rsi, so Idle doubles as off.
  - type: PowerCellSlot
    cellSlotId: cell_slot
    fitsInCharger: false     # it defaults to true (PowerCellSlotComponent.cs:23); an anchored machine must not be shoved into a recharger
  - type: ItemSlots
    slots:
      cell_slot:
        name: power-cell-slot-component-slot-name-default
        startingItem: PowerCellHigh
        ejectOnInteract: true
        whitelist:
          tags:
          - PowerCell
          - PowerCellSmall
        blacklist:
          components:
          - BatterySelfRecharger
        whitelistFailPopup: wf-crack-miner-cell-rejected
  # comment on the whitelist: BOTH tags are required. PowerCellSmall redeclares the Tag list with only its own tag (powercells.yml:82-84) while Medium/High/Hyper inherit BasePowerCell's PowerCell (:30-32), so a whitelist of PowerCell alone silently refuses small cells.
  # comment on the blacklist: self-recharging cells would be an infinite power source and would kill D15's swap entirely. The miner draws 1 J/s; PowerCellMicroreactor self-recharges at 12 J/s (powercells.yml:236-238, its own comment confirming joules per second against maxCharge 720) and PowerCellAntique at 40 J/s (:274-277), so either one can never empty. Raising the draw above 12 is not an option - it would put a PowerCellHigh under ninety seconds - so the refusal lives on the slot. ItemSlotsSystem.CanInsertWhitelist honours Blacklist (ItemSlotsSystem.cs:345-351) and EntityWhitelist.Components is a plain string array of component names (Content.Shared/Whitelist/EntityWhitelist.cs:32).
  # comment: the wrench is safe regardless of the slot, because AnchorableSystem subscribes InteractUsingEvent before: ItemSlotsSystem (AnchorableSystem.cs:46-47).
  - type: AmbientSound
    enabled: false
    sound:
      path: /Audio/Ambience/Objects/circular_saw.ogg
    range: 8
    volume: -5
  # comment: flipped by WFCrackMinerSystem.SetState. ASSET_REQUIREMENTS.md lists no crack-miner loop, so this borrows the gravity anchor's (anchors.yml:97-102).
  - type: Repairable
    qualities: [ Welding, Applicating ]
    fuelCost: 15
    doAfterDelay: 8
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 200
      behaviors:
      - !type:DoActsBehavior
        acts: [ "Breakage" ]
      - !type:PlaySoundBehavior
        sound:
          collection: MetalBreak
    - trigger:
        !type:DamageTrigger
        damage: 400
      behaviors:
      - !type:DoActsBehavior
        acts: [ "Destruction" ]
      - !type:PlaySoundBehavior
        sound:
          collection: MetalBreak
  # comment: BaseMachine's own Destructible has Destruction acts only, so the Breakage threshold that makes the `broken` sprite state reachable has to be spelled out here, exactly as WFGravityProjector does at machines.yml:147-154.
  - type: Machine
    board: WFCrackMinerCircuitboard
  - type: WFCrackMiner
Document two:
- type: entity
  id: WFCrackMinerEmpty
  parent: WFCrackMiner
  suffix: Empty
  # comment: ItemSlots' startingItem is applied on MapInitEvent and a machine spawned onto a live map always map-inits, so every factory- or admin-spawned miner otherwise arrives with a free full PowerCellHigh and its cell slot already occupied - which both hides 'needs a cell' regressions and makes ItemSlotsSystem.TryInsert fail (the slot is full, ItemSlotsSystem.cs:325-326). Every cell-behaviour test uses THIS prototype. Precedent MineralScannerEmpty, scanner.yml:46-54.
  components:
  - type: ItemSlots
    slots:
      cell_slot:
        name: power-cell-slot-component-slot-name-default
        ejectOnInteract: true
        whitelist:
          tags:
          - PowerCell
          - PowerCellSmall
        blacklist:
          components:
          - BatterySelfRecharger
        whitelistFailPopup: wf-crack-miner-cell-rejected

2) EDIT Resources/Prototypes/_WF/PlanetCracker/boards.yml - APPEND (do not touch the two existing documents):
- type: entity
  id: WFCrackMinerCircuitboard
  parent: BaseMachineCircuitboard
  name: crack miner machine board
  description: A machine printed circuit board for a crack miner.
  components:
  - type: Sprite
    state: engineering
  - type: MachineBoard
    prototype: WFCrackMiner
    requirements:
      Manipulator: 2
      Capacitor: 1
    stackRequirements:
      Plasteel: 5

3) CREATE Resources/Locale/en-US/_WF/planet-cracker/mining.ftl with exactly these keys under two '## ' section headers, matching the survey.ftl/chunk.ftl layout:
## Crack miner
wf-crack-miner-not-chunk = The drill will only bite on a slab of cut crust.
wf-crack-miner-no-vein = There is no seam under this tile.
wf-crack-miner-cell-rejected = The bay spits the cell back out; it will not take one that feeds itself.
wf-crack-miner-examine-state = It is { $state }.
wf-crack-miner-state-idle = idle
wf-crack-miner-state-mining = cutting into the rock
wf-crack-miner-state-exhausted = sitting over a spent seam
wf-crack-miner-state-broken = wrecked
wf-crack-miner-examine-vein = It is set over a seam of { $ore }.
wf-crack-miner-examine-remaining = Roughly { $percent }% of the seam is left, about { $remaining } units.
wf-crack-miner-examine-rate = At this seam it cuts about { $rate } units a minute.
wf-crack-miner-examine-no-vein = It is not set over a seam.
## wfcracker mining subcommands
cmd-wfcracker-veins-header = { $count } seams on { $grid }.
cmd-wfcracker-veins-row = { $vein } | { $ore } | { $remaining } / { $total } left | miner: { $miner }
cmd-wfcracker-veins-none = That chunk carries no seams.
cmd-wfcracker-mine-placed = Placed { $miner } on { $vein } ({ $ore }, { $remaining } left).
cmd-wfcracker-mine-no-vein = No free seam on that chunk.

4) EDIT Resources/Locale/en-US/_WF/planet-cracker/cracker.ftl - TWO LINES ONLY. On line 87, append ' | { $command } veins | { $command } mine' to the end of cmd-wfcracker-help. On line 98, change cmd-wfcracker-hint-sub to '<spawn|state|complete|disconnect|fall|extract|drop|veins|mine>'. Do not touch any other line in that file, and do not touch survey.ftl at all - it is being rewritten by the concurrent F2 stage E.

### E-tests
Depends on: B-server, D-data
Model: opus
Files: Content.IntegrationTests/Tests/_WF/PlanetCracker/CrackMinerTest.cs, Content.IntegrationTests/Tests/_WF/PlanetCracker/PlanetCrackerFixture.cs, Content.IntegrationTests/Tests/_WF/PlanetCracker/PlanetCrackerPrototypeTest.cs
Gate: dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj -c DebugOpt succeeds; the orchestrator runs the suite.

BEFORE EDITING: re-read PlanetCrackerFixture.cs and PlanetCrackerPrototypeTest.cs from disk. The concurrent F2 stage E workflow rewrites both files and its version is what you must build on - it adds AttachViewer, ForceMarkers and ApplySurfaceTo to the fixture and new ids to the prototype array. Merge, never overwrite.

FIXTURE ADDITIONS (PlanetCrackerFixture.cs):
- consts: MinerProto = "WFCrackMiner", MinerEmptyProto = "WFCrackMinerEmpty", CellProto = "PowerCellHigh", VeinProto = "WFCrackMinerTestVein" (the test-prototype id declared in CrackMinerTest.cs).
- public static async Task<(CrackerSite Site, EntityUid Vein)> BuildMinerSite(TestPair pair, Vector2i? tile = null): BuildCrackerInOrbit (which uses BuildOwnedStack and therefore a ground layer with WFPlanetLayerComponent -> WFPlanetNetworkComponent -> WFSurfaceAsclepiu whose `veins: WFVeinTableAsclepiu` at planets.yml:21 is what stops the vein self-deleting), lay FloorSteel over the target area, spawn VeinProto at the tile centre and anchor it using the DeployPair recipe verbatim (SpawnEntity at EntityCoordinates(site.Ground, new Vector2(x + 0.5f, y + 0.5f)), one tick, transform.AnchorEntity inside a WaitPost, two ticks - fixture :434-482), then AddComp<WFPlanetChunkComponent> to the GROUND GRID with WatchdogGrace set to an hour and ExtractedAt = CurTime. Comment both traps loudly: (a) WFDeepVeinSystem.OnMapInit QueueDels any vein whose tile is not in AllowedTiles and the fixture lays FloorSteel, which the shipped default { FloorPlanetGrass, FloorPlanetDirt } excludes - hence the test-prototype child; (b) the fake chunk marker exists so the F6 behaviour tests never depend on F5's extraction, and the long WatchdogGrace is what stops WFPlanetChunkSystem's 1 Hz sweep (WFPlanetChunkSystem.cs:122-157) from calling DropChunk on the planet's own ground grid.
- public static async Task<EntityUid> SeatCell(TestPair pair, EntityUid miner, float charge, string cell = CellProto): FIRST eject whatever is in the slot - server.System<ItemSlotsSystem>().TryEject(miner, "cell_slot", null, out _) inside a WaitPost, tolerating an empty slot - THEN spawn the cell, THEN Assert.That(server.System<ItemSlotsSystem>().TryInsert(miner, "cell_slot", cell, null), Is.True) so a silent refusal can never masquerade as a seated cell, THEN server.System<BatterySystem>().SetCharge(cell, charge). Comment why the eject and the assertion are mandatory: WFCrackMiner declares startingItem: PowerCellHigh, which ItemSlotsSystem spawns and inserts on MapInit (ItemSlotsSystem.cs:68-80), so an insert into an occupied slot returns false (CanInsert at :325-326) and the test would go on mining off the free full 1080 J cell. SetCharge is mandatory rather than a field write because BatteryComponent is [Access(typeof(SharedBatterySystem))] (Content.Shared/Power/Components/BatteryComponent.cs:11); read charge back with PowerCellSystem.TryGetBatteryFromSlot (PowerCellSystem.cs:205), the call SynthRechargeTest.cs:172 makes.
- a small static helper OreOnGrid(IEntityManager entMan, EntityUid grid, string oreEntity) returning the summed StackComponent.Count of every child of that grid whose prototype id matches.

PROTOTYPE TEST ADDITION: add "WFCrackMiner" and "WFCrackMinerEmpty" to the Prototypes array (currently :38-53, ending WFSurveyor). Both are safely spawnable on the FloorSteel test grid, unlike WFDeepVein - add a one-line comment saying so. That array already drives EveryPrototypeSpawns and EverySpriteStateExists, which is the RSI check for the two declared sprite layers.

CrackMinerTest.cs - new file, [TestFixture], 'using static Content.IntegrationTests.Tests._WF.PlanetCracker.PlanetCrackerFixture;'. Declare a [TestPrototypes] block with WFCrackMinerTestVein: parent WFDeepVein, categories HideSpawnMenu, overriding `- type: WFDeepVein / allowedTiles: [ FloorSteel ]`. Unless a test says otherwise use MinerEmptyProto and seat the cell explicitly; MinerProto is only for tests that want the free starting cell. Tests:
 1. RefusesToAnchorOffAChunk - build a site WITHOUT the chunk marker (or spawn on the cracker hull), spawn the miner (which arrives unanchored: the prototype sets Transform anchored: false), raise a new AnchorAttemptEvent(user, tool) directed at the miner and assert args.Cancelled is true. Assert on the event rather than driving a wrench and a do-after.
 2. RefusesToAnchorWithoutAVein - on the miner site, place the miner on a tile with no vein, raise AnchorAttemptEvent, assert Cancelled.
 3. AnchorsOverAVein - same site, on the vein's tile, assert the event is NOT cancelled; then AnchorEntity by hand and assert Transform(miner).Anchored.
 4. SpawnsUnanchoredAndDynamic - assert straight off the prototype that Transform(miner).Anchored is false and Comp<PhysicsComponent>(miner).BodyType == BodyType.Dynamic, so the E-N/E-O overrides can never be silently dropped by a later prototype edit.
 5. CanBePulledWhenUnanchored - the player-reachable move path: after unanchoring the miner, assert server.System<PullingSystem>().CanPull(player, miner) is true and TryStartPull succeeds. This is the regression guard for the static-body trap (PullingSystem.cs:384-387) that a code-driven SetCoordinates move would hide.
 6. MinesAtTheVeinRate - anchored over the vein with a full cell, run 60 seconds of ticks, assert the summed ore count on the grid is within one batch of vein.Rate (150) and that TotalYield - Remaining equals the ore produced plus the miner's Buffer.
 7. OutputIsStacksOfTheVeinsOre - assert every spawned entity's prototype id equals _proto.Index<OrePrototype>(vein.Ore).OreEntity, that each carries a StackComponent, and that no stack exceeds its StackPrototype MaxCount (100 for every ore, Resources/Prototypes/Stacks/Materials/ore.yml).
 8. ExhaustsAndStops - write vein.Remaining = 30 directly (a plain [DataField] with no [Access]), tick until it drains, assert Remaining == 0, exactly 30 units of ore on the grid, the appearance data for WFCrackMinerVisuals.State == Exhausted, and that further ticks add no more ore.
 9. StopsWhenTheCellEmpties - MinerEmptyProto, SeatCell with 5 J, tick 10 seconds, assert production stopped after about five ticks, State == Idle, and the cell reads zero through TryGetBatteryFromSlot.
10. ResumesOnCellSwap - from the drained state, SeatCell a fresh full cell (the helper ejects the flat one), tick, assert the ore count grows again and State == Mining.
11. RefusesASelfRechargingCell - MinerEmptyProto, spawn PowerCellMicroreactor, assert ItemSlotsSystem.TryInsert into cell_slot returns FALSE and that after ten seconds of ticks no ore was produced. This pins E-Q: at 12 J/s self-recharge (powercells.yml:236-238) against a 1 J/s draw the cell would never empty and D15's swap would be dead.
12. PausedChunkDoesNotMine - pause the map the miner is on, run 10 s of ticks, assert vein.Remaining and the ore count are unchanged; unpause and assert production resumes within one Interval. This pins E-P, since AutoGenerateComponentPause alone does not stop the Update loop.
13. MovesToAnotherVein - two test veins whose Ore fields are written to differ after MapInit; unanchor the miner, move it to the second vein's tile, re-anchor, tick, assert the second ore type now appears and the second vein's Remaining falls while the first's does not.
14. StopsWhenTheChunkDrops - set chunk.Dropped = true, tick, assert State == Idle and no further ore.
15. VisualStateKeys - drive the miner through idle/mining/exhausted/broken (the last by dealing 200 damage to trip the Breakage threshold) and assert AppearanceComponent carries the matching WFCrackMinerState each time.
16. EveryMinerStateHasAnRsiState - a connected pair ([PoolSettings { Connected = true }]) as in PlanetCrackerPrototypeTest.EverySpriteStateExists (:146-188): spawn the miner on the client, resolve its SpriteComponent's ActualRsi and assert TryGetState succeeds for idle, mining, exhausted, broken and mining-unshaded.
17. RidesUpAndMinesOnARealChunk - the one F5 integration case: BuildCrackerInOrbit, lay FloorSteel across the cut circle, place the test vein at a tile that DiscIndices (fixture :578) says is inside the circle, BuildExtracted, FindChunk, assert Transform(vein).Anchored is still true and Transform(vein).GridUid is the chunk (the BothAnchorsRideUpStillPairedAndLocked assertion at CrackExtractionTest.cs:195), then anchor a miner on the vein's chunk tile, seat a cell, tick and assert ore appears on the chunk grid.
Every test ends with await pair.CleanReturnAsync(). Do NOT write a 'delete the grid mid-batch' test: a grid delete raises no AnchorStateChangedEvent at all (EntityManager.cs:566/:597-628 flags the subtree Terminating before SharedTransformSystem.Component.cs:1598-1600 gates the anchored branch on the grid being at most MapInitialized), so there is nothing to assert against.


## TESTS
- CrackMinerTest.RefusesToAnchorOffAChunk - AnchorAttemptEvent raised on a miner standing on a grid with no WFPlanetChunkComponent comes back Cancelled.
- CrackMinerTest.RefusesToAnchorWithoutAVein - on a chunk grid but on a tile whose snap cell holds no WFDeepVeinComponent, AnchorAttemptEvent comes back Cancelled.
- CrackMinerTest.AnchorsOverAVein - on the vein's own tile index the attempt is not cancelled and the miner anchors.
- CrackMinerTest.SpawnsUnanchoredAndDynamic - straight off the prototype, Transform.Anchored is false and PhysicsComponent.BodyType is Dynamic, so the overrides that make the gate fire and the miner movable cannot be dropped silently.
- CrackMinerTest.CanBePulledWhenUnanchored - PullingSystem.CanPull/TryStartPull succeed on the unanchored miner, i.e. the player-reachable move path the design's 'move it to the next vein' depends on actually exists.
- CrackMinerTest.MinesAtTheVeinRate - 60 s of ticks produce 150 units (ore on the grid plus the miner's unspent Buffer) and TotalYield - Remaining matches.
- CrackMinerTest.OutputIsStacksOfTheVeinsOre - every spawned entity is OrePrototype.OreEntity for the vein's rolled ore, carries a StackComponent, and no stack exceeds maxCount 100.
- CrackMinerTest.ExhaustsAndStops - a vein pinned to Remaining 30 yields exactly 30 units, lands on Remaining 0, flips the appearance key to Exhausted and produces nothing afterwards.
- CrackMinerTest.StopsWhenTheCellEmpties - on WFCrackMinerEmpty a 5 J cell buys five ticks; the battery reads zero, the state falls back to Idle and output halts on the same tick the cell empties.
- CrackMinerTest.ResumesOnCellSwap - ejecting the flat cell and seating a full one resumes production and returns the state to Mining.
- CrackMinerTest.RefusesASelfRechargingCell - PowerCellMicroreactor is refused by the cell slot's BatterySelfRecharger blacklist and no ore is produced, so a +12 J/s cell can never make the 1 J/s miner an infinite-power machine.
- CrackMinerTest.PausedChunkDoesNotMine - a paused map produces no ore and burns no charge or Remaining for ten seconds, and resumes within one Interval on unpause.
- CrackMinerTest.MovesToAnotherVein - unanchor, re-anchor over a second vein with a different ore, and only the second vein's Remaining falls while the second ore type appears.
- CrackMinerTest.StopsWhenTheChunkDrops - setting WFPlanetChunkComponent.Dropped halts production within one tick and drops the state to Idle.
- CrackMinerTest.VisualStateKeys - AppearanceComponent carries WFCrackMinerState Idle / Mining / Exhausted / Broken at the right moments, Broken reached through the Destructible Breakage threshold at 200.
- CrackMinerTest.EveryMinerStateHasAnRsiState - on a connected pair, crack_miner.rsi resolves idle, mining, exhausted, broken and mining-unshaded, one per enum member plus the glow.
- CrackMinerTest.RidesUpAndMinesOnARealChunk - a vein placed inside the cut circle is still Anchored and parented to the chunk grid after BuildExtracted, and a miner wrenched onto its chunk tile produces ore there.
- PlanetCrackerPrototypeTest.EveryPrototypeSpawns / EverySpriteStateExists - extended with WFCrackMiner and WFCrackMinerEmpty, covering prototype indexing, spawn survival and the two declared sprite layers' RSI states.

## OPEN RISKS
- PlanetCrackerFixture.cs and PlanetCrackerPrototypeTest.cs are dirty in this worktree right now - the concurrent F2 stage E workflow owns them (git status shows both M plus PlanetNetworkTest.cs, and three untracked F2 test files: DeepVeinTest.cs, SurveyConsoleTest.cs, SurveyorTest.cs). Stage E of F6 must re-read both from disk at merge time and fold its additions into F2's version, never rebase over it.
- A dynamic-bodied anchorable machine is new ground for a MACHINE in this tree (the only bodyType: Dynamic entry under Entities/Structures/Machines is nuke.yml; WFGravityAnchor gets there through BaseStructureDynamic). It is the correct call for D15's swap-and-move loop, but it means an unanchored miner can be shoved around by explosions and thrown crates the way the gravity anchor can. If that turns out to be a nuisance in play the answer is a heavier density on the inherited fixture, not a return to Static - Static removes the move entirely.
- A miner built from a machine frame arrives anchored without ever raising AnchorAttemptEvent, because ConstructionSystem.Graph.cs:369 copies the frame's anchored state onto the new entity. That is not a hole in the rule - the Update gate refuses to mine off-chunk or off-vein regardless - but a player who builds a miner on the cracker hull gets a machine that looks installed and never runs, and only the examine line says why.
- There is no way to BUY a crack miner. F6 ships the prototype and its machine board but no cargo product and no shipyard entry, so in a live round a miner exists only by admin spawn, mapping or a circuit imprinter plus a machine frame. Where miners come from is a mapping/economy job left open.
- Nothing puts a PowerCellRecharger on the cracker hull. D15's 'swappable' half only works if crews can cycle cells; the recharger prototype exists (chargers.yml:71) and needs no code, but adding it to WFTestGridFactory's layout and to the eventual mapped hull is outside F6's file set. The BatterySelfRecharger blacklist makes this sharper, not softer: with microreactors refused, recharging is the only way to reuse a cell.
- BasePowerCell ships '- type: Riggable' (powercells.yml:35) and PowerCellSystem.OnChargeChanged detonates a rigged cell on its next charge change (:67-70). A sabotaged cell inserted into a miner explodes the first time the miner draws, on an airless chunk, next to whoever wrenched it. Intended stakes, but not optional.
- EMP empties every miner at once: SharedPowerCellSystem.OnCellEmpAttempt relays an EmpAttemptEvent from the cell up to the slot owner (:86-92) and SharedBatterySystem.OnEmpPulse drains the battery (:15-21). F8's site threats should be checked against this before they get an EMP.
- F5's ride-up is not a guarantee. MoveRiders refuses to move an anchored rider whose destination chunk tile is Tile.Empty and leaves it behind with a Log.Error (Extraction.cs:288-291, :310-319), so a vein near the rim or on a tile the biome never generated silently stays on the ground. The number of veins a crack actually delivers is an upper bound.
- WFDeepVeinComponent is [UnsavedComponent] (:18). A partly mined chunk does not survive a map save/load round trip - neither the vein nor its Remaining. Persistence has to be solved outside F6.
- The watchdog can drop a chunk mid-mine: WFPlanetChunkSystem.Update sweeps at 1 Hz and drops five seconds after extraction whenever the cracker's MapUid stops matching (:122-186). The miner holds no uid caches across ticks and gates on Dropped, but a crew that loses its hull loses everything the miners have dropped on the chunk.
- Ore left on the chunk is destroyed with it. F7 E-K deletes the grid ten seconds after the crash; only minded mobs are restored. Nothing in F6 or F7 warns a crew that their ore pile is about to be deleted - an alarm or a console readout for 'ore still on the chunk' is an unowned gap.
- Buffered ore is lost when a miner is deleted outright (the detaching-unanchor guard returns before the flush, and ComponentShutdown never flushes). At BatchSize 25 that is at most ten seconds of output, and the Breakage threshold at 200 flushes before the Destruction threshold at 400 on the normal damage path, so only an admin delete or a gibbing explosion actually loses anything.
- Two miners on one vein is impossible only because AnchorableSystem's TileFree refuses two hard bodies in a cell (AnchorableSystem.cs:142-148). If the miner's fixture is ever softened or its collision layers changed, the 'one miner per seam' rule silently disappears and the vein drains at double rate. The BodyType change does not affect this - TileFree tests CanCollide and Hard - but a future fixture edit would.
- The miner reveals seams by trial: a player who has not surveyed cannot see the vein (veins.yml:19-22) and finds it by wrenching and reading the 'no seam under this tile' popup. That may read as tedious in play; the alternative - the miner revealing the vein to whoever anchors it - is a one-line addition to OnAnchorAttempt if playtesting wants it.
- survey.ftl in this worktree does not currently match WFSurveyCommand's Loc calls (the command passes vein/ore/yield/rich to cmd-wfsurvey-veins-row, which takes ore/yield/tile, and cmd-wfsurvey-veins-header is absent). That is F2's drift, being resolved by the in-flight stage E; F6 must not touch survey.ftl.
