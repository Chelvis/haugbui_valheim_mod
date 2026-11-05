# M11 report — polish and release, version 0.0.11

M11 is delivered as tag `v0.0.11`. This file is the implementation session's report to the planner (2026-11-05). It is not in the tagged commit. If a number, a decision or a leftover below is wrong, ask for a correction. This report does not close that door.

## Where to look

The facts already on `origin/main`:

- Tag `v0.0.11` is `533acdc`. Tag `v0.0.10` stays `7d553e1`.
- `CHANGELOG.md`, section 0.0.11.
- `README.md` roadmap row M11, and `docs/HANDOVER.md` section 1.
- `docs/PRODUCT_REQUIREMENTS.md` section 23. Confirmed rows and **to confirm** rows are both there.

Only on this machine:

- This file, `docs/briefs/M11-report.md`.
- The full self-test report, which is not committed: `C:\Users\ponto\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\HaugbuiDev\BepInEx\haugbui_selftest\report.txt`.

## Git

```
533acdc v0.0.11: empty the bag on a clock, ignore object damage, and copy lists from the chest hover.
4137b26 Keep M11 on main without tagging 0.0.11.
7d553e1 v0.0.10: M10 UI and looks so the tomb, chests and upgrades can be told apart and managed.
8d06b9f v0.0.9: M9 world safety so a missing tomb is noticed and a bad blob is kept.
32b72e4 Slow the Haugbui to 1-6 m/s and keep the radius ring visible.
```

`v0.0.11` is `533acdc`. From `v0.0.10` to that commit: 93 files, +5226 / −591. `main` matches `origin/main`. Kelvin wrote Desatracar after the green full self-test. This report is not in that commit.

## Builds

On `533acdc`, after the tag:

- `dotnet test tests/Haugbui.Core.Tests`: 384 passed, 0 failed, 0 skipped.
- `dotnet build Haugbui.sln -c Release`: succeeded, 0 warnings, 0 errors.

The Debug build Kelvin played is the same behaviour. Its plugin string was still 0.0.10, because the version constants were bumped in the tagged commit after that run. Those constants do not change the checks.

## Self-test

Full `haugbui_selftest`, run with `and_exit`, so Kelvin was not at the screen. The report file is the result.

`Haugbui 0.0.10 self-test on Valheim 1.0.21 — 2026-11-05 15:15:44`

`RESULT: PASS — 709 passed, 0 failed, 0 skipped in 1024,7s`

Eleven hostile creatures were removed near the player. The header says 0.0.10 for the reason in Builds. That run is the validation the tag sits on.

An earlier full run the night before, 2026-11-04 22:55, failed 3 of 708. Those three were faults in the new checks (the worker was still paused, a copied filter was still on the clipboard, and the bag weight was printed rounded). They were fixed before the 15:15 pass. They were not product failures.

## Combat (HB-T-115)

Kelvin confirmed this on 2026-11-03. Do not retune it.

Offence and defence, levels 1 through 6: **0.5, 0.8, 1.1, 1.4, 1.7, 2.0**. The step is 0.30. Level 1 is 0.5 and does not fight. The troll is not an offence target. Level 6 offence stays 2.0.

## Thunderstore

`artifacts/Haugbui-0.0.11.zip` exists. It is gitignored. `manifest.json` inside it has `version_number` 0.0.11. These four paths were opened in the zip and are present:

- `plugins/Haugbui/textures/graustenchest_veins.png`
- `plugins/Haugbui/textures/Runetable_d.png`
- `plugins/Haugbui/textures/collection.png`
- `plugins/Haugbui/textures/delivery.png`

The source PNGs stay under `art/reference/` and are not in git. The zip is not committed.

## Decisions

Taken with Kelvin and already marked confirmed in section 23. Listed so a later session does not reopen them by accident:

- Carry cycle at 80%, then feed from the bag and store. While unloading, it does not fetch more input (2026-11-03, tightened 2026-11-04).
- Empty the bag every 3 real minutes, and first after leaving the tomb. Next task, not an interrupt. The clock restarts only when the emptying finishes (2026-11-04).
- Leave one of each prefab, except a Collection Chest, tomb storage, and a filter that lists that prefab (2026-11-03).
- Shift+C / Shift+V copy and paste a filter or a request list, in the editor and from the chest hover (2026-11-03).
- Offence and defence curve above (2026-11-03).
- Own 3D models stay out of v1. A dedicated server is not in v1 (2026-11-03).

Still marked **to confirm**. He has not signed these in play, and the tag does not promote them:

- Object damage does not scare it (HB-144, 2026-11-03). Eitr, a stake wall, fire and smoke.
- Hatching eggs and livestock food (HB-092, 2026-11-03), including an asksvin egg.
- The Shift+F key for Find the Haugbui. The behaviour is confirmed. The key is not (2026-11-03).
- The four look textures inside the Thunderstore package (2026-10-28). The 0.0.11 zip now contains them. The section 23 row stays **to confirm**.

Older **to confirm** rows in section 23 were not re-litigated for this tag.

## Left

- **HB-T-107** custom models. Confirmed out of v1. Not started.
- The on-disk missing-mod round trip (DLL removed). Still his observation, not a recorded suite. Carried since M9.
- Away mode is M12. Material piles and keep-lit are M13. Keep-lit is already decided (2026-11-04) and is not built.

## Open for the planner

Ask for a correction if the tag should move, if a ticket is unfinished, or if a **to confirm** row should change. The implementation session will treat that request as open work. It will not treat this file as the last word.
