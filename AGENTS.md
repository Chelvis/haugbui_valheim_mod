# Agent instructions — Haugbui

Rules for any AI agent working in this repository (Cursor, Claude Code or others). Read `docs/HANDOVER.md` first.

## Communication

- Talk to the product owner (Kelvin) in **Brazilian Portuguese**. Code, comments, commit messages and documentation in
  **English (en-US)**.
- Product decisions belong to Kelvin. When an implementation forces a choice the PRD does not answer, pick the safest
  option, record it as a row in `docs/PRODUCT_REQUIREMENTS.md` §23 (decisions log) marked "to confirm", and tell him.

## Ground truth

- `docs/PRODUCT_REQUIREMENTS.md` — requirements (`HB-xxx`) and the decisions log (§23). Every behaviour change updates it.
- `docs/IMPLEMENTATION_BACKLOG.json` — tickets (`HB-T-xxx`) per milestone (M1–M12) with dependencies and acceptance
  criteria. Keep it valid: every dependency exists and appears earlier, every PRD id is covered by a ticket.
- `docs/ARCHITECTURE.md`, `docs/TECHNICAL_FEASIBILITY.md`, `docs/DEVELOPMENT_GUIDE.md`.
- Never guess Valheim APIs. Verify every member in decompiled game sources (see HANDOVER "Game references") and add
  every private member, field or RPC the mod uses to `src/Haugbui.Core/GameApi/GameApiManifest.cs` (checked at startup
  and by a unit test against the real DLLs).

## Code

- `src/Haugbui.Core` (netstandard2.0, no Unity/Valheim/BepInEx/Jötunn references) holds domain logic with NUnit tests in
  `tests/Haugbui.Core.Tests`. `src/Haugbui` (net48) holds game code; adapters live in the `Haugbui.Integration`
  namespace (a `Haugbui.Game` namespace would hide Valheim's global `Game` class).
- Comments: default to none. Only explain what the code cannot say (workarounds, external API limits, traps, invariants,
  why something that looks wrong is right). One or two lines, English, no ticket ids, no decision narration.
- Item safety is non-negotiable: the Haugbui never creates, deletes or duplicates items; transfers put the item in the
  destination before removing it from the source; with `StrictItemData` ON it merges only identical item data and never
  makes items cheated or clean (PRD HB-061).
- v1 automates no recipe crafting at any crafting station (HB-122).

## Testing

- Every ticket with in-game behaviour gets checks in an in-game self-test suite (`src/Haugbui/SelfTest`, Debug builds
  only). Suites clean up what they create and verify item totals. Avoid manual checklists.
- Kelvin runs the console command `haugbui_selftest [suite…]` himself on Windows and sends back
  `BepInEx/haugbui_selftest/` (report.txt + PNGs) and `LogOutput.log`. Do not propose the `-haugbui-autotest` launch
  mode. Never commit test results.
- `dotnet test tests/Haugbui.Core.Tests` and Debug + Release builds must pass before asking for an in-game run.

## Git and versions

- Commit and push only after Kelvin explicitly approves (he used the keyword **"Desatracar"** for that). One approval
  covers one commit round. Push straight to `main` of `github.com/Chelvis/haugbui_valheim_mod`.
- Local hooks enforce it: `.git/hooks/pre-commit` and `pre-push` refuse unless the token file `.git/haugbui-desatracar`
  exists. Create the token only after Kelvin writes "Desatracar"; `pre-push` deletes it after allowing the push, so
  push branch and tag in one command (`git push origin main vX.Y.Z`). Never use `--no-verify`. In a fresh clone,
  recreate both hooks (sh scripts, LF line endings) before committing.
- Commit author is Kelvin's global git config (personal email); do not set a repo-local identity. Push over HTTPS with
  Git Credential Manager (no `gh` CLI on the Windows machine).
- Each completed milestone bumps the version — M7 = 0.0.7, M8 = 0.0.8 … — in `Directory.Build.props`
  (`HaugbuiVersion`) and `HaugbuiPlugin.PluginVersion`, and is tagged `vX.Y.Z` on the commit Kelvin's self-test
  validated. Tags are lightweight: push them explicitly.
- Never commit game assemblies, decompiled sources, prefab dumps, publicized assemblies, `Environment.props` or
  self-test results.
