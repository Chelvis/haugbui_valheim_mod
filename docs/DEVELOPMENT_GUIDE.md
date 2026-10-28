# Haugbui — Development Guide

Platform: **Windows** (the mod targets Windows only for now). Game: Valheim 1.0.20.
Stack: BepInExPack_Valheim 5.4.2351 · Jötunn 2.30.2 · .NET Framework 4.8 · C#.

## 1. Prerequisites

| Tool | Notes |
|---|---|
| Valheim (Steam) | Default path `C:\Program Files (x86)\Steam\steamapps\common\Valheim` |
| BepInExPack_Valheim 5.4.2351 | Installed into the game folder or an r2modman profile (`winhttp.dll`, `doorstop_config.ini`, `BepInEx\`) |
| Jötunn 2.30.2 | `BepInEx\plugins\Jotunn\` (Thunderstore `ValheimModding-Jotunn`) |
| Visual Studio 2022 (".NET desktop development" + ".NET Framework 4.8 targeting pack" + "Visual Studio Tools for Unity") **or** JetBrains Rider | |
| .NET SDK 8+ | For `dotnet build` / `dotnet test` from the command line |
| Optional: shudnal-ConfigurationManager | In-game F1 config editor |
| Optional: r2modman | Separate dev profile recommended |

## 2. Build

1. Clone the repository.
2. Copy `Environment.props.example` to `Environment.props` (git-ignored) and set `VALHEIM_INSTALL` and
   `MOD_DEPLOYPATH` (leave it empty to skip deployment).
3. Build:

   ```powershell
   dotnet build Haugbui.sln -c Debug
   ```

   How references resolve (`Directory.Build.props`, `src/Haugbui/Haugbui.csproj`):
   - Game DLLs from `VALHEIM_MANAGED` = `<VALHEIM_INSTALL>\valheim_Data\Managed`, or `.reference/Managed` when the game
     is not installed (macOS), or `../haugbui-reference/Managed` when the reference folder is kept next to the
     repository instead of inside it (synced source folder). `assembly_valheim` and `assembly_utils` are publicized at build time by
     `BepInEx.AssemblyPublicizer.MSBuild` (output in `obj/`, nothing written to the game folder). A game update only
     needs a rebuild.
   - BepInEx (`BepInEx.Core` 5.4.21 from nuget.bepinex.dev) and Jötunn (`JotunnLib` 2.30.2 from nuget.org) are
     compile-only references; the game provides them at runtime. `nuget.config` maps the packages to the feeds.
   - Debug copies `Haugbui.dll`, `Haugbui.Core.dll`, `.pdb` files and `Translations\` to `MOD_DEPLOYPATH`.
   - Debug builds also contain the spike commands (`haugbui_spike_*`); Release builds do not.
   - When the source folder is synced between machines (e.g. Google Drive "My computer"), set the environment
     variable `HAUGBUI_BUILD_ROOT` on all but one of them (e.g. `export HAUGBUI_BUILD_ROOT=~/.cache/haugbui-build`
     on the Mac): that machine's `bin/` and `obj/` then live outside the tree, so the two never overwrite each other.
     Pass `-p:MOD_DEPLOYPATH=` there if the shared `Environment.props` points at the other machine's plugins folder.
     Keep `.git` out of the synced folder too: `git init --separate-git-dir <path outside>` leaves a one-line `.git`
     file pointing to it.

## 3. Install locally (without building)

Copy the `Haugbui` folder (DLLs + `Translations`) to `<BepInEx>\plugins\Haugbui\`. Launch through Steam (vanilla folder) or
r2modman ("Start modded").

## 4. Test

### 4.1 Automated

```powershell
dotnet test tests/Haugbui.Core.Tests
```

`Haugbui.Core` has no Unity/Valheim references, so these tests also run on macOS/Linux. Covered domain logic:
progression table, item classification, storage selector, delivery ledger, production job math, reservation leases,
cooldown math, state-machine transitions, localization key parity.

### 4.2 In-game test setup

1. Launch with `-console` (Steam launch options) and press F5. Run `devcommands`.
2. Use a dedicated test world; **copy the world folder** (`%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\worlds_local\<World>\`,
   1.0 format: a folder of `_main.N.*` and `*.chunk` files) before destructive tests.
3. Useful commands: `tod 0.5` (noon) / `tod 0` (midnight), `skiptime <seconds>`, `env ThunderStorm`, `resetenv`,
   `spawn Greydwarf 3`, `god`, `nocost`, `killall`, `save`, `wind 0 1`. Some cheats can block achievements in 1.0.
4. Haugbui commands (HB-T-010): `haugbui_status`, `haugbui_dump_tomb`, `haugbui_reservations`, `haugbui_ledger`,
   `haugbui_registry`, `haugbui_radius` (show or hide the operational radius; the workbench circle, tinted purple);
   planned: `haugbui_plan` (dry run), `haugbui_cleanup`, `haugbui_kill`, `haugbui_cooldown_skip`.
5. `haugbui_dump_prefabs` (HB-T-091) writes `BepInEx\haugbui_prefabs.json` with station conversions, capacities,
   crafting recipes and item names from the installed game. Run it after each game update and attach the file when
   planning changes on the Mac.

### 4.3 Manual validation suites (spec §22)

Each item: setup → action → expected. Record pass/fail per release in `docs/test-runs/<version>.md`.

**Construction & progression**
- [ ] No bed → tomb ghost invalid, message shown. Own bed at 49 m valid, 51 m invalid; another profile's bed invalid.
- [ ] Second tomb within 5 m blocked.
- [ ] Upgrades placeable in any order within range; duplicates blocked; levels 1→6 shown on hover.
- [ ] Longsword as the only upgrade → level 2 with combat available; Altar only → level 2, works at noon.
- [ ] Destroy any upgrade → level −1 and its ability removed; rebuild → restored. Reload → same level.
- [ ] Attributes per level match the table (panel + speed log).

**Day and night**
- [ ] Without Altar: emerges at night (`tod 0.9`), returns before dawn, stays inside at noon.
- [ ] At dawn its carried items are in tomb storage and can be taken while it sleeps.
- [ ] With Altar: works at noon.
- [ ] `env ThunderStorm`, `env Misty`, an always-dark environment: behaviour unchanged.

**Inventory & storage**
- [ ] Equipment (sword, armour, cape, tool, trinket, arrows) never picked up/moved.
- [ ] Weight and slot limits respected at L1 (2 slots, 50 weight).
- [ ] Empty chest never receives items; wood→wood chest only; stone not into wood chest.
- [ ] Full matching chest → second matching chest used; none → item kept, retried.

**Collection & delivery**
- [ ] Collection Chest mixed content sorted; equipment left.
- [ ] Delivery request 100 Wood / 200 Stone / 50 Resin / 30 Iron with shortages → partial, lines stay open; edit and
  cancel; two Delivery Chests independent.

**Ground items & harvesting**
- [ ] Ground toggle OFF → nothing collected; ON → collected except fresh drops near the player.
- [ ] Mature crops harvested, immature ignored, nothing replanted.
- [ ] Full/empty beehive; full/empty bird nest. Harvest toggle OFF stops all.

**Production** (each supported station)
- [ ] Finite 20 → exactly 20 products then stop; infinite runs until disabled.
- [ ] Missing input / missing fuel → waiting state; resumes when supplied.
- [ ] Undiscovered material not selectable.
- [ ] Save/quit/reload mid-job → progress preserved.
- [ ] Manual foreign input pauses the job; manual use otherwise unaffected.
- [ ] Items requested by a Delivery Chest are not consumed.

**Combat**
- [ ] Warning on Longsword; combat OFF by default; enabling requires confirmation.
- [ ] Hostiles attacked; deer, tamed wolf, players, other Haugbuis ignored.
- [ ] Multiple enemies; collateral damage possible; combat OFF → flees when attacked.
- [ ] Death → items in tomb storage/overflow, countdown shown.

**Persistence**
- [ ] Save/reload in every state; exit during production; exit during cooldown (remaining time preserved).
- [ ] Multiple Haugbuis after reload: one creature per tomb, jobs keep owners.
- [ ] Missing-mod round trip (HB-T-078 checklist): remove plugin → load → walk to base → save → quit → reinstall → load.

**Performance**
- [ ] Stress world (3 tombs, 100 containers, 20 stations, 300 ground items): `haugbui_status` timing ≤ budget.
- [ ] 2-hour session: no growing memory or log spam.

### 4.4 In-game self-test (Debug build) — preferred

Most in-game checks are automated. Use a throwaway test world: the suites build, reload and destroy objects about
15 m around the player and remove everything they created.

- **Run:** in the world, `devcommands`, then `haugbui_selftest` (all suites) or
  `haugbui_selftest basics structures flight transfers haugbui storage chests harvest tribute production territory stations sleep combat world backup integrity torture`. Stand still. The backup suite copies the world folder and then deletes that copy; the full run takes longer than a minute.
  Add `and_exit` to close the game when the report is written (`haugbui_selftest and_exit`,
  `haugbui_selftest production combat and_exit`).
  Kelvin runs it himself and sends back the output folder and `LogOutput.log`.
- The code also contains an unattended launch mode (`SelfTest/AutoTest.cs`); it is not part of the workflow and is
  not proposed.
- **Output:** `BepInEx\haugbui_selftest\report.txt` (PASS/FAIL per check) and PNG pictures of each structure taken
  at noon from fixed angles (`tomb.png`, `Offering.png`, …, `tomb_without_mod.png`, `flyer.png`).

What the suites cover:
| Suite | Checks |
|---|---|
| basics | API self-check, recipe validation, every translation key resolves |
| structures | bed/spacing/upgrade rules and their results, ZDO saved as the vanilla host, tomb state, levels 1→6 in any order, abilities, combat warning, tomb storage, reload of every structure (look, refund, level, items), **mod-missing simulation** (tomb loads as a plain chest with items and keys, then comes back), removing an upgrade, rebinding to a rebuilt tomb, hover fields (name, level, state, HP, cooldown; upgrade bound and level), each piece's build icon reported as custom or fallback |
| flight | non-persistent flyer crosses a wall back and forth for 25 s, never stuck, stays above ground, takes damage |
| transfers | 10 s of random destination-first moves between two chests with forced saves, totals unchanged, also after reload |
| storage | registry against a scene scan and a brute-force radius query; access rules (own/foreign private chest, dungeon chest, tomb storage, player tombstone, cart, obliterator, auto-destroy crate, foreign ward with and without permission); cheated flag kept, a cheated stack moved next to a clean one stays separate (clean stack stays clean, custom data kept, clean wood never taken from the cheated stack), container in use untouched, 3 s of random transfers with saves and a reload conserve totals; `StrictItemData` ON by default, and OFF: a cheated item joins and taints a clean stack, the stack keeps its custom data, a clean item never clears a cheated stack, 2 s of random clean/cheated transfers conserve totals and never lower the cheated count; sorting: tomb storage sorted first, wood only into the wood chest, empty chest untouched, item without destination kept and retried once a matching chest appears, chest removed mid-task (task stops, items kept), items back in tomb storage at dawn, totals conserved; carried grid during a transfer; chest filter (empty list keeps the existing-item rule, Wood on an empty chest receives Wood and refuses Stone, clear-all restores the rule, the list survives a reload); two tombs sorting at once (own storage only, no shared reservation), leases released on death mid-task; loaded-area coverage |
| chests | Collection/Delivery Chests: 10 slots, saved as the wood chest host, name, cost above a wood chest, own look, ledger only on the Delivery Chest, role and Shift+E hint on the hover; access roles; request editor (Shift+E opens it, no equipment listed, spec example 100 Wood/200 Stone/50 Resin/30 Iron, edit, invalid amount), requests and delivered counts survive reload, cancel; closing the panel leaves the player able to move; collection (wood/stone sorted, item without destination and equipment stay, cheated wood kept apart with its custom data); delivery (exact amount, never above requested, short stock keeps the line open, two chests independent, Collection Chest as a source, taking items out keeps delivered, full chest gets nothing); totals conserved |
| harvest | Ground items (toggle off → nothing taken; old drops collected in one trip with every drop reserved; drop under 10 s waits; equipment, a drop near the player and an item without destination stay; cheated drop stays cheated), crops (toggle off untouched; ripe carrots harvested with the game's own pick, drops sorted, none left on the ground; sapling and wild berries untouched), hives and nests (full hive → honey, nest → feathers, empty hive ignored, never destroyed, toggle off stops them) |
| tribute | Runs with the tribute on (other suites turn it off). New tomb starts at the next dusk; night without work: dormant, no charge; work without tribute: "no tribute" on hover; each offer takes one item and adds its value; charged once and emerges; cheated value tracked and spent first; shift timer; panel buttons; Rest/reload in the same shift never charge again; new night without work goes home; work mid-shift charges once; death: no refund, back only at the dusk after the cooldown, charged; Altar: back at cooldown end; TributeEnabled=false; slots named in order, 4th tomb refused, destroyed tomb refunds Coins (cheated part cheated), slot keeps name and death, rebuild inherits the cooldown, Forfeit drops nothing |
| territory | Own tomb 20 m away refused with `TombSpacingSameOwner` (100 m); with the spacing lowered for the test, two overlapping tombs: deposits only into chests of the own territory, storage chests shared as sources, a Collection Chest belongs to the nearest tomb, positions split the same way, TerritoryMode off shares again. The runner turns both rules off (in memory) for the older suites |
| stations | Fermenter (recipes and yield 6 read from the prefab, exposed fermenter reported and not loaded, then with the cover rule skipped and 1 s fermentation: one base fed, tapped, 6 meads produced and stored); hot tub, frost kiln, shield generator kept above half fuel, never above max, nothing added once disabled; oven (cook time 4 s for the test): near dawn nothing loaded and the status says why, at night 3 loaded, taken off and stored, nothing burnt, fuel within capacity; `GroundFallbackToCollectionChest` off: a drop without destination stays, on: it goes to the Collection Chest, never tomb storage, and stays there |
| sleep | Sleep fast mode on the fake clock: a skipped afternoon banks nothing without the Altar; 2 s of world time do not finish a delivery trip; pushing world time forward completes it without flight, item totals conserved; the bank empties when the skip ends |
| combat | Two tombs on the fake clock, player in god mode, spawned monsters without loot (the runner's monster cleanup spares them). Toggle: OFF and unacknowledged by default, refused without the Longsword, binding the Longsword does not turn it on, first enable opens the yes/no confirmation, cancel keeps OFF and no ack, confirm stores `combat_ack` and turns it ON, later enables ask nothing. Targets: greydwarf attacking the player, skeleton attacking a Haugbui, greydwarf attacking a building, angry wild boar: valid; calm greydwarf, fleeing greydwarf, deer, calm boar, tamed wolf, player, the other Haugbui, a greydwarf outside the radius and a calm boss: ignored; during a raid the calm greydwarf, deer, boar and boss become valid (tamed/player/Haugbui/outside still ignored). A real `army_eikthyr` at the tomb: fights a calm deer or boar while it lasts, then only remaining attackers. Level 1: factors ×1/÷1 and a hit of 10 taken in full. Level 2: fights the nearest hostile, alert effect, kills a greydwarf, hit damage ×1.2, then the next hostile, back to Idle; two Haugbuis on one Greydwarf Elite. Interrupt: a greydwarf during a delivery trip suspends it (plan aborted after the step, carried wood kept), the delivery completes after the fight, wood totals conserved. Combat OFF (tribute on, shift paid): a pinned greyling targeting it causes no flight; a hit makes it fly home and circle outside the tomb while the attacker remains, no new charge, never attacks; once the attacker is gone it resumes the delivery. Longsword removed mid-fight stops combat at once (no further swing); rebuilt, combat is back. Sleep fast mode: no combat or flight while skipping, combat resumes after. Level 6: factors ×2/÷2, a kill with hits ×2.0, a hit of 10 halved and level 1 / level 6 health lost = 2 |
| world | No warning when the marker is hidden for the check. One placed tomb is recorded. Hiding that tomb's ZDO from the scan warns. Putting it back clears the warning |
| backup | With the option forced off, no backup folder. With it forced on, one timestamped folder appears next to the world and contains files. The suite deletes that folder and restores the option |
| integrity | A tomb whose blob is replaced with garbage keeps those bytes, does not throw, and does not run |
| torture | A wood transfer, a one-bar smelter job, a delivery of 5 wood and a death, each with a forced save and an in-process reload. Totals stay put. The transfer ledger reports no duplicate. Cooldown and job progress are still there after the reload |
| production | Station adapters (smelter capacities, eitr refinery/charcoal kiln/blast furnace/windmill recipes and fuel, no adapter for workbench, forge, cauldron, mead ketill, prep table or obliterator); Shift+E opens the panel on the smelter, only discovered recipes, finite job needs an amount, hover shows the automation status, closing the panel leaves the player able to move; level 1: panel explains the unlock level and nothing is produced, level 2 after the Offering; finite job of 3 copper (smelter set to 1 s per bar): 3 supplied/3 produced, queue and fuel within capacity, output trip reserves its drops, bars stored, clean ore first and cheated ore untouched, clean bars, nothing left at the output point, hover progress, no more ore once complete; job survives a reload and a raised target resumes; manual tin in the queue pauses the job (never removed) and it resumes when the queue is empty; ore requested by a Delivery Chest is delivered, not smelted; a job owned by another tomb is ignored until `JobTakeoverMinutes`; Production toggle off stops all tasks; unroofed spinning wheel reports "needs roof" |
| haugbui | spawned creature tamed and not an enemy of the player (green health bar), interaction plays the attack animation, countdown label not mirrored; creature prefab (not saved, no drops, tamed, purple material copies, aura parts), dormant at noon / emerges at night / returns inside the dawn lead (fake clock, so no `tod` needed), movement (arrival ≤ 1 m, max speed, no jumps), one creature after a tomb reload, deposit on entering and overflow with full storage (retrieve sends overflow to the player first, then tomb storage, leftovers stay, totals conserved), Rest toggle, death (items to storage, cooldown, countdown label, waits for night, resurrects), Altar (works at noon, dark aura on/off, returns when removed), toggle gating and persistence, a renamed servant keeps the name across reload and a rebuilt tomb, empty input restores the default, tomb destruction (carried + overflow dropped, creature gone) |

Not covered by the self-test (still manual, once per release): a real quit/reload from disk and loading the world
with the DLL removed (§4.5 step 7), and judging how the structures look (use the pictures).

### 4.4.1 M1 spikes (manual, superseded by the self-test) (Debug build, `devcommands`, on a copy of a test world)

Each spike prints PASS/FAIL or numbers; record them in `TECHNICAL_FEASIBILITY.md` as the ticket asks.

**First run (HB-T-003/006/012):** start the game, check `LogOutput.log` for `Haugbui 0.0.1 loaded`,
`Game API self-check passed (N members)` and `Recipe validation: 8/8 recipes valid`; run `haugbui_status`.
Optional: set the environment variable `HAUGBUI_FAKE_MISSING_FEATURE=Harvest` before launching a Debug build — the log
must show only the `Harvest` feature disabled.

**Prefab dump (HB-T-091):** in a world, run `haugbui_dump_prefabs`; send `BepInEx/haugbui_prefabs.json`.

**HB-T-009 transfer vs save:** build 2–4 chests within 10 m, put mixed stacks in them, stand next to them.
1. `haugbui_spike_transfer start 120 5` — moves items for 120 s (5 moves/frame) and saves every 5 s.
2. When it ends, the log must say `totals OK`. Also try killing the game process mid-run (Task Manager).
3. Reload the world, stand next to the chests, run `haugbui_spike_transfer verify` → `PASS`.

**HB-T-090 away mode:** next to a chest with a free slot and a smelter (or kiln) with room in its queue:
1. `haugbui_spike_remote mark`.
2. Travel more than 300 m (or `goto x,z` far away) and run `haugbui_spike_remote apply`, then `save`.
3. Optional: quit and reload the world while far away.
4. Go back, run `haugbui_spike_remote verify` → `PASS chest`, and watch the smelter process the remote ore.
5. `haugbui_spike_remote scan` anywhere: report both scan timings.

**HB-T-007 host prefab:** next to a wooden chest with some items:
1. `haugbui_spike_host tag` → the chest looks like a tombstone (or a purple box). `save`, quit.
2. Remove `Haugbui.dll` from `plugins\Haugbui\`, load the world: the chest is a normal chest with its items. `save`, quit.
3. Put the DLL back, load: the tombstone look returns; `haugbui_spike_host info` shows the same `guid`.

**HB-T-008 incorporeal flight:** next to a building with walls, a door and chests:
1. `haugbui_spike_fly spawn 4` → a passive Wraith appears in front of you (4 m/s).
2. `haugbui_spike_fly follow` and walk in and out of the building, through doors and over terrain bumps: it must pass
   through walls, chests and doors and never fall below the ground.
3. For a repeatable path: `haugbui_spike_fly point` at 5–10 places (inside/outside, behind walls), then
   `haugbui_spike_fly go` and watch it loop for ~10 minutes.
4. Hit it a few times (it must take damage), then `haugbui_spike_fly report`: send the line (stuck events must be 0).
5. `haugbui_spike_fly despawn`. Quit and reload: no Wraith may remain (it is never saved).

### 4.4.2 M3 Haugbui manual checks (once per release, after the self-test passes)

Only what the self-test cannot judge: `Shift+E` on the tomb opens the panel (Esc/E closes it, toggles survive a real
quit/reload); the creature looks purple and the smoke aura shows only with the Altar; the countdown label above a
tomb after a death keeps its remaining time across quit/restart. Send screenshots of the creature by night and
with the Altar.

### 4.5 M2 structures (Debug or Release build)

1. Hammer → **Haugbui** tab: Tomb and the five upgrades are listed with icons and the recipes of PRD §4.2
   (`nocost` helps).
2. Tomb rules: without a bed you own within 50 m the ghost is red and clicking shows "A tomb must be built near a bed
   you own"; with your bed it places. A second tomb closer than 5 m is refused.
3. Upgrades: refused with no tomb within 8 m; a second upgrade of the same kind is refused; any order works. The
   Longsword shows the combat warning popup.
4. Hover the tomb: "Level n" goes up by one per upgrade; `haugbui_dump_tomb` lists the bound upgrades.
5. Destroy an upgrade with the hammer: level drops by one and the refund is the Haugbui recipe.
6. `E` on the tomb opens "Tomb storage" (10 slots).
7. Save, quit, reload: same look, level and storage. Remove `Haugbui.dll`, load: a wooden chest and tables (or
   benches) with the same items; save, re-add the DLL, load: everything is back.
8. Destroy the tomb and build a new one within range: the upgrades rebind to it.
9. Send screenshots of each structure: the looks are first-pass kitbashes and their offsets need tuning
   (`Structures/StructureDefinitions.cs`). Also run `haugbui_dump_prefabs` again and send the file: it now lists
   every buildable piece, used to pick better hosts for the upgrades.

## 5. Logs and debugging

| Log | Location |
|---|---|
| BepInEx | `<BepInEx root>\LogOutput.log` (game folder or r2modman profile) |
| Unity player | `%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\Player.log` |
| Live console | `BepInEx\config\BepInEx.cfg` → `[Logging.Console] Enabled = true` |

- Enable `General.DebugLogging` in the Haugbui config for task, reservation and transfer-ledger logs.
- Breakpoint debugging: follow the Valheim-Modding wiki "Debugging Plugins via IDE" (swap Unity development player
  files matching the game's Unity 6 build, `player-connection-debug=1` in `boot.config`, attach VS/Rider). Restore
  original files afterwards (Steam "Verify integrity").

### Common problems

| Symptom | Check |
|---|---|
| Plugin not loading | `LogOutput.log` for missing Jötunn; BepInExPack version; DLL in `plugins\Haugbui` |
| "Game API self-check failed" | Game updated: regenerate publicized assemblies, run `dotnet test` with the new `Managed` DLLs in `.reference/` (the manifest test checks every member) |
| Pieces missing from hammer | Startup validation report (HB-T-012) — a recipe item/station name is invalid |
| Haugbui never emerges | `haugbui_status`: level, cooldown, rest toggle, day/night state, tomb owner |
| Items not sorted | No matching chest (rule §8.1), chest in a ward, chest in use, unloaded zone |
| Text shows `[haugbui_x]` | Key missing in the active language file; run key parity test |

## 6. API reference workflow (decompiled game)

Never guess game APIs. The game's `Managed` folder is copied to `.reference/Managed/` and decompiled to
`.reference/decompiled/` (git-ignored, never committed or redistributed). The GameApiManifest unit test checks every
member against `.reference/Managed`, else `../haugbui-reference/Managed`, else the game install the build resolved
(`VALHEIM_MANAGED`, or the `VALHEIM_MANAGED` / `VALHEIM_INSTALL` environment variables); it is skipped only when none
of them exists. On Windows
(ilspycmd 9.1; the latest ilspycmd does not install on the .NET 8 SDK):

```powershell
dotnet tool install -g ilspycmd --version 9.1.0.7988
Copy-Item "<VALHEIM_INSTALL>\valheim_Data\Managed\*.dll" .reference\Managed\
ilspycmd -p -o .reference\decompiled\assembly_valheim -r .reference\Managed .reference\Managed\assembly_valheim.dll
```

Repeat for `assembly_utils`, `assembly_guiutils` and Jötunn (`%USERPROFILE%\.nuget\packages\jotunnlib\2.30.2\lib\net462\Jotunn.dll`).
On macOS set `DOTNET_ROLL_FORWARD=Major` first.

After each game update: refresh `.reference/`, diff key files (Container, Smelter, Pickable, Beehive, Player, ZNetScene,
ZDOMan, EnvMan) and update `Haugbui.Core/GameApi/GameApiManifest.cs` + `TECHNICAL_FEASIBILITY.md`.

The `Managed` DLLs are enough for API work: prefab values (station capacities, recipes) live in the game's asset
bundles, not in code, and come from `haugbui_dump_prefabs` instead. Never copy the whole game folder into the
repository. To compile `Haugbui.dll` on macOS (no game run), also copy `BepInEx\core` and
`BepInEx\plugins\Jotunn` from the Windows install to `.reference/`; running and in-game testing stay on Windows.

## 7. Configuration

File: `BepInEx\config\<plugin guid>.cfg` (or F1 with ConfigurationManager). Defaults equal the PRD. Main sections:
`General`, `Progression` (CSV per level), `Timing`, `Death` (`CooldownDays`, world time), `Production`
(`ProductionUnlockLevel` = 2, allowed station kinds), `AwayMode`
(`Enabled`, `TickSeconds`), `Construction` (`BedDistance`, `TombSpacing`, `UpgradeRange`), `Combat` (`DamageMultiplier`,
the base multiplier of its hits; the per-level `CombatMultiplier` is a `Progression` row),
`Advanced` (tick intervals, reservation lease, debug).

## 8. How to add a production station

1. Inspect the station class in the decompile: inputs (RPC + args), fuel, capacity fields, output emission.
2. Create `src/Haugbui/Integration/StationAdapters/<Name>Adapter.cs` implementing `IStationAdapter`
   (`CanHandle`, `Kind`, `GetRecipes`, `Read`, `InputCapacity`, `FuelNeeded`, `Feed`, `AddFuel`, `OutputPoint`,
   `YieldPerInput`, `IsBlocked`).
3. Register it in `StationAdapterRegistry` (order matters: most specific first).
4. Add required members/RPC names to `Haugbui.Core/GameApi/GameApiManifest.cs` self-check.
5. Add the entry point (Interactable or Switch patch) if it differs from existing ones.
6. Add localization keys (`haugbui_station_<name>_*`) in both language files.
7. Add checks to the `production` self-test suite (`SelfTest/ProductionSuite.cs`) and, if it has new math (yield,
   timers), Core tests.
No change to the planner, job model or UI should be necessary.

## 9. How to add localization entries

1. Add the key (no `$`, lowercase, `haugbui_` prefix) to **both** `src/Haugbui/Translations/English/haugbui.json`
   and `src/Haugbui/Translations/Portuguese_Brazilian/haugbui.json`. Jötunn loads every
   `plugins/**/Translations/<Language>/*.json` automatically (verified in Jötunn 2.30.2
   `AutomaticLocalizationsLoading`).
2. Reference it in code as `"$haugbui_key"` (use `L.T("haugbui_key")` helper) — never hard-code player-facing text.
3. Avoid `'`, `"` and `;` right after a token (they are not token terminators in Valheim's localizer).
4. Run `dotnet test` (key parity).
5. Additional languages: add a folder named with the Valheim language name (e.g. `Spanish`); missing keys fall back
   to English.

## 10. How to extend the upgrade system

1. Add a new `UpgradeKind` in the Core progression definition and, if it grants an ability, map it in the ability table.
2. Add a piece definition (host prefab, visual kitbash, recipe, crafting station) to the structure registry.
3. Extend `ProgressionTable` defaults/config if the level count changes (all per-level arrays must match).
4. Add localization (name, description, hover) and placement messages.
5. Update PRD §4, the level/ability tests, and the backlog.

## 11. Repository conventions

- Comments in English, only where the code cannot express the reason.
- Game access only through `Integration/` adapters; Harmony patches stay thin and delegate.
- Every behaviour change updates the relevant document in `docs/`.
- Each completed milestone bumps the version (M1 = 0.0.1, M2 = 0.0.2, M3 = 0.0.3, …) in `Directory.Build.props` and
  `HaugbuiPlugin.PluginVersion`, and is tagged `vX.Y.Z` on `main`.
- Every in-game feature gets checks in a `haugbui_selftest` suite (`src/Haugbui/SelfTest`).
- The runner parks the player's servant slots (global keys) before each suite and restores them afterwards, and turns
  the tribute off for every suite except `tribute`; the fake clock (`FakeTime`) is coherent world time with a 2-hour day.
- Never commit `.reference/`, game DLLs, publicized assemblies or `Environment.props`.
- The plugin namespace for game adapters is `Haugbui.Integration`: a `Haugbui.Game` namespace would hide Valheim's
  global `Game` class.
