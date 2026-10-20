# Follow-up proposals (not scheduled)

Product-owner notes of 2026-10-19. Nothing here is implemented. Confirmed points are also rows in PRD §23.
Do not build them inside M8 unless the planner moves a ticket into the current milestone.

## 1. Live carried inventory on the tomb panel

Already specified: PRD HB-160 and backlog **HB-T-080** (M10, "Tomb panel v2"). The panel already refreshes about
every 15 frames and shows carried **weight**, state and activity (`TombPanel.Refresh`). It does not list the items.

Plan: keep HB-T-080 as the ticket. A read-only grid bound to `TombController.Carried` (prefab, amount, cheated mark),
updating on the same refresh. No new persistence. Pull it forward only if Kelvin wants it before M10.

## 2. Interaction gesture — applied 2026-10-19

`CreatureVisuals.InteractionAnimationStart` is `0.35f` and `InteractionAnimationSpeed` is `2f`. Needs a Debug build
before it shows in game.

## 3. Partial pickup of overweight stacks — required, not current behaviour (HB-T-113)

Observed: a stack of 50 Wood on the ground is ignored. Cause, not a weight bug in the transfer itself:

- Wood weighs **2** (`haugbui_prefabs.json`, dump of 1.0.17; stack of 50 = 100). Carry capacity is 50..150
  (HB-030), so a full stack fits only at level 5 (130) and 6 (150). With only the Longsword the tomb is level 2
  (capacity 70, 35 Wood).
- `CarriedInventory.CanAccept` already returns a partial amount.
- `PickupRoute.Plan` (`Haugbui.Core/Harvest/GatherRules.cs`) **skips** a drop unless `CanAccept >=` the whole stack.
  `GatherKeys.AddRoute` then plans `StepKind.PickUp` for `s.Item.Amount`. The drop is never visited.
- `TransferEngine.Move` into the carried inventory would already move a partial amount.
- `GatherDestinations.Has` only requires some chest room for the kind (HB-062/065), so a wood chest unblocks it.

Plan (new ticket, after M8): 

1. `PickupRoute` keeps a drop when `CanAccept > 0`, and the stop's amount is that room, not the whole stack.
2. The pick-up step requests that amount. The remainder stays on the ground, still reserved until the step ends,
   then the next plan takes another partial after the deposit.
3. Same rule for harvest and production-output drops (they share `PickupRoute`).
4. A single unit heavier than the capacity stays forever (cannot split a unit).
5. Core tests: 50 Wood, capacity 50, weight 2 → route takes 25; second plan takes the rest. Self-test: drop 50 Wood
   at level 1 or 2, a wood chest in range, totals conserved, nothing left on the ground.
6. PRD HB-031 already requires the partial take. HB-T-113 is the fix; the skip in `PickupRoute` is the bug.

## 4. Per-chest item filter — confirmed 2026-10-19

HB-062 stays the default: a chest receives an item only when it already holds one of the same prefab, quality and
world level. Kelvin confirmed an optional filter on top:

- Empty filter: HB-062.
- Non-empty filter: the chest accepts **only** the listed prefabs, even while empty, and refuses anything else.
- First UI: typeahead multi-select, plus clear-all. Icons later.
- Stored in a `haugbui_*` ZDO blob (no custom item). Wards, privacy and `StrictItemData` unchanged.
- Self-test: empty chest filtered to Wood receives Wood; Stone stays carried; clearing the list restores HB-062.

## 5. Production may take inputs from the ground — confirmed 2026-10-19

Today `SupplyPlanner.Pick` reads storage containers only (HB-113/087). Ground drops are a separate task and still
need a chest destination (HB-065), so ScrapIron on the ground with no ScrapIron chest never reaches the smelter.

Confirmed rule: **ground toggle off means the Haugbui ignores everything on the ground**, including job inputs and
fuel. No exception for production.

When the toggle is on, one ticket (not a milestone):

- A job may take input and fuel from eligible ground drops (same age, player-distance, transportable, ward and
  territory checks as HB-091/092).
- Order: **carried, then ground, then storage chests.** Delivery reservations (HB-087) apply to chests only.
- Each drop is reserved (`drop:<id>`) so the ground task does not take it on the same pass.
- Partial stacks (section 3) apply, otherwise a 50-stack still cannot be lifted.
- Self-test: toggle off, ore only on the ground → nothing smelted. Toggle on, ore only on the ground → smelted.
  With both a chest and a drop, the drop is used first. Item totals conserved.

## 6. Combat balance — confirmed as two curves, 2026-10-19

Defence is already a real parameter: incoming damage is divided by `CombatMultiplier` (PRD §23, 2026-10-16, still
marked to confirm as a formula). It applies whether or not the Longsword is bound, because monsters already attack
the tamed Haugbui and with combat off it only flees.

**Defence target** (the original ladder). Level 1 survives Meadows animals and struggles against Black Forest
monsters other than greylings. Level 6 survives long enough for the offence target below.

**Offence target** (shortened, because combat starts at level 2):

- Level 2: kills greydwarfs and shamans, and has some chance against a single greydwarf brute.
- Level 6: kills three cultists, or two fenrings, or one troll, in a row.
- Levels 3–5: even steps between those two ends. Do not publish a guessed table.

The current row (Wraith hit × 1.0..2.0, and the same numbers dividing damage taken) is too strong at level 2 and
too flat to reach the level-6 offence target. Plan: split offence and defence into two CSVs, lower the hit base,
keep HP at 20..120 unless measured fights show level 6 dying first, then set the rows from a logged fight
(hits dealt, hits taken, time-to-kill) at level 2 and level 6. The Altar still adds no resistance (HB-053).
Targets are only creatures attacking the base, the player or a Haugbui (decision 2026-10-17), so this ladder is
about those fights.

## 7. HP regen — confirmed 2026-10-19

Rest heal is applied: `Death.RestFullHealSeconds` default is **600** world seconds (about 10 real minutes). The
HaugbuiDev config was changed from 3600 to 600. Outdoor regen (1% of max HP every 10 real seconds, no heal from a
sleep skip, none while dead) is **HB-T-116** and is not implemented yet.
