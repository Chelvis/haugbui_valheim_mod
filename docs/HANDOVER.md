# Haugbui — Handover (2026-10-16)

State of the project when development moved from Claude Code to Cursor. Read this, then `AGENTS.md`, the PRD
decisions log (§23) and `docs/history/PLANNING_LOG.md` (detailed session log, M1–M7).

## 1. Where we are

- **Version 0.0.7** (tag `v0.0.7`, commit `7631d86` on `main`). Milestones **M1–M7 done** and validated in game by
  Kelvin with the in-game self-test (last full runs: 408/410 then fixes, final stations + production 80/80).
- Valheim updated to **1.0.20** during M7. The runtime API self-check passes, but the decompiled references used for
  API work are still **1.0.17** — regenerate them before M8 (section 5).

| Milestone | Scope | Version |
|---|---|---|
| M1 | Foundation, config, localization, diagnostics, spikes | 0.0.1 |
| M2 | Tomb + five upgrades, host-prefab persistence, levels | 0.0.2 |
| M3 | Creature (non-persistent lilac Wraith clone), flight, state machine, day/night, death, tomb panel | 0.0.3 |
| M4 | World registry, access policy, destination-first transfers, storage sorting (existing-item rule) | 0.0.4 |
| M5 | Collection and Delivery chests, request editor; identical-data stacking | 0.0.5 |
| M6 | Ground items, harvesting, tribute (wages), servant slots, death/new-tomb timing | 0.0.6 |
| M7 | Production automation (smelter family, fermenter, cooking, fuel-only), sleep fast mode, `StrictItemData`, territories | 0.0.7 |

## 2. What is next

Remaining milestones (tickets in `docs/IMPLEMENTATION_BACKLOG.json`):

| Milestone | Tickets | Notes |
|---|---|---|
| **M8 Combat** (next, 0.0.8) | HB-T-071..074 | Longsword grants combat; toggle OFF by default with warning; flee when OFF. Since M7 the creature is really tamed, so monsters already target it. |
| M9 World safety | HB-T-075..079 | World marker + guard warning, opt-in backup, integrity checks, missing-mod end-to-end, save/reload torture. |
| M10 UI + localization | HB-T-080..083, 099, 106, 107 | Panel v2, hovers, full pt-BR/en-US pass, looks (HB-T-099 has the owner's kitbash ideas), custom icons drawn by Kelvin (HB-T-106), optional custom 3D models (HB-T-107). |
| M11 Optimization + release | HB-T-084..089 | Performance budget (HB-201), multi-Haugbui validation, config review, README, Thunderstore package, regression checklist. |
| M12 Away mode | HB-T-094..097 | Remote logistics on world data while the player is far away; spike HB-T-090 already passed. Charges tribute shifts. |

Approved but not yet ticketed: renaming a servant in the tomb panel (PRD HB-139).

## 3. Known gaps carried forward

- Tomb zone unloaded while the Haugbui is out: it despawns and resumes on the next load; the deposit happens then.
- No custom emerge/enter VFX, idle or alert sounds.
- Non-owner peers see stale state text (multiplayer is out of scope for v1).
- Deposits do not reserve destination room (re-read when putting); each planning tick snapshots all containers in
  radius — measure in M11 (HB-201).
- Servant slots are not synced to other peers (multiplayer later).
- Not covered by tests: a real sleep (only simulated) and a real roofed fermenter (cover rule skipped in the test).
- Structure looks are placeholders (HB-T-099). Self-test pictures need a clearing (trees block the camera).

## 4. Product owner preferences

- Kelvin decides product questions; propose a recommendation with trade-offs, then record his answer in PRD §23.
- He tests on Windows himself and prefers automated self-test coverage over manual steps; trivial fixes may skip
  log-level validation if he says so.
- Fun/balance matters ("something must be left for the player to do"): tribute, territories, 3 servants per player,
  100 m spacing between own tombs.
- He values the "honest assistant" stance: never laundering or spreading cheated flags (configurable).

## 5. Environment

### Machines and folders (as used until now)

- **Mac** (planning + builds, no Valheim): repo at `~/Projects/M/haugbui-valheim-mod`, synced to the Windows PC with
  Google Drive "My computer". To keep the sync light, `.git` lives in `~/Projects/M/haugbui-git` (separate git dir; the
  repo holds a one-line `.git` file) and game references in `~/Projects/M/haugbui-reference` (`Managed/`,
  `decompiled/`, `dumps/`). Build outside the tree on the Mac:
  `HAUGBUI_BUILD_ROOT=$HOME/.cache/haugbui-build dotnet build Haugbui.sln -p:MOD_DEPLOYPATH=`.
- **Windows** (game + tests): builds in-tree with its own `Environment.props` (Valheim path, r2modman profile
  `HaugbuiDev` as `MOD_DEPLOYPATH`). That file is per machine and git-ignored.
- If you start fresh on one machine (e.g. Cursor on Windows), a normal `git clone` works; set `Environment.props` from
  `Environment.props.example`.

### Game references (never commit)

- `VALHEIM_MANAGED` resolves to the game's `valheim_Data/Managed`, else `.reference/Managed`, else
  `../haugbui-reference/Managed` (`Directory.Build.props`). The GameApiManifest unit test uses the same DLLs.
- Decompile with ilspycmd (8.x works; on macOS set `DOTNET_ROLL_FORWARD=Major`):
  `ilspycmd -p -o <ref>/decompiled/assembly_valheim -r <ref>/Managed <ref>/Managed/assembly_valheim.dll`
  (also `assembly_utils`, `assembly_guiutils`, Jötunn from the NuGet cache). Search with a plain `grep` over `./*.cs`
  (one generated file is named `-PrivateImplementationDetails-.cs`).
- Prefab values (capacities, conversions, recipes) are asset data: run `haugbui_dump_prefabs` in game and keep the JSON
  next to the references.
- **To do before M8:** copy the 1.0.20 `Managed` folder and re-decompile; rerun `dotnet test` (manifest check).

### Accounts

- The repo belongs to Kelvin's personal GitHub account. Commit author:
  `Kelvin Marques <11822757+Chelvis@users.noreply.github.com>` (repo-local git config).

## 6. Workflow that worked: planner + implementation sessions

- One **planner** session owns product decisions, the PRD/backlog, milestone order and reviews. It does not implement
  features. For each milestone it writes a self-contained brief (goal, ticket ids, product rules, file locations,
  build/test/git/version rules) and starts an **implementation** session (Cursor: a new chat/agent or background
  agent) that works in the main checkout, never in a separate worktree when the folder is synced.
- The implementation session proposes a short plan, implements in rounds, extends the self-test, asks Kelvin to run
  it, fixes failures, and only after his approval commits, bumps the version and tags. It reports back to the planner:
  commit/tag, test numbers, decisions taken alone, gaps.
- Only one session edits docs at a time; relay decisions instead of editing files another session is changing.
- Milestone briefs used so far are in `docs/history/PLANNING_LOG.md`; the M7 brief is a good template.

## 7. Quick reference

- Build: `dotnet build Haugbui.sln -c Debug` (deploys to `MOD_DEPLOYPATH`), `-c Release` (no spikes/self-test).
- Unit tests: `dotnet test tests/Haugbui.Core.Tests` (about 300 tests).
- In game (Debug build, `devcommands`): `haugbui_selftest [basics structures flight transfers haugbui storage chests
  harvest tribute territory production stations sleep]`; diagnostics `haugbui_status`, `haugbui_dump_tomb`,
  `haugbui_dump_prefabs`, `haugbui_reservations`, `haugbui_ledger`, `haugbui_registry`.
- Config: `BepInEx/config/com.kelvinmarques.haugbui.cfg`.
