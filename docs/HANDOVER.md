# Haugbui — Handover (2026-10-19)

State of the project when development moved from Claude Code to Cursor (updated 2026-10-19, M8 done). Read
this, then `AGENTS.md`, the PRD
decisions log (§23) and `docs/history/PLANNING_LOG.md` (detailed session log, M1–M7).

## 1. Where we are

- **Version 0.0.8** (tag `v0.0.8` on `main`). Milestones **M1–M8 done** and validated in game by Kelvin with the
  in-game self-test (M8 full run 2026-10-19: 494 passed, 2 storage failures from world wood drops; storage re-run
  after the suite stopped picking ground items: 55/55). Combat: 72/72 before that full run.
- Valheim updated to **1.0.20** during M7. The runtime API self-check passes, and since 2026-10-16 the decompiled
  references in `.reference/` are 1.0.20 too (section 5); the GameApiManifest unit test passes against them.

| Milestone | Scope | Version |
|---|---|---|
| M1 | Foundation, config, localization, diagnostics, spikes | 0.0.1 |
| M2 | Tomb + five upgrades, host-prefab persistence, levels | 0.0.2 |
| M3 | Creature (non-persistent lilac Wraith clone), flight, state machine, day/night, death, tomb panel | 0.0.3 |
| M4 | World registry, access policy, destination-first transfers, storage sorting (existing-item rule) | 0.0.4 |
| M5 | Collection and Delivery chests, request editor; identical-data stacking | 0.0.5 |
| M6 | Ground items, harvesting, tribute (wages), servant slots, death/new-tomb timing | 0.0.6 |
| M7 | Production automation (smelter family, fermenter, cooking, fuel-only), sleep fast mode, `StrictItemData`, territories | 0.0.7 |
| M8 | Combat (Longsword, nearest aggressor, raid overlap, level scaling, flee without entering) | 0.0.8 |

## 2. What is next

Remaining milestones (tickets in `docs/IMPLEMENTATION_BACKLOG.json`):

| Milestone | Tickets | Notes |
|---|---|---|
| **M9 World safety** (next, 0.0.9) | HB-T-075..079 | World marker + guard warning, opt-in backup, integrity checks, missing-mod end-to-end, save/reload torture. |
| M10 UI + localization | HB-T-080..083, 099, 106, 107, 111 | Panel v2, hovers, full pt-BR/en-US pass, looks (HB-T-099 has the owner's kitbash ideas), custom icons drawn by Kelvin (HB-T-106), optional custom 3D models (HB-T-107). |
| M11 Optimization + release | HB-T-084..089 | Performance budget (HB-201), multi-Haugbui validation, config review, README, Thunderstore package, regression checklist. |
| M12 Away mode | HB-T-094..097 | Remote logistics on world data while the player is far away; spike HB-T-090 already passed. Charges tribute shifts. |

Renaming a servant in the tomb panel (PRD HB-139) is ticket HB-T-111 in M10.

## 3. Known gaps carried forward

- Tomb zone unloaded while the Haugbui is out: it despawns and resumes on the next load; the deposit happens then.
- No custom emerge/enter VFX or idle sounds. Combat plays the Wraith's own alert effect.
- Combat defence (incoming damage divided by the level multiplier) is still marked "to confirm" in PRD §23.
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

### Machine and folders (since 2026-10-16: Windows only, Cursor)

- Everything happens on Kelvin's Windows PC: repo at `D:\ProjetosGit\haugbui_valheim_mod` (normal clone, `.git` inside,
  no Drive sync). Game at `F:\SteamLibrary\steamapps\common\Valheim` (an old `E:\SteamLibrary\...\Valheim` folder only
  holds leftover BepInEx files; ignore it). Test profile (r2modman) `HaugbuiDev`:
  `C:\Users\ponto\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\HaugbuiDev`; Debug builds deploy there via
  `Environment.props` (per machine, git-ignored). Self-test output and `LogOutput.log` are in that profile's `BepInEx\`.
- Until M7 planning ran on a Mac with the repo synced through Google Drive, a separate git dir and
  `../haugbui-reference`; that setup is retired (details in `docs/history/PLANNING_LOG.md`). `HAUGBUI_BUILD_ROOT` and
  the `../haugbui-reference` fallback still work if it is ever needed again.

### Game references (never commit)

- `VALHEIM_MANAGED` (build) resolves to the game's `valheim_Data/Managed`, else `.reference/Managed`, else
  `../haugbui-reference/Managed` (`Directory.Build.props`). The GameApiManifest unit test only looks in
  `.reference/Managed` or `../haugbui-reference/Managed` and is **skipped** when neither exists (M8 adds a fallback to
  the game install).
- **Done 2026-10-16:** the 1.0.20 `Managed` DLLs are in `.reference/Managed` and decompiled with ilspycmd **9.1**
  (`dotnet tool install -g ilspycmd --version 9.1.0.7988`; the latest version does not install on the .NET 8 SDK) into
  `.reference/decompiled/{assembly_valheim, assembly_utils, assembly_guiutils, Jotunn}` (`Version.CurrentVersion =
  1.0.20`). `dotnet test`: 297/297, manifest test included. Redo both steps after each game update (DEVELOPMENT_GUIDE §6).
- Search the decompile with ripgrep/grep over `*.cs` (one generated file is named `-PrivateImplementationDetails-.cs`).
- Prefab values (capacities, conversions, recipes, attack damage) are asset data: run `haugbui_dump_prefabs` in game
  and keep the JSON in `.reference/dumps/`. The last dump is from 1.0.17; a 1.0.20 dump is welcome for M8.

### Accounts and git

- The repo belongs to Kelvin's personal GitHub account (`Chelvis`). Commit author: Kelvin's global git config
  (`Kelvin Marques <kelvin.marques@gmail.com>`), decision 2026-10-16; no repo-local identity. Commits up to 0.0.7 used
  the noreply address, which is fine.
- No `gh` CLI on this machine: push over HTTPS with Git Credential Manager (`credential.helper = manager`). On
  2026-10-16 a dry-run push failed with "Invalid username or token", so Kelvin may need to sign in again (GCM prompt)
  before the first push.
- **Approval guard (local hooks, never committed):** `.git/hooks/pre-commit` refuses a commit and `.git/hooks/pre-push`
  refuses a push unless the token file `.git/haugbui-desatracar` exists. An agent creates the token only after Kelvin
  writes "Desatracar"; `pre-push` deletes it after allowing the push (one approval = one round). Push the branch and
  the tag in **one** command (`git push origin main vX.Y.Z`), because the token is gone after the first push. Never
  bypass with `--no-verify`. The hooks live only in this clone; recreate them in a new clone (see AGENTS.md).

## 6. Workflow that worked: planner + implementation sessions

- One **planner** session owns product decisions, the PRD/backlog, milestone order and reviews. It does not implement
  features. For each milestone it writes a self-contained brief (goal, ticket ids, product rules, file locations,
  build/test/git/version rules) in `docs/briefs/M<n>.md` and starts an **implementation** session (Cursor: a new
  chat/agent started with "Read docs/briefs/M<n>.md and follow it") that works in the main checkout, never in a
  separate worktree.
- The implementation session proposes a short plan, implements in rounds, extends the self-test, asks Kelvin to run
  it, fixes failures, and only after his approval commits, bumps the version and tags. It reports back to the planner:
  commit/tag, test numbers, decisions taken alone, gaps.
- Only one session edits docs at a time; relay decisions instead of editing files another session is changing.
- Briefs up to M7 are in `docs/history/PLANNING_LOG.md` (the M7 brief is the template); from M8 on they are in
  `docs/briefs/`.

## 7. Quick reference

- Build: `dotnet build Haugbui.sln -c Debug` (deploys to `MOD_DEPLOYPATH`), `-c Release` (no spikes/self-test).
- Unit tests: `dotnet test tests/Haugbui.Core.Tests` (341 tests at 0.0.8, incl. the manifest check when
  `.reference/Managed` or the game install exists).
- In game (Debug build, `devcommands`): `haugbui_selftest [basics structures flight transfers haugbui storage chests
  harvest tribute territory production stations sleep combat] [and_exit]`; diagnostics `haugbui_status`, `haugbui_dump_tomb`,
  `haugbui_dump_prefabs`, `haugbui_reservations`, `haugbui_ledger`, `haugbui_registry`.
- Config: `BepInEx/config/com.kelvinmarques.haugbui.cfg`.
