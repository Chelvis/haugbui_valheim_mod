# M10 report — UI and localization, version 0.0.10

Tag pushed. Report from the implementation session to the planner (2026-10-28).

## Git

```
7d553e1 v0.0.10: M10 UI and looks so the tomb, chests and upgrades can be told apart and managed.
8d06b9f v0.0.9: M9 world safety so a missing tomb is noticed and a bad blob is kept.
32b72e4 Slow the Haugbui to 1-6 m/s and keep the radius ring visible.
22177fa v0.0.8: M8 combat so the Haugbui can defend its radius.
b1d2ab4 Add handover docs for continuing outside Claude Code
```

Tag `v0.0.10` is on `7d553e1`. Tag `v0.0.9` is still `8d06b9f`. `main` matches `origin/main`. One commit, 56 files. This report is **not** in that commit.

## Builds

Debug build of the shipped tree passed and was deployed to the HaugbuiDev profile before the version strings were bumped; the bump itself is comments and constants only. A full `dotnet test` and a Release build were **not** repeated on `7d553e1`. During the session, `PieceRecipe` tests were 12/12 after the chest-cost change. Do not treat 0.0.10 as having a fresh full-suite number.

## Self-test

No `haugbui_selftest` report was collected for 0.0.10. Kelvin approved the build in play on 2026-10-27 and 2026-10-28 ("Tudo ótimo") and wrote **Desatracar**. That is the validation the tag sits on. The suites in the commit cover the M10 checks (carried grid, filter, rename, panel close, icon custom-vs-fallback, vein pulse curve, offer rows, chest marks). They were not executed as a recorded run.

## Looks Kelvin approved

In play, not from a self-test picture folder.

| Piece | What shipped |
|---|---|
| Tomb | Grausten chest, yaw 180, scale 1.2. Original colour, relief and metal. Purple vein mask on the chest's own glow, one 128×128 map, pulsed (5 s loop: off, full from 30% to 70%, off). Skull stains left as they are. Break stays the host's. |
| Offering | Feast on the table. Feast build-menu icon, as he asked. |
| Treasure | Open treasure chest, lead, front facing the ghost. Distance mesh `lod1` not copied. |
| Bracelets | Standing brazier without coal, ash or flame. Three silver rings in the bowl. He called them perfect. Column look remains in code. |
| Longsword | Primal Slayer on the wall. Icon is still that exported sprite, not a new drawing. |
| Altar | Rune table, his `Runetable_d.png`, blue rune glow. Distance mesh `low` not copied. |
| Collection | Wood chest, his green colour PNG, emerald up arrow floating above, no board, no Sign component. |
| Delivery | Wood chest, his blue colour PNG, original wood relief (his normal map warped the mesh and was reverted), sky-blue down arrow. |

The arrows rise 5 cm in one second and return in the next. Opening the tomb or either Haugbui chest hides the crafting column. A vanilla chest still shows it. The return countdown sits 50 cm lower than the old 2.4 m mark.

**Not in git.** `art/reference/` and `art/icon-sources/` are gitignored. The vein mask, the altar albedo, and both chest colour PNGs exist only on this machine. A clone builds, and those looks fall back to the vanilla maps. The eight hammer icons **are** in `art/icons/` (64×64, not the 128×128 the ticket asked for; he drew and approved these).

## Icons

All eight files are present. None of the eight fall back while those files ship.

- His drawings: bracelets, treasure, altar, tomb, collection, delivery.
- Offering: the vanilla feast icon, which he told us to keep.
- Longsword: the exported Primal Slayer icon. He did not replace the file. He accepted closing M10 with it.

## Decisions taken alone

PRD §23, marked **to confirm** unless the row already says confirmed. Kelvin approved the looks and the close of M10 in play. He has not gone through the log row by row.

- Tomb yaw 180 so the ghost shows the front. Treasure yaw 180 removed for the same reason.
- Vein mask: branch texels only. Skull mesh was not split. Skull stains deferred. Backup of the earlier mask stays at `art/reference/grausten-chest/backup/graustenchest_veins_best.png` (not in git). One emission texture, replaced in place, not stacked. Pulse is a recolour of that same material.
- Altar glow recoloured from the purple pixels in `Runetable_e` to blue. His edited `Runetable_d.png` is the stone colour.
- Bracelets on `piece_brazierfloor01`, rings flat, 3 cm toward the bowl centre. Marble-column look kept in code.
- Break debris uses the meshes the player sees for offering, treasure, bracelets and altar. Tomb, sword and the two chests keep the host break. Treasure `lod1` and altar `low` are not copied.
- Build costs confirmed 2026-10-26, then chests changed 2026-10-27: collection Wood ×10 + AncientSeed ×2; delivery Wood ×10 + GreydwarfEye ×10. Workbench. The HaugbuiDev cfg was edited to match, because BepInEx keeps old recipe strings.
- Upgrade arrow on the five upgrades only (`Piece.m_isUpgrade`).
- Haugbui storage hides the crafting column.
- Chest hover no longer repeats the piece name in the role line.
- Tribute offers: three rows, two wider columns.
- Countdown label local Y 1.9 (was 2.4).

Rows he had already confirmed before this session (sword model, break debris, altar glow, build costs, upgrade arrow, the first five icons) stay confirmed.

## API

`InventoryGui.CloseContainer` was added to `GameApiManifest.cs` (private). The crafting column is hidden through the public `Show`/`Hide` and `m_crafting` as well. No other new private game members for the looks: public `Piece.m_isUpgrade`, Jötunn `AssetUtils.LoadTexture` / `LoadSpriteFromFile`.

## Left for M11

Scheduled on M11:

- **HB-T-084** performance budgets
- **HB-T-085** multi-Haugbui coordination check
- **HB-T-086** configuration review
- **HB-T-087** README, protection statement, known limitations
- **HB-T-088** Thunderstore package. **Must decide where the gitignored PNGs live**, or the package will not contain the vein glow, the altar stone, or the chest colours.
- **HB-T-113** partial pickup of overweight stacks
- **HB-T-114** production inputs from the ground
- **HB-T-115** combat offence and defence curves
- **HB-T-116** outdoor HP regen
- **HB-T-089** release regression checklist

**HB-T-107** (custom 3D models) was not started. Kelvin did not supply models. Do not block M11 on it.

Also still open from M9, not M10 work: the on-disk missing-mod round trip (DLL removed), and a full `haugbui_selftest` of every suite.
