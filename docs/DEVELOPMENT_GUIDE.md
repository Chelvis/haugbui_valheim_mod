# Haugbui — Development Guide

Platform: **Windows** (the mod targets Windows only for now). Game: Valheim 1.0.17.
Stack: BepInExPack_Valheim 5.4.2351 · Jötunn 2.30.2 · .NET Framework 4.8 · C#.

> The solution described here is created by ticket HB-T-001. Until then, this guide is the target state.

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
     is not installed (macOS). `assembly_valheim` and `assembly_utils` are publicized at build time by
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
4. Haugbui commands (HB-T-010): `haugbui_status`, `haugbui_dump_tomb`, `haugbui_reservations`, `haugbui_plan`
   (dry run), `haugbui_cleanup`, `haugbui_kill`, `haugbui_cooldown_skip`.
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

- **Manual run:** in the world, `devcommands`, then `haugbui_selftest` (all suites) or
  `haugbui_selftest basics structures flight transfers haugbui`. Stand still for about a minute.
- **Unattended run:** in r2modman, *Settings → Set launch parameters*:
  `-console -haugbui-autotest "<character>" "<world>" -haugbui-autotest-quit`, then *Start modded*. The mod picks the
  character and world from the main menu, waits for the spawn, runs every suite, writes the report and quits.
- **Output:** `BepInEx\haugbui_selftest\report.txt` (PASS/FAIL per check) and PNG pictures of each structure taken
  at noon from fixed angles (`tomb.png`, `Offering.png`, …, `tomb_without_mod.png`, `flyer.png`).

What the suites cover:
| Suite | Checks |
|---|---|
| basics | API self-check, recipe validation, every translation key resolves |
| structures | bed/spacing/upgrade rules and their results, ZDO saved as the vanilla host, tomb state, levels 1→6 in any order, abilities, combat warning, tomb storage, reload of every structure (look, refund, level, items), **mod-missing simulation** (tomb loads as a plain chest with items and keys, then comes back), removing an upgrade, rebinding to a rebuilt tomb |
| flight | non-persistent flyer crosses a wall back and forth for 25 s, never stuck, stays above ground, takes damage |
| transfers | 10 s of random destination-first moves between two chests with forced saves, totals unchanged, also after reload |
| haugbui | creature prefab (not saved, no drops, tamed, purple material copies, aura parts), dormant at noon / emerges at night / returns inside the dawn lead (fake clock, so no `tod` needed), movement (arrival ≤ 1 m, max speed, no jumps), one creature after a tomb reload, deposit on entering and overflow with full storage, Rest toggle, death (items to storage, cooldown, countdown label, waits for night, resurrects), Altar (works at noon, dark aura on/off, returns when removed), toggle gating and persistence, tomb destruction (carried + overflow dropped, creature gone) |

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
4. Hover the tomb: "Level n/6" goes up by one per upgrade; `haugbui_dump_tomb` lists the bound upgrades.
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

Never guess game APIs. On the Windows machine use ILSpy/dnSpy on `assembly_valheim.dll`. On macOS (planning machine),
the game's `Managed` folder is copied to `.reference/Managed/` and decompiled to `.reference/decompiled/` (git-ignored,
never committed or redistributed):

```bash
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH" DOTNET_ROLL_FORWARD=Major
ilspycmd -p -o .reference/decompiled/assembly_valheim -r .reference/Managed .reference/Managed/assembly_valheim.dll
```

After each game update: refresh `.reference/`, diff key files (Container, Smelter, Pickable, Beehive, Player, ZNetScene,
ZDOMan, EnvMan) and update `Haugbui.Core/GameApi/GameApiManifest.cs` + `TECHNICAL_FEASIBILITY.md`.

The `Managed` DLLs are enough for API work: prefab values (station capacities, recipes) live in the game's asset
bundles, not in code, and come from `haugbui_dump_prefabs` instead. Never copy the whole game folder into the
repository. To compile `Haugbui.dll` on macOS (no game run), also copy `BepInEx\core` and
`BepInEx\plugins\Jotunn` from the Windows install to `.reference/`; running and in-game testing stay on Windows.

## 7. Configuration

File: `BepInEx\config\<plugin guid>.cfg` (or F1 with ConfigurationManager). Defaults equal the PRD. Main sections:
`General`, `Progression` (CSV per level), `Timing`, `Death` (`CooldownSeconds`, world time), `Production`
(`ProductionUnlockLevel` = 2, allowed station kinds), `AwayMode`
(`AwayModeEnabled`, `RemoteTickSeconds`), `Construction` (`BedDistance`, `TombSpacing`, `UpgradeRange`), `Combat`,
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
7. Add a manual test block to §4.3 and, if it has new math (yield, timers), Core tests.
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
- Never commit `.reference/`, game DLLs, publicized assemblies or `Environment.props`.
- The plugin namespace for game adapters is `Haugbui.Integration`: a `Haugbui.Game` namespace would hide Valheim's
  global `Game` class.
