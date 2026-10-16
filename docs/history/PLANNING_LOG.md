# Haugbui — planning log (M1–M7)

Verbatim log kept by the Claude Code planner and implementation sessions from 2026-10-12 to 2026-10-16
(account names and session ids removed). It records findings, decisions, test runs and gaps per milestone; the
current summary is `docs/HANDOVER.md`. Notes about the "Desatracar" keyword, the `auth-gate` hook and switching
GitHub accounts describe the old Claude Code setup and do not apply elsewhere.

# Haugbui planning session — progress

Original goal: produce the 5 planning deliverables in `docs/` (TECHNICAL_FEASIBILITY.md, PRODUCT_REQUIREMENTS.md,
ARCHITECTURE.md, IMPLEMENTATION_BACKLOG.json, DEVELOPMENT_GUIDE.md) for the Valheim mod Haugbui. No gameplay code yet.
Original spec = the long "Project Haugbui" prompt (25 sections) from Kelvin on 2026-10-12.

## Environment (done)
- Target: Windows only. Planning machine is macOS without Valheim.
- Kelvin's Valheim `Managed/` folder (Valheim **1.0.17**) is in `.reference/Managed/` and decompiled (ilspycmd 8.2) to
  `.reference/decompiled/<assembly>/` — gitignored, never commit/redistribute.
- .NET 8 SDK + ilspycmd installed in `~/.dotnet` (use `PATH=$HOME/.dotnet:$HOME/.dotnet/tools:$PATH DOTNET_ROLL_FORWARD=Major`).
- `.gitignore` created. Repo is not a git repo yet.

## Key verified findings (Valheim 1.0.17 decompiled unless noted)
- Valheim 1.0 released 2026-09-09 (Deep North, new chunked save folder format). BepInExPack_Valheim 5.4.2351, Jötunn 2.30.2.
- Missing mod: ZDOs with unknown prefab are kept on load (ZDOMan.cs:423) but the server/host DESTROYS them when the
  zone is instantiated (ZNetScene.cs:225-234). Custom ZDO keys on vanilla prefabs survive (ZDO.Save saves all keys).
  Unknown items inside containers are dropped on next save of that container (Inventory.cs:966).
- Non-persistent ZDOs are never saved (ZDOMan.cs:1638 filters Persistent).
- EnvMan.IsDay/IsNight are static and weather-independent; IsDaylight is weather-dependent (m_alwaysDark) — don't use it.
- Day length prefab 1800 s (third-party; code default 1200) — read EnvMan.instance.m_dayLengthSec at runtime.
- ZNet.GetTimeSeconds world time persisted in the world save; pauses when game closed.
- Simulation distance default guarantees ≥128 m loaded around player; lowest setting only ≥64 m.
- Smelter family (smelter, blastfurnace, charcoal_kiln, windmill, piece_spinningwheel, eitrrefinery, bathtub, piece_FrostKiln):
  RPC_AddOre(string prefab, bool cheated), RPC_AddFuel, RPC_EmptyProcessed; output always spawns as ground ItemDrop at m_outputPoint.
- Beehive + piece_birdnest (Beehive component, Feathers) — RPC_Extract; honey/feathers drop on ground.
- Pickable RPC_Pick(int bonus); crops destroyed after pick; m_harvestable flag; drops on ground.
- Container RPC names changed in 1.0 (RPC_RequestOpen/RPC_OpenResponse...). No static container list. Only the ZDO owner's
  inventory edits persist.
- BaseAI.IsEnemy static; tamed creatures never target players. Faction enum has DeepNorth.
- CraftingStation.Interact opens crafting UI — custom level counting recommended instead of CraftingStation/StationExtension.
- No existing mod named Haugbui. Prior art: Cheb's Necromancy, HiredHands, Automatics, AzuAutoStore, MultiUserChest, Hearthfolk.

## Decisions (product owner, 2026-10-13 — applied to all docs; PRD §23 is now a decisions log)
1. Host-prefab strategy (spike HB-T-007): structures persist as vanilla prefabs + marker keys; the mod changes their look
   at runtime. Fallback: Jötunn custom prefabs + backups.
2. Creature non-persistent; state in the tomb ZDO. No custom items.
3. Upgrades in ANY order: level = 1 + distinct upgrades; every upgrade raises attributes; Longsword → combat,
   Altar → full-time + dark aura, independently.
4. Cooldown: world time only (no real-time option).
5. Production unlock at level 2 (config). Cooking stations dawn-aware; cauldron via RecipeCraftingAdapter;
   workbench-type crafting prepared behind `RecipeStationAllowList`, off in v1.
6. Entering the tomb (dawn, rest, unload) or dying: carried items → tomb storage (player can take them while it sleeps).
7. Transfers destination-first then remove from source, same frame (Dvergr Tidier principle).
8. Movement: incorporeal kinematic flight (owner accepted physics if easier; flight is easier).
9. Away mode: logistics on ZDO data while the player is elsewhere (sorting, delivery, smelter refill). Not harvesting,
   output collection, cooking, cauldron, combat. Chunk loader rejected. Spike HB-T-090 in M1; milestone M12 (release 1.1).
10. Recipe quantities are tunable defaults; only the brief's mandatory ingredients are fixed.

## Status
- [x] Session 1: research + five docs
- [x] Session 2: decisions applied (PRD 118 req IDs, backlog 98 tickets M1–M12, validated)
- [x] Session 3 (2026-10-13): sleep fast mode (HB-046, HB-T-098), recipe cost design (PRD §4.3), M1 Mac-side code:
  - Solution builds on macOS: Directory.Build.props (VALHEIM_MANAGED falls back to .reference/Managed), nuget.config
    (BepInEx feed), compile-only BepInEx.Core 5.4.21 + JotunnLib 2.30.2, BepInEx.AssemblyPublicizer.MSBuild 0.4.3.
    JotunnModStub NOT used (documented in feasibility §1).
  - Core: ProgressionTable, TickScheduler, PieceRecipe/DefaultRecipes/RecipeValidator, GameApiManifest/Checker, JsonWriter.
  - Plugin: HaugbuiPlugin (GUID com.kelvinmarques.haugbui, v0.0.1), HaugbuiConfig, Log, Integration/GameApiSelfCheck,
    Integration/StartupValidation, commands haugbui_status + haugbui_dump_prefabs; DEBUG spikes for HB-T-007/009/090.
  - 50 NUnit tests pass on macOS, incl. manifest checked against the real 1.0.17 DLLs via MetadataLoadContext.
  - Namespace `Haugbui.Game` renamed to `Haugbui.Integration` (clashed with Valheim's global `Game`).
  - README.md (en-US) written; git repo initialised locally.

## Pending
- v0.0.1 pushed to github.com/Chelvis/haugbui_valheim_mod (commit 9e2272f, tag v0.0.1, 2026-10-13). Only collaborator:
  Chelvis (admin). Repo-local git identity: Kelvin Marques <11822757+Chelvis@users.noreply.github.com>.
  Push recipe (gh has both accounts; active one is the work account <work account>):
  `gh auth switch --user Chelvis && git -c credential.helper= -c 'credential.helper=!gh auth git-credential' push && gh auth switch --user <work account>`.
  The local auth-gate hook needs the keyword "Desatracar" from Kelvin before commit/push.
- No LICENSE chosen yet.
- HB-T-008 flight spike not written yet (needs Character/BaseAI reading).
- Remaining HB-T-010 commands come with the tickets that create their systems.
- In-game validation of M1 on Windows: DEVELOPMENT_GUIDE §4.4.

## Windows validation (2026-10-13, Kelvin)
- Load OK, API self-check 43/43, recipes 8/8 (all names verified), haugbui_status OK.
- HB-T-009 PASS (2 chests, 120 s, verify after reload). HB-T-090 chest PASS; smelter consumed 2 ore + 4 coal (processed).
- HB-T-007 look applied on load, GUID/items kept; raft mesh bug fixed (skip TombStone.m_floater) — uncommitted.
- Dump saved to .reference/dumps/haugbui_prefabs_1.0.17.json; findings in TECHNICAL_FEASIBILITY §4.1.
- Open: confirm copper bars dropped, scan timings, no-DLL step; decide RecipeStationAllowList (+ piece_MeadCauldron, piece_preptable?).

## M1 closed / M2 started (2026-10-13)
- Decision: v1 has NO recipe crafting at any CraftingStation (HB-122); HB-T-092 removed. Committed+pushed 557ddc8.
- HB-T-008 flight spike written (Spikes/FlightSpike.cs, guide §4.4) — NOT committed yet (hook needs new "Desatracar").
- M2 code (uncommitted): Core Persistence (Blob, PersistentSlot, TombState/DeliveryRequests/StationJob serializers),
  Core Structures (StructureKind, TombProgression, PlacementRules incl. RebindOrphans), Geometry/Vec3; 116 tests pass.
  Game: Integration/ZdoKeys+ZdoBlobStore, Structures/* (definitions with first-pass kitbash looks, visuals, HaugbuiStructure
  IPlaced+Hoverable, StructureRegistry level/rebind, PlacementCheck+patches, HostPrefabSystem + Piece.Awake/Container hover
  patches, CombatWarning), haugbui_dump_tomb, dump now lists pieces. Guide §4.5 has the Windows test steps.
- Approach change: no placeholder→host object swap; OnPlaced calls ZDO.SetPrefab(host hash) (ARCHITECTURE §8.1).
- Pending M2: HB-T-024 beam (P1) not done; upgrade host is 'piece_table' with fallbacks (unverified name) until the new
  dump; looks need in-game tuning.

## Self-test (2026-10-13, uncommitted until "Desatracar")
- Kelvin asked to automate in-game tests and only test manually a more finished version.
- src/Haugbui/SelfTest (DEBUG only): SelfTestContext (results, cleanup, Reload = ResetZDO + m_instances.Remove + Destroy,
  ZNetScene recreates), Screenshots (temp camera, forced noon, PNG), suites basics/structures/flight/transfers,
  SelfTestRunner (+ haugbui_selftest), AutoTest (-haugbui-autotest "<char>" "<world>" [-haugbui-autotest-quit] drives
  FejdStartup.SetSelectedProfile/OnCharacterStart/FindWorld/OnWorldStart). HostPrefabSystem.SuppressAugmentation simulates
  the mod missing for one object. Guide §4.4.
- Not yet run in game. Unverified names: wall prefabs for the flight suite ("woodwall", ...), "bed", "piece_table".
- Option discussed: run Claude Code on Kelvin's Windows PC to drive build → autotest → read report.

## M2 status (2026-10-13, end of session 3)
- Self-test run #2 on Windows: 81 pass; 13 fails were all visual parts (item world models live under "attach";
  fixed by no longer skipping "attach"). Spike-chest lid bug fixed (confirmed by Kelvin).
- Uncommitted (needs "Desatracar"): attach fix, clear-viewpoint camera, HB-T-024 UpgradeBeam (vanilla
  StationExtension connection effect) + self-test check, arena 9 m, HB-T-099 ticket (looks ideas incl. FeastMeadows,
  treasure chest, Bracelets of the Brave tinted silver on beech stump, Krom, black marble altar).
- Tomb glow tinted purple (VisualPart.Emission → material copy; self-test reports shader/emission/map). Ideas for Krom aura
  ('Draugr feng') and altar purple wisp recorded in HB-T-099.
- M2 is functionally complete; looks are placeholders until HB-T-099 (M10). Kelvin should rerun haugbui_selftest and
  send the folder + haugbui_dump_prefabs output (pieces list) when convenient.
- Kelvin runs haugbui_selftest manually (no r2modman autotest). Extend self-test suites for every new in-game feature.

- Versioning rule (Kelvin): each milestone bumps the version; M2 = v0.0.2 (tagged), M3 will be v0.0.3.

## Next session: start M3 (HB-T-027..039 incl. 025, 040) — creature, flight controller, state machine, day/night,
  death/cooldown, tomb panel v1. Reuse Spikes/FlightSpike (non-persistent Wraith clone, AI off, excludeLayers).
  Add a self-test suite per ticket.

## Next steps
1. Write HB-T-008 flight spike.
2. Kelvin runs §4.4 on Windows and sends haugbui_prefabs.json; record spike results; go/no-go for host prefab (M2).
Usage stop rule (Kelvin, 2026-10-12): stop at 95% of the 5-hour limit; never use extra (USD) usage.

## Parent session notes (2026-10-13, after v0.0.2)
- This session ("Planner") is now the parent; M3 runs in child session "Implement M3 of the Haugbui Valheim mod"
  (worktree from v0.0.2).
- Self-test run 3 (v0.0.2): 95 pass, 0 fail, 1 skip (beam check, spot >10 m from player). Purple tomb glow works
  (tombstone material: Standard shader, HDR _EmissionColor (4.05,0.18,0.18), white emission mask).
- New PO decisions relayed to the M3 session: creature tinted purple (material copies); the Wraith's own dark aura is
  ON only while the Altar is bound (same condition as daytime work), OFF otherwise; HB-053 unchanged (correction:
  an earlier relay said "level 6" — Kelvin clarified it is the Altar, not the level).

## M3 session (2026-10-13, worktree eager-lalande-11da26) — committed 498e5c9, tagged v0.0.3, pushed to main (not yet run in game)
- Version bumped to 0.0.3 (Directory.Build.props + HaugbuiPlugin.PluginVersion); README roadmap M3 ✅; guide §4.4
  table + §4.4.2 manual checks. Tag v0.0.3 only after Kelvin's "Desatracar" and a self-test run.
- Core (155 tests pass): Creature/ItemRules (GameItemType mirror), CarriedInventory + ItemStacksSerializer +
  DepositPlanner, HaugbuiStateMachine (priority Dead > Combat > Returning > tasks, atomic Interacting step, persisted
  subset Dormant/Idle/Dead), PresencePolicy (night = fraction ≤0.25 or ≥0.75, dawn lead, Altar, Rest, world-time
  cooldown, countdown format), HealthMath (dormant regen, rescale on level change), Steering. TombState schema v2
  (CreatureState, position, HealthTime; v1 still loads). Manifest: new feature "Creature".
- Game: Creature/CreaturePrefab (Haugbui_Creature = Wraith clone, non-persistent, no CharacterDrop, tamed, purple
  material copies, aura = Wraith particle systems), HaugbuiCreature (AI disabled + BaseAI.UpdateAI prefix, flight
  steering, 1 m arrival to collider ClosestPoint, telemetry), CreatureVisuals (state light cues, pulse, EffectsQuality
  Low halves; aura on only while Altar bound — PO decision relayed by Planner, corrected from "level 6"),
  TombController (presence tick 0.5 s, spawn/resume/return/enter, death → deposit + cooldown, deposit destination-first
  + overflow blob, destruction drops carried+overflow and unbinds upgrades, toggles with gating), ItemBridge,
  CountdownLabel (TMP + Billboard, 1 Hz, 20 m), UI/TombPanel (Shift+E on tomb; Jötunn wood panel; toggles; open
  storage), hover status line, haugbui_dump_tomb shows creature/carried/overflow. Integration/TimeSource (fake clock
  for self-test). SelfTest/HaugbuiSuite ("haugbui") covers the tickets.
- Known gaps / decisions taken alone (tell Kelvin): tomb unload while out keeps state Idle + position and despawns the
  creature; deposit happens on next load if it is no longer out-time (instead of at unload). No idle sounds (AI off).
  No custom emergence VFX: the Wraith's death effect is reused. Non-owner peers see stale state text in panel/hover
  (countdown label reads the ZDO, so it is correct). State cue "alert sound" not done. HB-T-098 sleep fast mode untouched.
- Never run in game. Next: Kelvin runs `haugbui_selftest` (all) on Windows → fix failures → "Desatracar" → commit,
  tag v0.0.3, push (recipe above). Possible first failures to look at: aura particle paths (root-level systems are
  skipped), Character.Damage on a tamed creature with no attacker, panel layout.
- M3 session report (2026-10-13): M3 code complete, builds, 155 unit tests pass; creature purple tint (_Color/_TintColor/
  _EmissionColor on material copies) and Altar-gated aura done; new "haugbui" self-test suite. Not yet committed or run in
  game — needs Kelvin's "Desatracar" in that session to push, then a self-test run on Windows.
- Self-test run 4 (v0.0.3): 141 pass, 3 fail. Fixes committed+pushed as 6f923cb (zip removed from the repo; tag v0.0.3 left on 498e5c9; was
  uncommitted): Wraith flight root motion overrode the speed cap (prefix on Character.ApplyRootMotion zeroes it
  for Haugbui; manifest updated); overflow test used too few items; Altar test raced the registry recompute; state
  machine now allows Returning → Idle when it should stay out again (Altar built / Rest off on the way). Countdown label
  created inactive so TMP stops warning about the missing LiberationSans font; new check "countdown label has a font".
  Creature materials found: wraith (Custom/Creature) and wraith_smoke (Custom/LitParticles), both _Color white.
- Self-test run 5 (real 6f923cb build, 81 API members): 144 pass, 1 fail ("max speed 3.00 of 2"). Diagnosis: telemetry
  artifact — per-frame delta at ~75 fps catches one 0.04 m physics step in a 13 ms frame (0.04/0.0133 = 3.0); max
  frame jump 0.04 m = 2 m/s × 0.02 s. Root-motion prefix reverted (not needed); speed now sampled in FixedUpdate.
  Also found: deposit did not top up a partly filled stack (CanAddItem is all-or-nothing) → ItemBridge.AddTo offers
  only what fits. Creature tint changed to lighter lilac (0.82, 0.66, 1) at Kelvin's request. Uncommitted.
- 2026-10-13 late: repo root now synced to Windows via Google Drive "My computer"; worktree eager-lalande-11da26 deleted
  (its uncommitted changes applied to root first; branch deleted, it was merged). Mac builds out of tree:
  `HAUGBUI_BUILD_ROOT=$HOME/.cache/haugbui-build dotnet build Haugbui.sln -p:MOD_DEPLOYPATH=` (Directory.Build.props
  opt-in; RepoPaths reads the repo root from assembly metadata). Uncommitted in root: run-5 fixes + this build change.
- Self-test run 6 (02e8b7e): 145/145 PASS. Sync slimming: `.git` moved to /Users/kelvinmarques/Projects/M/haugbui-git
  (separate-git-dir, repo has a gitfile), `.reference` moved to /Users/kelvinmarques/Projects/M/haugbui-reference
  (Directory.Build.props and RepoPaths fall back to ../haugbui-reference), in-tree bin/obj deleted. Uncommitted.

## Parent session (Planner, <session id>) — 2026-10-13
- M3 accepted by Kelvin (colours OK). Tag v0.0.3 moved from 498e5c9 to a124fe2 (tested code + build-path fix), force-pushed.
- M4 child session offered (task chip "Implement M4 of the Haugbui Valheim mod"): works in the root (no worktree), target
  v0.0.4, reports back to "Planner".

## M4 session (child of Planner, 2026-10-13/14, root folder, no worktree) — code complete, NOT yet run in game
- Version 0.0.4 (Directory.Build.props + HaugbuiPlugin.PluginVersion). README roadmap M4 ✅ (0.0.4), guide §4.4 storage
  suite row + commands, ARCHITECTURE §4/§6/§6.1/§7 updated. Tag v0.0.4 only on the commit Kelvin's self-test validates.
- Core (194 tests pass): Geometry/SpatialHash (16 m cells, alloc-free query) + LoadedCoverage; Storage/StorageRules
  (ContainerSnapshot, StorageSelector: existing-item rule = same prefab/quality/world level, rank whole-fit → fewest new
  slots → nearest, split across chests), Storage/TransferEngine (IItemEndpoint, destination-first, rollback, ledger
  record with conservation flag, CarriedEndpoint), Storage/AccessRules (ContainerRules exclusion table, WardAccess with
  vanilla semantics: one permitting ward is enough), Coordination/ReservationTable (leases), RetryBackoff (2→60 s),
  TaskPlanner/TaskPlan/TaskStep (Take/Put). ItemStack now keeps Cheated/CrafterId/CrafterName/CustomData (blob v2, v1
  loads); CarriedInventory.Count/Remove.
- Game: Registry/WorldRegistry (+ RegistryTracker, Awake postfixes incl. ShieldGenerator.Start, cleared on
  ZNetScene.Awake), Integration/AccessPolicy (SinglePlayerAccessPolicy by owner ID), ContainerEndpoint, LoadedArea,
  Coordination/Reservations (table + TransferEngine + ledger ring), Brain/WorkContext + StorageProviders (deposit 100,
  sort tomb storage 50) + HaugbuiWorker (scheduler tick, step execution through the state machine, abort on unloaded
  target / travel timeout / interruption, releases on death/enter/unload). TombController wires the worker; panel shows
  activity + "partially loaded" hint. Commands haugbui_reservations, haugbui_ledger, haugbui_registry. Manifest: ~45
  members under Features.Containers. ItemBridge: quality-aware RoomFor/Count/RemoveFrom, full item data.
- Self-test: new suite "storage" (StorageSuite.cs); FakeTime/WaitFor/PlaceStructure/PlaceVanilla shared; HaugbuiSuite
  pauses the worker (Worker.Paused) so nearby chests cannot change M3 checks.
- Decisions taken alone (told Kelvin): stacking onto an existing stack follows vanilla (stack becomes cheated; joining
  items' custom data dropped). Backoff and retry use world time. Ward rule = vanilla chest-open rule for the owner.
  Container freshness guard only blocks "never loaded" (DataRevision bumps on any key, verified in ZDO.Set).
- Known gaps: deposits do not reserve destination room (re-read at put time); WorkContext snapshots every container
  in radius per plan (perf budget HB-201 measured in M11); manual "Debug compare in a large base" → haugbui_registry.
- Next: Kelvin runs `haugbui_selftest` (all) on Windows → fix failures → "Desatracar" → commit, tag v0.0.4, push →
  SendMessage report to "Planner".
- Self-test run 7 (0.0.4 uncommitted): 187 pass, 3 fail, log clean. Fixed: obliterator verdict came out "Vehicle"
  (its container uses m_rootObjectOverride) → obliterator checked first; random-transfer totals ignored the carried
  inventory (test bug; 0 duplicates in the ledger) → carried included. All sorting/reservation/loaded-area checks passed.
- Self-test run 8: 188 pass, 2 fail — both "50 m disc fully loaded" assumptions (75% loaded where Kelvin stood; the mod
  reported it correctly). Replaced by place-independent checks (tomb spot loaded, panel hint ⇔ coverage < 100%).
  Kelvin's feedback applied: flight suite now stacks 4 walls (no support wear) and checks the patrol line hits them
  (a single wall sat half underground; GetGroundHeight sees terrain only); runner removes hostile MonsterAI creatures
  within 80 m every second during the run (report header shows the count) — a monster had killed a test Haugbui.
  Trees not cleared: flight ignores them by design and no check depends on them (Kelvin asked "if it makes sense").
- Self-test run 9: 191/191 PASS (3 hostiles cleared, the Haugbui crossed the 4-wall stack). "Desatracar": committed
  8c857ab "v0.0.4: M4 inventory, transfers and storage sorting", tag v0.0.4 (lightweight; v0.0.1–0.0.3 are annotated),
  pushed to main. Google Drive temp folder `.tmp.drivedownload/` was caught before the push and added to .gitignore.
  M4 DONE; report sent to Planner.
- M4 done by child session: v0.0.4 at 8c857ab (tagged, pushed), self-test 191/191 on Windows, 194 Core tests.
  Child decisions: stacking onto an existing stack follows vanilla (stack becomes cheated if any part is; customData of
  incoming items is lost — minor deviation from HB-061); ward = vanilla open-chest rule; container guard only blocks
  "never loaded". Gaps: deposits don't reserve destination space; WorkContext snapshots all containers each planning
  (M11 perf budget). Next: M5 (Collection and Delivery chests).
- PO decision (2026-10-13): Haugbui moves must NEVER make items cheated. Rule handed to the M5 session as its first task:
  merge into an existing stack only when quality, world level, m_cheated and customData are identical; otherwise use
  another slot/destination; carry the source flag exactly (also for station RPCs). Update HB-061 + self-test checks.
- M5 child session offered (task chip "Implement M5 of the Haugbui Valheim mod"), target v0.0.5, reports to "Planner".

## M5 session (child of Planner, 2026-10-13/14, root folder, no worktree) — code complete, NOT yet run in game
- Cheated-flag fix (PO decision 2026-10-13, HB-061): Haugbui never calls the public `Inventory.AddItem` overloads (they
  merge by name/quality/world level; `AddItem(ItemData)` sets m_cheated on the target stack). `ItemBridge.AddTo` tops up
  identical stacks (`ItemStack.StacksWith`: prefab, quality, world level, variant, cheated, crafter, custom data) and
  fills empty slots via private `Inventory.AddItem(item, amount, x, y)` (never touches m_cheated; raises Changed only with
  a local player, so AddTo raises it otherwise). Count/Remove/RoomFor on every endpoint and `ContainerSnapshot` are now
  identical-only, so a cheated item never comes out clean either. PRD HB-061/062/063 + decisions log, ARCHITECTURE §7,
  Core tests (selector, transfers incl. 10k random with cheated stacks), storage self-test checks added.
- M5: Core `DeliveryLedger` (≤10 lines, one open line per item, edit ≤ delivered completes, cancel, clear completed,
  RecordDelivered capped, `Reserved` for HB-087), `DeliveryPlanner` (one trip per line: carried first, nearest sources,
  capped by remaining/chest room/carry), `ContainerSnapshot.Clone/Accept`, `ContainerUse.DeliveryTarget`. 212 tests.
- Game: chests = host piece_chest_wood (5x2), look parts (placeholder), container names; `DeliveryChest` component
  (ledger in `haugbui_requests`, owner writes, re-read on DataRevision change); hover hint + Shift+E → `UI/DeliveryPanel`
  (search box, dropdown of discovered transportable items, amount, Request/Edit/X/Clear done/Close); `WorkContext` roles
  (destinations, sources, collection chests, delivery chests); providers `DeliveryProvider` (70, key
  deliver:<chest>:<item>) and `CollectionProvider` (45, key take:<chest>, rescans every CollectionIntervalSeconds while
  idle); worker records delivered on Put. Manifest: Inventory.AddItem(slot)/GetItemAt/GetWidth/GetHeight/Changed,
  Player.m_localPlayerExists/IsMaterialKnown, Container.CheckAccess/m_checkGuardStone, ZDO.DataRevision.
- Self-test: new suite "chests" (ChestSuite.cs). Version 0.0.5; README roadmap M5 ✅ (0.0.5), guide §4.4 rows.
- Decisions taken alone (tell Kelvin): requests are per prefab (any quality/world level/flag; each moved stack keeps its
  data); editor refuses a second open line for the same item (Request edits it instead); a delivery step that moves
  nothing does not trigger the no-destination backoff; leftovers of an aborted/short trip go back through the normal
  deposit (may land in another matching chest).
- Next: Kelvin runs `haugbui_selftest` (all) on Windows → fix failures → "Desatracar" → commit (cheated fix may be a
  separate commit first), tag v0.0.5 on the validated commit, push → SendMessage report to "Planner".

## PO decision: tribute (wages) system — approved 2026-10-14, to be specified after M5 (avoid doc conflicts)
- The Haugbui charges per shift ("expediente"). Shift = one night out; with the Altar there are 2 shifts per day (day + night).
- Cost per shift in coin units: levels 1–2 = 5, levels 3–4 = 10, levels 5–6 = 15 (configurable).
- Values (confirmed by Kelvin): Coins 1, Amber 5, AmberPearl 10, Ruby 20, SilverNecklace 30.
- Credit is a NUMBER stored in the tomb ZDO (no custom item; survives missing mod). Tomb panel gets a "Tribute" area:
  balance + one "Offer" button per valuable that takes it from the player's inventory. No change/refund items.
- Charge only when a shift starts AND there is work to do (collection chest items, open delivery line, ripe harvest,
  station to supply...); otherwise stay dormant, no charge, re-check periodically. Rest toggle = off, no charge.
- Insufficient balance: does not emerge; panel says "no tribute". Death mid-shift: no refund.
- Away mode (M12) and sleep fast mode (HB-T-098) count as shifts and charge the same.
- Tomb destroyed: remaining balance dropped as Coins on the ground by default; configurable in the BepInEx .cfg
  (option to forfeit).
- Panel shows a timer with the time left in the current shift (until the dawn return lead, or the end of the day shift
  with the Altar).
- Next step: Planner writes PRD requirements + backlog tickets after the M5 session reports, then assigns the work.
- Self-test run 10 (0.0.5 uncommitted, 2026-10-14): 247/247 PASS (chests suite all green, cheated checks green), API
  self-check 133 members, log clean. Weak check found: "never delivers more than requested" read 0 because the one-trip
  delivery finished before the loop sampled the chest (passes trivially) — fix (sample after the loop / per frame
  including the last) pending, not in the validated build.
- "Desatracar" (2026-10-14): committed cd30e74 "v0.0.5: M5 Collection and Delivery chests; moved items never become
  cheated" (single commit), tag v0.0.5 (lightweight: --follow-tags skips it, and sending a tag without the gh
  credential helper hangs on the stale keychain credential — always pass the gh helper), on main at origin. M5 DONE;
  report sent to Planner. Pending: strengthen "never delivers more than requested" self-test check. Next: M6.
- M5 done by child session: v0.0.5 at cd30e74 (tagged), self-test 247/247, 212 Core tests; identical-data stacking
  (HB-061) applied. Gap to fix in M6: self-test "never delivers more than requested" passes trivially.
- Tribute specified by Planner (2026-10-14): PRD §17.1 HB-164..HB-169 + HB-159, tickets HB-T-100..103 added to M6
  (renamed "Ground item collection, harvesting and tribute"); HB-T-094 and HB-T-098 depend on HB-T-101. Assumption:
  resurrection within an already paid shift does not charge again. 102 tickets / 125 req IDs, coverage OK.
- 2026-10-14 PO decisions recorded by Planner: cooldown 1 day (CooldownDays); without Altar return at the next dusk after
  cooldown, with Altar immediately; new tombs start at the next dusk; servant identity in per-player world slots
  (default 3, configurable, eternal, no dismissal, names "Haugbui", "Haugbui #2"…), stored in ZoneSystem global key
  values; refund coins never falsely cheated (cheated part tracked). Tickets HB-T-104 (registry) and HB-T-105 (timing)
  added to M6. Assumption dropped: no same-shift resurrection any more.

## M6 session (child of Planner, 2026-10-14, root folder, no worktree) — code complete, NOT yet run in game
- Version 0.0.6 (Directory.Build.props + HaugbuiPlugin.PluginVersion); README roadmap M6 ✅ (0.0.6); guide §4.4 rows
  `harvest` and `tribute`; ARCHITECTURE §5.1 (M6 priorities, harvest via own RPCs), §5.2 (presence, shifts, tribute,
  slots), §6 keys, §8 (TombState v3, slot global keys), §13 config; PRD decisions log row "M6 implementation choices";
  backlog HB-T-101 AC changed to "each return after a death is a new, charged shift". Tag v0.0.6 only on the commit
  Kelvin's self-test validates.
- Round 1 (HB-T-054..057): Core `Harvest/GatherRules` (GroundItemRules: age ≥ 10 s, player ≥ 3 m, not piece/in tar,
  transportable, owner, ward; HarvestRules: crops = `Pickable.m_harvestable` (the scythe flag), hives/nests level ≥ 1,
  wild pickables and sap by config, never pickables that aggravate; PickupRoute: nearest-chain ≤ 10 m hops, ≤ 8 stops,
  whole items only). StepKind PickUp/Harvest. Game: `DropEndpoint` (ItemDrop as transfer source, owner only, SetStack /
  Destroy), `HarvestGateway` (RPC_Pick / RPC_Extract to the local owner run at once → diff ItemDrop.s_instances, picked
  up in the same step), providers in `Brain/GatherProviders.cs` (OwnDrops 61, Harvest 60, Ground 40; both need a
  destination), worker resolves any MonoBehaviour target, optional steps skip a vanished drop/crop, own leftover drops
  kept 5 min. Config `10 - Harvest` (WildPickables, SapCollectors). Self-test suite `harvest`.
- M5 gap fixed: ChestSuite "never delivers more than requested" now samples for 3 planning intervals after the
  delivery completes and requires exactly 15 seen in the chest.
- Round 2 (HB-T-100..105): Core `WorldClock` (raw 0.15/0.85 boundaries verified in EnvMan.RescaleDayFraction /
  CalculateNight; shift ids 2d / 2d+1; NextDusk, DuskAtOrAfter), `PresencePolicy` on world time (StartTime, ReturnTime,
  WaitRemaining, Timer) — fixes the old dawn-lead math that used the rescaled fraction; `Tribute/TributeRules`
  (TributeTable from config with fallback, TributeBalance spends the cheated part first, ShiftRules), `ServantSlots`
  (slot blob, lower-case hex, key names, LowestVacant). TombState v3 (slot index, balance, cheated balance, last paid
  shift, start time). Game: `ServantRegistry` (global keys written straight to ZoneSystem.m_globalKeys/Values —
  verified: world save writes non-server-option keys, load lower-cases via GlobalKeyAdd, SetGlobalKey would log every
  new text in the player profile), TombController StartShift/Offer/refund/slot binding (new tombs in OnPlaced, old
  tombs on load in GUID order), PlacementResult.MaxTombs, panel Tribute area (balance, cost, shift timer, one Offer
  button per valuable), hover balance and statuses, countdown = time to the real return. Config: `CooldownDays`
  (replaces CooldownSeconds), `MaxHaugbuisPerPlayer`, `11 - Tribute`. Self-test suite `tribute`; runner parks the
  player's slots per suite and turns the tribute off for the older suites; FakeTime is now coherent world time (2 h day).
- 257 Core tests pass; Debug and Release build clean.
- Decisions taken alone (told Kelvin, PRD log): world-time night; TributeEnabled=false = old behaviour (every night,
  no work check); harvest/ground need a destination; Offer = one item per click; death resets the paid shift.
- Known gaps: slots are not synced to other peers (MP later); the work check while dormant runs every
  WorkCheckSeconds (10 s) and scans like a planning tick; sleep fast mode / away mode only have the StartShift hook.
- Next: Kelvin runs `haugbui_selftest` (all) on Windows → fix failures → "Desatracar" → commit, tag v0.0.6, push →
  SendMessage report to "Planner".
- Self-test run 11 (0.0.6, 2026-10-14): 313 pass, 0 fail, 1 skip (beam spot > 10 m), API self-check 157 members, log
  clean. Harvest and tribute suites all green; M5 delivery check now non-trivial and green. "Desatracar": committed as
  "v0.0.6: M6 ground items, harvesting, tribute, servant slots and death timing", tag v0.0.6, pushed to main.
  M6 DONE; report sent to Planner.
- Planner relayed a bug from Kelvin's run (2026-10-14): the creature never turns to face its flight direction. Asked to
  be part of the M6 commit, but the v0.0.6 tag must sit on the validated build, so it is a follow-up fix (needs a new
  self-test run and "Desatracar").
- 2026-10-14 Kelvin feedback: (a) creature never faces its flight direction → relayed to the M6 session as a bug fix with
  a self-test check. (b) Build-menu icons are generic (vanilla item icons) → planned ticket after M6 reports: custom
  icons drawn by Kelvin in Photoshop (PNG, square 128×128, transparent, one per piece: tomb, 5 upgrades, collection and
  delivery chests), loaded with Jötunn AssetUtils.LoadSpriteFromFile from the plugin folder. (c) Kelvin is studying
  custom 3D models → extend HB-T-099/new ticket: Blender → Unity 6000.0.x (game runs Unity 6000.0.75) prefab →
  AssetBundle loaded by the mod; looks only, host-prefab persistence unchanged; only own/licensed assets.
- M6 done by child session: v0.0.6 at a7e49db (tagged), self-test 313/313 (+1 usual beam skip), 157 API members,
  257 Core tests. Facing bug being fixed as a separate follow-up commit by the M6 session. Decisions awaiting Kelvin:
  PRD log row "M6 implementation choices" (world-time night, TributeEnabled=false = no work check, pickups need a
  destination chest, Offer = 1 item per click, death resets the paid shift).
- Facing fix (uncommitted, after v0.0.6): HaugbuiCreature.LateUpdate turns the yaw (360°/s, yaw only) toward the
  horizontal body velocity, or toward the goal while slower than 0.3 m/s, and keeps SetLookDir in step; HeadingError
  telemetry. HaugbuiSuite Movement adds a return leg and "faces its flight direction (under 20° after turning)".
  Committed and pushed as e1a3393 "Haugbui faces its flight direction" on Kelvin's "Desatracar" BEFORE an in-game run
  (version still 0.0.6, no tag). Pending: Kelvin runs `haugbui_selftest haugbui` to validate the heading check.
- 2026-10-14 Kelvin confirmed the M6 choices; ground items/harvest without destination are not picked up, optional config
  fallback into a Collection Chest (never tomb storage) = HB-T-108 (M7, P2). Icons HB-T-106 and custom 3D models
  HB-T-107 added to M10. Planner edits to PRD log + backlog are uncommitted; the M7 session includes them in its first
  commit. M6 facing fix still uncommitted in HaugbuiCreature.cs / SelfTest/HaugbuiSuite.cs (M6 session).
- Facing fix pushed by the M6 session: e1a3393 (on top of v0.0.6, no new tag). Not validated in game yet — Kelvin
  should run `haugbui_selftest haugbui`.

## M7 session (child of Planner, 2026-10-14, root folder, no worktree) — round 1 code complete, NOT yet run in game
- Plan agreed in rounds: round 1 = HB-T-058..065, 069, 070 (job model, smelter family, panel, outputs, reservations,
  manual use, level gate); round 2 = HB-T-066 fermenter, 067 fuel-only (adapters already exist for hot tub, frost kiln,
  shield generator; needs self-test), 068 cooking (dawn-aware), 108 Collection Chest fallback, 098 sleep fast mode,
  then version 0.0.7 + tag.
- Core (281 tests): `Production/ProductionRules.cs` — ProductionJob (wanted/supplied/produced/phase/edit), JobOwnership
  (claim/own/takeover, owner present blocks takeover, OwnerSeen refresh ≤ 1/min), StationFill (inputs half-queue rule,
  fuel to burn the queue within capacity, fuel-only top-up ≤ half), SupplyPlanner (carried first, nearest, minus delivery
  reservations, one cheated flag per trip, clean first). StationJobState blob v2 (+OwnerSeen). StepKind Feed/Fuel,
  TaskPlan.Tag.
- Game: `Integration/StationAdapters/` (IStationAdapter, SmelterAdapter, ShieldGeneratorAdapter, StationAdapterRegistry,
  StationJobs), `Brain/ProductionProviders.cs` (ProductionProvider outputs 80 / feed 75, ProductionStatus, ProductionGate),
  WorkContext stations + Reserved(), worker Supply (one unit per RPC, destination first) + output credit,
  `UI/ProductionPanel.cs` (+ Switch.Interact/GetHoverText patches), config `JobTakeoverMinutes`, translations, manifest.
  Self-test suite `production` (ProductionSuite.cs). Docs: ARCHITECTURE §9 rewritten, guide §4.4 row, PRD log rows
  (smelter cheated-flag rule, takeover rule).
- Decisions taken alone (tell Kelvin): smelter queue flag rule; fuel always clean; takeover rule; outputs of the job's
  item at the output point count as produced (capped at supplied × yield); a finite job also completes when all is
  supplied and the station holds nothing of it any more.
- Next: Kelvin runs `haugbui_selftest production` (or all) → fix → "Desatracar" (first commit includes the Planner's
  PRD/backlog edits) → round 2.
- 2026-10-14 M7 chip failed to start (worktree creation vs. separate git dir); Kelvin opened "Haugbui M7 production
  automation" manually from session-prompts/M7.md in ../haugbui-reference. Future milestone prompts go there too.
- 2026-10-14 PO decision relayed to the M7 session: HB-061 strict item-data rule becomes optional (StrictItemData,
  default ON). OFF = vanilla stacking semantics; never launders cheated flags; refund tracking kept. Ticket HB-T-109.
- Self-test run 12 (round 1, all suites): 362 pass, 2 fail, log clean. Both failures had one cause: fuel ran out exactly
  with the job, the manual TinOre stayed queued without coal, and "manual use" also blocked fuel → queue stuck, job never
  resumed, takeover could not feed (later delivery/toggle checks passed trivially). Fix: during manual use the Haugbui
  adds no inputs but keeps the queue fuelled. New checks: "manual input still gets fuel", "station empty after manual use".
- PO decision relayed by Planner (2026-10-14): HB-061 strict stacking becomes optional — config `General.StrictItemData`
  (default ON). PRD HB-061 paragraph + log row, backlog HB-T-109 (M7). Implemented: Core `Storage/StackingRules`
  (CanJoin/Join; ContainerSnapshot uses it), IItemEndpoint.CountKind/RemoveKind (vanilla mode measures the kind and rolls
  back identical first), ItemBridge.Joinable/AddTo taint/CountKind/RemoveKind, carried inventory always strict, smelter
  rule "never clean into a cheated queue" in both modes. Core: StackingRulesTests + 10k random transfers in both modes
  (OFF: conserved, cheated count never drops). Self-test: storage suite `VanillaStacking` block. 287 Core tests.
- 2026-10-14 Kelvin in-game feedback relayed to M7: mirrored countdown label (rotate 180°), interaction animation =
  Wraith attack sped up 1.5–2×, HP bar green (BaseAI.IsEnemy(player, creature) must be false).
- 2026-10-14 PO decision relayed to M7 (HB-T-110): same-owner tomb spacing 100 m (TombSpacingSameOwner; others keep
  5 m) + territories: each work target belongs to the nearest own tomb in radius; storage chests may still be used as a
  SOURCE by any own Haugbui in radius (reservations prevent conflicts). Config TerritoryMode default ON.
- 2026-10-14 (Kelvin: "apply everything now, I test once"): applied, NOT yet run in game:
  - Planner observations: (1) countdown label mirrored → Billboard.m_invert = true (+ check "reads the right way");
    (2) red HP bar → Character.m_tamed is private/non-serialized, the prefab's true never reached instances; now set on
    the instance + ZDO s_tamed in HaugbuiCreature.Awake (manifest Character.m_tamed); checks "spawned creature is tamed",
    "not an enemy of the player". Side effect told to Kelvin: monsters (undead too) now see it as a target (ARCH §10).
    (3) interaction gesture: CreatureVisuals.Pulse triggers the default weapon's attack animation (name read at runtime
    from Humanoid.m_defaultItems; "0" suffix for chained/random attacks), 1.75× while in the attack-tagged state; off with
    EffectsQuality=Low; check "interaction plays the attack animation".
  - HB-T-110: PlacementResult.OwnTombTooClose + haugbui_msg_tomb_too_close_own; config TombSpacingSameOwner (100) and
    TerritoryMode (on); Core Territory.OwnerOf; WorkContext.Mine filters destinations, Collection/Delivery Chests,
    stations (same-owner job taken over at once), harvest and ground items; plain chests stay shared sources. Runner sets
    SameOwnerSpacing 0 and suspends territories for the older suites; new suite "territory". 293 Core tests.

## RESUME HERE (M7 session, usage stop at ~90% on 2026-10-14)
- Kelvin's next step: run `haugbui_selftest` (all suites incl. production, territory) and drop report + LogOutput.log in
  the root. Then fix failures, "Desatracar" → first commit (include Planner PRD/backlog edits; not yet a version bump).
- Remaining M7 (round 2): HB-T-066 FermenterAdapter (RPC_AddItem(hash, cheated) when Empty, RPC_Tap when Ready, drops
  appear 1.5 s later via DelayedTap, check cover with Cover.GetCoverForPoint since RPC_AddItem skips it, yield 6);
  HB-T-067 self-test for hot tub / frost kiln / shield generator (adapters exist); HB-T-068 CookingStationAdapter
  (slots "slot"+i / "slotstatus"+i, burn at 2× cookTime, RPC_AddItem(name, cheated), RPC_RemoveDoneItem(userPoint, 1)
  removes the first done/burnt slot, fire check private IsFireLit, oven fuel; load only if done before the return lead or
  with the Altar; remove done items with top priority); HB-T-108 GroundFallbackToCollectionChest; HB-T-098 sleep fast
  mode (WorkBudget in Core, EnvMan.IsTimeSkipping, teleport steps, StartShift charge); then version 0.0.7 (Directory.Build.props,
  HaugbuiPlugin.PluginVersion, README roadmap), tag v0.0.7, SendMessage report to "Planner" (include StrictItemData,
  HB-T-110, the three visual fixes, decisions taken alone).
- Self-test run 13 (all suites, 2026-10-14): 386 pass, 1 fail, log clean. Kelvin confirmed the 3 visual fixes in game.
  Fail "manual input still gets fuel — fuel 2,0": test bug (the queue burns 2/1000 per second at 1000 s/bar, so 1.99 <
  m_fuelPerProduct); check now "> 1". Kelvin: the gesture is still too long → only the second half is played
  (CreatureVisuals.SkipWindUp: once the attack state or the transition into it is current, Animator.Play(state, 0, 0.5)),
  still 1.75×; new check "interaction animation starts halfway". Manifest: Character.m_animator. Kelvin lifted the usage
  stop for this task ("sem se preocupar com créditos").
- Self-test run 14 (haugbui + production): 105 pass, 1 fail. Gesture OK for v1 (Kelvin: "better, not perfect, enough
  for V1"); tamed/green bar/label checks green. Fail "bars stored in the copper chest — +7": run 13 ended with the job
  disabled while 4 bars were still owed, nobody collected them (disabled jobs were skipped), and this run at the same spot
  picked them up as outputs. Fix: a disabled job is still listed while supplied × yield > produced, so its owed products
  are collected (no feeding); suite removes loose Copper/Tin near the arena at start and end; new check "products of a
  disabled job are still collected". .gitignore: haugbui_selftest*/ and LogOutput.log (Drive made "haugbui_selftest (1)").
- Self-test run 15 (production): 52/52 PASS, log clean. "Desatracar": committed 18cecdb "M7 round 1: smelter-family
  production, StrictItemData option, tomb territories" (44 files incl. Planner PRD/backlog edits), pushed to main as
  Chelvis, switched back. No tag (still 0.0.6). Round-1 report sent to Planner. Next: M7 round 2 (see RESUME HERE above).
- M7 round 1 pushed by the M7 session: 18cecdb (no tag, still 0.0.6). Self-test 386/387 (test bug fixed) then
  production 52/52; 293 Core tests. Done: production jobs HB-T-058..065/069/070, StrictItemData (HB-T-109),
  same-owner spacing + territories (HB-T-110), mirrored label, green HP bar (m_tamed not serialized → now tamed;
  monsters target it), interaction gesture at 1.75×. Child decisions in PRD log: smelter queue flag rule, fuel clean
  only, job takeover after JobTakeoverMinutes=10, produced counted on pickup. Round 2: fermenter, fuel-only self-test,
  dawn-aware cooking, HB-T-108 fallback, sleep fast mode, then v0.0.7.

## M7 round 2 (2026-10-15, Kelvin: "apply everything, I test once") — code complete, NOT yet run in game
- HB-T-066 FermenterAdapter (cover rule reported; IgnoreCoverForTests DEBUG), HB-T-068 CookingStationAdapter (urgent
  Operate 105, dawn rule cook+30 s vs WorkContext.WorkSecondsLeft, panel warning), Operate step (StepKind.Operate,
  IStationAdapter.Operate/OutputRadius, StationState.ReadyActions/NextReady/BurntItem/Urgent), Shift+E/hover patches on
  Fermenter/CookingStation Interact/GetHoverText. HB-T-067: suite checks for hot tub, frost kiln, shield generator.
  HB-T-108: config Harvest.GroundFallbackToCollectionChest; GatherDestinations falls back to Collection Chests and
  remembers the kind (Worker.FallbackKinds) so DepositProvider may use them only for those kinds; ForcedForTests.
  HB-T-098: Core WorkBudget (+tests), TombController.Budget/SleepFastMode/SkippingForTests, Worker.FastForward,
  HaugbuiCreature.Teleport. Suites "stations" and "sleep". Version 0.0.7 (props, plugin, README roadmap M7 ✅).
  297 Core tests; Debug/Release clean. Docs: ARCH §9/§14.1, guide rows, PRD log row.
- Next: Kelvin runs `haugbui_selftest` (all) → fix → "Desatracar" → commit, tag v0.0.7 → M7 report to Planner.
- Self-test run 16 (all suites, on Valheim **1.0.20** — game updated; runtime API self-check PASS): 408 pass, 2 fail.
  (1) frost kiln never fuelled: its recipe has no input and output FrozenFuel, which Kelvin's character has not
  discovered → "recipe not discovered". Fix: fuel-only recipes (no input item) need no discovery (ProductionGate.Known).
  (2) stations suite crashed: ShieldGenerator sets m_nview in Start (a frame after placement) → adapter View falls back
  to GetComponent<ZNetView>(); suite waits 2 frames after placing. The vanilla ShieldGenerator.OnDestroy NRE was a
  consequence (destroyed before Start). Oven and fallback checks did not run yet. Sleep suite all green.
  Note: decompiled references in haugbui-reference are still 1.0.17; ask Kelvin for the 1.0.20 Managed folder when convenient.
- Self-test run 17 (stations, 1.0.20): 27 pass, 1 fail "near dawn: status says so" (status empty). Cause: with no raw
  item allowed and wood in reach, the provider took the "fuel but nothing to cook" early return, which set no status.
  Fix: one idle branch sets the status for every reason (manual use, too close to dawn, waiting for input/fuel,
  finishing, working). Fermenter, fuel-only stations (frost kiln fixed), oven and Collection Chest fallback all green.
- Self-test run 18 (stations + production): 80/80 PASS. "Desatracar": committed 7631d86 "v0.0.7: M7 ...", tag v0.0.7,
  pushed as Chelvis, switched back. M7 DONE; report delivered to Planner. Open: decompiled refs still 1.0.17 (game 1.0.20).


---

## Appendix: milestone brief template (the M7 brief given to the implementation session)

You are continuing the Haugbui Valheim mod (repo github.com/Chelvis/haugbui_valheim_mod, branch main; latest tag v0.0.6 at a7e49db, plus the creature facing fix e1a3393 on top, validated by Kelvin in game). Talk to the user (Kelvin) in pt-BR; code, comments and docs in en-US. Goal: implement milestone M7 of docs/IMPLEMENTATION_BACKLOG.json ("Production automation": HB-T-058..HB-T-070, HB-T-098 sleep fast mode and HB-T-108 optional Collection Chest fallback, in backlog order), finishing as version 0.0.7. A parent session named "Planner" coordinates milestones: send it a short report (SendMessage to "Planner") when M7 is done, and relay any product decision Kelvin makes.

UNCOMMITTED PLANNER EDITS: docs/PRODUCT_REQUIREMENTS.md (decisions log: M6 choices confirmed, HB-T-108 decision) and docs/IMPLEMENTATION_BACKLOG.json (new HB-T-106 and HB-T-107 in M10, HB-T-108 in M7) are modified in the working tree. Keep them and include them in your first commit.

PRODUCT RULES FOR M7 (PRD §13 and decisions log): v1 automates only simple production machines — Smelter family (smelter, blast furnace, charcoal kiln, windmill, spinning wheel, eitr refinery; fuel-only bathtub, frost kiln; shield generator fuel), Fermenter, CookingStation family (cooking station, iron cooking station, stone oven, frost foundry; dawn-aware loading so nothing burns, HB-121). NO recipe crafting at any CraftingStation (HB-122). Production unlocks at ProductionUnlockLevel (default 2). Finite/infinite jobs per station via the Shift+E panel. Station RPCs must carry the source item's cheated flag exactly (RPC_AddOre(name, cheated)): the Haugbui never makes items cheated nor clean (HB-061). Inputs come from storage minus Delivery reservations (HB-087). Sleep fast mode (HB-046/HB-T-098) charges tribute shifts like normal work (HB-167). HB-T-108: config GroundFallbackToCollectionChest (default OFF) — when ON, ground items/harvest with no storage destination go to the nearest Collection Chest with room, never to tomb storage. Prefab values (capacities, fuel per product, conversions incl. new 1.0 ones like GoldOre→Gold and Oat; frost kiln produces from fuel only; fermenter yields 6) are in TECHNICAL_FEASIBILITY §4.1 and the dump /Users/kelvinmarques/Projects/M/haugbui-reference/dumps/haugbui_prefabs_1.0.17.json.

WORK LOCATION: work directly in /Users/kelvinmarques/Projects/M/haugbui-valheim-mod (the root), never in a git worktree. Kelvin syncs that folder to his Windows PC with Google Drive and builds/tests from it. Do not edit the shared Environment.props (Windows paths). .git lives in /Users/kelvinmarques/Projects/M/haugbui-git (separate git dir). Game references (decompiled Valheim 1.0.17 + Jötunn 2.30.2, Managed DLLs, prefab dumps) live in /Users/kelvinmarques/Projects/M/haugbui-reference.

READ FIRST: /Users/kelvinmarques/Projects/M/haugbui-valheim-mod/haugbui-planning-progress.md (history incl. M3–M6 logs), docs/PRODUCT_REQUIREMENTS.md, docs/ARCHITECTURE.md (§9 production adapters, IStationAdapter), docs/TECHNICAL_FEASIBILITY.md (§4), docs/DEVELOPMENT_GUIDE.md (§8 how to add a production station), and this project's memory files. Never guess game APIs: verify every member in the decompiled sources (use /usr/bin/grep with ./*.cs) and add every private member/RPC you use to src/Haugbui.Core/GameApi/GameApiManifest.cs.

BUILD ON THE MAC: export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH" DOTNET_ROLL_FORWARD=Major; build with HAUGBUI_BUILD_ROOT=$HOME/.cache/haugbui-build dotnet build Haugbui.sln -p:MOD_DEPLOYPATH= (Debug and -c Release) and run dotnet test tests/Haugbui.Core.Tests.

TESTING: Kelvin runs haugbui_selftest himself on Windows and drops haugbui_selftest/ + LogOutput.log into the repo root (or a zip in ~/Downloads). Every ticket with in-game behaviour gets checks in a self-test suite (src/Haugbui/SelfTest, DEBUG only), with the fake clock where time matters; tests clean up and conserve item totals (count station queues and outputs). Avoid manual checklists. Log-level validation is optional for trivial fixes.

GIT: commits and pushes only after Kelvin writes "Desatracar" (a local hook blocks them otherwise; one keyword = one round). Push as his personal account and switch back: gh auth switch --user Chelvis, then push with git -c credential.helper= -c 'credential.helper=!gh auth git-credential' push; push the tag explicitly with the same -c flags (tags are lightweight, --follow-tags skips them); then gh auth switch back to the other account. Repo-local author is already set. End commit messages with: Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>. Push directly to main. The version tag goes on the commit Kelvin's self-test validated.

VERSIONING: completing M7 bumps the version to 0.0.7 (HaugbuiVersion in Directory.Build.props and HaugbuiPlugin.PluginVersion), tagged v0.0.7. Update the README roadmap and the progress file as you go.

USAGE: check plan usage (mcp__ccd_session_mgmt__get_usage) at checkpoints; stop at 95% of the 5-hour limit, never use extra USD usage, and write a resume section into the progress file before stopping. M7 is large (about 15 tickets): split it into rounds with Kelvin if needed.

Start by reading the files above, then propose a short M7 plan to Kelvin in pt-BR and implement it.
