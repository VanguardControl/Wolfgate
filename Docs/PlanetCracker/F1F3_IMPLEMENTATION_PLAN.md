# F1 skeleton + F3 implementation plan (architect output, revision 2, 2026-09-13)

Produced by the F1+F3 design workflow after two adversarial review passes (17 findings, all folded in or rejected with evidence). Orchestrator notes: C# gates run in DebugOpt; headless console commands are exercised through the integration tests instead of a live console; the cloud-layer render cut-off (OPEN RISKS) is deferred to the F4/F5 design.

Every reviewer issue below was re-verified against the worktree. Sixteen distinct findings (four were duplicate pairs); fifteen accepted in full, one accepted as an observation with its proposed fix rejected. See "Rejected critiques" at the end.

# F1 skeleton + F3 implementation plan (architect output, revision 2, 2026-09-13)

Scope: F1 **skeleton only** (components, prototypes and two code-built test grids) plus **F3 in full** (gravity anchors end to end). No BUI, no vessel prototype, no map files, no beams, no fissures, no extraction.

Everything below was verified against the worktree at `.claude/worktrees/modest-chaum-01364c`. Line numbers are from that checkout.

---

## A. Headline decisions and what they rest on

### A.1 Two upstream lines, both in one file

Revision 1 claimed zero upstream edits. That was only achievable by abandoning D11, which the brief lists as settled. This revision spends **two marked lines**, both in `Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs`, and routes everything else through events, new components and a `_WF`-resident partial class. Every other hook that looked like it needed an edit:

| Wanted | Route taken | Evidence |
|---|---|---|
| Veto anchoring on the wrong map / wrong footprint | subscribe `AnchorAttemptEvent` on the new component, popup then `Cancel()` | `Content.Shared/Construction/EntitySystems/AnchorableSystem.cs:273-284` raises it inside `Valid()`; precedent `Content.Server/_NF/Storage/AnchorableStorageSystem.cs:29` |
| React to anchor/unanchor from every cause | subscribe `AnchorStateChangedEvent` (engine), not `UserAnchoredEvent` | one subscription covers wrench, explosion and grid destruction; `Content.Server/_Mono/ScuttleDevice/ScuttleDeviceSystem.cs:121` precedent |
| Pin biome ground under the footprint | `BiomeSystem.ReserveTiles` is **public** | `Content.Server/Parallax/BiomeSystem.PlanetSetup.cs:76` |
| Guarantee both halves of a pair replicate | `EnsureComp<CEPvsOverrideComponent>` at runtime | `Content.Server/_CE/ZLevels/PVS/CEPvsOverrideComponent.cs:9` (bare marker), system at `CEPvsOverrideSystem.cs:33-36` |
| Bind anchors aboard a purchased cracker | **broadcast** `SubscribeLocalEvent<ShipyardShuttlePurchaseEvent>` | raised broadcast at `ShipyardSystem.Consoles.cs:447-448` and `Content.Server/_WF/Shipyard/ShipyardSystem.WolfgateDeed.cs:108-109` |
| Bind anchors aboard an admin/ERT-spawned cracker | call `BindAboard` from `AdminVesselSpawnSystem.TrySpawnVessel` — that file is `_WF` | `Content.Server/_WF/Administration/Systems/AdminVesselSpawnSystem.cs:32-53` raises nothing today |
| Projector part tiers | subscribe `RefreshPartsEvent` / `UpgradeExamineEvent` on the new component | `Content.Server/_NF/Construction/ConstructionSystem.Machine.Upgrades.cs:127/:135` |
| Centrifuge = gravgen variant | prototype only | `GravityGeneratorComponent` is `[Access(typeof(GravityGeneratorSystem))]` (`Content.Server/Gravity/GravityGeneratorComponent.cs:7-8`); F4 will need `_WF` partials, F1 skeleton does not |
| **D11: anchors aboard count against gravgen lift** | `CEZLevelsSystem` is `partial` (`CEZLevelsSystem.cs:18`), so the helper lives in a new `_WF` file; the two call sites are the only upstream lines | see §A.4 |

`Content.Shared/_WF/Administration/WolfgateAdminCommands.cs` gains one const and `AdminVesselSpawnSystem.cs` gains one call; both files are already `_WF`, so per the F0 precedent neither is an upstream edit.

### A.2 No multi-tile entities exist; a 3×3 machine is one anchored tile

`SharedTransformSystem.AnchorEntity` (`RobustToolbox/Robust.Shared/GameObjects/Systems/SharedTransformSystem.Component.cs:73`) registers exactly one `Vector2i` via `SharedMapSystem.AddToSnapGridCell`. `AnchorableSystem` checks exactly one tile (`TileFree(EntityCoordinates,…)` at `:287-296`, used from `:142-147` and `:237-242`). A machine's "3×3-ness" is entirely its `PhysShapeAabb` bounds, exactly as the stock gravity generator does (`Resources/Prototypes/Entities/Structures/Machines/gravity_generator.yml:32-41`, `bounds: "-1.5,-1.5,1.5,1.5"`).

Consequences handled explicitly:
- The 9-tile footprint check is **new code** in the `AnchorAttemptEvent` handler, looping the public `AnchorableSystem.TileFree(Entity<MapGridComponent>, Vector2i, layer, mask)` (`AnchorableSystem.cs:302`).
- The check must be **re-run on completion**. `AnchorAttemptEvent` fires before the 8-second tool do-after starts (`AnchorableSystem.cs:278`, do-after launched at `:249`); `OnAnchorComplete` re-validates only the single tile the engine cares about (`:142-147`). Anything anchored into the other eight tiles during the delay would be silently swallowed. `BeforeAnchoredEvent` is not cancellable (`Content.Shared/Construction/Components/AnchorableComponent.cs:85-88`), so the only non-upstream route is post-hoc: re-run `FootprintFree` in the `AnchorStateChangedEvent{Anchored:true}` handler and `_transform.Unanchor` with a popup if it now fails.
- Only 1 of 9 tiles is protected from biome unload by `HasAnchoredEntity`, so the other 8 must be pinned with `ReserveTiles`.

### A.3 "Planet ground layer" means `GridUid == MapUid`, not `MapUid`

**Corrected from revision 1.** `TransformComponent.MapUid` is the ground-layer map for *every* entity on that layer, including a crate sitting on a landed transport's deck — so a `MapUid`-only test passes inside the transport's cargo bay, which is exactly the case the rule exists to forbid. Anchoring resolves the grid from the coordinates (`AnchorableSystem.cs:288-296` → `_transformSystem.GetGrid(coordinates)`) and registers the entity into *that* grid's snap cell, so the grid is the authority.

On a Wolfgate planet ground layer the map entity **is** the grid: `WFPlanetNetworkSystem.BuildNetwork` spawns the ground via `_planet.SpawnPlanet(surface.Ground, runMapInit: false)` (`Content.Server/_WF/PlanetCracker/Planets/WFPlanetNetworkSystem.cs:122`) and `PlanetNetworkTest.cs:95-99` asserts the ground carries both `MapGridComponent` and `BiomeComponent`. So the gate is:

```csharp
/// <summary>Resolves the biome-backed ground grid an entity is standing on, or fails on any other grid or map.</summary>
private bool TryGetPlanetGround(TransformComponent xform, out Entity<MapGridComponent> ground)
{
    ground = default;
    if (xform.GridUid is not { } grid || xform.MapUid != grid)   // must be ON the ground map itself, not a hull parked on it
        return false;
    if (!HasComp<WFPlanetLayerComponent>(grid))
        return false;
    if (!TryComp<CEZMapComponent>(grid, out var z) || z.Depth != 0)
        return false;
    if (!TryComp<MapGridComponent>(grid, out var mapGrid))
        return false;
    ground = (grid, mapGrid);
    return true;
}
```

`CEZMapComponent` is shared, networked and carries no `[Access]` (`Content.Shared/_CE/ZLevels/Core/Components/CEZMapComponent.cs:13-26`). `CEZGroundLayerComponent` is **not** the test — its own doc comment says it is presentational (`CEZGroundLayerComponent.cs:10-15`).

This also rejects the mapper-loaded base grid (`surface.GroundGrid`, loaded onto the ground map at `WFPlanetNetworkSystem.cs:126-133`), which is correct: `ReserveTiles` would be meaningless there. Every downstream call — `FootprintFree`, `ReserveFootprint`, the crate's `InteractUsingEvent` and its do-after re-check — takes that same resolved `Entity<MapGridComponent>`, so all four agree on one entity.

### A.4 D11 is honoured with two marked upstream lines

`CEZLevelsSystem.HasPooledGravgenSupport` weighs only each **grid body's** `PhysicsComponent.FixturesMass` (`CEZLevelsSystem.Gravity.cs:452`), which is `tileCount × ShuttleSystem.TileDensityMultiplier` = `tileCount × 0.5` (`Content.Server/Shuttles/Systems/ShuttleSystem.cs:85`, applied at `:122-127`). Cargo aboard is a separate body and contributes **zero**. Revision 1 concluded from this that D11 was unenforceable and replaced it with an examine line plus a popup — a warning with no mechanical consequence, i.e. the decision abandoned. The brief lists D11 as settled and budgets one-to-three marked upstream lines per edit, so this revision spends them.

`CEZLevelsSystem` is `public sealed partial class` (`CEZLevelsSystem.cs:18`, and eight more partial files beside it), so the helper is **new `_WF` code**, not an upstream edit:

```csharp
// Content.Server/_WF/PlanetCracker/Cracker/CEZLevelsSystem.WFVirtualMass.cs
/// <summary>Mass content adds to a grid's pooled gravgen load: anchors and crates riding as cargo (design D11).</summary>
public float GetWFVirtualMass(EntityUid grid, IReadOnlyCollection<EntityUid>? networkGrids = null)
```
- returns `WFGridAnchorLoadComponent.VirtualMass` on `grid`, summed over `networkGrids` when one is given.
- `CEZGridNetworkComponent.Grids` is a `HashSet<EntityUid>` (`Content.Shared/_CE/ZLevels/Core/Components/CEZGridNetworkComponent.cs:21`), which satisfies `IReadOnlyCollection<EntityUid>`.

The two upstream lines, each marked `// WOLFGATE`:
1. `CEZLevelsSystem.Gravity.cs`, immediately after line 452 (`mass += body.FixturesMass;`, inside the `foreach`, after the braceless `if` so it runs unconditionally): `mass += GetWFVirtualMass(grid); // WOLFGATE: crated anchors aboard count against pooled lift (D11).`
2. `CEZLevelsSystem.Gravity.cs`, immediately after the `if`/`else` that ends at line 606-607 in `TryGetGravgenLoad`: `gridMass += GetWFVirtualMass(gridUid, networkGrids); // WOLFGATE: same virtual mass the lift check uses, so the readout agrees.` Without this second line the console readout and the test would disagree with the rule that actually drops the ship.

`WFAnchorCapacitySystem` maintains `WFGridAnchorLoadComponent` on each carrying grid at 1 Hz. **Two safety rules on the count, both load-bearing:**
- Only **unanchored** `WFGravityAnchorComponent` / `WFAnchorCrateComponent` entities count. A wrenched-down anchor is terrain, not cargo.
- Grids carrying `WFPlanetLayerComponent` are skipped outright. A planet ground layer is itself a grid; adding 6 mass per deployed anchor to it could flip `HasPooledGravgenSupport` for the whole planet network.

Numbers: `VirtualMass = 6f` per anchor and per crate (a `[DataField]` on both components). Transport hull 31.5 + one anchor 6 = 37.5 ≤ 40 (flies); + a second = 43.5 > 40 (drops). The examine line and the `LargeCaution` popup stay as the friendly warning before the drop.

### A.5 The anchor is one entity, not two

`PullingSystem.CanPull` refuses `BodyType.Static` (`Content.Shared/Movement/Pulling/Systems/PullingSystem.cs:384`). `AnchorEntity` forces `Static` (`SharedTransformSystem.Component.cs:88`) and `Unanchor` restores `Dynamic` (`:152`). Parenting off `BaseStructureDynamic` (`Resources/Prototypes/Entities/Structures/base_structure.yml:38-65`: `anchored: false`, `bodyType: Dynamic`, `- type: Anchorable`, and `- type: Pullable` inherited from `BaseStructure:28`) gives the whole loose↔deployed behaviour for free. The `DeployableBarrier` two-fixture trick is not needed.

The fixture's `layer` is deliberately `[MidImpassable, LowImpassable]` and never `Impassable`: `CESharedZLevelsSystem.IsWall` (`Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.WallCollision.cs:129-141`) only counts `CollisionGroup.Impassable`, so a deployed anchor does not bounce a passing hull. Record it in a YAML comment.

### A.6 Verb kind, and what unanchoring is actually refused for

Both anchor verbs go in **one** `GetVerbsEvent<AlternativeVerb>` subscription. Refusals use `Disabled = true` + `Message` rather than hiding the verb (`Content.Shared/Verbs/Verb.cs:91/:101`), copying `Content.Server/_Mono/ScuttleDevice/ScuttleDeviceSystem.cs:78-99`.

**Corrected from revision 1:** unanchoring is refused **only** when `IsArmed(State)` — `Drilling`, `Locked` or `Off`. Revision 1 also refused in `Paired`, which the design does not ask for and which would make placement irreversible: pairing is automatic on the second wrench-down, so the crew could never adjust `d`, the value that sets cut radius, crack time and vein count. The design says "Unwrenching a **locked** anchor is refused" (`Docs/PlanetCracker/PLANET_CRACKER_DESIGN.md:159`) and explicitly lists `AnchorsPlaced → Surveying` on "anchor unwrenched or destroyed" (`:82`), and D8 says anchors persist between visits (`:300`). Unanchoring a `Paired` anchor dissolves the pair and drops both halves — the transition revision 1 documented but made unreachable.

"Moved out of band" is dropped from the transition table: an anchored entity on a planet ground grid cannot move.

### A.7 `AutoGenerateComponentState(true)` **and** `AutoGenerateComponentPause`

Every new networked component passes `AutoGenerateComponentState(true)` (raise `AfterAutoHandleStateEvent`); the four F0 components next door use the bare attribute, so copying their line silently gives no state event (`RobustToolbox/Robust.Shared/Analyzers/ComponentNetworkGeneratorAuxiliary.cs:55-59`).

**Added in revision 2:** `WFGravityAnchorComponent` must also carry the **class-level** `AutoGenerateComponentPause`. `[AutoPausedField]` on `DrillEnd` is inert without it — the unpause system is generated from the class attribute, not the field (`RobustToolbox/Robust.Shared/Analyzers/ComponentPauseGeneratorAttributes.cs:7-25`: "When this attribute is set on a Component, an EntitySystem will automatically be generated that increments any fields tagged with AutoPausedFieldAttribute"). There is no analyzer diagnostic for the mismatch. Every in-fork user pairs them: `Content.Shared/Anomaly/Components/AnomalyComponent.cs:19` + `:91`, `Content.Shared/Animals/UdderComponent.cs:13`. This matters here because `WFPlanetNetworkSystem` builds every layer with `runMapInit: false` (`:122`, `:145-152`) and admins pause maps; without it a drill running on a paused map completes the instant it unpauses.

### A.8 Event by-ref/by-value, verified per event

RT throws `"Attempted to subscribe by-ref and by-value to the same broadcast event!"` when a subscription disagrees with the `[ByRefEvent]` attribute (`RobustToolbox/Robust.Shared/GameObjects/EntityEventBus.Broadcast.cs:216-222`).

| Event | Kind | Subscribe |
|---|---|---|
| `ShipyardShuttlePurchaseEvent` | plain `sealed class`, **no** `[ByRefEvent]` (`Content.Shared/_Mono/Shipyard/ShipyardShuttlePurchaseEvent.cs:3-7`) | **by value**, broadcast |
| `RefreshPartsEvent`, `UpgradeExamineEvent` | `EntityEventArgs` classes (`ConstructionSystem.Machine.Upgrades.cs:127`, `:135`) | by value, directed |
| `RepairedEvent` | `[ByRefEvent] readonly record struct` (`Content.Shared/Repairable/RepairableSystem.cs:85-87`) | **by ref**, directed |
| `AnchorAttemptEvent`, `UnanchorAttemptEvent`, `BreakageEventArgs` | classes | by value, directed |

Note for the record: `ShipyardShuttlePurchaseEvent` is raised **broadcast** at both sites (`RaiseLocalEvent(purchaseEv)` with no uid, `ShipyardSystem.Consoles.cs:448` and `ShipyardSystem.WolfgateDeed.cs:109`), so the directed subscription at `Content.Server/_Mono/Ships/Systems/ShuttleRestrictionsSystem.cs:33` never fires. That is an upstream bug, out of scope, and it makes our broadcast subscription unambiguously safe.

---

## B. Shared vocabulary

All under `Content.Shared/_WF/PlanetCracker/`. No license headers, `/// <summary>` one-liners, `[Dependency] private X _x = default!;`.

### B.1 `Anchors/WFAnchorState.cs`

```csharp
/// <summary>Lifecycle of one gravity anchor, from crate contents to a locked drill head.</summary>
public enum WFAnchorState : byte
{
    Loose,     // uncrated, unanchored, draggable
    Deployed,  // wrenched down on a planet ground layer, no partner
    Paired,    // partner found within the band on the same ground grid
    Drilling,  // unattended drill running
    Locked,    // drill finished; the pair is targetable by F4
    Off,       // switched off after locking (F7 disconnect)
    Broken,    // destructible Breakage threshold crossed; repairable
}
```

**Transitions** (the only legal moves; `WFGravityAnchorSystem` is the sole writer):

| From | To | Trigger |
|---|---|---|
| `Loose` | `Deployed` | `AnchorStateChangedEvent{Anchored:true}` on a planet ground grid, footprint re-verified |
| `Deployed` | `Loose` | `AnchorStateChangedEvent{Anchored:false}` |
| `Deployed` | `Paired` | `TryPair` succeeds |
| `Paired` | `Deployed`/`Loose` | partner unanchored / broken / destroyed, **or this anchor unanchored** |
| `Paired` | `Drilling` | `AlternativeVerb` "start drill" |
| `Drilling` | `Locked` | `CurTime >= DrillEnd` |
| `Drilling` | `Deployed`/`Loose` | pair dissolved mid-drill |
| `Locked` | `Off` | `AlternativeVerb` "switch off", if no handler cancels `WFAnchorSwitchOffAttemptEvent` |
| `Deployed`/`Paired`/`Drilling`/`Locked`/`Off` | `Broken` | `BreakageEventArgs` |
| `Broken` | `Deployed` (anchored) or `Loose` | `RepairedEvent` |
| any | *entity gone* | `ComponentShutdown` → pair dissolved, `WFAnchorDestroyedEvent` |

`Damaged` is a **flag**, not a state, set independently by `DamageChangedEvent`. **It gates nothing in F3** (see §C.2).

### B.2 `Anchors/WFGravityAnchorComponent.cs`

```csharp
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class WFGravityAnchorComponent : Component
{
    /// <summary>Where this anchor is in its lifecycle; the only writer is WFGravityAnchorSystem.</summary>
    [DataField, AutoNetworkedField] public WFAnchorState State = WFAnchorState.Loose;

    /// <summary>The other anchor of this pair, or null when unpaired.</summary>
    [DataField, AutoNetworkedField] public NetEntity? Partner;

    /// <summary>The cracker grid that owns this anchor, so two crackers cannot share a pair.</summary>
    [DataField, AutoNetworkedField] public NetEntity? Cracker;

    /// <summary>When the running drill finishes.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan DrillEnd;

    /// <summary>How long the drill takes, per design section 5.</summary>
    [DataField] public TimeSpan DrillDuration = TimeSpan.FromMinutes(5);

    /// <summary>True past the damage threshold; networked now, consumed by F4's crack pause. Gates nothing in F3.</summary>
    [DataField, AutoNetworkedField] public bool Damaged;

    /// <summary>Closest two anchor centres may be and still pair, in tiles.</summary>
    [DataField] public float MinDistance = 16f;

    /// <summary>Furthest two anchor centres may be and still pair, in tiles.</summary>
    [DataField] public float MaxDistance = 40f;

    /// <summary>Tiles added to half the pair distance to get the cut radius (design D21).</summary>
    [DataField] public float CutPadding = 2f;

    /// <summary>Damage at which the prototype's Destructible Breakage threshold fires; keep in sync with the YAML.</summary>
    [DataField] public float BreakDamage = 300f;

    /// <summary>Fraction of BreakDamage at which the anchor counts as damaged (design section 5: 50%).</summary>
    [DataField] public float DamageFraction = 0.5f;

    /// <summary>Half-width of the square footprint that must be free and reserved, in tiles.</summary>
    [DataField] public int FootprintRadius = 1;

    /// <summary>Mass this adds to a carrying hull's gravgen load while it rides as cargo (design D11).</summary>
    [DataField] public float VirtualMass = 6f;
}
```

There is no percentage API on `DamageableComponent` — `DamageChangedEvent` (`Content.Shared/Damage/Systems/DamageableSystem.cs:533`) carries `Damageable.TotalDamage` and nothing else — hence the explicit `BreakDamage` mirror, with a test asserting it equals the prototype's Breakage trigger.

`Component.Owner` exists (obsolete) in Robust, so the ownership field is named `Cracker`, never `Owner`.

### B.3 `Anchors/WFAnchorCrateComponent.cs`

```csharp
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFAnchorCrateComponent : Component
{
    /// <summary>What unpacking spawns.</summary>
    [DataField] public EntProtoId Contents = "WFGravityAnchor";

    /// <summary>Tool quality needed to crack the crate open.</summary>
    [DataField] public ProtoId<ToolQualityPrototype> Tool = "Prying";

    /// <summary>Seconds of do-after to unpack.</summary>
    [DataField] public float Delay = 6f;

    /// <summary>The cracker grid this crate (and the anchor inside it) belongs to.</summary>
    [DataField, AutoNetworkedField] public NetEntity? Cracker;

    /// <summary>Half-width of the square the unpacked anchor needs free, in tiles.</summary>
    [DataField] public int FootprintRadius = 1;

    /// <summary>Mass this adds to a carrying hull's gravgen load (design D11).</summary>
    [DataField] public float VirtualMass = 6f;
}
```

Deliberately not `FlatpackComponent`: `SharedFlatpackSystem.OnFlatpackInteractUsing` (`Content.Shared/Construction/SharedFlatpackSystem.cs:62`) checks one tile (`:90-97`), and re-using it would put this handler in the same `(FlatpackComponent, InteractUsingEvent)` pair as upstream.

### B.4 `Anchors/WFAnchorVisuals.cs`

```csharp
public enum WFAnchorVisuals : byte { State, Damaged }
public enum WFAnchorVisualLayers : byte { Base, Glow, Damage }
public enum WFCrateVisuals : byte { Open }
public enum WFCrateVisualLayers : byte { Base, Stencil }
```

Driven entirely from YAML `GenericVisualizer` keyed on enum member names — confirmed for a custom enum at `Resources/Prototypes/_Mono/Entities/Structures/Machines/economy.yml:25-32`. No client visualizer C# needed.

### B.5 `Anchors/WFAnchorEvents.cs`

All broadcast, so any number of later systems may subscribe without hitting the one-directed-subscription rule.

```csharp
/// <summary>Raised when two anchors of the same owner pair up within the band.</summary>
[ByRefEvent] public readonly record struct WFAnchorPairFormedEvent(EntityUid A, EntityUid B, float Distance);

/// <summary>Raised when a pair stops being a pair, for any reason.</summary>
[ByRefEvent] public readonly record struct WFAnchorPairDissolvedEvent(EntityUid A, EntityUid B);

/// <summary>Raised when an anchor's unattended drill starts.</summary>
[ByRefEvent] public readonly record struct WFAnchorDrillStartedEvent(EntityUid Anchor);

/// <summary>Raised when an anchor's drill completes and it locks.</summary>
[ByRefEvent] public readonly record struct WFAnchorDrillFinishedEvent(EntityUid Anchor);

/// <summary>Raised when an anchor crosses (either way) the damage threshold F4 uses to pause the crack.</summary>
[ByRefEvent] public readonly record struct WFAnchorDamagedEvent(EntityUid Anchor, bool Damaged);

/// <summary>Raised when an anchor hits its Breakage threshold and must be repaired and re-locked.</summary>
[ByRefEvent] public readonly record struct WFAnchorBrokenEvent(EntityUid Anchor);

/// <summary>Raised as an anchor entity terminates, after its pair has been dissolved.</summary>
[ByRefEvent] public readonly record struct WFAnchorDestroyedEvent(EntityUid Anchor);

/// <summary>Cancellable: later features (F7) veto switching an anchor off outside the disconnect window.</summary>
public sealed class WFAnchorSwitchOffAttemptEvent(EntityUid anchor, EntityUid user) : CancellableEntityEventArgs
{
    public EntityUid Anchor = anchor;
    public EntityUid User = user;
    public string? Reason;
}

/// <summary>Raised after an anchor is switched off.</summary>
[ByRefEvent] public readonly record struct WFAnchorSwitchedOffEvent(EntityUid Anchor);

/// <summary>Do-after for prying a crate open; must be shared and NetSerializable.</summary>
[Serializable, NetSerializable] public sealed partial class WFAnchorUncrateDoAfterEvent : SimpleDoAfterEvent;
```

`WFAnchorSwitchOffAttemptEvent` is a class raised **broadcast by value** so `Cancel()` works and many systems may veto. F3 subscribes nothing to it; the "only in later states" rule in F3 is the verb's own `State != Locked` guard. F7 adds the real veto.

### B.6 `Anchors/SharedWFGravityAnchorSystem.cs`

Pure helpers, **no subscriptions**:

```csharp
public abstract partial class SharedWFGravityAnchorSystem : EntitySystem
{
    /// <summary>Cut radius for a pair, per design D21: half the centre distance plus the padding.</summary>
    public static float GetCutRadius(float distance, float padding) => distance / 2f + padding;

    /// <summary>True when the two centres are inside the pairing band.</summary>
    public static bool InBand(float distance, float min, float max) => distance >= min && distance <= max;

    /// <summary>True for states where the anchor is drilled in and must not be unwrenched.</summary>
    public static bool IsArmed(WFAnchorState s) => s is WFAnchorState.Drilling or WFAnchorState.Locked or WFAnchorState.Off;
}
```

### B.7 `Cracker/` shared components

```csharp
// WFCrackState.cs — design section 3, verbatim.
public enum WFCrackState : byte
{ Idle, Surveying, AnchorsPlaced, AnchorsLocked, Cracking, Cracked, Disconnecting, Released, Falling }
```

```csharp
// WFPlanetCrackerComponent.cs — on the cracker GRID.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFPlanetCrackerComponent : Component
{
    /// <summary>Current stage of the crack, per design section 3.</summary>
    [DataField, AutoNetworkedField] public WFCrackState State = WFCrackState.Idle;

    /// <summary>The mapper-placed berth marker on this hull, resolved at map init.</summary>
    [DataField, AutoNetworkedField] public NetEntity? Berth;

    /// <summary>The targeted pair, once F4 exists.</summary>
    [DataField, AutoNetworkedField] public NetEntity? AnchorA;
    [DataField, AutoNetworkedField] public NetEntity? AnchorB;
}
```

```csharp
// WFChunkBerthComponent.cs — mapper-placed marker entity on the cracker grid.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFChunkBerthComponent : Component
{
    /// <summary>Berth rectangle in tiles; design section 5 wants at least 48x48 on the real hull.</summary>
    [DataField, AutoNetworkedField] public Vector2i Size = new(48, 48);

    /// <summary>Tiles from the marker to the berth centre, along the marker's own facing.</summary>
    [DataField, AutoNetworkedField] public float Distance = 26f;
}
```

Berth centre is `markerWorldPos + markerWorldRot.ToWorldVec() * Distance`. `Angle.Zero` is `Direction.South` in Robust, so facing north is `Angle.FromDegrees(180)`.

```csharp
// WFCentrifugeComponent.cs — marker so F4 finds the ship's centrifuge without a gravgen query.
[RegisterComponent, NetworkedComponent] public sealed partial class WFCentrifugeComponent : Component;

// WFCrackConsoleComponent.cs / WFSectorSurveyConsoleComponent.cs — shells, no BUI yet.
[RegisterComponent, NetworkedComponent] public sealed partial class WFCrackConsoleComponent : Component;
[RegisterComponent, NetworkedComponent] public sealed partial class WFSectorSurveyConsoleComponent : Component;
```

```csharp
// WFGravityProjectorComponent.cs
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFGravityProjectorComponent : Component
{
    /// <summary>Crack-time multiplier from machine parts: 1.0 at tier 1 down to 0.7 at tier 4 (design D12).</summary>
    [DataField, AutoNetworkedField] public float CrackTimeMultiplier = 1f;

    /// <summary>Per-tier scaling factor; 0.888^3 is 0.70, so tier 4 hits the design's floor.</summary>
    [DataField] public float PartScaling = 0.888f;

    /// <summary>Which part type drives the multiplier.</summary>
    [DataField] public ProtoId<MachinePartPrototype> RatedPart = "Capacitor";

    /// <summary>Set by the Destructible Breakage threshold; there is no engine-side broken flag.</summary>
    [DataField, AutoNetworkedField] public bool Broken;

    /// <summary>What the projector is doing, for the sprite and for F4's grace timer.</summary>
    [DataField, AutoNetworkedField] public WFProjectorState State = WFProjectorState.Off;
}

// WFProjectorVisuals.cs
public enum WFProjectorState : byte { Off, Idle, Charging, Firing, Broken }
public enum WFProjectorVisuals : byte { State }
public enum WFProjectorVisualLayers : byte { Base, Emitter }
```

```csharp
// WFAnchorCapacityComponent.cs — on a hull's gravgen; the player-facing half of D11.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFAnchorCapacityComponent : Component
{
    /// <summary>How many crated or loose anchors this hull is rated to carry.</summary>
    [DataField, AutoNetworkedField] public int Capacity = 1;

    /// <summary>How many are aboard right now, recounted on a one-second throttle.</summary>
    [ViewVariables, AutoNetworkedField] public int Aboard;
}
```

```csharp
// WFGridAnchorLoadComponent.cs — on a GRID; the mechanical half of D11, read by the _CE lift check.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WFGridAnchorLoadComponent : Component
{
    /// <summary>Summed VirtualMass of the unanchored anchors and crates riding on this grid.</summary>
    [ViewVariables, AutoNetworkedField] public float VirtualMass;
}
```

---

## C. Server systems and every subscription

### C.1 Grep proof that no pair collides

```
grep -rn "WFGravityAnchor\|WFAnchorCrate\|WFPlanetCracker\|WFChunkBerth\|WFCentrifuge\|WFGravityProjector\|WFCrackConsole\|WFSectorSurveyConsole\|WFAnchorCapacity\|WFGridAnchorLoad" \
  --include=*.cs --include=*.yml --include=*.ftl Content.Server Content.Shared Content.Client Content.IntegrationTests Resources | grep -v "/obj/"
```

**Result today: 0 hits.** Every component this plan subscribes on is brand new, so every `(component, event)` pair is unclaimed by construction. Re-run before Stage 3 and Stage 4.

The two non-directed subscriptions:

| Broadcast subscription | Existing subscribers | Verdict |
|---|---|---|
| `SubscribeLocalEvent<ShipyardShuttlePurchaseEvent>` (by value) | `grep -rn "SubscribeLocalEvent<ShipyardShuttlePurchaseEvent>"` → **0**; the only handler is directed on `VesselComponent` (`ShuttleRestrictionsSystem.cs:33`) and never fires because both raises are broadcast | legal |
| `SubscribeLocalEvent<WFAnchorSwitchOffAttemptEvent>` (F7 later) | new event | legal |

### C.2 `Content.Server/_WF/PlanetCracker/Anchors/WFGravityAnchorSystem.cs` (+ `.Pairing.cs`)

`public sealed partial class WFGravityAnchorSystem : SharedWFGravityAnchorSystem` — partial is mandatory (RA0049).

Dependencies: `SharedTransformSystem`, `SharedMapSystem`, `AnchorableSystem`, `SharedPopupSystem`, `SharedAppearanceSystem`, `SharedAmbientSoundSystem`, `SharedAudioSystem`, `BiomeSystem`, `IGameTiming`.

**Subscriptions — all on `WFGravityAnchorComponent`:**

| # | Event | Handler |
|---|---|---|
| 1 | `MapInitEvent` | pushes initial appearance data |
| 2 | `AnchorAttemptEvent` | `TryGetPlanetGround` (§A.3), then 9-tile free + non-empty check; popup then `args.Cancel()` |
| 3 | `UnanchorAttemptEvent` | refuses **only** when `IsArmed(State)`; popup then `Cancel()` |
| 4 | `AnchorStateChangedEvent` | anchored → **re-run `TryGetPlanetGround` and `FootprintFree`**; on failure popup and `_transform.Unanchor`, else `Deployed`, `ReserveFootprint`, `EnsureComp<CEPvsOverrideComponent>`, `TryPair`. Unanchored → `Dissolve`, `Loose`, `RemComp<CEPvsOverrideComponent>` |
| 5 | `GetVerbsEvent<AlternativeVerb>` | "start drill" and "switch off" |
| 6 | `ExaminedEvent` | state, pair distance + cut radius, drill percentage, damaged line |
| 7 | `DamageChangedEvent` | recompute `Damaged` against `BreakDamage * DamageFraction`; raise `WFAnchorDamagedEvent` on change. **Nothing else.** |
| 8 | `BreakageEventArgs` | `State = Broken`, dissolve, stop ambience, raise `WFAnchorBrokenEvent` |
| 9 | `RepairedEvent` (**by ref**) | clear `Broken`/`Damaged`, back to `Deployed`/`Loose`, `TryPair` |
| 10 | `ComponentShutdown` | dissolve, raise `WFAnchorDestroyedEvent` |

Plus `Update`, throttled to 1 Hz: for each `Drilling` anchor, if `_timing.CurTime >= comp.DrillEnd` → `SetState(Locked)`, `_ambient.SetAmbience(uid, false)`, `_audio.PlayPvs(new SoundCollectionSpecifier("MetalThud"), uid)`, raise `WFAnchorDrillFinishedEvent`.

**Corrected from revision 1: the drill does not pause while damaged.** Revision 1 pushed `DrillEnd` forward while `Damaged`, which is invented mechanics. The brief's F3 scope asks only for "below 50% 'damaged' flag networked for later features", and the design puts the 50% pause on the **crack** timer during `Cracking` (`PLANET_CRACKER_DESIGN.md:92`, §5 "Anchor damage pause threshold"). F3 sets the flag and raises the event; F4 decides what the flag gates when it builds the crack timer. `AutoGenerateComponentPause` is still required for map pausing (§A.7).

**Key helpers:**

```csharp
/// <summary>Every tile of the square footprint must exist and be free of hard colliders.</summary>
private bool FootprintFree(Entity<MapGridComponent> grid, Vector2i origin, int radius, PhysicsComponent body)
{
    for (var dx = -radius; dx <= radius; dx++)
    for (var dy = -radius; dy <= radius; dy++)
    {
        var idx = origin + new Vector2i(dx, dy);
        if (!_map.TryGetTileRef(grid.Owner, grid.Comp, idx, out var tile) || tile.Tile.IsEmpty)
            return false;
        if (!_anchorable.TileFree(grid, idx, body.CollisionLayer, body.CollisionMask))
            return false;
    }
    return true;
}
```
The non-empty requirement is load-bearing: `AddToSnapGridCell` silently returns false on an empty tile (`SharedMapSystem.Grid.cs:1290`).

```csharp
/// <summary>Pins the biome tiles under the footprint so the ground cannot unload out from under a deployed anchor.</summary>
private void ReserveFootprint(EntityUid uid, Entity<MapGridComponent> ground)
```
`ReserveTiles` is `public void ReserveTiles(EntityUid mapUid, Box2 bounds, List<(Vector2i Index, Tile Tile)> tiles, BiomeComponent? biome = null, MapGridComponent? mapGrid = null)` (`BiomeSystem.PlanetSetup.cs:76`). The list is an accumulator the caller must `Clear()` — keep one `private readonly List<(Vector2i, Tile)> _reservedTiles = new();` field, exactly as `WFPlanetNetworkSystem.cs:136-138` does. Because `TryGetPlanetGround` guarantees `GridUid == MapUid`, the reserved map and the checked grid are the same entity and world position equals grid-local position.

**Pairing** (`.Pairing.cs`):

```csharp
/// <summary>Finds a compatible partner for a freshly deployed anchor and forms the pair.</summary>
public bool TryPair(Entity<WFGravityAnchorComponent> anchor)
```
- iterate `EntityQueryEnumerator<WFGravityAnchorComponent, TransformComponent>()`
- skip self; skip when `other.Partner is not null`; skip when `other.State is Loose or Broken`
- require `otherXform.GridUid == xform.GridUid` (same ground grid — stricter and cheaper than the map test, and identical on a ground layer)
- require `other.Cracker == anchor.Comp.Cracker` (both null allowed, for dev/test hand-spawns)
- `dist = (worldA - worldB).Length()`; require `InBand(dist, MinDistance, MaxDistance)`
- on the closest match: set both `Partner`, both `State = Paired`, `Dirty` both, appearance both, raise `WFAnchorPairFormedEvent`

```csharp
/// <summary>Breaks a pair and demotes whichever half survives.</summary>
public void Dissolve(Entity<WFGravityAnchorComponent> anchor)
```
- resolve the partner; clear both `Partner`
- for each surviving half in `Paired`/`Drilling`/`Locked`/`Off`, drop to `Deployed` (or `Loose` if unanchored), stop its ambience, zero its `DrillEnd`
- raise `WFAnchorPairDissolvedEvent`

**Single writer**: `private void SetState(Entity<WFGravityAnchorComponent> ent, WFAnchorState state)` sets the field, `Dirty`s, writes `_appearance.SetData(uid, WFAnchorVisuals.State, state)` and toggles `_ambient.SetAmbience(uid, state == Drilling)`. Nothing else assigns `State`.

### C.3 `Content.Server/_WF/PlanetCracker/Anchors/WFAnchorCrateSystem.cs`

`public sealed partial class WFAnchorCrateSystem : EntitySystem`

| # | Subscription | Handler |
|---|---|---|
| 1 | `<WFAnchorCrateComponent, InteractUsingEvent>` | tool quality via `_tool.HasQuality`; refuse inside a container; **`TryGetPlanetGround`** (§A.3); 3×3 footprint free on that grid; then `_tool.UseTool(..., new WFAnchorUncrateDoAfterEvent())` |
| 2 | `<WFAnchorCrateComponent, WFAnchorUncrateDoAfterEvent>` | re-run every check against the resolved grid, `SpawnAtPosition(comp.Contents, snappedCoords)`, copy `Cracker` onto the new anchor, `PlayPvs` unwrap sound, `QueueDel(crate)` |
| 3 | `<WFAnchorCrateComponent, ExaminedEvent>` | one line naming the tool and "only on a planet surface" |

Refusals popup first (`_popup.PopupEntity(Loc.GetString("wf-anchor-crate-not-ground"), uid, args.User)`), then return.

### C.4 `Content.Server/_WF/PlanetCracker/Cracker/WFCrackerSystem.cs`

`public sealed partial class WFCrackerSystem : EntitySystem`. F1 skeleton: state storage and berth resolution only.

| # | Subscription | Handler |
|---|---|---|
| 1 | `<WFPlanetCrackerComponent, MapInitEvent>` | `State = Idle`; scan the grid for a `WFChunkBerthComponent` and store `Berth` |
| 2 | `<WFChunkBerthComponent, MapInitEvent>` | back-link: if the berth's `GridUid` has a cracker component, set its `Berth` |
| 3 | `<WFChunkBerthComponent, ExaminedEvent>` | prints size and computed centre |

```csharp
public void SetState(Entity<WFPlanetCrackerComponent> ent, WFCrackState state);
public bool TryGetBerthCentre(Entity<WFPlanetCrackerComponent> ent, out MapCoordinates centre);
```

### C.5 `Content.Server/_WF/PlanetCracker/Cracker/WFGravityProjectorSystem.cs`

`public sealed partial class WFGravityProjectorSystem : EntitySystem`

| # | Subscription | Handler |
|---|---|---|
| 1 | `<…, MapInitEvent>` | initial appearance |
| 2 | `<…, RefreshPartsEvent>` (by value) | `comp.CrackTimeMultiplier = MathF.Pow(comp.PartScaling, args.PartRatings[comp.RatedPart] - 1f)`, `Dirty` |
| 3 | `<…, UpgradeExamineEvent>` (by value) | `args.AddPercentageUpgrade("wf-projector-upgrade-crack-time", comp.CrackTimeMultiplier)` |
| 4 | `<…, ExaminedEvent>` | **added in revision 2**: `wf-projector-examine-multiplier`, plus `wf-projector-examine-broken` when `Broken`. Without it those two locale keys have no consumer and `Broken` has no player-visible surface in F1 |
| 5 | `<…, PowerChangedEvent>` | `Off` ↔ `Idle` when not broken |
| 6 | `<…, BreakageEventArgs>` | `Broken = true`, `State = Broken` |
| 7 | `<…, RepairedEvent>` (by ref) | `Broken = false`, recompute from power |

`RefreshPartsEvent` and `UpgradeExamineEvent` are server-only (`ConstructionSystem.Machine.Upgrades.cs:127-160`), so this system cannot be shared. `GetPartsRatings` populates an entry for **every** `MachinePartPrototype` and defaults absent parts to `1.0f` (`:100-113`), so tier 1 gives exactly 1.0. Tiers: 0.888^0 = 1.000, ^1 = 0.888, ^2 = 0.789, ^3 = 0.700.

### C.6 `Content.Server/_WF/PlanetCracker/Cracker/WFCrackerOwnershipSystem.cs`

`public sealed partial class WFCrackerOwnershipSystem : EntitySystem`

| # | Subscription | Handler |
|---|---|---|
| 1 | `SubscribeLocalEvent<ShipyardShuttlePurchaseEvent>(OnPurchased)` — **broadcast, by value** | `if (HasComp<WFPlanetCrackerComponent>(args.Shuttle)) BindAboard(args.Shuttle);` |

```csharp
/// <summary>Stamps every unowned anchor and crate resting on this cracker as belonging to it.</summary>
public int BindAboard(EntityUid cracker)
```
Iterates `EntityQueryEnumerator<WFAnchorCrateComponent, TransformComponent>()` and `EntityQueryEnumerator<WFGravityAnchorComponent, TransformComponent>()`; for each whose `xform.GridUid == cracker` and whose `Cracker is null`, sets `Cracker = GetNetEntity(cracker)` and `Dirty`s.

**Three callers, added in revision 2:**
1. the purchase hook above (console purchase and Wolfgate deed both raise it);
2. `WFTestGridFactory.BuildCracker`, after the crates spawn;
3. **`AdminVesselSpawnSystem.TrySpawnVessel`** (`Content.Server/_WF/Administration/Systems/AdminVesselSpawnSystem.cs:53`, just before the admin-log line) — one added call, guarded by `HasComp<WFPlanetCrackerComponent>`. That path raises no event today and feeds both `SpawnVesselCommand.cs:74` and `ErtSystem.cs:148`, which the brief names. The file is `_WF`, so this costs nothing against the upstream budget.

### C.7 `Content.Server/_WF/PlanetCracker/Cracker/WFAnchorCapacitySystem.cs`

`public sealed partial class WFAnchorCapacitySystem : EntitySystem`

| # | Subscription | Handler |
|---|---|---|
| 1 | `<WFAnchorCapacityComponent, ExaminedEvent>` | `wf-transport-capacity-examine` with `aboard` / `capacity`, coloured green/red |

Plus a 1 Hz `Update` that, in one sweep:
- builds a per-grid tally of **unanchored** `WFGravityAnchorComponent` and `WFAnchorCrateComponent` entities and their summed `VirtualMass`, **skipping any grid with `WFPlanetLayerComponent`** (§A.4);
- `EnsureComp<WFGridAnchorLoadComponent>` with `VirtualMass` set on each carrying grid, `RemComp` when the tally is zero;
- updates every `WFAnchorCapacityComponent.Aboard` from its own grid's tally and, on the rising edge past `Capacity`, `_popup.PopupEntity(Loc.GetString("wf-transport-capacity-exceeded"), gravgenUid, PopupType.LargeCaution)`.

### C.8 `Content.Server/_WF/PlanetCracker/Cracker/CEZLevelsSystem.WFVirtualMass.cs`

New `_WF` file declaring `public sealed partial class CEZLevelsSystem` (the class is already partial across nine files, `CEZLevelsSystem.cs:18`). No `[Dependency]` fields — it uses the inherited `EntitySystem` query helpers only, so it cannot collide with the `_CE` partials' dependency list.

```csharp
/// <summary>Mass content adds to a grid's pooled gravgen load: anchors and crates riding as cargo (design D11).</summary>
public float GetWFVirtualMass(EntityUid grid, IReadOnlyCollection<EntityUid>? networkGrids = null)
```
Returns `WFGridAnchorLoadComponent.VirtualMass` on `grid`, or the sum over `networkGrids` when one is supplied. Returns 0 when no component is present, which is the overwhelmingly common case and one query miss.

---

## D. Client

### D.1 `Content.Client/_WF/PlanetCracker/Anchors/WFCrackCircleOverlay.cs`

`sealed class WFCrackCircleOverlay : Overlay`, `public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;`

Constructed with `IoCManager.InjectDependencies(this)` then `_entityManager.System<SharedTransformSystem>()` cached in the ctor — the `_CE` house pattern (`Content.Client/_CE/ZLevels/Core/Overlays/CEZGridConnectorOverlay.cs:35-41`). This is the first `Overlay` subclass under `Content.Client/_WF`; `Content.Client/Mining/MiningOverlay.cs:12` and `Content.Client/Physics/JointVisualsOverlay.cs:12` are the templates.

`Draw(in OverlayDrawArgs args)`:
```
handle = args.WorldHandle; handle.SetTransform(Matrix3x2.Identity);
foreach (uid, comp, xform) in EntityQueryEnumerator<WFGravityAnchorComponent, TransformComponent>()
    if (xform.MapID != args.MapId) continue;                    // MiningOverlay.cs:62
    if (comp.Partner is not { } netPartner) continue;
    if (!_entityManager.TryGetEntity(netPartner, out var partner)) continue;   // may be outside PVS
    if (uid.Id > partner.Value.Id) continue;                    // draw each pair once
    if (!_xformQuery.TryComp(partner, out var otherXform) || otherXform.MapID != args.MapId) continue;
    a = _transform.GetWorldPosition(xform); b = _transform.GetWorldPosition(otherXform);
    centre = (a + b) / 2f; radius = SharedWFGravityAnchorSystem.GetCutRadius((a - b).Length(), comp.CutPadding);
    colour = ColourFor(comp);
    ring = GetRing(centre, radius);                             // cached
    handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, ring.Span, Color.ToSrgb(colour));
    handle.DrawLine(a, b, colour.WithAlpha(0.35f));
    handle.DrawCircle(a, 0.6f, colour, false); handle.DrawCircle(b, 0.6f, colour, false);
handle.SetTransform(Matrix3x2.Identity);
```

**Cost guard.** `DrawingHandleBase.DrawCircle` emits `Math.Max(16, (int)(radius * 16))` segments (`RobustToolbox/Robust.Client/Graphics/Clyde/Clyde.RenderHandle.cs:432`) — ~352 GL lines at a 22-tile radius per z-pass per viewport. So the ring is a cached `ValueList<Vector2>` recomputed only when `|Δcentre| > 0.01` or `|Δradius| > 0.01`, at `Math.Clamp((int)(radius * 8f), 64, 256)` segments, emitted with one `DrawPrimitives(LineStrip, …)`. `DrawPrimitives` does **not** do the sRGB conversion `DrawCircle` does at `:441` — call `Color.ToSrgb` yourself, as `Content.Client/_WF/Shuttles/UI/ShipViewControl.cs:266` does.

Colours from `Content.Client/_WF/Stylesheets/WolfgateSkin.cs`, never literals: either half `Broken`/`Damaged` → `Danger` (`:39`); both `Locked`/`Off` → `Good` (`:37`); either `Drilling` → `Caution` (`:38`); otherwise `AccentDim` (`:26`).

**Visibility limits.** Drawn on the ground layer, visible there and from one or two air layers up. **Not** visible from orbit: `ScalingViewport.RenderZLevels`' downward pass breaks at the first `CEZCloudLayerComponent` (`Content.Client/_CE/ZLevels/Core/ScalingViewport.CEZLevels.cs:186-191`) and `WFSurfaceAsclepiu` sets `cloudLayer: true` at depth 3 of a 0..4 stack (`Resources/Prototypes/_WF/PlanetCracker/planets.yml:8`). Look-up renders exactly one map up (`:215-218`). Listed in open risks.

### D.2 `Content.Client/_WF/PlanetCracker/Anchors/WFCrackCircleOverlaySystem.cs`

`public sealed partial class WFCrackCircleOverlaySystem : EntitySystem` with `[Dependency] private IOverlayManager _overlay = default!;`
- `Initialize`: `_overlay.AddOverlay(new WFCrackCircleOverlay());`
- `Shutdown`: `_overlay.RemoveOverlay<WFCrackCircleOverlay>();`
- `SubscribeLocalEvent<WFGravityAnchorComponent, AfterAutoHandleStateEvent>(OnState)` — invalidates the cached ring.

**Sandbox**: only `System.Numerics`, `System.Math`/`MathF`, `System.Collections.Generic`, `Robust.Client.Graphics`, `Robust.Shared.Maths` — all whitelisted (`RobustToolbox/Robust.Shared/ContentPack/Sandbox.yml:646`, `:1248-1249`).

---

## E. Prototypes

All under `Resources/Prototypes/_WF/PlanetCracker/`. Sprite paths follow the `_WF` convention of no leading slash (`Resources/Prototypes/_WF/Entities/Structures/Machines/safety_deposit_box.yml:48`). **Every state name is copied verbatim from the generated `meta.json`** (re-verified this revision; full inventory in Stage 2's instructions).

### E.1 `anchors.yml`

```yaml
# Gravity anchors and their crates. F3.

- type: entity
  id: WFGravityAnchor
  parent: BaseStructureDynamic
  name: gravity anchor
  description: A squat drilling rig on hydraulic legs, far too heavy to carry and just about draggable.
  components:
  - type: Sprite
    sprite: _WF/PlanetCracker/Structures/gravity_anchor.rsi
    noRot: true
    layers:
    - map: [ "enum.WFAnchorVisualLayers.Base" ]
      state: off
    - map: [ "enum.WFAnchorVisualLayers.Glow" ]
      state: drilling-unshaded
      shader: unshaded
      visible: false
    - map: [ "enum.WFAnchorVisualLayers.Damage" ]
      state: damaged
      visible: false
  - type: Appearance
  - type: GenericVisualizer
    visuals:
      enum.WFAnchorVisuals.State:
        enum.WFAnchorVisualLayers.Base:
          Loose:    { state: off }
          Deployed: { state: deployed }
          Paired:   { state: deployed }
          Drilling: { state: drilling }
          Locked:   { state: locked }
          Off:      { state: off }
          Broken:   { state: broken }
        enum.WFAnchorVisualLayers.Glow:
          Loose:    { visible: false }
          Deployed: { visible: false }
          Paired:   { visible: false }
          Drilling: { visible: true, state: drilling-unshaded }
          Locked:   { visible: true, state: locked-unshaded }
          Off:      { visible: false }
          Broken:   { visible: false }
      enum.WFAnchorVisuals.Damaged:
        enum.WFAnchorVisualLayers.Damage:
          True:  { visible: true }
          False: { visible: false }
  - type: InteractionOutline
  - type: Transform
    anchored: false
    noRot: true
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: "-1.5,-1.5,1.5,1.5"
        density: 50          # 9 m2 x 50 = 450 mass, matching the stock 3x3 gravity generator
        mask:
        - MachineMask
        layer:               # deliberately NOT Impassable: CE IsWall only counts Impassable,
        - MidImpassable      # so a deployed anchor does not bounce a passing hull.
        - LowImpassable
  - type: Anchorable
    delay: 8
  - type: Damageable
    damageContainer: StructuralInorganic
    damageModifierSet: StructuralMetallic
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 300          # keep in sync with WFGravityAnchorComponent.BreakDamage
      behaviors:
      - !type:DoActsBehavior
        acts: [ "Breakage" ]
      - !type:PlaySoundBehavior
        sound:
          collection: MetalBreak
    - trigger:
        !type:DamageTrigger
        damage: 600
      behaviors:
      - !type:DoActsBehavior
        acts: [ "Destruction" ]
      - !type:PlaySoundBehavior
        sound:
          collection: MetalBreak
  - type: Repairable
    # BaseStructure narrows this to Applicating only (base_structure.yml:32-34, the Mono nanite
    # change), so the welder D9 asks for has to be spelled out again here.
    qualities:
    - Welding
    - Applicating
    fuelCost: 15
    doAfterDelay: 8
  - type: AmbientSound
    enabled: false
    sound:
      path: /Audio/Ambience/Objects/circular_saw.ogg
    range: 12
    volume: -4
  - type: WFGravityAnchor
  - type: StaticPrice
    price: 0

- type: entity
  id: WFAnchorCrate
  parent: BaseStructureDynamic
  name: gravity anchor crate
  description: A reinforced shipping crate with lift points. Pry it open on a planet surface.
  components:
  - type: Sprite
    sprite: _WF/PlanetCracker/Structures/anchor_crate.rsi
    noRot: true
    layers:
    - map: [ "enum.WFCrateVisualLayers.Base" ]
      state: closed
  - type: Appearance
  - type: GenericVisualizer
    visuals:
      enum.WFCrateVisuals.Open:
        enum.WFCrateVisualLayers.Base:
          True:  { state: open }
          False: { state: closed }
  - type: InteractionOutline
  - type: Transform
    anchored: false
    noRot: true
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: "-1.0,-1.0,1.0,1.0"
        density: 60
        mask:
        - MachineMask
        layer:
        - MidImpassable
        - LowImpassable
  - type: Anchorable
    flags: None              # crates are dragged, never wrenched down
  - type: WFAnchorCrate
    contents: WFGravityAnchor
    tool: Prying
    delay: 6
  - type: StaticPrice
    price: 0                 # the crates that ship with the hull must not inflate its appraisal;
                             # the 400,000 of design section 5 lives on the Replacement variant.

- type: entity
  id: WFAnchorCrateReplacement
  parent: WFAnchorCrate
  name: replacement gravity anchor crate
  suffix: Replacement
  components:
  - type: Sprite
    sprite: _WF/PlanetCracker/Structures/anchor_crate.rsi
    noRot: true
    layers:
    - map: [ "enum.WFCrateVisualLayers.Base" ]
      state: closed
    - map: [ "enum.WFCrateVisualLayers.Stencil" ]
      state: replacement
  - type: StaticPrice
    price: 400000            # design section 5 / D17: the cargo-purchasable replacement
```

### E.2 `machines.yml`

As revision 1, with these corrections:
- `WFCentrifuge` and `WFGravityProjector` both gain the explicit `qualities: [ Welding, Applicating ]` on their `Repairable` block, for the same `BaseStructure` reason.
- `WFGravityProjector` keeps the inherited **1×1** fixture and gains a YAML comment recording why (see Rejected critiques #6): the art is 64×64 (2×2) and overhangs a single anchored tile, because an even-sized AABB centred on a tile centre cannot align to the snap grid and anchoring claims one tile regardless.

```yaml
- type: entity
  id: WFCentrifuge
  parent: [ BaseMachinePowered, ConstructibleMachine ]
  name: gravitic centrifuge
  description: A caged ring rotor. Its spin is what holds a ship and a chunk of planet in orbit.
  components:
  - type: Sprite
    sprite: _WF/PlanetCracker/Structures/centrifuge.rsi
    noRot: true
    layers:
    # BOTH layers are mandatory: Content.Client/Gravity/GravitySystem.cs:35 does an unconditional
    # LayerMapGet(Core) whenever PowerChargeVisuals.Charge appears, and throws without it.
    - map: [ "enum.GravityGeneratorVisualLayers.Base" ]
      state: off
    - map: [ "enum.GravityGeneratorVisualLayers.Core" ]
      state: glow-unshaded
      shader: unshaded
  - type: Appearance
  - type: Transform
    anchored: true
    noRot: true
  - type: Physics
    bodyType: Static
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeAabb
          bounds: "-1.5,-1.5,1.5,1.5"
        density: 200
        mask:
        - MachineMask
        layer:
        - MachineLayer
  - type: Anchorable
    flags: None              # PowerChargeSystem.cs:35 zeroes Charge on unanchor, which would drop the ship
  - type: ApcPowerReceiver
    powerLoad: 12000
  - type: ExtensionCableReceiver
  - type: PowerCharge
    windowTitle: wf-centrifuge-window-title
    idlePower: 200
    activePower: 12000
    chargeRate: 0.00417      # 240 s cold to full, design section 5
  - type: GravityGenerator
    maxHandledMass: 3000     # NEVER <= 0: Gravity.cs:69 reads <= 0 as infinite lift while the
                             # console readout at :593 only reads < 0 that way.
    lightRadiusMin: 0.75
    lightRadiusMax: 3.0
    # The placeholder RSI has no startup/idle/activating/activated core states, so all four point
    # at the one animated glow state.
    coreStartupState: glow-unshaded
    coreIdleState: glow-unshaded
    coreActivatingState: glow-unshaded
    coreActivatedState: glow-unshaded
    spriteMap:
      broken: "broken"
      unpowered: "off"
      off: "off"
      on: "spinning"
  - type: WFCentrifuge
  - type: AmbientSound
    enabled: false
    sound:
      path: /Audio/Ambience/Objects/gravity_gen_hum.ogg
    range: 10
    volume: -4
  - type: Repairable
    qualities: [ Welding, Applicating ]   # BaseStructure narrows this to Applicating
    fuelCost: 25
    doAfterDelay: 10
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 400
      behaviors:
      - !type:DoActsBehavior
        acts: [ "Breakage" ]
  - type: Machine
    board: WFCentrifugeCircuitboard
  - type: ActivatableUI
    key: enum.PowerChargeUiKey.Key
  - type: ActivatableUIRequiresPower
  - type: UserInterface
    interfaces:
      enum.PowerChargeUiKey.Key:
        type: PowerChargeBoundUserInterface

- type: entity
  id: WFGravityProjector
  parent: [ BaseMachinePowered, ConstructibleMachine ]
  name: gravity projector
  description: A glass emitter on a riveted hull bracket. It beams straight down.
  # The placeholder art is 64x64 (2x2) but the fixture is deliberately left at the inherited 1x1
  # (base_structuremachines.yml:10-20). An even-sided AABB centred on a tile centre straddles four
  # tiles by half-tiles and cannot align to the snap grid, and AnchorEntity claims one tile either
  # way, so the sprite simply overhangs its machine tile. Keep mapped projectors >= 2 tiles apart.
  components:
  - type: Sprite
    sprite: _WF/PlanetCracker/Structures/gravity_projector.rsi
    layers:
    - map: [ "enum.WFProjectorVisualLayers.Base" ]
      state: off
    - map: [ "enum.WFProjectorVisualLayers.Emitter" ]
      state: emitter-unshaded
      shader: unshaded
      visible: false
  - type: Appearance
  - type: GenericVisualizer
    visuals:
      enum.WFProjectorVisuals.State:
        enum.WFProjectorVisualLayers.Base:
          Off:      { state: off }
          Idle:     { state: idle }
          Charging: { state: charging }
          Firing:   { state: firing }
          Broken:   { state: broken }
        enum.WFProjectorVisualLayers.Emitter:
          Off:      { visible: false }
          Idle:     { visible: false }
          Charging: { visible: true }
          Firing:   { visible: true }
          Broken:   { visible: false }
  - type: Transform
    anchored: true
    noRot: false             # 4-direction sprite: the mapper faces it at the berth
  - type: Rotatable
    rotateWhileAnchored: true
  - type: ApcPowerReceiver
    powerLoad: 6000
  - type: WFGravityProjector
  - type: Machine
    board: WFGravityProjectorCircuitboard
  - type: Repairable
    qualities: [ Welding, Applicating ]
    fuelCost: 20
    doAfterDelay: 8
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 250
      behaviors:
      - !type:DoActsBehavior
        acts: [ "Breakage" ]

- type: entity
  id: WFTransportGravgen
  parent: GravityGeneratorMini
  name: transport gravity generator
  suffix: Anchor transport
  description: Rated for one hull and one anchor. The placard is very clear about the "one".
  components:
  - type: GravityGenerator
    maxHandledMass: 40       # 63-tile transport hull = 31.5 FixturesMass. One crated anchor adds
                             # 6 virtual mass (37.5, flies); two add 12 (43.5, drops). See D11.
  - type: WFAnchorCapacity
    capacity: 1
```

### E.3 `boards.yml`

Unchanged from revision 1: `WFCentrifugeCircuitboard` (Capacitor 4, Manipulator 4, Plasteel 10) and `WFGravityProjectorCircuitboard` (Capacitor 2 — the rated part — Manipulator 2, Plasteel 5), both `parent: BaseMachineCircuitboard` with `state: engineering`. Part ids verified at `Resources/Prototypes/_NF/MachineParts/machine_parts.yml:2,7,12,17`. `ConstructionSystem.Machine.cs:27-33` spawns one part entity per requirement at tier 1 on map-init then calls `RefreshParts`, so a spawned projector arrives at multiplier 1.0.

### E.4 `consoles.yml`

`WFCrackConsole` and `WFSectorSurveyConsole` unchanged (BaseComputer children, per-layer `sprite:` override on `computerLayerScreen` — the fork idiom at `Resources/Prototypes/_Mono/Entities/Structures/Machines/FireControl/gunnery.yml:291-293` — state `idle`, no `ActivatableUI`, no `UserInterface`).

**`WFChunkBerthMarker` corrected**: it now parents `MarkerBase`.

```yaml
- type: entity
  id: WFChunkBerthMarker
  parent: MarkerBase
  name: chunk berth marker
  description: Marks where the extracted chunk hangs. Face it away from the hull.
  categories: [ Mapping ]
  components:
  - type: Sprite
    sprite: _WF/PlanetCracker/Interface/icons.rsi
    state: chunk
    scale: 2, 2
    noRot: false
  - type: Rotatable
    rotateWhileAnchored: true
  - type: WFChunkBerth
```

Revision 1 declared it parentless with no `ClickableComponent`, so it could never be clicked or examined — `GameplayStateBase.GetClickableEntities` filters sprite-tree hits through `clickQuery.TryGetComponent(entity.Uid, out var component)` (`Content.Client/Gameplay/GameplayStateBase.cs:152-158`), which killed the `<WFChunkBerthComponent, ExaminedEvent>` handler that is the mapper's only feedback. `MarkerBase` (`Resources/Prototypes/Entities/Markers/marker_base.yml:1-18`) supplies `Transform anchored: true`, `Clickable`, `InteractionOutline`, `placement: mode: SnapgridCenter`, `drawdepth: Overdoors`, `Marker` and `RequiresGrid`.

**Recorded consequence of `MarkerComponent`, deliberate:** `Content.Client/Markers/MarkerSystem.cs:27-36` sets `sprite.Visible = MarkersVisible`, and `ClickableSystem.CheckClick` returns false on `!sprite.Visible` (`Content.Client/Clickable/ClickableSystem.cs:58`). So the berth marker is invisible and unexaminable to ordinary players and becomes both when the marker toggle is on — which is the mapper/admin context the examine line is for, and is strictly better than a 2×-scaled chunk icon permanently floating on the hull. `categories: [ Mapping ]` (a real category, `Resources/Prototypes/Entities/categories.yml:23`) keeps it spawnable, unlike `HideSpawnMenu`.

---

## F. Test-grid factory

`Content.Server/_WF/PlanetCracker/Testing/WFTestGridFactory.cs`, `public sealed partial class WFTestGridFactory : EntitySystem`, **no subscriptions**.

Dependencies: `IMapManager`, `SharedMapSystem`, `ITileDefinitionManager`, `SharedTransformSystem`, `ShuttleSystem`, `SharedPowerReceiverSystem`, `WFCrackerOwnershipSystem`.

```csharp
/// <summary>Builds the tiny cracker hull in code and returns its grid.</summary>
public EntityUid BuildCracker(MapId map, Vector2 offset);

/// <summary>Builds the micro anchor transport in code and returns its grid.</summary>
public EntityUid BuildTransport(MapId map, Vector2 offset);
```

Shared build sequence:
1. `var grid = _mapMan.CreateGridEntity(map); _transform.SetLocalPosition(grid.Owner, offset);`
2. `var floor = new Tile(_tileDefs["FloorSteel"].TileId);` — through `ITileDefinitionManager` (`Content.Shared/Maps/ContentTileDefinition.cs:36`), never `new Tile(1)`.
3. One `List<(Vector2i, Tile)>` and one `_map.SetTiles(grid.Owner, grid.Comp, tiles)` call (`SharedMapSystem.Grid.cs:810`) — `CEZGridConnectorSystem` re-floods on every `TileChangedEvent` anywhere in the world (`CEZGridConnectorSystem.cs:105`).
4. `SpawnEntity(proto, new EntityCoordinates(grid.Owner, new Vector2(x + 0.5f, y + 0.5f)))` per entity; set `LocalRotation` where facing matters.
5. `EnsureComp<ShuttleComponent>(grid.Owner); _shuttle.Enable(grid.Owner, force: true);`
6. `_receiver.SetNeedsPower(uid, false)` on every powered machine — `PowerNetSystem.IsPoweredCalculate:330` short-circuits on `!NeedsPower`, so the centrifuge's `PowerCharge` ramps and `GravityGeneratorSystem.OnActivated:97-105` sets `GravityActive` within a tick. No cabling, no fuel.
7. Cracker only: `_ownership.BindAboard(grid.Owner)` after the crates spawn.

### F.1 Tiny cracker — exact layout

Hull: **15 × 15 = 225 tiles**, indices `(0,0)`…`(14,14)`, all `FloorSteel`. `FixturesMass = 112.5`.

| Entity prototype | Tile (x, y) | LocalRotation | Role |
|---|---|---|---|
| `ComputerShuttle` | 2, 2 | 0° | shuttle console |
| `WFCrackConsole` | 4, 2 | 0° | crack console shell |
| `DebugGyroscope` | 7, 2 | 0° | angular thrust |
| `WFCentrifuge` | 7, 7 | 0° | 3×3, gravgen role |
| `WFGravityProjector` | 5, 14 | 180° (North) | hull edge, faces the berth |
| `WFGravityProjector` | 9, 14 | 180° (North) | hull edge, faces the berth |
| `WFChunkBerthMarker` | 7, 14 | 180° (North) | berth centre 8 tiles out at grid-local (7.5, 22.5) |
| `AirlockShuttle` | 0, 7 | 270° (West) | docking port |
| `WFAnchorCrate` | 2, 11 | 0° | cargo bay, unanchored |
| `WFAnchorCrate` | 5, 11 | 0° | cargo bay, unanchored |
| `DebugThruster` | 1, 1 | 0° (South) | `LinearThrust[0]` |
| `DebugThruster` | 13, 1 | 90° (East) | `LinearThrust[1]` |
| `DebugThruster` | 1, 13 | 180° (North) | `LinearThrust[2]` |
| `DebugThruster` | 13, 13 | 270° (West) | `LinearThrust[3]` |

**14 spawned entities** (6 singletons + 2 projectors + 2 crates + 4 thrusters). Revision 1 said 13 while listing 14 rows, and the Stage 5 instruction and Stage 6 test both hard-asserted 13; corrected everywhere.

After spawning, the factory overrides the berth for the tiny hull: `berth.Size = new Vector2i(12, 12); berth.Distance = 8f;` (the 48×48/26 defaults are for the real map), and `EnsureComp<WFPlanetCrackerComponent>` on the grid.

`DebugThruster` (`Resources/Prototypes/Entities/Structures/Shuttles/thrusters.yml:160-168`) and `DebugGyroscope` (`:280-291`) are mandatory over the real ones: both carry `requireSpace: false` and `needsPower: false`, whereas `ThrusterSystem.CanEnable:478` requires `NozzleExposed` at `:499`, so on a solid rectangular hull every real thruster would be silently disabled with `LinearThrust` stuck at 0.

Thruster direction index is `(int)LocalRotation.GetCardinalDir() / 2` (`ThrusterSystem.cs:342`): South→0, East→1, North→2, West→3.

### F.2 Micro transport — exact layout

Hull: **7 × 9 = 63 tiles**, indices `(0,0)`…`(6,8)`, all `FloorSteel`. `FixturesMass = 31.5`.

| Entity prototype | Tile (x, y) | LocalRotation | Role |
|---|---|---|---|
| `ComputerShuttle` | 3, 1 | 0° | shuttle console |
| `WFTransportGravgen` | 3, 4 | 0° | mini gravgen variant, rated 40 |
| `AirlockShuttle` | 3, 0 | 0° (South) | docking port |
| `WFAnchorCrate` | 3, 7 | 0° | the one crate it may carry, unanchored |
| `DebugThruster` | 0, 1 | 0° (South) | |
| `DebugThruster` | 6, 1 | 90° (East) | |
| `DebugThruster` | 0, 7 | 180° (North) | |
| `DebugThruster` | 6, 7 | 270° (West) | |

**8 spawned entities.**

### F.3 How the transport gravgen rating is derived

1. **Measurement.** `FixturesMass = tileCount × ShuttleSystem.TileDensityMultiplier`; `TileDensityMultiplier = 0.5f` (`ShuttleSystem.cs:85`, applied at `:122-127`; the inverse recovers tile count at `Content.Server/_Mono/Cleanup/GridCleanupSystem.cs:58`). The 63-tile transport measures **31.5**.
2. **What the engine does not count.** `HasPooledGravgenSupport` sums only grid-body `FixturesMass` (`CEZLevelsSystem.Gravity.cs:452`); the 450-mass anchor and 240-mass crate aboard contribute zero. That is why D11 needs §A.4's two lines, not a bigger number.
3. **The rating.** `maxHandledMass: 40` = `ceil(31.5 × 1.25)`. `40 > 31.5` (flies with margin); `40 < 50` (a real downgrade from stock `GravityGeneratorMini`, `gravity_generator.yml:147`); `40 < 31.5 + 2 × 6` (two crated anchors drop it) and `40 ≥ 31.5 + 6` (one does not).
4. **The cracker.** 225 tiles → 112.5; centrifuge `maxHandledMass: 3000`, a placeholder to be re-derived against the real map, explicitly not 0.
5. **Assertions** read `CEZLevelsSystem.TryGetGravgenLoad(grid, out mass, out capacity)` (`Gravity.cs:569`) rather than recomputing `× 0.5`, and — because of upstream hook #2 — that mass now includes the virtual mass the lift check uses.

---

## G. Admin command

`Content.Server/_WF/PlanetCracker/Commands/WFCrackerCommand.cs`

```csharp
[AdminCommand(AdminFlags.Spawn | AdminFlags.Mapping)]
public sealed partial class WFCrackerCommand : LocalizedEntityCommands
{
    public override string Command => WolfgateAdminCommands.Cracker;   // "wfcracker"
}
```

Shape copied verbatim from `Content.Server/_WF/PlanetCracker/Planets/Commands/WFPlanetCommand.cs:25-92`: subcommand consts, `Subcommands.Contains(args[0])` guard, `shell.WriteError(Loc.GetString(...))` + `shell.WriteLine(Help)`, `GetCompletion` building `CompletionResult.FromHintOptions` per arg index from loc keys.

- `wfcracker spawn cracker` → `BuildCracker(callerMapId, callerWorldPos + (8, 8))`
- `wfcracker spawn transport` → `BuildTransport(callerMapId, callerWorldPos + (8, -12))`

Caller position resolved as `Content.Server/_WF/Administration/Commands/SpawnVesselCommand.cs:49-54` does. Not gated on `wf.planet_networks`; admin-flag gated only. No admin-tab button (`WolfgateTab.xaml:12-17` rows each need a `WindowType`; `wfplanet` set the precedent).

`Content.Shared/_WF/Administration/WolfgateAdminCommands.cs` gains one line after `Planet` (`:16`): `public const string Cracker = "wfcracker";`

---

## H. Locale

`Resources/Locale/en-US/_WF/planet-cracker/anchors.ftl` — as revision 1, minus `wf-anchor-examine-drill-paused` (no drill pause in F3), plus `wf-anchor-locked-unwrench` retained for the `IsArmed` refusal only.

```
## Placement
wf-anchor-not-ground = The anchor only grips bare planet surface, not a deck.
wf-anchor-no-room = The anchor needs three by three tiles of clear, solid ground.
wf-anchor-lost-room = Something moved into the rig's footprint; it will not sit.
wf-anchor-locked-unwrench = The anchor is drilled in. Switch it off first.

## Examine
wf-anchor-examine-state = It is { $state }.
wf-anchor-examine-unpaired = No partner anchor within { $min } to { $max } tiles.
wf-anchor-examine-pair = Paired at { $distance } tiles; the cut would be { $radius } tiles across.
wf-anchor-examine-drill = Drilling: { $percent }% complete.
wf-anchor-examine-damaged = The housing is split and sparking.

## States
wf-anchor-state-loose = loose
wf-anchor-state-deployed = wrenched down
wf-anchor-state-paired = paired
wf-anchor-state-drilling = drilling
wf-anchor-state-locked = locked
wf-anchor-state-off = switched off
wf-anchor-state-broken = broken

## Verbs
wf-anchor-verb-drill = Start drilling
wf-anchor-verb-drill-unpaired = It has no partner anchor yet.
wf-anchor-verb-drill-busy = It is already running.
wf-anchor-verb-off = Switch off
wf-anchor-verb-off-not-locked = It has not locked yet.
wf-anchor-verb-off-refused = { $reason }

## Crate
wf-anchor-crate-not-ground = Unpack the anchor on bare planet surface, not on a deck.
wf-anchor-crate-no-room = There is no room here for a three by three rig.
wf-anchor-crate-in-container = Take the crate out first.
wf-anchor-crate-examine = Pry it open on a planet surface to deploy the anchor.
```

`Resources/Locale/en-US/_WF/planet-cracker/cracker.ftl` — as revision 1; `wf-projector-examine-multiplier` and `wf-projector-examine-broken` now have a consumer (§C.5 subscription 4).

```
## Machines
wf-centrifuge-window-title = Gravitic centrifuge
wf-projector-upgrade-crack-time = crack time
wf-projector-examine-multiplier = Rated at { $percent }% of stock crack time.
wf-projector-examine-broken = The emitter housing is cracked.

## Berth
wf-berth-examine = Berth { $width } by { $height } tiles, centred { $distance } tiles out.

## Anchor capacity
wf-transport-capacity-examine = Rated for { $capacity } anchor(s); { $aboard } aboard.
wf-transport-capacity-exceeded = Gravity generator overloaded: too many anchors aboard.

## wfcracker command
cmd-wfcracker-desc = Spawn the code-built planet cracker test grids.
cmd-wfcracker-help = Usage: { $command } spawn <cracker | transport>
cmd-wfcracker-invalid-args = Expected: spawn <cracker | transport>.
cmd-wfcracker-unknown-kind = No test grid named "{ $kind }". Try cracker or transport.
cmd-wfcracker-no-map = Attach to an entity on a map first.
cmd-wfcracker-spawned = Built { $kind } as { $grid } on map { $map }.
cmd-wfcracker-hint-sub = <spawn>
cmd-wfcracker-hint-kind = <cracker|transport>
```

---

## I. Stale claims in the design doc

Unchanged from revision 1 except items 3 and 4:

1. **§3 D9 "Broken (the destructible threshold…)"** implies an engine-side broken state. There is none — `SharedDestructibleSystem.BreakEntity` (`Content.Shared/Destructible/SharedDestructibleSystem.cs:19-23`) only raises `BreakageEventArgs`; every consumer keeps its own flag. `PowerChargeComponent.Intact` is never set false in this fork, so the stock gravgen's `broken` sprite state is unreachable.
2. **§4/F3 "wrench down"** assumes `AnchorableSystem` validates a machine's footprint. It checks one tile at attempt (`:237-242`) and one at completion (`:142-147`). The nine-tile check, at both moments, is new code here.
3. **§3 D9 "repair with a welder"** is true of the component default (`RepairableComponent.cs:33-34`) but **not** of anything parented off `BaseStructure`, which the fork narrowed to `Applicating` only (`base_structure.yml:32-34`, "#Mono: Nanite applicator"). Every new prototype here re-declares `qualities: [ Welding, Applicating ]` so the design's welder loop actually works; a fork-wide decision on nanite-only repair is out of scope.
4. **§4/F3 "drag (standard pulling)"** implies a heavy anchor drags slowly. `PullerComponent.WalkSpeedModifier`/`SprintSpeedModifier` are a flat 0.95 regardless of mass (`Content.Shared/Movement/Pulling/Components/PullerComponent.cs:28-30`) and the joint is `Stiffness 0` (`PullingSystem.cs:501`).
5. **§4/F1 D11's stated mechanism** (`GridHasActiveGravgen`, mass of cargo) is wrong twice over: the symbol does not exist, and the real gate weighs only grid `FixturesMass` (`Gravity.cs:452`). The *decision* stands and is implemented via §A.4's virtual mass; only the named mechanism is stale.
6. **§4/F4 "rotor animates at charge speed"** and **ASSET_REQUIREMENTS.md #4 "playback speed is set in code"** — there is no API to scale RSI playback. `SpriteSystem.FrameUpdate` advances by raw `frameTime` (`RobustToolbox/Robust.Client/GameObjects/EntitySystems/SpriteSystem.cs:160-161`).
7. **ASSET_REQUIREMENTS.md #4 "-unshaded interior glow"** — the generated centrifuge state is `glow-unshaded`, not `spinning-unshaded`.
8. **ASSET_REQUIREMENTS.md #11/#12** describe fissure and crack-ring stages as a separate field; the placeholders encode the stage in the state name.
9. **ASSET_REQUIREMENTS.md #1 "multi-tile machines are one big state"** is true of the art, not of occupancy — and for the 64×64 projector the art cannot even be aligned to a 2×2 block without an offset AABB (see §E.2).
10. **§4/F5 D4 "the same ring visible from orbit"** — the cloud layer terminates the downward pass walk (`ScalingViewport.CEZLevels.cs:186-191`, `planets.yml:8`); look-up renders one map (`:215-218`).
11. **§4/F5 "extraction must call BiomeSystem.Preload"** — `Preload` (`BiomeSystem.PlanetSetup.cs:152`) queues marker-layer chunk origins into a dictionary `CleanupUpdateCycle` clears every tick; `ReserveTiles` is what materialises and freezes tiles.
12. **§4/F4 "adding ForceAnchorComponent at runtime needs a small helper"** — wrong for that direction: `AddComponentInternal` re-raises `MapInitEvent` (`EntityManager.Components.cs:428-429`). Only the release helper is missing.
13. **§9 "a fixture that builds a ground layer"** and F0 plan §A "zero z-level tests" are stale: `PlanetNetworkTest.cs` exists with eight tests and a code-built hull helper (`SpawnShip`, `:454-485`).
14. **ASSET_REQUIREMENTS.md checklist "Maps under Resources/Maps/_WF/Shuttles/"** — neither `Resources/Maps/_WF` nor `Resources/SharedMaps/_WF` exists; the live convention is `/SharedMaps/...` (`Resources/Prototypes/_Mono/Shipyard/archer.yml:16`).

---

## J. Out of scope

- Vessel prototype / shipyard entry. `Content.IntegrationTests/Tests/_NF/ShipyardTests.cs:77-93` loads every `VesselPrototype.ShuttlePath` and asserts `Price >= AppraiseGrid × MinPriceMarkup`, so an entry without a real map is guaranteed red CI. (This is also why the shipped crates are `price: 0`, §E.1.)
- Any BUI: crack console, survey console, centrifuge dial, berth ghost on radar.
- Beams, sky beams, crack rings, fissures, extraction, chunk, crack miners, surveyor, deep veins.
- What the `Damaged` flag gates. F3 sets and networks it; F4 decides.
- F4's `_WF` partials of `GravityGeneratorSystem` / `PowerChargeSystem`.
- The `ForceAnchor` release helper.

---

## Rejected critiques

**One finding rejected in part; no finding rejected in full.**

**Issue 6 — projector 2×2 fixture: observation accepted, proposed fix rejected.**

The observation is correct and is now folded in as an explicit, commented decision: the placeholder art is 64×64 with 4 directions (`Resources/Textures/_WF/PlanetCracker/Structures/gravity_projector.rsi/meta.json`: `size {x:64,y:64}`, states `off`/`idle`/`charging`/`firing`/`broken`/`emitter-unshaded`, all `directions: 4`), revision 1 gave it no `Fixtures` block, and it therefore inherits `bounds: "-0.45,-0.45,0.45,0.45"` from `BaseMachineIndestructible` (`Resources/Prototypes/Entities/Structures/Machines/base_structuremachines.yml:10-20`). The plan was silent about that and should not have been.

The proposed fix — `bounds: "-1.0,-1.0,1.0,1.0"` — is rejected as incorrect. Entities are spawned and anchored on **tile centres** (`new Vector2(x + 0.5f, y + 0.5f)`, §F), and a fixture AABB is centred on the entity origin. A 3×3 AABB (`-1.5 … 1.5`) centred on a tile centre covers exactly nine tiles, which is why the stock gravity generator's bounds work (`gravity_generator.yml:32-41`). An **even**-sided 2×2 AABB centred on a tile centre spans `x-1.0 … x+1.0` around a half-integer coordinate, i.e. it straddles four tiles by half a tile in each direction and aligns to nothing. It would also not change occupancy: `SharedTransformSystem.AnchorEntity` registers exactly one `Vector2i` (`SharedTransformSystem.Component.cs:73`) and `AnchorableSystem` checks exactly one tile, so the machine still claims one tile whatever the AABB says.

The only geometrically correct 2×2 would be an offset AABB (`"-0.5,-0.5,1.5,1.5"`) plus a matching `Sprite` `offset: 0.5, 0.5`, which breaks down the moment the `Rotatable` projector is turned — the block would swing to a different pair of neighbouring tiles per facing, and mappers would have to reason about which. Given "placeholder sprites are used as-is" is a settled decision and this is the F1 *skeleton*, the resolution is: keep the inherited 1×1 collision, let the art overhang (as many wall-mounted machines do), record the reasoning in a YAML comment (§E.2), keep mapped projectors ≥ 2 tiles apart — the §F.1 layout already places them 4 apart at x=5 and x=9 — and carry the alignment question into the real-map open risks rather than baking a misaligned AABB into the prototype now.

Every other finding was reproduced in the code and is folded into the plan above: the `MapUid`/`GridUid` gate (§A.3), the missing `AutoGenerateComponentPause` (§A.7), the blanket `Anchored` assertion versus the unanchorable crates (tests, and the crate's `flags: None` at `AnchorableComponent.cs:39` / `AnchorableSystem.cs:267-271`), `BaseStructure`'s `Applicating`-only `Repairable` (§E.1, §I.3), the parentless berth marker (§E.4), the missing completion-time footprint re-check (§A.2, §C.2), the 13-versus-14 entity count (§F.1), the over-broad unanchor refusal (§A.6), D11's hollow replacement (§A.4, upstream hooks), the missing admin/ERT ownership binding (§C.6), the out-of-scope drill pause (§C.2), the crate price (§E.1), and the orphan projector locale keys (§C.5).

## FILES
- [create] Content.Shared/_WF/PlanetCracker/Anchors/WFAnchorState.cs — The anchor lifecycle enum: Loose, Deployed, Paired, Drilling, Locked, Off, Broken. Damaged is a separate flag, not a state, and in F3 it gates nothing.
- [create] Content.Shared/_WF/PlanetCracker/Anchors/WFAnchorVisuals.cs — Appearance keys and sprite layer enums: WFAnchorVisuals{State,Damaged}, WFAnchorVisualLayers{Base,Glow,Damage}, WFCrateVisuals{Open}, WFCrateVisualLayers{Base,Stencil}. Driven from YAML GenericVisualizer; no client visualizer C#.
- [create] Content.Shared/_WF/PlanetCracker/Anchors/WFGravityAnchorComponent.cs — [RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]. State, Partner, Cracker, DrillEnd (TimeOffsetSerializer + AutoPausedField), DrillDuration 5min, Damaged, MinDistance 16, MaxDistance 40, CutPadding 2, BreakDamage 300, DamageFraction 0.5, FootprintRadius 1, VirtualMass 6. BOTH the (true) state argument and the class-level AutoGenerateComponentPause are load-bearing: without the latter the AutoPausedField is inert (RobustToolbox/Robust.Shared/Analyzers/ComponentPauseGeneratorAttributes.cs:7-25) with no analyzer diagnostic.
- [create] Content.Shared/_WF/PlanetCracker/Anchors/WFAnchorCrateComponent.cs — Contents (WFGravityAnchor), Tool (Prying), Delay 6s, Cracker, FootprintRadius 1, VirtualMass 6. Deliberately not FlatpackComponent: its room check is single-tile (SharedFlatpackSystem.cs:90-97) and reuse would collide on (FlatpackComponent, InteractUsingEvent).
- [create] Content.Shared/_WF/PlanetCracker/Anchors/WFAnchorEvents.cs — Broadcast notification events (PairFormed, PairDissolved, DrillStarted, DrillFinished, Damaged, Broken, Destroyed, SwitchedOff) as [ByRefEvent] record structs; WFAnchorSwitchOffAttemptEvent as a CancellableEntityEventArgs class (the F7 veto hook, raised broadcast by value); WFAnchorUncrateDoAfterEvent as [Serializable, NetSerializable] SimpleDoAfterEvent.
- [create] Content.Shared/_WF/PlanetCracker/Anchors/SharedWFGravityAnchorSystem.cs — Abstract partial with static helpers only and NO subscriptions: GetCutRadius(distance, padding) = distance/2 + padding (D21), InBand, IsArmed(state) = Drilling/Locked/Off.
- [create] Content.Shared/_WF/PlanetCracker/Cracker/WFCrackState.cs — Design section 3 verbatim: Idle, Surveying, AnchorsPlaced, AnchorsLocked, Cracking, Cracked, Disconnecting, Released, Falling.
- [create] Content.Shared/_WF/PlanetCracker/Cracker/WFPlanetCrackerComponent.cs — On the cracker grid. State (WFCrackState), Berth (NetEntity?), AnchorA/AnchorB (NetEntity?).
- [create] Content.Shared/_WF/PlanetCracker/Cracker/WFChunkBerthComponent.cs — Mapper-placed marker data. Size (Vector2i, default 48x48) and Distance (default 26 tiles). Berth centre = markerWorldPos + markerWorldRot.ToWorldVec() * Distance.
- [create] Content.Shared/_WF/PlanetCracker/Cracker/WFCentrifugeComponent.cs — Bare networked marker so F4 finds the ship's centrifuge without querying GravityGeneratorComponent, which is [Access(typeof(GravityGeneratorSystem))] (Content.Server/Gravity/GravityGeneratorComponent.cs:7-8).
- [create] Content.Shared/_WF/PlanetCracker/Cracker/WFGravityProjectorComponent.cs — CrackTimeMultiplier (networked), PartScaling 0.888 (0.888^3 = 0.70, design section 5's tier-4 floor), RatedPart Capacitor, Broken bool, State (WFProjectorState).
- [create] Content.Shared/_WF/PlanetCracker/Cracker/WFProjectorVisuals.cs — WFProjectorState{Off,Idle,Charging,Firing,Broken}, WFProjectorVisuals{State}, WFProjectorVisualLayers{Base,Emitter}.
- [create] Content.Shared/_WF/PlanetCracker/Cracker/WFCrackConsoleComponent.cs — Bare networked marker for the crack console shell. No BUI until F4.
- [create] Content.Shared/_WF/PlanetCracker/Cracker/WFSectorSurveyConsoleComponent.cs — Bare networked marker for the sector survey console shell. No BUI until F2.
- [create] Content.Shared/_WF/PlanetCracker/Cracker/WFAnchorCapacityComponent.cs — On a hull's gravgen: Capacity (default 1) and Aboard. The player-facing half of D11 - the examine line and the overload popup.
- [create] Content.Shared/_WF/PlanetCracker/Cracker/WFGridAnchorLoadComponent.cs — On a GRID: VirtualMass, the summed cargo mass of unanchored anchors and crates aboard. The mechanical half of D11, read by the two marked lines in CEZLevelsSystem.Gravity.cs through the _WF partial helper.
- [create] Content.Server/_WF/PlanetCracker/Anchors/WFGravityAnchorSystem.cs — public sealed partial (RA0049). Ten subscriptions, all on the new component: MapInitEvent, AnchorAttemptEvent (TryGetPlanetGround requiring GridUid == MapUid, then 9-tile free and non-empty check, popup then Cancel), UnanchorAttemptEvent (refuse ONLY when IsArmed), AnchorStateChangedEvent (re-run the ground and footprint checks, then Deployed + ReserveTiles + CEPvsOverride + TryPair, or Unanchor with a popup), GetVerbsEvent<AlternativeVerb>, ExaminedEvent, DamageChangedEvent (flag + event only), BreakageEventArgs, RepairedEvent (BY REF), ComponentShutdown. Plus a 1 Hz Update that only checks CurTime >= DrillEnd - no damaged pause.
- [create] Content.Server/_WF/PlanetCracker/Anchors/WFGravityAnchorSystem.Pairing.cs — TryPair (same GridUid, same Cracker, distance in band, closest match), Dissolve (clear both Partner, demote survivors, raise WFAnchorPairDissolvedEvent), and the single SetState writer that also pushes appearance and ambience.
- [create] Content.Server/_WF/PlanetCracker/Anchors/WFAnchorCrateSystem.cs — Three subscriptions on WFAnchorCrateComponent: InteractUsingEvent (tool quality, not-in-container, TryGetPlanetGround, 3x3 room on that resolved grid, then _tool.UseTool with WFAnchorUncrateDoAfterEvent), the do-after (re-check everything, spawn WFGravityAnchor, copy Cracker, QueueDel the crate), ExaminedEvent.
- [create] Content.Server/_WF/PlanetCracker/Cracker/WFCrackerSystem.cs — F1 skeleton state holder. <WFPlanetCrackerComponent, MapInitEvent>, <WFChunkBerthComponent, MapInitEvent>, <WFChunkBerthComponent, ExaminedEvent>. Public SetState and TryGetBerthCentre.
- [create] Content.Server/_WF/PlanetCracker/Cracker/WFGravityProjectorSystem.cs — SEVEN subscriptions on WFGravityProjectorComponent: MapInitEvent, RefreshPartsEvent (by value; multiplier = PartScaling^(rating-1)), UpgradeExamineEvent (by value), ExaminedEvent (new in revision 2 - the consumer for wf-projector-examine-multiplier and -broken), PowerChangedEvent, BreakageEventArgs, RepairedEvent (by ref). Server-only because the two upgrade events live in Content.Server/_NF/Construction.
- [create] Content.Server/_WF/PlanetCracker/Cracker/WFCrackerOwnershipSystem.cs — One broadcast BY-VALUE subscription: SubscribeLocalEvent<ShipyardShuttlePurchaseEvent> (the event carries no [ByRefEvent], Content.Shared/_Mono/Shipyard/ShipyardShuttlePurchaseEvent.cs:3-7). Public BindAboard(cracker) stamps Cracker onto every unowned anchor and crate on that grid. Three callers: the purchase hook, WFTestGridFactory, and AdminVesselSpawnSystem.
- [create] Content.Server/_WF/PlanetCracker/Cracker/WFAnchorCapacitySystem.cs — One subscription: <WFAnchorCapacityComponent, ExaminedEvent>. Plus a 1 Hz sweep that tallies UNANCHORED anchors and crates per grid (skipping any grid with WFPlanetLayerComponent), maintains WFGridAnchorLoadComponent.VirtualMass on carrying grids, updates Aboard, and pops a LargeCaution on the rising edge past Capacity.
- [create] Content.Server/_WF/PlanetCracker/Cracker/CEZLevelsSystem.WFVirtualMass.cs — New _WF file declaring `public sealed partial class CEZLevelsSystem` (the class is already partial across nine _CE files, CEZLevelsSystem.cs:18). Holds GetWFVirtualMass(grid, networkGrids = null), which returns WFGridAnchorLoadComponent.VirtualMass for a grid or the sum over a network. No [Dependency] fields, so it cannot collide with the _CE partials' dependency list. This is what keeps the D11 upstream edit down to one line per call site.
- [create] Content.Server/_WF/PlanetCracker/Testing/WFTestGridFactory.cs — No subscriptions. BuildCracker (15x15 = 225 tiles, FOURTEEN entities, berth overridden to 12x12 at distance 8) and BuildTransport (7x9 = 63 tiles, 8 entities). Tiles through ITileDefinitionManager, one bulk SetTiles call, SetNeedsPower(false) instead of cabling, ShuttleSystem.Enable(force: true), BindAboard on the cracker.
- [create] Content.Server/_WF/PlanetCracker/Commands/WFCrackerCommand.cs — [AdminCommand(AdminFlags.Spawn | AdminFlags.Mapping)] LocalizedEntityCommands, Command => WolfgateAdminCommands.Cracker. 'spawn <cracker|transport>' at the caller's world position plus an offset. Shape from WFPlanetCommand.cs:25-92; no CVar gate, no admin-tab button.
- [create] Content.Client/_WF/PlanetCracker/Anchors/WFCrackCircleOverlay.cs — First Overlay subclass under Content.Client/_WF. WorldSpaceBelowEntities; per-pair ring at radius = distance/2 + CutPadding, drawn once per pair (uid.Id ordering guard), map-filtered, tolerant of an unresolvable partner NetEntity. Cached ValueList<Vector2> emitted with one DrawPrimitives(LineStrip, span, Color.ToSrgb(colour)); colours from WolfgateSkin.
- [create] Content.Client/_WF/PlanetCracker/Anchors/WFCrackCircleOverlaySystem.cs — AddOverlay in Initialize, RemoveOverlay<T>() in Shutdown, and <WFGravityAnchorComponent, AfterAutoHandleStateEvent> to invalidate the cached ring.
- [create] Resources/Prototypes/_WF/PlanetCracker/anchors.yml — WFGravityAnchor (BaseStructureDynamic; gravity_anchor.rsi off/deployed/drilling/locked/broken/damaged/drilling-unshaded/locked-unshaded; 3x3 PhysShapeAabb density 50; layer MidImpassable+LowImpassable and never Impassable; Anchorable delay 8; Destructible 300 Breakage / 600 Destruction; Repairable with EXPLICIT qualities [Welding, Applicating]; AmbientSound; StaticPrice 0), WFAnchorCrate (anchor_crate.rsi closed/open, Anchorable flags: None, StaticPrice 0 so the shipped crates do not inflate the hull's appraisal), WFAnchorCrateReplacement (adds the 'replacement' stencil layer and carries the 400000 price of design section 5 / D17).
- [create] Resources/Prototypes/_WF/PlanetCracker/machines.yml — WFCentrifuge (centrifuge.rsi off/spinning/broken/glow-unshaded; BOTH GravityGeneratorVisualLayers.Base and .Core mapped; all four coreXState = glow-unshaded; maxHandledMass 3000 never <= 0; chargeRate 0.00417; Anchorable flags: None; explicit Repairable qualities; PowerChargeBoundUserInterface), WFGravityProjector (gravity_projector.rsi 4-dir off/idle/charging/firing/broken/emitter-unshaded; Rotatable; Machine board; explicit Repairable qualities; NO Fixtures override, with a comment recording that the 64x64 art overhangs its single anchored tile because an even-sided AABB cannot align to the snap grid), WFTransportGravgen (GravityGeneratorMini child, maxHandledMass 40, WFAnchorCapacity capacity 1).
- [create] Resources/Prototypes/_WF/PlanetCracker/boards.yml — WFCentrifugeCircuitboard (Capacitor 4, Manipulator 4, Plasteel 10) and WFGravityProjectorCircuitboard (Capacitor 2 - the rated part - Manipulator 2, Plasteel 5). ConstructionSystem.Machine.cs:27-33 stocks tier-1 parts on map-init so a spawned projector arrives at multiplier 1.0.
- [create] Resources/Prototypes/_WF/PlanetCracker/consoles.yml — WFCrackConsole and WFSectorSurveyConsole (BaseComputer children with a per-layer sprite override on computerLayerScreen; no ActivatableUI, no UserInterface), and WFChunkBerthMarker with parent: MarkerBase - which supplies Clickable, InteractionOutline, Marker, SnapgridCenter placement and Overdoors draw depth - keeping categories: [Mapping], the icons.rsi 'chunk' sprite at scale 2,2 and Rotatable.
- [create] Resources/Locale/en-US/_WF/planet-cracker/anchors.ftl — Placement refusals (including the new wf-anchor-lost-room for the completion-time footprint re-check), examine lines, state names, verb labels and disabled-verb messages, crate strings. No drill-paused key - F3 has no drill pause.
- [create] Resources/Locale/en-US/_WF/planet-cracker/cracker.ftl — Centrifuge window title, projector upgrade and examine keys (all now consumed), berth examine, anchor capacity examine and overload popup, and the cmd-wfcracker-* set.
- [edit] Content.Shared/_WF/Administration/WolfgateAdminCommands.cs — One line after Planet (line 16): public const string Cracker = "wfcracker";. Already an _WF file, so per the F0 precedent not an upstream edit.
- [edit] Content.Server/_WF/Administration/Systems/AdminVesselSpawnSystem.cs — One added call in TrySpawnVessel, just before the admin-log line at :51: if the loaded grid has WFPlanetCrackerComponent, call WFCrackerOwnershipSystem.BindAboard on it. This path raises no event today and feeds both SpawnVesselCommand.cs:74 and ErtSystem.cs:148, so without it an admin- or ERT-spawned cracker carries unbound anchors. Already an _WF file, so not an upstream edit.
- [edit] Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs — UPSTREAM. Two // WOLFGATE-marked lines, one per call site, both calling GetWFVirtualMass from the _WF partial: one inside HasPooledGravgenSupport's foreach after line 452 so crated anchors count against pooled lift (D11), one after the mass branches in TryGetGravgenLoad (~line 607) so the readout and the tests see the same number the lift check uses.
- [create] Content.IntegrationTests/Tests/_WF/PlanetCracker/CrackerTestGridTest.cs — Factory grids: exact entity counts (14 cracker, 8 transport), tile counts and FixturesMass (112.5 and 31.5), Anchored asserted per role (true for the machines, FALSE for the crates), gravgen ratings and the D11 virtual-mass behaviour read through CEZLevelsSystem.TryGetGravgenLoad, ownership binding through both the factory and the admin-spawn path, and berth centre geometry. House style copied from PlanetNetworkTest.cs.
- [create] Content.IntegrationTests/Tests/_WF/PlanetCracker/GravityAnchorTest.cs — F3 end to end on a real Asclepiu stack built with WFPlanetNetworkSystem.BuildNetwork: deck-versus-ground refusal, footprint refusal at attempt AND at completion, ReserveTiles survival, pairing band, cross-owner refusal, drill timer, lock, damage flag (no pause), break and repair with the tool the prototype accepts, destroy, unanchor gating, switch-off gating and veto, and the events raised.
- [create] Content.IntegrationTests/Tests/_WF/PlanetCracker/PlanetCrackerPrototypeTest.cs — Headless prototype-load check: every new EntityPrototype indexes and spawns; every RSI state named by every Sprite layer exists in the referenced RSI; the centrifuge maps both gravgen layers; the projector starts at tier 1; the anchor's BreakDamage mirrors its Destructible trigger; and every new Repairable block accepts Welding.

## UPSTREAM HOOKS
- Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs:452 — Insert ONE // WOLFGATE-marked line immediately after `mass += body.FixturesMass;` inside HasPooledGravgenSupport's foreach — at the same indentation as the braceless `if` above it, so it runs unconditionally for every grid in the rigid set: `mass += GetWFVirtualMass(grid); // WOLFGATE: crated anchors aboard count against pooled lift (D11).` GetWFVirtualMass is defined in the new _WF partial Content.Server/_WF/PlanetCracker/Cracker/CEZLevelsSystem.WFVirtualMass.cs; CEZLevelsSystem is already `public sealed partial class` (CEZLevelsSystem.cs:18). Without this line D11 cannot be enforced at all: the sweep weighs only grid-body FixturesMass (= tiles x ShuttleSystem.TileDensityMultiplier 0.5, ShuttleSystem.cs:85/:122-127) and cargo contributes zero.
- Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs:607 — Insert ONE // WOLFGATE-marked line immediately after the if/else that sets gridMass in TryGetGravgenLoad (the network loop ends at :601, the single-grid branch at :606), before `return true;`: `gridMass += GetWFVirtualMass(gridUid, networkGrids); // WOLFGATE: same virtual mass the lift check uses, so the readout agrees.` Without it the public load readout — which F4's console and every test in this plan read — would disagree with the rule that actually drops the ship.

## STAGES
### Stage 1 - shared vocabulary
Files: Content.Shared/_WF/PlanetCracker/Anchors/WFAnchorState.cs, Content.Shared/_WF/PlanetCracker/Anchors/WFAnchorVisuals.cs, Content.Shared/_WF/PlanetCracker/Anchors/WFGravityAnchorComponent.cs, Content.Shared/_WF/PlanetCracker/Anchors/WFAnchorCrateComponent.cs, Content.Shared/_WF/PlanetCracker/Anchors/WFAnchorEvents.cs, Content.Shared/_WF/PlanetCracker/Anchors/SharedWFGravityAnchorSystem.cs, Content.Shared/_WF/PlanetCracker/Cracker/WFCrackState.cs, Content.Shared/_WF/PlanetCracker/Cracker/WFPlanetCrackerComponent.cs, Content.Shared/_WF/PlanetCracker/Cracker/WFChunkBerthComponent.cs, Content.Shared/_WF/PlanetCracker/Cracker/WFCentrifugeComponent.cs, Content.Shared/_WF/PlanetCracker/Cracker/WFGravityProjectorComponent.cs, Content.Shared/_WF/PlanetCracker/Cracker/WFProjectorVisuals.cs, Content.Shared/_WF/PlanetCracker/Cracker/WFCrackConsoleComponent.cs, Content.Shared/_WF/PlanetCracker/Cracker/WFSectorSurveyConsoleComponent.cs, Content.Shared/_WF/PlanetCracker/Cracker/WFAnchorCapacityComponent.cs, Content.Shared/_WF/PlanetCracker/Cracker/WFGridAnchorLoadComponent.cs
Gate: dotnet build Content.Shared/Content.Shared.csproj -c Debug -- zero errors and zero analyzer warnings (in particular no RA0049).

Create every shared component, enum and event exactly as specified in plan section B. Rules that are easy to get wrong: (1) every networked component passes AutoGenerateComponentState(TRUE) - the four F0 components in Content.Shared/_WF/PlanetCracker/Planets/ pass no argument and copying their attribute line silently disables AfterAutoHandleStateEvent (RobustToolbox/Robust.Shared/Analyzers/ComponentNetworkGeneratorAuxiliary.cs:55-59); (2) WFGravityAnchorComponent ALSO needs the class-level AutoGenerateComponentPause - [AutoPausedField] on DrillEnd is inert without it because the unpause system is generated from the CLASS attribute (RobustToolbox/Robust.Shared/Analyzers/ComponentPauseGeneratorAttributes.cs:7-25) and there is NO analyzer diagnostic for the mismatch; the in-fork pairing to copy is Content.Shared/Anomaly/Components/AnomalyComponent.cs:19 plus its field at :91; (3) the ownership field on the anchor and the crate is named Cracker, never Owner, because Component.Owner already exists; (4) DrillEnd is [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]; (5) all eight notification events are [ByRefEvent] readonly record structs raised BROADCAST, while WFAnchorSwitchOffAttemptEvent is a CancellableEntityEventArgs class raised broadcast BY VALUE so many systems can veto - a directed cancellable event would allow only one subscriber per component; (6) WFAnchorUncrateDoAfterEvent must be [Serializable, NetSerializable] and inherit SimpleDoAfterEvent or SharedDoAfterSystem.TryStartDoAfter asserts (Content.Shared/DoAfter/SharedDoAfterSystem.cs:190); (7) SharedWFGravityAnchorSystem declares NO subscriptions, only static helpers; (8) both WFGravityAnchorComponent and WFAnchorCrateComponent carry `[DataField] public float VirtualMass = 6f;` - the D11 number, chosen so one crated anchor keeps the 31.5-mass transport under its 40 rating and two do not; (9) no license headers, /// <summary> one-liner on the type and every member, LF line endings.

### Stage 2 - prototypes and locale
Files: Resources/Prototypes/_WF/PlanetCracker/anchors.yml, Resources/Prototypes/_WF/PlanetCracker/machines.yml, Resources/Prototypes/_WF/PlanetCracker/boards.yml, Resources/Prototypes/_WF/PlanetCracker/consoles.yml, Resources/Locale/en-US/_WF/planet-cracker/anchors.ftl, Resources/Locale/en-US/_WF/planet-cracker/cracker.ftl
Gate: dotnet build Content.YAMLLinter/Content.YAMLLinter.csproj -c Release && dotnet run --project Content.YAMLLinter -c Release --no-build -- zero errors. (Release, per the house YAML validation workflow; a Debug lint run is not trusted.)

Write the four prototype files and two locale files exactly as given in plan sections E and H. The verified RSI inventory (re-read from Resources/Textures/_WF/PlanetCracker/**/meta.json this revision) is: gravity_anchor.rsi 96x96 - deployed, drilling, locked, off, broken, damaged, drilling-unshaded, locked-unshaded (all 1-direction); anchor_crate.rsi 64x64 - closed, open, replacement; centrifuge.rsi 96x96 - off, spinning, broken, glow-unshaded; gravity_projector.rsi 64x64 - off, idle, charging, firing, broken, emitter-unshaded (all 4-direction); crack_console.rsi 32x32 - idle, targeting, cracking, alert; survey_console.rsi 32x32 - idle, scanning; Interface/icons.rsi 16x16 - anchor, projector, centrifuge, chunk, warning, beam. A state not in meta.json fails RSI validation at load. Non-negotiable details: (1) the centrifuge's unshaded state is 'glow-unshaded', NOT spinning-unshaded; (2) sprite paths use the _WF no-leading-slash convention (Resources/Prototypes/_WF/Entities/Structures/Machines/safety_deposit_box.yml:48); (3) WFCentrifuge MUST map BOTH enum.GravityGeneratorVisualLayers.Base and .Core - Content.Client/Gravity/GravitySystem.cs:35 does an unconditional LayerMapGet(Core) whenever PowerChargeVisuals.Charge changes and throws otherwise - and all four coreXState fields point at glow-unshaded; (4) WFCentrifuge maxHandledMass is 3000 and must never be <= 0 (Gravity.cs:69 reads <= 0 as infinite lift while the readout at :593 only reads < 0 that way); (5) the anchor's fixture layer is [MidImpassable, LowImpassable] and never Impassable, with the reason in a YAML comment; (6) EVERY new Repairable block must spell out `qualities: [ Welding, Applicating ]` - BaseStructure narrows the component default to Applicating only (Resources/Prototypes/Entities/Structures/base_structure.yml:32-34, the Mono nanite change, overriding RepairableComponent.cs:33-34), so without this the welder repair D9 asks for silently does not work and Stage 6's repair test fails; (7) WFCentrifuge and WFAnchorCrate both carry `- type: Anchorable / flags: None`; (8) WFGravityProjector gets NO Fixtures block - it keeps the inherited 1x1 - plus the YAML comment explaining that the 64x64 art overhangs its single anchored tile because an even-sided AABB centred on a tile centre cannot align to the snap grid and AnchorEntity claims one tile regardless; (9) WFChunkBerthMarker MUST be `parent: MarkerBase` (Resources/Prototypes/Entities/Markers/marker_base.yml:1-18) - without ClickableComponent it can never be examined (Content.Client/Gameplay/GameplayStateBase.cs:152-158) and the berth examine handler would be dead; keep categories: [ Mapping ]; (10) WFAnchorCrate is `price: 0` and only WFAnchorCrateReplacement carries `price: 400000`, so the crates shipping with the hull do not inflate the appraisal ShipyardTests.cs:85-93 measures; (11) the anchor's Destructible Breakage trigger is 300 and must match WFGravityAnchorComponent.BreakDamage - a Stage 6 test asserts this.

### Stage 3 - F3 server: anchors and crates
Files: Content.Server/_WF/PlanetCracker/Anchors/WFGravityAnchorSystem.cs, Content.Server/_WF/PlanetCracker/Anchors/WFGravityAnchorSystem.Pairing.cs, Content.Server/_WF/PlanetCracker/Anchors/WFAnchorCrateSystem.cs
Gate: dotnet build Content.Server/Content.Server.csproj -c Debug -- zero errors. Then start the headless server once (dotnet run --project Content.Server -c Debug -- --cvar net.port=1213) and confirm it reaches 'Server started' with no duplicate-directed-subscription crash, no by-ref/by-value subscription exception and no ErrorNode in the log, then stop it.

Implement plan sections C.1-C.3. Before writing a line, re-run the grep in plan C.1 and confirm the only hits are Stage 1's and Stage 2's files. Specifics: (1) both classes are `public sealed partial class` (RA0049); (2) the ground-layer gate is TryGetPlanetGround, which requires `xform.GridUid is {} grid && xform.MapUid == grid` FIRST, then WFPlanetLayerComponent, then CEZMapComponent.Depth == 0, then MapGridComponent. MapUid alone is WRONG: it is the ground map for every entity on the layer INCLUDING cargo sitting on a landed transport's deck, which is exactly the case the rule forbids. Anchoring resolves the grid from the coordinates (AnchorableSystem.cs:288-296) and registers into that grid's snap cell, and on a Wolfgate ground layer the map entity IS the grid (WFPlanetNetworkSystem.cs:122, asserted at PlanetNetworkTest.cs:95-99). Never use CEZGroundLayerComponent, whose own doc comment says it is presentational (CEZGroundLayerComponent.cs:10-15). Pass the resolved Entity<MapGridComponent> into FootprintFree and ReserveFootprint so all three agree on one entity; (3) use AnchorStateChangedEvent as the ONLY anchor/unanchor handler - not UserAnchoredEvent, which would double-fire on the wrench path and miss explosions; (4) AnchorAttemptEvent and UnanchorAttemptEvent carry no reason field, so always _popup.PopupEntity(...) BEFORE args.Cancel(); (5) the footprint check loops the public AnchorableSystem.TileFree(Entity<MapGridComponent>, Vector2i, layer, mask) (AnchorableSystem.cs:302) over all nine tiles AND requires each tile non-empty, because AddToSnapGridCell silently returns false on an empty tile (SharedMapSystem.Grid.cs:1290); (6) RE-RUN the ground and footprint checks in the AnchorStateChangedEvent{Anchored:true} handler, not only at attempt time: AnchorAttemptEvent fires before the 8 s do-after starts (AnchorableSystem.cs:278, do-after at :249) and OnAnchorComplete re-validates only ONE tile (:142-147), so something can anchor inside the footprint during the delay. BeforeAnchoredEvent is not cancellable (AnchorableComponent.cs:85-88), so on failure popup wf-anchor-lost-room and _transform.Unanchor; (7) on success call BiomeSystem.ReserveTiles(ground, footprintAabb.Enlarged(0.2f), scratchList, biome, grid) with a reusable List field you Clear() first (signature at Content.Server/Parallax/BiomeSystem.PlanetSetup.cs:76, precedent at WFPlanetNetworkSystem.cs:136-138) - anchoring itself protects only one of nine tiles (BiomeSystem.ChunkLoader.cs:46/:281); (8) EnsureComp<CEPvsOverrideComponent> on anchor, RemComp on unanchor; (9) UnanchorAttemptEvent refuses ONLY when IsArmed(State) (Drilling/Locked/Off), NOT when Paired: the design refuses unwrenching a LOCKED anchor (PLANET_CRACKER_DESIGN.md:159) and explicitly allows AnchorsPlaced -> Surveying on 'anchor unwrenched' (:82); refusing at Paired would make placement irreversible, since pairing is automatic on the second wrench-down and the crew could never adjust d; (10) exactly ONE SetState writer, which also does _appearance.SetData(uid, WFAnchorVisuals.State, state) and _ambient.SetAmbience(uid, drilling); (11) the drill Update is 1 Hz and does NOT pause while Damaged - DamageChangedEvent sets the networked flag and raises WFAnchorDamagedEvent and nothing more; the 50% pause in the design is on the CRACK timer during Cracking (PLANET_CRACKER_DESIGN.md:92) and belongs to F4; (12) RepairedEvent is a [ByRefEvent] readonly record struct (Content.Shared/Repairable/RepairableSystem.cs:85-87) so subscribe BY REF; (13) both verbs go in ONE GetVerbsEvent<AlternativeVerb> subscription using Disabled + Message rather than hiding the verb (Content.Shared/Verbs/Verb.cs:91/:101, pattern at ScuttleDeviceSystem.cs:78-99); (14) the switch-off verb raises the broadcast by-value cancellable WFAnchorSwitchOffAttemptEvent and F3 subscribes nothing to it; (15) the crate does NOT use FlatpackComponent.

### Stage 4 - F1 server: cracker, projector, ownership, capacity, and the D11 hook
Files: Content.Server/_WF/PlanetCracker/Cracker/WFCrackerSystem.cs, Content.Server/_WF/PlanetCracker/Cracker/WFGravityProjectorSystem.cs, Content.Server/_WF/PlanetCracker/Cracker/WFCrackerOwnershipSystem.cs, Content.Server/_WF/PlanetCracker/Cracker/WFAnchorCapacitySystem.cs, Content.Server/_WF/PlanetCracker/Cracker/CEZLevelsSystem.WFVirtualMass.cs, Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs, Content.Server/_WF/Administration/Systems/AdminVesselSpawnSystem.cs
Gate: dotnet build Content.Server/Content.Server.csproj -c Debug -- zero errors. Headless server start as in Stage 3. Confirm by grep that CEZLevelsSystem.Gravity.cs contains exactly two // WOLFGATE lines and that no other file outside _WF was touched.

Implement plan sections C.4-C.8 plus the two upstream lines. Specifics: (1) all four _WF systems are `public sealed partial class`; (2) WFCrackerSystem is a skeleton - state storage and berth resolution only; TryGetBerthCentre = markerWorldPos + markerWorldRot.ToWorldVec() * berth.Distance, and Angle.Zero is Direction.South in Robust so a marker facing north is Angle.FromDegrees(180); (3) WFGravityProjectorSystem is server-only because RefreshPartsEvent and UpgradeExamineEvent are EntityEventArgs CLASSES declared in Content.Server/_NF/Construction/ConstructionSystem.Machine.Upgrades.cs:127 and :135 - subscribe both BY VALUE; read args.PartRatings[comp.RatedPart] (GetPartsRatings fills an entry for every MachinePartPrototype and defaults an absent part to 1.0f, :100-113) and set CrackTimeMultiplier = MathF.Pow(comp.PartScaling, rating - 1f); it needs SEVEN subscriptions including an ExaminedEvent handler emitting wf-projector-examine-multiplier and, when Broken, wf-projector-examine-broken, or those two locale keys have no consumer and the Broken flag has no player-visible surface in F1; (4) WFCrackerOwnershipSystem subscribes ShipyardShuttlePurchaseEvent BROADCAST and BY VALUE - the event carries no [ByRefEvent] (Content.Shared/_Mono/Shipyard/ShipyardShuttlePurchaseEvent.cs:3-7) and RT throws on a by-ref/by-value mismatch (EntityEventBus.Broadcast.cs:216-222); both raise sites are broadcast (ShipyardSystem.Consoles.cs:448, ShipyardSystem.WolfgateDeed.cs:109) so there are zero existing broadcast subscribers; do NOT use ShipBoughtEvent; (5) BindAboard must be public - it has three callers: this hook, WFTestGridFactory in Stage 5, and AdminVesselSpawnSystem; (6) add ONE guarded call to AdminVesselSpawnSystem.TrySpawnVessel just before the admin-log line at :51 - that method raises nothing today and feeds SpawnVesselCommand.cs:74 and ErtSystem.cs:148, so without it an admin- or ERT-spawned cracker carries unbound anchors; that file is _WF so it costs nothing against the upstream budget; (7) WFAnchorCapacitySystem's 1 Hz sweep counts only UNANCHORED anchors and crates and SKIPS any grid carrying WFPlanetLayerComponent - a planet ground layer is itself a grid, and adding mass to it could flip HasPooledGravgenSupport for the whole planet network; it maintains WFGridAnchorLoadComponent.VirtualMass on carrying grids (EnsureComp when non-zero, RemComp when zero), updates Aboard, and pops only on the RISING edge past Capacity; (8) CEZLevelsSystem.WFVirtualMass.cs declares `public sealed partial class CEZLevelsSystem` in the _CE namespace but lives under _WF; it must declare NO [Dependency] fields, only EntitySystem's inherited query helpers, so it cannot collide with the _CE partials; (9) the two upstream lines in CEZLevelsSystem.Gravity.cs are the ONLY edits outside _WF in this whole feature: `mass += GetWFVirtualMass(grid);` after :452 inside the foreach (at the same indent as the braceless if, so it runs unconditionally) and `gridMass += GetWFVirtualMass(gridUid, networkGrids);` after the if/else ending at :606 in TryGetGravgenLoad, each with a // WOLFGATE comment. CEZGridNetworkComponent.Grids is a HashSet<EntityUid> (Content.Shared/_CE/ZLevels/Core/Components/CEZGridNetworkComponent.cs:21), which satisfies IReadOnlyCollection<EntityUid>.

### Stage 5 - test-grid factory and admin command
Files: Content.Server/_WF/PlanetCracker/Testing/WFTestGridFactory.cs, Content.Server/_WF/PlanetCracker/Commands/WFCrackerCommand.cs, Content.Shared/_WF/Administration/WolfgateAdminCommands.cs
Gate: dotnet build Content.Server/Content.Server.csproj -c Debug -- zero errors. Headless server start, then in the server console run `wfcracker spawn cracker` and `wfcracker spawn transport` and confirm both report a grid with no exception in the log.

Implement plan sections F and G. The tile layouts and entity lists in F.1 and F.2 are exact - 15x15 with FOURTEEN entities for the cracker (6 singletons: ComputerShuttle, WFCrackConsole, DebugGyroscope, WFCentrifuge, WFChunkBerthMarker, AirlockShuttle; plus 2 WFGravityProjector, 2 WFAnchorCrate, 4 DebugThruster) and 7x9 with 8 for the transport - and the Stage 6 tests assert those counts, so do not improvise. Specifics: (1) WFTestGridFactory declares NO subscriptions; (2) resolve tiles through ITileDefinitionManager (_tileDefs["FloorSteel"].TileId, Content.Shared/Maps/ContentTileDefinition.cs:36), never `new Tile(1)`; (3) build one List<(Vector2i, Tile)> and call SharedMapSystem.SetTiles ONCE per grid - CEZGridConnectorSystem re-floods every connector on every TileChangedEvent anywhere in the world (CEZGridConnectorSystem.cs:105); (4) spawn at new EntityCoordinates(grid.Owner, new Vector2(x + 0.5f, y + 0.5f)) - the tile-centre offset is not added for you; (5) use DebugThruster (thrusters.yml:160-168) and DebugGyroscope (:280-291), never Thruster/Gyroscope: both carry requireSpace: false and needsPower: false, while ThrusterSystem.CanEnable:478 requires an exposed nozzle at :499, so on a solid hull a real thruster is silently disabled with LinearThrust stuck at 0; set LocalRotation to 0/90/180/270 degrees, mapping to LinearThrust index (int)GetCardinalDir()/2 = South 0, East 1, North 2, West 3 (ThrusterSystem.cs:342); (6) power the grid with SharedPowerReceiverSystem.SetNeedsPower(uid, false) on every powered machine rather than cabling - PowerNetSystem.IsPoweredCalculate:330 short-circuits on !NeedsPower, letting the centrifuge's PowerCharge ramp to MaxCharge so GravityGeneratorSystem.OnActivated:97-105 sets GravityActive within a tick; (7) after spawning the two crates on the cracker, call WFCrackerOwnershipSystem.BindAboard(grid) and override the berth marker's component to Size (12,12) / Distance 8; the crates stay UNANCHORED - they are BaseStructureDynamic with Anchorable flags: None and cannot be anchored at all (AnchorableComponent.cs:39, AnchorableSystem.cs:267-271); (8) add `public const string Cracker = "wfcracker";` after Planet (line 16) of WolfgateAdminCommands.cs; (9) the command's shape is copied from Content.Server/_WF/PlanetCracker/Planets/Commands/WFPlanetCommand.cs:25-92, resolving the caller's map and world position the way SpawnVesselCommand.cs:49-54 does, with a +(8,8) / +(8,-12) offset; (10) no admin-tab button.

### Stage 6 - client overlay and integration tests
Files: Content.Client/_WF/PlanetCracker/Anchors/WFCrackCircleOverlay.cs, Content.Client/_WF/PlanetCracker/Anchors/WFCrackCircleOverlaySystem.cs, Content.IntegrationTests/Tests/_WF/PlanetCracker/CrackerTestGridTest.cs, Content.IntegrationTests/Tests/_WF/PlanetCracker/GravityAnchorTest.cs, Content.IntegrationTests/Tests/_WF/PlanetCracker/PlanetCrackerPrototypeTest.cs
Gate: dotnet build Content.Client/Content.Client.csproj -c Debug && dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj -c Debug, then dotnet test Content.IntegrationTests --filter "FullyQualifiedName~_WF.PlanetCracker" -- all tests green, including the eight pre-existing PlanetNetworkTest cases. Do not trust local CRLF, mapchecker or map-test results on Windows; read those from the PR checks with gh --repo.

Implement plan section D and the test list. Client specifics: (1) first Overlay subclass under Content.Client/_WF - copy Content.Client/Mining/MiningOverlay.cs:12 for structure, Content.Client/Physics/JointVisualsOverlay.cs:12 for the NetEntity-target shape, and the _CE ctor idiom of IoCManager.InjectDependencies(this) then _entityManager.System<T>() (CEZGridConnectorOverlay.cs:35-41); (2) Space is OverlaySpace.WorldSpaceBelowEntities; (3) filter on xform.MapID != args.MapId (MiningOverlay.cs:62) - during a z-pass args.MapId IS the lower map, so no manual transform; (4) TryGetEntity on the partner MUST be allowed to fail - anchors are 16-40 tiles apart and the far one can be outside PVS; (5) draw each pair once with a uid.Id ordering guard; (6) cache the ring vertices and emit one DrawPrimitives(DrawPrimitiveTopology.LineStrip, span, Color.ToSrgb(colour)) - DrawCircle emits max(16, radius*16) segments (Clyde.RenderHandle.cs:432), ~352 lines at a 22-tile radius per z-pass per viewport, and DrawPrimitives does NOT do the sRGB conversion DrawCircle does at :441; (7) colours from Content.Client/_WF/Stylesheets/WolfgateSkin.cs (Danger/Good/Caution/AccentDim), never literals; (8) overlay exceptions are swallowed into the 'clyde.overlay' sawmill (Clyde.HLR.cs:164-168), so a missing circle means reading the client log; (9) nothing may touch System.IO, System.Diagnostics.Process or reflection - Content.Client is sandbox-verified by Content.IntegrationTests/Tests/Utility/SandboxTest.cs:14. Test specifics: follow the PlanetNetworkTest.cs house style exactly (#nullable enable, await using var pair = await PoolManager.GetServerClient(), server.WaitPost / WaitAssertion / WaitRunTicks, and ALWAYS DeleteNetwork and delete any map you created before pair.CleanReturnAsync - the pool runs two pairs concurrently); enable wf.planet_networks with pair.Server.CfgMan.SetCVar before building a stack; build real stacks with WFPlanetNetworkSystem.BuildNetwork the way PlanetNetworkTest.BuildStandalone:76-97 does; read masses through CEZLevelsSystem.TryGetGravgenLoad rather than recomputing tiles x 0.5; allow 15 ticks, not 1, before asserting anything power-derived (the fork's power loop runs every 0.5 s, Content.IntegrationTests/Tests/Power/PowerTest.cs:204). Do NOT write a blanket 'every spawned entity is Anchored' assertion - the crates cannot be anchored at all; assert Anchored true per named machine role and Anchored false for the crates, and cover the original intent by asserting TryGetTileRef is non-empty under every spawned entity.


## TESTS
- CrackerTestGridTest.CrackerGridHasTheExpectedContents - BuildCracker on a plain pair.CreateTestMap() map produces 225 non-empty tiles and exactly FOURTEEN spawned entities: one each of ComputerShuttle, WFCrackConsole, WFCentrifuge, WFChunkBerthMarker, AirlockShuttle and DebugGyroscope, two WFGravityProjector, two WFAnchorCrate and four DebugThruster.
- CrackerTestGridTest.EverythingSitsOnASolidTile - for every one of the fourteen entities, TryGetTileRef under its coordinates is non-empty. This is the layout-typo guard that replaces revision 1's blanket Anchored assertion.
- CrackerTestGridTest.OnlyTheMachinesAreAnchored - Transform.Anchored is true for the two consoles, the centrifuge, the berth marker, the airlock, the gyroscope, both projectors and all four thrusters, and FALSE for both WFAnchorCrate. The crates parent BaseStructureDynamic (anchored: false, base_structure.yml:39-49) and carry Anchorable flags: None, which AnchorableSystem.cs:267-271 rejects outright, so they can never be anchored.
- CrackerTestGridTest.TransportGridHasTheExpectedContents - BuildTransport produces 63 non-empty tiles and exactly 8 entities: ComputerShuttle, WFTransportGravgen, AirlockShuttle, one unanchored WFAnchorCrate and four DebugThruster.
- CrackerTestGridTest.HullMassMatchesTileCount - PhysicsComponent.FixturesMass is 112.5 on the cracker and 31.5 on the transport, i.e. tileCount * ShuttleSystem.TileDensityMultiplier. Breaks loudly if upstream changes the multiplier (ShuttleSystem.cs:85).
- CrackerTestGridTest.TransportGravgenIsRatedForTheHullPlusOneAnchor - after 15 ticks, TryGetGravgenLoad(transport) reports capacity 40 and mass 37.5 with one crate aboard (hull 31.5 + one VirtualMass 6); assert mass <= capacity, capacity < 50 (a real downgrade from stock GravityGeneratorMini, gravity_generator.yml:147) and capacity < 63 (it cannot lift twice its own hull).
- CrackerTestGridTest.SecondAnchorOverloadsTheTransport - spawn a second WFAnchorCrate on the transport, run 15 ticks, and assert TryGetGravgenLoad's mass rises to 43.5 and now EXCEEDS capacity 40. This is the test that proves D11 actually bites: without the two // WOLFGATE lines in CEZLevelsSystem.Gravity.cs the mass would stay at 31.5 regardless of cargo (Gravity.cs:452).
- CrackerTestGridTest.DeployedAnchorsDoNotLoadAPlanetLayer - on a real Asclepiu ground layer, wrench two anchors down and assert the ground map has no WFGridAnchorLoadComponent and that TryGetGravgenLoad for the layer is unchanged. Guards the two exclusions in the capacity sweep (anchored entities and WFPlanetLayerComponent grids); without them a deployed anchor could flip the lift check for a whole planet network.
- CrackerTestGridTest.AnchorCapacityCountsWhatIsAboard - with two crates on the transport, WFAnchorCapacityComponent.Aboard reaches 2 within two seconds while Capacity stays 1.
- CrackerTestGridTest.CentrifugeIsRatedAboveTheHull - the cracker's WFCentrifuge reports capacity 3000 through TryGetGravgenLoad, strictly greater than the hull's 112.5 and strictly greater than zero (the <= 0 means infinite trap at Gravity.cs:69 versus the readout at :593).
- CrackerTestGridTest.CentrifugeActivatesWithoutCabling - after 15 ticks the cracker grid has an enabled GravityComponent, proving the SetNeedsPower(false) route drives PowerCharge to MaxCharge and GravityGeneratorSystem.OnActivated.
- CrackerTestGridTest.BerthCentreSitsOffTheHull - TryGetBerthCentre returns grid-local (7.5, 22.5) for the tiny cracker, 8 tiles beyond the marker on the north hull edge, and that point is outside the 15x15 tiled area.
- CrackerTestGridTest.CratesAreBoundToTheCracker - both WFAnchorCrate aboard the spawned cracker have Cracker set to the cracker grid's NetEntity, and a crate spawned on an unrelated grid has Cracker null.
- CrackerTestGridTest.AdminSpawnBindsAnchorsToo - call WFCrackerOwnershipSystem.BindAboard directly on a hand-built grid carrying WFPlanetCrackerComponent and two unowned crates, and assert both are stamped. Covers the AdminVesselSpawnSystem / ErtSystem path, which raises no ShipyardShuttlePurchaseEvent (AdminVesselSpawnSystem.cs:32-53).
- GravityAnchorTest.AnchoringIsRefusedOffAPlanetGround - wrenching a WFGravityAnchor leaves it unanchored and Loose on (a) a plain test grid, (b) an air layer at depth 1 of a real Asclepiu stack, and (c) a SHUTTLE GRID PARKED ON THE GROUND LAYER; on the ground layer itself it succeeds and reaches Deployed. Case (c) is the one a MapUid-only gate would wrongly allow, and is the intended transport-cargo-bay flow the rule forbids.
- GravityAnchorTest.AnchoringIsRefusedWithoutAClearFootprint - place a WallSolid one tile diagonally from the target on the ground layer and assert the anchor refuses; remove it and assert it succeeds. Covers the nine-tile check AnchorableSystem does not do (it checks one tile at :237-242).
- GravityAnchorTest.FootprintIsRecheckedWhenTheWrenchFinishes - start the 8 s anchoring do-after on a clear 3x3, anchor a WallSolid into the footprint mid-delay, let the do-after complete, and assert the anchor ends up UNANCHORED with the wf-anchor-lost-room popup. Guards the gap between AnchorAttemptEvent (raised before the do-after, AnchorableSystem.cs:278) and OnAnchorComplete's single-tile re-check (:142-147).
- GravityAnchorTest.DeployedAnchorReservesItsFootprint - after anchoring, all nine tile indices under the anchor are present in the ground map's BiomeComponent.ModifiedTiles for their chunk origin and are non-empty. Guards the F0 open risk that biome chunks unload out from under a parked structure.
- GravityAnchorTest.PairingRespectsTheBand - two anchors of the same owner at 10 tiles do not pair; at 16, 28 and 40 they do (Paired, Partner set both ways, one WFAnchorPairFormedEvent with the right Distance); at 48 they do not.
- GravityAnchorTest.PairingRespectsOwnership - two anchors bound to different crackers at 24 tiles do not pair; two with the same Cracker do; two with both Cracker null do (the dev/test hand-spawn case).
- GravityAnchorTest.PairingRespectsTheGrid - an anchor on the ground layer and one on another map never pair, even at an in-band world distance.
- GravityAnchorTest.DrillRunsAndLocks - start the drill on a paired anchor: Drilling, WFAnchorDrillStartedEvent fires, examine reports a rising percentage, and with DrillDuration overridden to 2 s the anchor reaches Locked within 4 s and raises WFAnchorDrillFinishedEvent.
- GravityAnchorTest.DamageSetsTheFlagAndRaisesTheEvent - apply damage past BreakDamage * DamageFraction mid-drill: Damaged flips true and WFAnchorDamagedEvent fires with Damaged true, the networked flag reaches the client, and the drill STILL locks on its original deadline. F3 only reports the condition; what it gates is F4's decision (design section 3 puts the pause on the crack timer). Heal it and assert the event fires again with Damaged false.
- GravityAnchorTest.BreakDamageMatchesThePrototype - WFGravityAnchorComponent.BreakDamage equals the value of the prototype's Destructible Breakage-acting DamageTrigger. Guards the hand-maintained mirror that exists because DamageChangedEvent exposes only TotalDamage (DamageableSystem.cs:533).
- GravityAnchorTest.BreakingDissolvesThePairAndNeedsARelock - damage a locked anchor past its Breakage threshold: Broken, WFAnchorBrokenEvent raised, both Partner fields cleared, survivor demoted to Deployed, one WFAnchorPairDissolvedEvent. Repair it WITH A WELDER and assert it returns to Deployed and re-pairs - this is the assertion that fails if the prototype's Repairable qualities were left at BaseStructure's Applicating-only default (base_structure.yml:32-34).
- GravityAnchorTest.DestroyingDissolvesThePair - QueueDel one half of a locked pair: WFAnchorDestroyedEvent and WFAnchorPairDissolvedEvent are raised and the survivor is Deployed with Partner null.
- GravityAnchorTest.UnwrenchingIsRefusedOnlyWhenArmed - a Drilling, Locked or Off anchor stays anchored when wrenched; a Deployed OR PAIRED one unanchors, and unanchoring a paired half dissolves the pair and leaves the survivor Deployed. The Paired case is the one that lets a crew re-site anchors to change the cut radius.
- GravityAnchorTest.SwitchOffIsGatedAndCancellable - the switch-off verb is Disabled while Paired or Drilling; on a Locked anchor it succeeds (Off, WFAnchorSwitchedOffEvent). With a test subscriber cancelling the broadcast WFAnchorSwitchOffAttemptEvent, the anchor stays Locked - proving the F7 veto hook works.
- GravityAnchorTest.CrateUnpacksOnlyOnAGroundLayer - prying a WFAnchorCrate refuses on a plain grid, on an air layer, and on a shuttle grid parked on the ground layer; on the ground layer with a clear 3x3 it spawns a WFGravityAnchor, copies Cracker from the crate, and deletes the crate.
- GravityAnchorTest.CutRadiusFollowsD21 - GetCutRadius(24f, 2f) == 14f and GetCutRadius(40f, 2f) == 22f, matching design section 5's 20-44 tile diameter range.
- PlanetCrackerPrototypeTest.EveryPrototypeSpawns - index and spawn WFGravityAnchor, WFAnchorCrate, WFAnchorCrateReplacement, WFCentrifuge, WFGravityProjector, WFTransportGravgen, WFCrackConsole, WFSectorSurveyConsole, WFChunkBerthMarker, WFCentrifugeCircuitboard and WFGravityProjectorCircuitboard with no exception and no error in the server log.
- PlanetCrackerPrototypeTest.EverySpriteStateExists - for each of those prototypes, resolve every SpriteComponent layer's RSI through the client half's IResourceCache and assert the named state is present. The regression guard on 'prototypes reference the placeholder RSIs verbatim'; specifically catches the centrifuge's glow-unshaded (not spinning-unshaded).
- PlanetCrackerPrototypeTest.CentrifugeMapsBothGravgenLayers - the WFCentrifuge sprite maps both enum.GravityGeneratorVisualLayers.Base and .Core. Without Core, Content.Client/Gravity/GravitySystem.cs:35 throws a KeyNotFoundException on the client every time the charge changes.
- PlanetCrackerPrototypeTest.EveryRepairableAcceptsAWelder - WFGravityAnchor, WFCentrifuge and WFGravityProjector all list 'Welding' in RepairableComponent.Qualities. Without the explicit list they inherit BaseStructure's Applicating-only narrowing and design D9's welder repair silently does not work.
- PlanetCrackerPrototypeTest.ProjectorStartsAtTierOne - a spawned WFGravityProjector has a machine_parts container holding its board-required parts and CrackTimeMultiplier exactly 1.0 after map-init (ConstructionSystem.Machine.cs:27-33 stocks tier-1 parts, GetPartsRatings defaults to 1.0 at :100-113).
- PlanetCrackerPrototypeTest.ShippedCratesAreFree - WFAnchorCrate's StaticPrice is 0 and WFAnchorCrateReplacement's is 400000. Guards the appraisal headroom ShipyardTests.cs:85-93 will measure once the real cracker map exists.

## OPEN RISKS
- The two // WOLFGATE lines in CEZLevelsSystem.Gravity.cs sit in a hot-ish path and change a fork-wide rule. HasPooledGravgenSupport runs on the throttled gravity check (_nextGravityCheckTime, Gravity.cs:55-56) over every grid in every rigid set, and now does one extra component-query miss per grid. Cheap, but any future content that adds WFGridAnchorLoadComponent to a grid changes whether that grid flies. The capacity sweep's two exclusions - only unanchored entities count, and WFPlanetLayerComponent grids are skipped - are the whole safety margin; loosening either could drop a planet network out of the sky.
- The one-anchor rule now drops the transport rather than merely warning. 31.5 hull + 6 per crated anchor against a 40 rating means the second anchor takes the hull from 37.5 to 43.5 and HasPooledGravgenSupport returns false. That is exactly what D11 asks for, but it is a hard punishment with a one-second popup as the only warning, and the numbers are tuned to a 63-tile test hull. When the real transport map exists, re-derive maxHandledMass and VirtualMass together or the rule will fire at the wrong cargo count.
- The crack circle is invisible from orbit, which is where the crew will be. ScalingViewport.RenderZLevels' downward pass breaks at the first CEZCloudLayerComponent (Content.Client/_CE/ZLevels/Core/ScalingViewport.CEZLevels.cs:186-191) and WFSurfaceAsclepiu sets cloudLayer: true at depth 3 of a 0..4 stack (Resources/Prototypes/_WF/PlanetCracker/planets.yml:8), so an orbiting player renders depth 3 only; look-up renders exactly one map (:215-218). The overlay is useful on the surface and the two air layers and nowhere else. Options for F4/F5: drop the cloud layer from crackable planets, add a _WF opt-out to the walk (a further _CE edit), or accept that orbit sees the site only through the console diagram.
- Dragging a 450-mass anchor will feel exactly like dragging a chair. PullerComponent.WalkSpeedModifier/SprintSpeedModifier are a flat 0.95 regardless of mass (Content.Shared/Movement/Pulling/Components/PullerComponent.cs:28-30) and the pull joint has Stiffness 0 (PullingSystem.cs:501). Making the drag feel heavy needs a new mass-scaled movement modifier this plan does not build.
- The gravity projector's 64x64 art overhangs a 1x1 machine. An even-sided fixture AABB centred on a tile centre cannot align to the snap grid, and AnchorEntity claims one tile whatever the AABB says, so the plan keeps the inherited 1x1 collision and lets the sprite overlap its neighbours. On the real cracker map this needs either a mapper convention (>= 2 tiles between projectors, and nothing walkable directly north of one) or a re-cut 32x32 asset. The F.1 test hull already spaces them 4 apart.
- Biome chunks can still unload under things this plan does not reserve - a landed transport, a dropped crate, a player standing still while the rest of the crew is in orbit. ReserveTiles pins the anchor footprints only. HasGroundUnderFootprint reads live tiles (Gravity.cs:627) and UnloadTiles writes Tile.Empty back roughly 10 s after the last viewer leaves (BiomeSystem.ChunkLoader.cs:294), so a parked transport can lose its ground intermittently. F1's landing behaviour will have to reserve tiles at touchdown or ForceAnchor the landed hull.
- An anchored machine over an emptied tile does NOT fall or unanchor on a planet map. DeparentAllEntsOnTile reparents survivors to gridXform.MapUid (SharedTransformSystem.cs:116), and on a biome map the grid IS the map, so SetParent hits its own early-out (SharedTransformSystem.Component.cs:776-777); CEZ physics also sleeps anchored bodies (CESharedZLevelsSystem.Activation.cs:154-161). F5's extraction must move anchors explicitly rather than assuming the engine drops them into the hole.
- WFCentrifuge.maxHandledMass 3000 is a placeholder with no real hull behind it. It must exceed cracker hull mass plus chunk mass, and the largest chunk (radius 22) is roughly 1520 tiles = 760 mass while a capital hull could be 1500+. Re-derive it against the real map, and never set it to 0 or negative.
- PowerChargeComponent.Active - and therefore GravityGeneratorComponent.GravityActive, the grid's GravityComponent and DescendZ - flips true only at MaxCharge and false only at 0 (PowerChargeSystem.cs:144/:152), with no hysteresis. Design D25's 0.98/0.95 is a console-only concept that must be computed server-side from PowerChargeComponent.Charge, because PowerChargeState quantises it to a byte and only updates while that machine's UI is open (:187/:215). F4 will need _WF partials of both PowerChargeSystem and GravityGeneratorSystem; both components are [Access]-restricted (GravityGeneratorComponent.cs:7-8, PowerChargeComponent.cs:8).
- Anchorable flags: None on the centrifuge blocks the wrench path but nothing else. PowerChargeSystem.OnAnchorStateChange (:35-43) still zeroes Charge and Active on any other cause of unanchoring - an explosion, a grid split - which during Cracking would drop the ship with no warning. F4 should add PreventGridAnchorChanges or its own veto.
- WFGravityAnchorComponent.BreakDamage is a hand-maintained mirror of the prototype's Destructible Breakage trigger, because DamageChangedEvent exposes only TotalDamage (DamageableSystem.cs:533). A Stage 6 test asserts they match, but anyone editing the YAML threshold without the component field silently changes when the anchor reports damaged.
- The berth marker is invisible and unexaminable to ordinary players, by design: MarkerBase brings MarkerComponent, Content.Client/Markers/MarkerSystem.cs:27-36 sets sprite.Visible = MarkersVisible, and ClickableSystem.CheckClick returns false on an invisible sprite (Content.Client/Clickable/ClickableSystem.cs:58). Mappers and admins must toggle markers on to read the berth examine line. If F4 ever wants an in-round berth indicator it needs a second, non-Marker entity or a radar blip rather than this one.
- The circle overlay redraws every frame per z-pass per viewport, and a 22-tile radius is a 176-segment line strip even after the cost guard. Up to three passes render on a planet stack, so roughly 500 lines per frame with one pair on screen. If F4 draws multiple sites at once, move to a cached render target.
- PVS: a 40-tile pair can have one half outside the viewer's replication set. This plan adds CEPvsOverrideComponent to deployed anchors, which is a GLOBAL override (CEPvsOverrideSystem.cs:33-36) - every client replicates every deployed anchor everywhere. Cheap at four anchors, wrong if anchors ever become numerous; AddForceSend or AddSessionOverride are the narrower options.
- CEZGridConnectorSystem marks itself dirty on every TileChangedEvent anywhere in the world and re-floods every connector (CEZGridConnectorSystem.cs:105). Each anchor deployment triggers a ReserveTiles sweep writing up to nine tiles. Negligible at four anchors, but it is the path F5's extraction will hammer with a whole circle of tiles.
- wfcracker spawn builds a grid at the caller's position plus a fixed offset with no collision check - FTLAntiCollisionSystem only runs on FTL arrival. An admin in a crowded area can overlap the new hull with something.
- Content.Client/_WF gains its first Overlay in this feature, and overlay exceptions are swallowed into the 'clyde.overlay' sawmill rather than crashing (Clyde.HLR.cs:164-168). A wrong transform or a null deref presents as 'the circle just is not there'. Visual verification needs a running client, which the user's standing preference says not to drive while they are working - plan a headless-plus-screenshot pass or ask first.
- Every F0 open risk still applies underneath this work: z-eyes generate biome terrain and ore under any orbiting hull; CEZMapComponent.MapAbove is null on every planet-network layer so traversal only works through the network dictionary; the orbit beacon leaks to every console in the sector; and a hull whose gravgen dies while on a layer is stranded by the outbound FTL gate.
