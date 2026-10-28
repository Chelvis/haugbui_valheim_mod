# M9 report — World safety, version 0.0.9

Tag pushed. Report from the implementation session to the planner (2026-10-22).

## Git

```
8d06b9f v0.0.9: M9 world safety so a missing tomb is noticed and a bad blob is kept.
32b72e4 Slow the Haugbui to 1-6 m/s and keep the radius ring visible.
22177fa v0.0.8: M8 combat so the Haugbui can defend its radius.
b1d2ab4 Add handover docs for continuing outside Claude Code
7631d86 v0.0.7: M7 fermenter, cooking stations, fuel-only stations, sleep fast mode
```

Tag `v0.0.9` is on `8d06b9f`. Tag `v0.0.8` is still `22177fa`. `main` matches `origin/main`. One commit.

## Builds

Core tests: **356 passed**, 0 failed, 0 skipped (341 at 0.0.8). The manifest check ran against the 1.0.20 assemblies. Debug and Release builds passed. Debug was deployed to the HaugbuiDev profile.

## Self-test

Kelvin, world `haugbui test`, Valheim 1.0.20. Command both times: `haugbui_selftest world backup integrity torture`. This is the M9 slice only, not the full suite (the M8 full run was 494).

| When | Result | Time |
|---|---|---|
| 2026-10-21 16:32 | FAIL — 29 passed, 1 failed, 1 skipped | 7.9 s |
| 2026-10-21 20:27 | PASS — 33 passed, 0 failed, 0 skipped | 8.1 s |

First run: backup skipped because the world file-source flag was not exactly `Local`, even though `worlds_local\haugbui test` exists. Torture failed `ore and bars conserved` (`ore 1, bars 0, station 0`): one ore became one bar, the bar chest was empty, and the existing-item rule left the bar in the tomb. The check did not count tomb storage. Not an item loss.

Second run, after the fix: backup created `haugbui_backup_haugbui test_20261021-202712`, saw the world files, and deleted that folder. Torture reported `ore 1, bars 1, station 0`. Wood stayed at 20 across transfer, delivery, and reload. Death cooldown survived the in-process reload. Eight seconds is expected: the test world is small, and production and delivery use the fake clock.

On the way into that second session the load still warned once: the marker remembered 1 tomb, the scan found 0. That count had been saved by an earlier test after the tomb ZDO was already gone. Kelvin quit right after the passing run (`ZNet Shutdown` at 20:27:27). The next entry was not checked.

## Manual missing-mod load

Not done. Kelvin has not quit, removed `Haugbui.dll`, loaded, looked, saved, and put the DLL back. No Task Manager kill during a save either.

## Protection table

`TECHNICAL_FEASIBILITY.md` §9.3 was not replaced with an observed table. It is still the host-prefab design:

| Protected | Not protected |
|---|---|
| Structures as vanilla objects, their containers and items, Haugbui keys, carried inventory in the tomb ZDO, station automation settings | Looks and behaviour while the mod is missing; a hammer hit without the mod refunds only the host's vanilla cost and drops the items |
| A world saved without the mod, then opened with it | Anything another mod deletes; hand edits; Valheim save bugs |

The in-process simulation (augmentation suppressed, object reloaded from its ZDO) covers every structure, plus a tomb with items, a delivery request, and a station job. That simulation is not the on-disk round trip. The paragraph in §9.3 says so.

## Decisions taken alone

PRD §23, all marked **to confirm**, dated 2026-10-19.

- **World guard scan (HB-173).** Count is tomb ZDOs in `ZDOMan.m_objectsByID`. `LoadChunks` still loads every chunk on 1.0.20, so an unloaded zone does not warn. A shortfall keeps the remembered count, so putting the ZDO back clears the warning. A tomb removed while the mod is loaded updates the count in the same `ZNetScene.Destroy` call, before a later save can snapshot the old number. An unreadable count is rewritten from the scan with no popup. A marker schema newer than 1 is not overwritten.
- **World marker schema (HB-172).** Schema starts at 1. It is not the tomb blob schema (v3).
- **World backup (HB-172).** Opt-in, default off, once per load when the world has Haugbui data. Old backup folders are not deleted. The copy waits while `ZNet.IsSaving` and is discarded if a save starts mid-copy. Implementation note, after the first self-test: the folder copied is `worlds_local/<world name>` via `World.GetSaveDirectory(FileSource.Local, name)`, even when the world's file-source flag is not exactly `Local`. A world with no local folder is still skipped. The §23 row still says "file source Local only"; that sentence is behind the code.
- **Blob repair (HB-175).** Readable blobs are clamped (level cache 1..6, tribute, job counters, request lines). A request with delivered above requested is closed by raising the request, so nothing more is owed. Corrupt and future blobs stay byte-for-byte and that object does not run. Reservations are cleared on load because they are not saved.

## API members added

Feature `WorldSafety` in `GameApiManifest.cs`:

- `ZNet.GetWorld`
- `ZNet.GetWorldName`
- `ZNet.IsSaving`
- `ZNet.Save(bool, bool, bool)`
- `World.GetSaveDirectory(FileHelpers+FileSource)`
- `World.GetSaveDirectory(FileHelpers+FileSource, string)`
- `World.m_fileSource`
- `World.m_worldName`
- type `FileHelpers` in `assembly_utils`

Global keys still go straight to `ZoneSystem.m_globalKeys` / `m_globalKeysValues`. Re-checked on the 1.0.20 decompile: `SetGlobalKey` still routes an RPC, and `GlobalKeyAdd` still lower-cases the whole line and copies it into the player profile. Marker values are plain numbers, so the lower-casing is harmless. `SetGlobalKey` is not called.

## Left for M10+

- HB-T-078 on-disk table, after Kelvin does the DLL round trip.
- Optional kill of the process during a save. Not done.
- Full `haugbui_selftest` (all suites) was not re-run on 0.0.9.
- The §23 backup row should be edited to match the `worlds_local/<name>` path, if the planner accepts that fix.
- Next milestone is M10 (UI and localization).
