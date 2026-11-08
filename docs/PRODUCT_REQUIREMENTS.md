# Haugbui — Product Requirements Document

Version 0.2 (planning baseline, product-owner decisions of 2026-10-13 applied) · Game: Valheim 1.0.20 (Windows, single-player first)
Requirement IDs (`HB-xxx`) are referenced by `IMPLEMENTATION_BACKLOG.json`. Text marked **[Decision]** is a design
choice made during planning to resolve ambiguity or a technical limitation; **[Open]** items need product-owner input.

## 1. Vision

Haugbui adds an undead servant bound to a burial tomb. The player builds and enriches the tomb; in return the spectral
Haugbui organizes resources, fulfils requests, harvests, runs production stations and, once given its king's sword,
defends its burial place. It is a supernatural guardian, translucent and floating, not a walking corpse.

**Release scope v1:** single-player (local worlds, Windows). Multiplayer is out of scope but must not be made
impossible (see ARCHITECTURE §15).

## 2. Glossary

| Term | Meaning |
|---|---|
| Tomb | Buildable structure the Haugbui is bound to (home, spawn, progression, management UI). |
| Upgrade | One of five physical structures near the tomb that raise its level. |
| Operational radius | Sphere around the **tomb** in which the Haugbui works. |
| Storage container | A player-built vanilla container eligible as a sorting destination/source (§8.3). |
| Carried inventory | Items the Haugbui is carrying. |
| Tomb storage | The tomb's own container: receives carried items on death/rest, holds overflow. |
| Job | Per-station production automation settings and progress. |

## 3. The Tomb

- **HB-001** The tomb is built with the hammer (Haugbui build category). Cost (tunable, see §4.3): Stone ×20,
  TrophySkeleton ×5, SurtlingCore ×2; requires a nearby Workbench.
- **HB-002** Visual: based on the player tombstone mesh with stones/bones; the vanilla `TombStone` *component* is not used
  (it self-destructs when empty — see feasibility §2.2).
- **HB-003** Placement is valid only within **50 m** (config) of a bed whose owner is the placing player. The ghost
  turns invalid and the hover/centre message says why: "$haugbui_msg_need_bed".
- **HB-004** Multiple tombs are allowed. Placement is blocked within **5 m** (config `TombSpacing`) of another player's
  tomb, to keep visuals and upgrade ownership unambiguous ("$haugbui_msg_tomb_too_close"), and within **100 m** (config
  `TombSpacingSameOwner`) of another tomb of the same owner ("$haugbui_msg_tomb_too_close_own") [Decision 2026-10-14].
  Operational areas may still overlap; overlapping areas of one owner are split into territories (HB-155).
- **HB-005** Each tomb has an owner (its creator). Only the owner can manage it (in single-player always true).
- **HB-006** The tomb shows on hover: name, level, Haugbui state, HP, cooldown remaining.
- **HB-007** The tomb has its own storage (10 slots) used for death recovery, resting deposits and overflow; the owner
  can open it from the tomb panel.
- **HB-008** Deconstructing the tomb: the Haugbui vanishes (despawn VFX); carried items, overflow and tomb storage drop on
  the ground at the tomb (never destroyed); upgrades become unbound (they rebind to a new tomb built within range).
- **HB-009** If the tomb is destroyed by damage, same as HB-008.

## 4. Progression

### 4.1 Levels

Upgrades can be built **in any order**. Level and abilities are two independent things:

| Level | Condition | Unlocks (level-based) |
|---|---|---|
| 1 | basic tomb | Summoning, night work: collection chest, delivery, storage sorting, ground items, harvesting |
| 2 | any 1 upgrade | attributes, **production automation** (config `ProductionUnlockLevel`, default 2) |
| 3–5 | any 2–4 upgrades | attributes |
| 6 | all 5 upgrades | attributes |

| Upgrade | Ability granted (independent of level) |
|---|---|
| Offering, Personal Treasure, Silver Bracelets | none (attributes only) |
| Fallen King's Longsword | **combat** |
| Altar of Darkness | **full-time operation (daytime)**, dark aura (HB-053) |

- **HB-010** Level = 1 + number of **distinct upgrade kinds** present within **8 m** (config) of the tomb and bound to
  it, in any order. Every upgrade raises the attributes one step (§5); only the Longsword (combat) and the Altar
  (full-time) grant abilities, each independently of the other upgrades. Production unlocks by level (§13.3).
  [Decision 2026-10-13, product owner: order is irrelevant; any-order is not harder to build than sequential.]
- **HB-011** Each upgrade kind counts once per tomb. One upgrade binds to exactly one tomb (nearest at placement).
- **HB-012** Workbench-like feedback: a faint spectral beam connects each upgrade to its tomb (like workbench
  extensions). The beam shows only while the player is using that tomb (its panel or its storage), not while merely
  standing nearby. The hover text and the tomb panel show the current level only ("Level n") [Decision 2026-10-19,
  beam timing 2026-11-02].
- **HB-013** Level and abilities are recomputed when upgrades are placed/destroyed and on load. Attribute changes apply
  immediately (HP max raises current HP proportionally). Losing the Longsword or Altar removes that ability at once
  (combat stops; a daytime Haugbui returns to its tomb).

### 4.2 Upgrades (costs are tunable defaults, rationale in §4.3)

| Upgrade | Visual | Mandatory per brief | Default recipe | Station | Biome |
|---|---|---|---|---|---|
| **HB-020 Offering** | plate with roasted boar meat and a mead mug | boar meat, honey, a Black Forest ingredient | CookedMeat ×5, CookedDeerMeat ×3, Carrot ×10, Thistle ×5, Honey ×10, Wood ×10 | Workbench | Black Forest |
| **HB-021 Personal Treasure** | reinforced iron chest with coins, rubies, amber | coins, rubies, amber pearls, iron | Coins ×150, Ruby ×3, AmberPearl ×3, Amber ×5, Iron ×10 | Forge | Swamp |
| **HB-022 Silver Bracelets** | small stand with silver arm rings | silver | Silver ×10, Bronze ×5, WolfClaw ×3 | Forge | Mountains |
| **HB-023 Fallen King's Longsword** | black-metal two-handed longsword on a wall mount | black metal | BlackMetal ×20, Chain ×2, TrophyDraugrElite ×1 | Forge | Plains |
| **HB-024 Altar of Darkness** | dark altar radiating purple energy | eitr, black marble | BlackMarble ×20, Eitr ×10, WitheredBone ×20, Ectoplasm ×10 | Galdr table | Mistlands |

All item and station names above were verified in game on 1.0.17 (startup validation 8/8 and `haugbui_dump_prefabs`,
2026-10-13). All recipe ingredients
and stations are validated at startup against `ObjectDB`/`ZNetScene` (backlog HB-T-012); a missing name disables
that piece with a logged error instead of breaking the game.

### 4.3 Cost design

The brief only fixed each upgrade's mandatory ingredient; the rest is a balance proposal (product owner delegated it,
2026-10-13), meant to be tuned in playtests. Principles:

- **The tomb is a burial-chamber reward.** Tomb, Collection Chest and Delivery Chest use burial loot and early materials,
  so level 1 is useful as soon as the player clears the first crypt — fitting for a mound-dweller.
- **One upgrade per biome.** Each upgrade's mandatory material belongs to a different biome, so the Haugbui grows
  with the playthrough. Since order is free, the player picks which bonus comes first once they own the materials.
- **The first upgrade is cheap.** Any upgrade reaches level 2, which unlocks production. The Offering is the
  cheapest (Black Forest, workbench), so automation arrives around the time the player builds the first smelter.
- **Ability upgrades cost a trophy or rare material.** Combat (Longsword) needs a Draugr Elite trophy — the "fallen
  king" — and full-time work (Altar) needs Mistlands eitr: the two strongest effects arrive late.
- **Quantities stay below the matching vanilla station** of the same tier (e.g. the Longsword costs less black metal
  than a black-metal weapon set), so the upgrade feels like decoration that pays off, not a resource sink.
- Ghost-themed extras (withered bone, ectoplasm) keep the undead flavour without adding new farming.

- **HB-025** Upgrades have no gameplay interaction other than hover info; they can be deconstructed (refund = recipe).
- **HB-026 Combat warning.** When the player places the Longsword and it binds to a tomb, a modal popup explains: the Haugbui will defend its
  burial place at all costs and may damage your buildings during combat. Combat stays **OFF** until the player enables
  it in the tomb panel; the first enable requires confirming the same warning (stored as `combat_ack`).

## 5. Attributes

- **HB-030** Progression (defaults, all configurable):

| Attribute | L1 | L2 | L3 | L4 | L5 | L6 |
|---|---:|---:|---:|---:|---:|---:|
| Carry capacity (weight units) | 50 | 70 | 90 | 110 | 130 | 150 |
| Max movement speed (m/s) | 1 | 2 | 3 | 4 | 5 | 6 |
| Operational radius (m) | 50 | 60 | 70 | 80 | 90 | 100 |
| Inventory slots | 2 | 3 | 4 | 5 | 6 | 6 |
| HP | 20 | 40 | 60 | 80 | 100 | 120 |
| Offence multiplier (attack damage ×) | 0.5 | 0.8 | 1.1 | 1.4 | 1.7 | 2.0 |
| Defence multiplier (incoming damage ÷) | 0.5 | 0.8 | 1.1 | 1.4 | 1.7 | 2.0 |

- **HB-031** Carry capacity uses vanilla item weight (`m_weight × stack`). Weight and slots limit how many units of a stack fit. A stack that does not fit whole is taken in part; the rest stays and is collected on later trips. A single unit heavier than the capacity is never taken. The Haugbui works in cycles [Decision 2026-11-03]: it keeps picking up while its bag is under 80% of its weight and something else still fits. An item that does not fit at all is skipped. At 80%, or when nothing else fits or nothing is left to collect, it feeds production stations from the bag and then stores the rest. While the bag is unloading it does not stop at a chest or a drop to fetch more input. Every 3 real minutes, and as the first chore after it leaves the tomb, emptying the bag is the next task. It does not interrupt the task already under way. The 3 minutes start again only when that emptying finishes. A fight still comes first, and only when the Longsword is bound and combat is on. Items no chest will take stay in the bag, and the emptying still counts as finished [Decision 2026-11-04]. A Delivery Chest is the exception: that trip stays in front and loads every open line that fits before it walks. Done food is the other exception, on the stone oven, the cooking station, the iron cooking station and the frost foundry: it is taken off as soon as it is ready.
- **HB-032** Movement speed is a maximum; the Haugbui accelerates/decelerates smoothly and hovers slowly when idle.
- **HB-033** Operational radius is centred on the tomb. **Limitation:** only objects in loaded zones exist; at the
  lowest simulation-distance setting the effective radius may be smaller (UI shows "partially loaded" hint). When the
  tomb itself is unloaded, away mode applies (§13.4).
- **HB-034** Interactions require the Haugbui within **1.0 m** of the target's nearest surface/interaction point.
- **HB-035** HP regenerates in two ways [Decision 2026-10-19]. While it rests in the tomb, healing is linear in world time, full in `RestFullHealSeconds` (default 600, about 10 real minutes; a sleep skip completes it). While outside, it regains 1% of max HP every 10 real seconds (full in about 16.7 minutes) and a sleep skip does not heal it. No regen while dead. The outdoor rate is config.

## 6. Day and night

- **HB-040** Without the Altar of Darkness the Haugbui emerges only when `EnvMan.IsNight()` and returns to its tomb before dawn
  (starts returning when the day fraction is within a configurable lead, default ~1 real minute before 0.25).
  During the day it stays dormant inside the tomb.
- **HB-041** With the Altar of Darkness bound it may operate at any time; it does not return at sunrise.
  Placing the altar explains that daylight work is a second paid shift. An altar that reattaches to a rebuilt tomb
  does not show that explanation again.
- **HB-042** Weather has no effect (never use `IsDaylight`, which is false during "always dark" weather).
- **HB-043** If dawn arrives while it is far away, it flies back (no teleport); it stops starting new tasks once the
  return lead begins, but completes the current atomic step.
- **HB-044** Whenever the Haugbui enters its tomb (dawn, Rest toggle, tomb unloading) or dies, everything it carries is
  moved to **tomb storage**, which the player can open while it sleeps (tomb panel or `E` on the tomb). Tomb storage
  is the first source it sorts from when it emerges again. If tomb storage is full, the rest goes to overflow (HB-131).
  [Decision 2026-10-13, product owner]
- **HB-046** **Sleeping.** When the player sleeps, the game fast-forwards world time to the next morning in ~12 real
  seconds (and wakes at day fraction 0.15, which is still night). During that skip the Haugbui works in **fast mode**:
  it executes its normal tasks without visible flight (the screen is faded), each step consuming simulated time
  (travel + interaction) from a budget equal to the world time skipped while it may work (night only, or all of it
  with the Altar). Same rules as normal work (storage rule, reservations, cooking dawn rule, destination-first);
  combat is paused. Result: a sleeping player loses none of the servant's night. Config `SleepFastMode`, default ON.
- **HB-045** Every transfer puts the item in its destination before removing it from its source, in the same frame
  (prior art: Dvergr Tidier). An interrupted Haugbui never loses or duplicates what it carries (HB-044).

## 7. Appearance, behaviour and feedback

- **HB-050** Based on the Wraith (flying creature), recoloured translucent/spectral, ethereal smoke particles,
  wraith/ghost idle sounds at low frequency. No drops on death.
- **HB-051** Passes through walls, doors, furniture, chests, terrain objects; never blocked by buildings. Never sinks
  below terrain. [Decision 2026-10-13: the product owner accepted physical movement if simpler; incorporeal flight is
  the simpler option — no navmesh, doors, stairs or stuck recovery. See feasibility §5.]
- **HB-052** Visual feedback (VFX/SFX, performance-capped): emerging, returning/entering, working (subtle glow),
  collecting (pickup sparkle), delivering, operating station (vanilla station add effects), detecting enemy (alert
  sound), fighting (attack animation), defeat (ghost death VFX), resurrection (spawn VFX at tomb).
- **HB-053** Altar of Darkness appearance: dark aura, subtle purple particles (Dvergr-mage style), smoke inspired by the evil
  bone pile, slightly stronger glow. The altar does **not** grant extra damage resistance.
- **HB-054** "Effects quality" config (Low/Normal) reduces particle counts.

## 8. Inventory and storage

- **HB-060** Transportable items: item types Material, Consumable, Trophy, Misc, Fish, AmmoNonEquipable. Never: any
  item where `IsEquipable()` is true (weapons, tools, armor, clothing/capes, utility/accessories, trinkets, ammo,
  torches, shields) nor Customization/None. Classification uses the game's item data, never names.
- **HB-061** The Haugbui never creates or deletes items; all moves preserve item data (quality, world level, cheated
  flag, custom data). Items must never end up marked as cheated because of the Haugbui [Decision 2026-10-13]: it merges
  items into an existing stack only when their data is identical (quality, world level, variant, cheated flag, crafter,
  custom data); otherwise it uses an empty slot or another destination, or keeps the item and retries (HB-065). It never
  sets the cheated flag and carries the source's flag exactly, including into station RPCs (e.g. `RPC_AddOre(name,
  cheated)`). Taking items for a delivery or job takes identical items only, so a cheated item never comes out clean.
  **This strict rule is configurable** [Decision 2026-10-14]: config `StrictItemData` (General, default ON). With it OFF
  the Haugbui stacks like a player moving items: it tops up any stack of the same item, quality and world level, and the
  vanilla rules apply to the result (a cheated item joining a stack makes the stack cheated; the stack keeps its own
  custom data and crafter). Even OFF it never does what vanilla would not: it never clears a cheated flag, never marks
  an item cheated out of nothing, takes identical items from its sources, and station RPCs carry the source's flag (a
  clean item is never fed into a station whose queue is cheated). The tribute balance tracks its cheated part in both
  modes.

### 8.1 Storage rule

- **HB-062** It deposits an item only into a storage container that already contains ≥1 item of the same prefab,
  quality and world level. Empty chests never receive items. The item tops up only identical stacks (HB-061); a
  matching chest with no identical stack takes it into an empty slot. **Chest filter** [Decision 2026-10-19]: a chest
  may store a list of prefabs. Empty list keeps this rule. A non-empty list accepts only those prefabs, even while the
  chest is empty, and refuses everything else. Each listed prefab may also store how many stacks of it the chest may
  hold. An empty cap means the chest's own room is the only limit. A chest already over that cap is not emptied and
  receives nothing more, including no top-up; a chest sitting on the cap can still fill the open stacks. The hover
  shows how many items are in the filter, not their names. Suggestion lists show only items the character has picked
  up. First UI: typeahead multi-select and clear-all; icons later. Copy Filters and Paste Filters keep one list in
  memory until the game closes. Paste replaces that chest's whole list, including stack caps, and saves immediately.
  The chest hover shows Shift+C and Shift+V. Looking at the chest, those keys copy and paste its filter. The same
  press does not toggle walk or auto loot.
- **HB-063** Destination choice among matching containers: (1) can accept the whole carried stack, (2) fewest
  free-slot fragmentation (prefer topping identical stacks), (3) nearest to the Haugbui. Never exceed capacity.
- **HB-064** In its sorting role it never removes items from ordinary storage containers. Removal from storage
  happens only to fulfil Delivery Chest requests (§10) and production jobs (§13).
- **HB-065** If no destination exists, it keeps the item and retries with exponential backoff (max every 60 s);
  it does not pick up new items of that type from the Collection Chest or ground while no destination exists.
- **HB-066** Containers skipped: in use by a player, not loaded, not permitted (§16), and these kinds: Haugbui
  tomb storage, Collection Chest (as destination), Delivery Chest (except for its own requests), player tombstones,
  carts/ship storage (`m_wagon`, moving vehicles), Obliterator, containers with `m_autoDestroyEmpty`, non-player-built
  containers (dungeon chests).

## 9. Collection Chest

- **HB-070** Buildable chest, **exactly 10 slots**, distinct look (bone/rune trim, faint glow). Cost (tunable, §4.3):
  Wood ×10, AncientSeed ×2; Workbench.
- **HB-071** Every collection cycle (config, default 10 s while working) a Haugbui whose radius contains it takes
  eligible items that have a valid destination and distributes them per §8.1.
- **HB-072** Ineligible items (equipment) stay in the chest untouched.

## 10. Delivery Chest

- **HB-080** Buildable chest, **exactly 10 slots**, distinct look. Cost (tunable, §4.3): Wood ×10,
  GreydwarfEye ×10; Workbench.
- **HB-081** `E` opens the chest normally; `Shift+E` (alt-interact) opens the request editor.
- **HB-082** A request line = {item, requested quantity}. Up to 10 lines [Decision]. Item picker lists transportable
  items the player has discovered. Copy Request and Paste Request keep one list in memory until the game closes.
  Paste replaces that chest's whole request list and writes it immediately, keeping what was already delivered the
  same way Save does. The panel stays open. Copy stays off while a requested amount is empty. An empty copy is a
  real copy, and pasting it clears the other chest. While the editor is open, and while looking at the chest,
  Shift+C copies and Shift+V pastes. The hover shows both. Those presses do not toggle walk or auto loot. The filter
  editor uses the same two keys.
- **HB-083** The ledger tracks `delivered` per line. Remaining = requested − delivered. The Haugbui never fetches
  more than remaining, never removes more from a source than it delivers. One trip to a Delivery Chest loads every
  open line that still fits in the bag, then puts them all in.
- **HB-084** Sources: storage containers and Collection Chests in radius (never other Delivery Chests or tomb
  storage). Insufficient stock → deliver what exists and keep the line open. An ordinary storage chest keeps one of
  each prefab, so it still accepts that item later. A filter that lists the prefab may be emptied of it. Collection
  Chests and tomb storage are still emptied. The same leave-one rule applies when a production job takes from storage.
- **HB-085** Lines can be edited (requested < delivered → line completes) or cancelled; completed lines show as done
  until cleared.
- **HB-086** Multiple Delivery Chests are independent; requests are stored on each chest.
- **HB-087** Items reserved by open requests (remaining quantities) are not consumed by production jobs.
- **HB-088** Delivery stops when the chest has no room; player removing items does not reduce `delivered`.

## 11. Ground item collection

- **HB-090** Toggle per tomb, default ON.
- **HB-091** Collects transportable items lying within the radius that have a valid destination.
- **HB-092** Skips: items younger than 10 s, items within 3 m of a player, items currently owned/being picked up by
  someone else, items reserved by another Haugbui, item-stand/piece items. A single egg that the game can hatch
  stays. That is a hen egg or an asksvin egg warming up (`EggGrow`), including while it is still cold, and an asksvin
  egg that hatches when someone stands near it (`EggHatch`). A stack of eggs is still collected, because a stack does
  not hatch. Food
  on the ground stays when a tamed animal that eats that item is inside that animal's own search range. The same
  item farther away is still collected. Chests are unchanged.
- **HB-093** Production outputs and harvest drops are collected as part of those tasks even if this toggle is OFF.

## 12. Harvesting

- **HB-100** Toggle per tomb, default ON.
- **HB-101** Mature crops: cultivated `Pickable` objects with `m_harvestable` that are not picked. Growing plants are
  ignored. Wild pickables (berries, mushrooms…) are ignored by default (config to include).
- **HB-102** Beehives (`piece_beehive`) and Birds' Nests (`piece_birdnest`, Feathers) are extracted only when
  level ≥ 1 unit.
- **HB-103** Uses the game's own pick/extract actions (normal drop tables, effects); then picks up the resulting drops.
- **HB-104** Never plants, replants, harvests immature crops, destroys hives/nests or harms animals.
- **HB-105** Sap collectors: optional extra (config, default OFF) — not in the original spec.

## 13. Production automation

### 13.1 Supported stations (v1)

All stations below unlock at `ProductionUnlockLevel` (default **2**).

| Station | Mode |
|---|---|
| Smelter, Blast furnace, Charcoal kiln, Windmill, Spinning wheel, Eitr refinery | inputs + fuel, outputs from ground |
| Fermenter | load base, tap when ready |
| Hot tub, Frigid kiln, Shield generator | fuel maintenance only |
| Cooking station, Iron cooking station, Stone oven, Frost foundry | inputs + fuel, remove done items; dawn-aware (below) |
| Any recipe station: Workbench, Forge, Black forge, Stonecutter, Artisan table, Galdr table, Cauldron, Mead ketill, Food preparation table | **not in v1** (V2 candidate) |
| Obliterator | **never** |

- **HB-121** Cooking stations: the Haugbui loads a slot only if it can come back before the item burns (it will
  still be outside, or it has the Altar). Before entering the tomb it removes every done item; items still cooking
  when it leaves may burn, as for a player — the panel warns about this. The stone oven, the cooking station,
  the iron cooking station and the frost foundry pull a done item immediately, ahead of the carry cycle. Smelters
  and the other machines do not burn a finished product, so their outputs wait for the cycle.
- **HB-122** v1 automates only simple production machines (a station that converts or consumes items placed in it:
  the Smelter, CookingStation and Fermenter families). It never crafts **recipes** at any `CraftingStation`;
  recipe crafting is a V2 candidate. [Decision 2026-10-13, product owner]

### 13.2 Behaviour

- **HB-110** `Shift+E` on a supported station opens the automation panel (per-station settings).
- **HB-111** Settings: enabled, recipe (input → output from the station's own conversion list), mode Finite/Infinite,
  target quantity (finite). Persisted on the station.
- **HB-112** Finite target = number of **products generated by this job** (not storage totals). Progress shows
  supplied inputs, products collected, remaining. Persists across saves. Reset on the panel zeros supplied and
  produced and keeps the target, the recipe, the mode and whether the job is on. Items already in the station stay
  there. The same finite amount then runs again [Decision 2026-11-07].
- **HB-113** The Haugbui supplies inputs and fuel from storage containers (§8 sources, minus delivery reservations),
  keeps the station topped up without exceeding capacity, and collects outputs from the station's output point into
  storage (§8.1; retained if no destination). **Ground** [Decision 2026-10-19]: only while the ground-items toggle is
  on. Then the order is carried items, then eligible ground drops, then storage chests. Toggle off ignores everything
  on the ground, including job inputs and fuel. Material piles (HB-176) are a later source and are not in this order yet.
- **HB-177** **Keep lit** [Decision 2026-11-04]. M13, not M11 or M12. On a `Fireplace` the player can refill (the game's own `m_canRefill`, and not `m_infiniteFuel`), Shift+E only toggles keep-lit, stored on that piece. Default off. The hover gains one line: `$haugbui_keep_lit_on` is "[Shift+E] keep lit (on)" / "[Shift+E] manter aceso (ligado)", and `$haugbui_keep_lit_off` is the off line. While on, the Haugbui refuels when the shown fuel is 1 or 0, up to full, using that fire's own fuel item and the game's own add-fuel call. The unit leaves the source only after the fire's fuel rises (HB-045). Sources follow HB-113. A full fire, a fire already above 1, a smelter, a kiln, an oven and a shield generator are not this task. Wards and territories apply.
- **HB-176** **Material piles** [Decision 2026-11-02]. A later milestone, not M11. While the tomb toggle is on (default **off**), the Haugbui may dismantle a player-built pile whose recipe is a single stack of one transportable material (wood, stone, marble, metal and the like) and take that refund for a delivery or a production job. Furniture, chests and other buildings are not piles. A pile in a ward the owner cannot use is left alone. The refund is received before the piece is removed (HB-045); what does not fit the carry limit stays as a ground drop and is collected in parts (HB-031). Per tomb, piles are used either before chests or only after chests fall short. Carried items stay first. Ground items stay as in HB-113 and are not reordered by this toggle.
- **HB-114** Infinite mode runs while resources exist and until disabled; never creates items.
- **HB-115** Missing input/fuel → job shows "waiting for X" and resumes automatically.
- **HB-116** Only recipes in the station's conversion list whose input and output materials the owner has discovered
  [Decision: vanilla has no smelting "unlock" — feasibility §4].
- **HB-117** Manual use: the player may still use the station. The Haugbui never removes queued items; if the queue
  holds a different input than the job recipe, the job pauses ("manual use") until the queue empties.
- **HB-118** One Haugbui owns a station job at a time (persisted owner); others ignore it.
- **HB-119** Station-specific restrictions are reported, not bypassed (spinning wheel roof, windmill wind, kiln
  smoke, fermenter exposure).
- **HB-120** Automation can be inspected (status line) on station hover.

### 13.3 Unlock level (§13.5 of the brief)

**Level 2, configurable** (`ProductionUnlockLevel`). [Decision 2026-10-13, product owner: production should arrive
early.] Night-only work (without the Altar) already limits throughput.

### 13.4 Away mode (working while the player is elsewhere)

- **HB-125** While the game is running, a tomb whose checkbox is on keeps its area loaded only while that Haugbui
  may be out. Rest, a revive wait, and daytime without the Altar do not keep it loaded. While he may be out, the
  existing Haugbui keeps flying and working. A tomb with the checkbox off does not keep its area loaded.
- **HB-126** The same rules as when the player is there, because it is the same Haugbui: night or Altar, toggles,
  the existing-item rule, wards, `StrictItemData`, leave-one, delivery reservations, territories, the bag and
  tribute. There is no second budget.
- **HB-127** Cooking, plants, beehives, ground items, fermenters and combat are not a separate simulation. They
  keep going while the area is loaded, which is while he may be out.
- **HB-128** The checkbox starts off, including tombs saved before it existed. `AwayMode.Enabled` (default ON) is
  a master switch; both must be on. The panel shows the checkbox and a warning that this uses more CPU.
  `AwayMode.TickSeconds` stays in the config so older files load, and nothing reads it.
- **HB-129** Nothing happens while the game is closed (world time is paused).

## 14. Death and resurrection

- **HB-130** The Haugbui can be damaged and killed only while outside its tomb.
- **HB-131** On death: ghost-death VFX, creature removed, death time stored in the tomb, all carried items moved to
  tomb storage; remainder goes to overflow (persisted list shown in the tomb panel, retrievable by the owner). Nothing is
  destroyed.
- **HB-132** Cooldown default **1 Valheim day** of world time (config `CooldownDays`, in days so it follows the game's
  day length). Countdown shown above the tomb (world-space label, 1 Hz) and in hover/panel, counting down to the
  moment it can return.
- **HB-133** When the cooldown ends: **without the Altar** it returns only at the **start of the next night** (dusk),
  even if it is already night when the cooldown ends; **with the Altar** it returns immediately. Returning starts a new
  shift and is charged normally (HB-167). [Decision 2026-10-14]
- **HB-136** **New tomb:** a newly built tomb starts working only at the **start of the next night**, with or without
  the Altar. When it inherits a servant still in cooldown (HB-137), the later of the two times applies, so destroying
  and rebuilding a tomb is never faster than waiting. [Decision 2026-10-14]
- **HB-134** Survives saves, exits, restarts, reloads (stored in the tomb ZDO; no in-memory-only timers).
- **HB-135** The cooldown uses **world time** only: it pauses while the game is closed and sleeping advances it.
  [Decision 2026-10-13, product owner; no real-time option.]

### 14.1 Servant identity (the Haugbui outlives its tomb)

Lore: once a Haugbui binds itself to you there is no getting rid of it. It joins you to fulfil its purpose, prove its
worth and become worthy of Valhalla, no longer bound to its tomb or to the earthly world, and it does not give up.
Whenever its tomb is destroyed, it returns as soon as a new tomb is built.

- **HB-137** **Servant slots.** Each player has a fixed number of servant slots per world (default **3**, config
  `MaxHaugbuisPerPlayer`; its description warns that many Haugbuis can cause performance problems and errors). Slots
  exist from the start and are filled when a servant is first created. A slot stores the servant's identity: name,
  death/cooldown state and toggles. It is saved in the world (global key values, verified API:
  `ZoneSystem.m_globalKeysValues`), not in the tomb, so it survives tomb destruction and a missing mod.
- **HB-138** **Binding.** Building a tomb binds it automatically to the owner's lowest slot that has no tomb; when all
  slots are bound the tomb cannot be placed ("$haugbui_msg_max_tombs"). Destroying a tomb leaves its slot vacant with
  name and cooldown kept. Not inherited by a new tomb: level and abilities (recomputed from nearby upgrades), carried
  items, overflow and tomb storage (dropped at the old tomb, HB-008), tribute balance (refunded, HB-159).
- **HB-139** **Name and no dismissal.** A new servant is named "Haugbui" (first slot) or "Haugbui #2", "Haugbui #3"…;
  slot numbers are not shown elsewhere. Renaming in the tomb panel is planned (customizable name). There is no way to
  dismiss a servant: once created it is permanent in that world.

## 15. Combat

- **HB-140** Available while the Longsword is bound, toggle default OFF, first enable requires warning confirmation (HB-026).
- **HB-141** Detects valid hostiles inside the operational radius: alive, not players, not tamed, not Haugbuis,
  enemies of a tamed creature (the game's rule; Dvergr only once angry), in loaded zones. **Outside a raid** only
  creatures attacking a player, a Haugbui or a player-built piece (monster AI with that target and not fleeing), or that
  hurt a Haugbui in the last 10 s. **During a raid** whose area overlaps the operational radius, every creature that is
  not tamed is a target until the raid ends; then only remaining attackers [Decision 2026-10-17]. **Bosses are valid
  targets** (at the player's own risk; no exclusion and no config) [Decision 2026-10-16]. It always fights the
  **nearest** valid target [Decision 2026-10-16].
- **HB-142** On detection: suspend task (after current atomic step), alert sound, fly to target, attack with its
  spectral blade (wraith-based melee), return to previous tasks when no hostile remains (re-planned). **Level
  scaling** [Decision 2026-10-16]: the combat multiplier of its level (§5, config, default 1.0× at level 1 to 2.0× at
  level 6) multiplies its attack damage and divides the damage it takes (level 6 takes half damage). The multiplier is
  level-based only; the Altar grants no extra resistance (HB-053). No new fight starts once the return lead (or Rest)
  sent it home; a fight in progress ends and it flies home. Level 1 never fights: the Longsword is itself an upgrade,
  so a Haugbui with combat is at least level 2. Offence and defence are two configurable rows (§5). Offence is
  0.5, 0.8, 1.1, 1.4, 1.7, 2.0. Defence uses the same row, so level 1 takes double damage and level 6 still takes half.
  The Altar grants no extra resistance (HB-053). Defence target: level 1 handles Meadows animals and struggles against
  Black Forest monsters other than greylings, up to level 6 surviving the offence target. Offence target: level 2
  kills greydwarfs and shamans and has some chance against one greydwarf brute; level 6 kills three cultists, or two
  fenrings, in a row. A troll is not a target. Levels 3–5 are even steps between those ends [Decision 2026-11-02].
- **HB-143** Collateral damage to player structures is possible by design (weapon hits pieces near the enemy).
- **HB-144** Toggle OFF: never initiates attacks. When it **takes damage from a creature** (being targeted alone does
  not count), it flees to its tomb and waits there **without entering** (entering would end the shift and charge
  tribute again, HB-167); when no attacker remains it resumes work (re-planned). Carried items stay carried during the
  flight (HB-045) [Decision 2026-10-16]. An attacker remains while it is alive, loaded and inside the operational
  radius and either targets the Haugbui or hit it in the last 10 s; the Haugbui also waits until 5 s have passed
  without damage. Damage with no creature behind it does not count: the eitr refinery's cloud, refined eitr and a
  spike wall do not scare it or make it drop the trip it was on [Decision 2026-11-03].
- **HB-145** Several Haugbuis may attack the same enemy without state conflicts.

## 16. Ownership and wards

- **HB-150** Acts on behalf of the tomb owner. A container/station/pickable is usable only if: inside no ward, or in
  a ward where the owner is permitted/creator; private chests only if the owner is the creator.
- **HB-151** Proximity never grants permission. Design supports per-owner checks for multiplayer.

### 16.1 Multiple Haugbuis (brief §15)

- **HB-155** Overlapping operational areas are allowed. **Territories** [Decision 2026-10-14, config `TerritoryMode`,
  default ON]: where the radii of one owner's tombs overlap, every work target belongs only to the nearest of that
  owner's tombs whose radius contains it, and only that tomb's Haugbui acts on it. Work targets: Collection Chests,
  Delivery Chests, production stations and their jobs, harvestables (crops, hives, nests), ground items, and storage
  chests used as deposit destinations. Storage chests used as a source (taking items for a delivery or a production
  job) stay shared by every Haugbui of that owner whose radius covers them; reservations prevent conflicts. Tombs of
  different owners are not affected. Rationale: several servants on one base must still leave work for the player.
- **HB-156** Two Haugbuis never target the same ground item, harvestable, station step or delivery line at the same
  time (lease reservations, and territories for one owner, HB-155); never exceed container capacity; never compete for
  the same station outputs.
- **HB-157** A station job has exactly one owning tomb (persisted); reservations expire safely on death, despawn,
  rest or reload and are never persisted.
- **HB-158** Haugbuis cooperate on different tasks in the same area (e.g. one sorts, one delivers); with territories
  (HB-155) one owner's Haugbuis share only storage chests as sources.

## 17. Tomb management UI

- **HB-160** Panel shows: level, HP/max HP, carried inventory (live, read-only), carry weight/capacity, speed,
  radius, current activity, state, cooldown.
- **HB-161** Toggles: Ground items, Harvesting, Combat (Longsword), Production (production level), Rest (deactivate:
  returns and stays dormant).
- **HB-162** Buttons: open tomb storage, retrieve overflow.
- **HB-163** Valheim look (wood panel, Norse font), keyboard/gamepad closable, blocks player input while open.

### 17.1 Tribute (wages)

The Haugbui does not work for free: each shift is paid with valuables offered at the tomb.

- **HB-164** **Shift.** A shift is one stretch of time the Haugbui may work: the night (from dusk until the dawn return,
  HB-040); with the Altar, the day is a second shift (2 shifts per day). Shifts are identified by world day + day/night,
  so a shift is paid at most once.
- **HB-165** **Cost per shift** in coin units (configurable): levels 1–2 = 5, levels 3–4 = 10, levels 5–6 = 15.
- **HB-166** **Credit.** The tomb keeps a tribute balance (a number in coin units, stored in the tomb ZDO; no custom
  item). Values (configurable): Coins 1, Amber 5, AmberPearl 10, Ruby 20, SilverNecklace 30. The tomb panel has a
  Tribute area with the balance and one "Offer" button per valuable that takes one unit from the player's inventory
  and adds its value. No change is given back.
- **HB-167** **Charging.** At the start of a shift the cost is charged only if there is work to do (Collection Chest
  items, open delivery line, ground items, ripe harvest, station to supply…). With no work it stays dormant, charges
  nothing and re-checks periodically during the shift. The Rest toggle = off, never charged. Away mode (§13.4) and
  sleep fast mode (HB-046) are shifts and are charged the same way.
- **HB-168** **Insufficient balance:** the Haugbui does not emerge; the tomb hover and panel show "no tribute".
  Death during a paid shift: no refund; it never returns within the same shift (HB-133), so its return is a new,
  charged shift.
- **HB-169** **Shift timer.** The panel shows the time left in the current shift (until the dawn return lead, or the end
  of the Altar day shift); while dormant, the time until the next shift starts.
- **HB-159** **Tomb destroyed:** the remaining balance is dropped at the tomb as Coins (default) or forfeited
  (config `TributeOnTombDestroyed` = `DropCoins` | `Forfeit`). Dropped Coins are never marked cheated; only the part
  of the balance that came from cheated valuables is refunded as cheated Coins (the balance tracks it separately, as
  HB-061 requires). Config `TributeEnabled` (default ON) turns the whole system off.

## 18. Persistence and world safety

- **HB-170** Persist: tomb identity (GUID), owner, level cache, upgrade bindings, HP, carried inventory, overflow,
  death state, cooldown, toggles, production jobs and progress, delivery requests. Reservations are transient leases,
  rebuilt after load (by design they must expire safely).
- **HB-171** No static/in-memory-only source of truth; all data in ZDO keys; never overwrite existing data with defaults.
- **HB-172** Missing-mod protection (feasibility §9): no custom items; non-persistent creature; structures persisted as
  vanilla host prefabs with marker keys (pending spike); world marker; warnings on load; opt-in backup.
- **HB-173** World guard warning: on load, if the world marker exists but fewer tombs are found than recorded, show a
  prominent popup with what is missing and how to restore a backup.
- **HB-174** Documentation (README + this PRD) states exactly what is protected (feasibility §9.3).
- **HB-175** Integrity: no duplication/loss under death, save, reload, crash; production queue/ledger consistency;
  cooldown never lost; level always recomputable.

## 19. Configuration

- **HB-180** BepInEx config with defaults from this document: tribute (enabled, costs per level, values, refund on
  destruction), cooldown, base radius & per-level tables, speed,
  capacity, slots, HP, combat multiplier, tick intervals, production unlock level & allowed stations, away mode, bed distance, tomb spacing, upgrade
  range, effects quality, debug logging. Normal play needs no configuration. Technical settings grouped under
  "Advanced".

## 20. Localization

- **HB-190** All player-facing text in `Translations/Portuguese_Brazilian` and `Translations/English` JSON
  (pt-BR is the development language; English is the game fallback and must be complete).
- **HB-191** Keys `haugbui_*`; covers names, descriptions, prompts, config descriptions, errors, statuses, labels,
  production controls, warnings, cooldown messages. Norse tone ("Haugbui", "túmulo/tomb", "oferenda/offering").
  Config descriptions are English-only in the `.cfg` (BepInEx limitation) [Decision], localized in UI.

## 21. Performance

- **HB-200** No per-frame world scans; registries + time-sliced planning; staggered ticks; capped particles;
  no unbounded retries; no concurrent edits of the same inventory.
- **HB-201** Budget target: ≤0.3 ms average per Haugbui per frame with 100 containers in range (measured with debug
  timing).

## 22. Out of scope (v1)

Multiplayer/dedicated servers, simulation while the game is closed, keeping distant areas loaded (chunk loader),
crafting recipes at any crafting station (V2 candidate), gamepad-specific UI polish beyond vanilla
navigation, compatibility patches for other logistics mods (documented only), additional languages.

## 23. Decisions log

| Date | Question | Decision |
|---|---|---|
| 2026-10-13 | Cooldown clock (HB-135) | World time only |
| 2026-10-13 | Production unlock (§13.3) | Level 2, configurable; cooking stations included |
| 2026-10-13 | Recipe crafting (HB-122) | None in v1 (no workbench, forge, cauldron, mead ketill, prep table…); V2 candidate |
| 2026-10-13 | Upgrade order (HB-010) | Any order; level = 1 + upgrades; Longsword and Altar grant independent abilities |
| 2026-10-13 | Recipe quantities | Delegated to planning; defaults and rationale in §4.3; tunable |
| 2026-10-13 | Work while the player sleeps (HB-046) | Fast mode proportional to the skipped night |
| 2026-10-13 | Carried items at dawn (HB-044) | Moved to tomb storage, accessible while the Haugbui sleeps |
| 2026-10-13 | Movement (HB-051) | Incorporeal flight (simpler than physical pathing) |
| 2026-10-13 | Structures if the mod is removed (HB-172) | Persist as vanilla objects whose look the mod changes at runtime (spike HB-T-007) |
| 2026-10-13 | Working while the player is away (§13.4) | Away mode on world data, logistics only (M12, spike in M1) |
| 2026-10-14 | Death and new tombs (HB-132/133/136) | 1-day cooldown; without Altar return only at the next dusk; with Altar immediately; new tombs start at the next dusk |
| 2026-10-14 | Servant identity (§14.1) | Servants live in per-player world slots (default 3, configurable), outlive their tomb, cannot be dismissed |
| 2026-10-14 | Tribute (§17.1) | Paid per shift with a credit balance offered at the tomb panel; charged only when there is work; values Coins 1 / Amber 5 / AmberPearl 10 / Ruby 20 / SilverNecklace 30 |
| 2026-10-13 | Cheated flag on moved items (HB-061) | Never marks anything cheated: merges only identical item data, carries the flag exactly |
| 2026-10-14 | Ground items and harvest without a destination (HB-T-108) | Not picked up (default). Optional config: deposit them in any Collection Chest in range, never in tomb storage |
| 2026-10-14 | M6 implementation choices (confirmed by the product owner 2026-10-14) | Night, shifts and dusk computed from world time (same boundaries as `EnvMan.IsNight`); with `TributeEnabled = false` the Haugbui works every night without the work check; ground items and harvests are taken only when a chest can store the product (HB-065/091); the panel's Offer button takes one item per click |
| 2026-10-14 | Cheated flag at smelters (HB-061, M7 implementation choice) | The smelter keeps one cheated flag for its whole queue (the last item added sets it), so the Haugbui only adds items with the queue's flag (clean first when it is empty) and never feeds cheated fuel (fuel loses the flag) |
| 2026-10-14 | Station job takeover (HB-118, M7 implementation choice) | Another Haugbui takes over a job only while the owning tomb is not loaded/able and has not worked it for `JobTakeoverMinutes` (default 10) of world time |
| 2026-10-14 | Item data "code of conduct" (HB-061) | Optional: `StrictItemData` (default ON) keeps identical-data stacking; OFF stacks like vanilla (cheated taint and custom data follow vanilla), but never clears a cheated flag nor marks items cheated out of nothing |
| 2026-10-14 | Several servants of one owner (HB-004/155) | Same-owner tombs at least 100 m apart (`TombSpacingSameOwner`); with `TerritoryMode` (default ON) each work target belongs to the nearest of the owner's tombs covering it; storage chests stay shared as sources only. "Something must be left for the player to do" |
| 2026-10-15 | M7 round 2 implementation choices | Fermenter: an exposed/unroofed fermenter is reported and not loaded (the game's own load RPC skips that check). Cooking: a slot is loaded only if the item is done 30 s before the dawn return lead (no limit with the Altar); done items are taken off before anything else, and only when they are the job's own. Sleep fast mode: each step costs travel at max speed plus 1.5 s; a skipped afternoon gives nothing without the Altar. `GroundFallbackToCollectionChest` applies to ground items and harvests only |
| 2026-10-16 | Combat OFF and attacked (HB-144) | Flight is triggered only by taking damage, not by being targeted. It flees to its tomb, waits there without entering (no shift end, no new tribute charge) and resumes work when no attacker remains |
| 2026-10-16 | Bosses (HB-141) | Valid targets, at the player's own risk; no exclusion, no config |
| 2026-10-16 | Level scaling in combat (HB-142, §5) | Configurable multiplier per level, default 1.0× at level 1 rising to 2.0× at level 6, applied to its attack damage; level-based only, the Altar still grants no resistance (HB-053) |
| 2026-10-16 | Defence formula (HB-142) — **to confirm** | Incoming damage is divided by the same level multiplier (level 6 takes half damage) |
| 2026-10-16 | Combat details (HB-141/142/144, M8, confirmed by the product owner) | Always the nearest valid target. No new fight once a return is pending (dawn lead, Rest, lost Altar); a fight in progress ends and it flies home. Flight ends when no attacker remains (alive, loaded, inside the radius and targeting it or hit it in the last 10 s) and 5 s passed without damage; damage without an attacker needs only the 5 s. Dvergr only once angry (vanilla rule for tamed creatures). Weapon: the Wraith's own `wraith_melee`, not cloned; config `Combat.DamageMultiplier` (default 1.0) multiplies its hits on top of the level multiplier. Alert: the Wraith's own alerted effect, once per engagement and at most every 10 s (none with `EffectsQuality` Low) |
| 2026-10-16 | M8 implementation choices — **to confirm** | Hurt with combat ON but no valid target (e.g. the attacker stands outside the radius): it flees like with combat OFF. Losing the Longsword keeps the Combat toggle and the acknowledgement, so a rebuilt Longsword brings combat back without asking (a load-order gap must never switch the toggle off). The confirmation hides the tomb panel until it is answered (the vanilla popup draws under it) |
| 2026-10-17 | Combat targets (HB-141, confirmed by the product owner) | Mark as a target only creatures that attack the base, the player or a Haugbui. Attacking = monster AI going for that target and not fleeing (the game distinguishes flee vs attack). Base = any player-placed piece; a Haugbui counts as the base. When a raid's area overlaps the operational radius, every creature that is not tamed is a target until the raid ends; afterwards only remaining attackers. Always the nearest valid target. |
| 2026-10-19 | Chest filter (extends HB-062, HB-T-112) | A chest with a filter accepts only the listed prefabs, even while empty, and refuses everything else. An empty filter keeps HB-062. First UI: typeahead multi-select and a clear-all; icons later |
| 2026-10-19 | Production inputs on the ground | Only while the ground-items toggle is on. Toggle off: the Haugbui ignores everything on the ground, including job inputs and fuel. When on, a job takes carried items, then ground drops, then storage chests |
| 2026-10-19 | Combat ladder (HB-142, §5) — **to confirm** the numbers | Defence stays its own per-level parameter (not the same row as offence). Defence target: the 2026-10-19 biome ladder from level 1 (meadows animals easy, Black Forest hard except greylings) up to level 6. Offence target, from the first level that can fight (2, the Longsword): greydwarfs and shamans, and some chance against one greydwarf brute, up to level 6 killing three cultists, or two fenrings, or one troll, in a row. Calibrate from measured fights; do not ship the current 1.0–2.0 Wraith row as that ladder |
| 2026-10-19 | HP regen (HB-035, confirmed) | Resting in the tomb: full in 600 world seconds (about 10 real minutes; a sleep skip completes it). Outside: 1% of max HP every 10 real seconds (about 16.7 minutes from empty to full); a sleep skip does not heal it. No regen while dead. Both rates are config (`RestFullHealSeconds`, `OutdoorRegenPercent`, `OutdoorRegenSeconds`) |
| 2026-10-19 | Movement speed (HB-030) | Max speed per level is 1, 2, 3, 4, 5, 6 m/s |
| 2026-10-19 | Level display (HB-012) | Tomb panel and hover show the current level only ("Level 2"), not "2/6" |
| 2026-10-19 | World guard scan (HB-173) — **to confirm** | The count is tomb ZDOs in `ZDOMan.m_objectsByID`. `LoadChunks` loads every chunk, so a tomb in an unloaded zone is still counted and does not raise the warning. A shortfall keeps the remembered count, so putting the ZDO back clears the warning. A tomb removed while the mod is loaded updates the count in the same call that deletes its ZDO, so a save cannot remember a tomb that is already gone. An unreadable count is rewritten from the scan without a popup. A marker schema newer than 1 is not overwritten |
| 2026-10-19 | World marker schema (HB-172) — **to confirm** | The marker schema starts at 1. It is not the tomb blob schema (v3) |
| 2026-10-19 | World backup (HB-172) — **to confirm** | Opt-in, default off, once each time a world with Haugbui data loads. Local folder worlds only (`FileHelpers.FileSource.Local` and `World.GetSaveDirectory`); cloud and legacy files are skipped. The copy waits while `ZNet.IsSaving` and is discarded if a save starts during the copy. Old backup folders are not deleted |
| 2026-10-19 | Blob repair (HB-175) — **to confirm** | Readable blobs are clamped in place (level cache 1..6, tribute, job counters, request lines). A request with delivered above requested is closed by raising the request, so it does not ask for more items. Corrupt and future blobs stay byte-for-byte and that object does not run. Reservations are cleared on load because they are not saved |
| 2026-10-22 | Servant name length (HB-139, confirmed) | Same cap as the character name: 15. The creation screen enables Done from 3 characters (`FejdStartup`) and the name field stops at 15. Trimmed, control characters removed. Empty or whitespace restores "Haugbui" / "Haugbui #2" |
| 2026-10-22 | Chest filter entry (HB-062, confirmed) | Shift+E (alt-interact) on a player-built vanilla chest opens the filter. E still opens the chest. Haugbui pieces keep their own panels |
| 2026-10-22 | Chest filter size (HB-062, confirmed) | The saved list stops at 10 prefab names. A full chest still refuses an item that does not fit, even when the filter names it and the chest does not currently hold it. The black metal chest is the largest vanilla storage (32 slots); the cap is not that slot count |
| 2026-10-23 | Longsword model (confirmed) | The wall piece shows the Primal Slayer (`THSwordSlayerNature`). The Brutal Slayer is red, and the sword is black metal |
| 2026-10-23 | Break debris (confirmed) | Offering, treasure, bracelets and the altar shatter the meshes the player sees. Their old table-plank fragment roots are not used. Tomb, longsword and the two chests keep the host's own break, which already matches |
| 2026-10-23 | Tomb albedo test (to confirm) | The colour blocks proved the diffuse map paints the stone. The branch veins are the dark grain in that same map, not a second image |
| 2026-10-26 | Tomb texture (confirmed) | The colour-block test is off. The tomb uses the original grausten colour, plus the purple emission mask on the dark grain |
| 2026-10-26 | Altar rune glow (confirmed) | The purple pixels in `Runetable_e` are stripped and the rune glow is blue. The edited `Runetable_d.png` replaces the stone colour |
| 2026-10-26 | Tomb facing (to confirm) | The grausten chest visual is yawed 180 degrees, so the hammer ghost shows the front (skull and branch) on the first selection |
| 2026-10-26 | Tomb vein glow (to confirm) | Best result so far is kept: the branch-shaped texels only, without the straight bars and without splitting the skulls off the mesh. A copy is saved at `art/reference/grausten-chest/backup/graustenchest_veins_best.png` |
| 2026-10-26 | Build costs (confirmed) | Tomb: Stone ×20, TrophySkeleton ×5, SurtlingCore ×2, workbench. Offering: CookedMeat ×5, CookedDeerMeat ×3, Carrot ×10, Thistle ×5, Honey ×10, Wood ×10, workbench. Bracelets: Silver ×10, Bronze ×5, WolfClaw ×3, forge. Longsword: BlackMetal ×20, Chain ×2, TrophyDraugrElite ×1, forge. Altar swaps BoneFragments for WitheredBone ×20. Treasure is unchanged |
| 2026-10-26 | Bracelets lie flat (to confirm) | The ring mesh is already flat. The extra 90 degree tilt was standing it up. The rings rest in the bowl, each 3 cm closer to the centre than the previous spread |
| 2026-10-26 | Treasure facing (to confirm) | The treasure visual is no longer yawed 180 degrees, so the hammer ghost shows the chest front |
| 2026-10-23 | Treasure break (to confirm) | The treasure chest's distance mesh (`lod1`) is not copied. It is a second chest, and the break was throwing both. The chest the player sees still breaks |
| 2026-10-26 | Altar break (to confirm) | The rune table's distance mesh (`low`) is not copied. It is a second altar, and the break was throwing both |
| 2026-10-23 | Bracelets on a brazier (to confirm) | Test look: `piece_brazierfloor01` as the hammer shows it, without coals, flames or the ash mesh in the bowl. Three bracelets lie on the rim, spread apart, using the silver ingot material. The black-marble column look stays in code |
| 2026-10-22 | Cheated mark on the carried grid (HB-160, confirmed) | A localized word after the amount, "(cheated)" / "(adulterado)". With StrictItemData the Haugbui still stacks only identical item data, so a cheated stack never joins a clean one |
| 2026-10-26 | Upgrade arrow (confirmed) | Offering, treasure, bracelets, longsword and the altar set `Piece.m_isUpgrade`, so the hammer shows the same up arrow as vanilla upgrades. The tomb and the two chests do not |
| 2026-10-26 | Build-menu icons, five upgrades (confirmed) | Offering uses the vanilla feast icon. Bracelets, treasure and the altar use the owner's 64×64 PNGs. The longsword uses the Primal Slayer icon until a replacement is saved. Tomb, collection and delivery use the owner's 64×64 PNGs |
| 2026-10-26 | Storage opens without the crafting list (to confirm) | Opening the tomb, the collection chest or the delivery chest hides the crafting column. A vanilla chest still shows it. Closing the inventory brings it back |
| 2026-10-26 | Delivery chest look, test (to confirm) | The trophy and eyes are off. It is the plain wood chest. `delivery-chest.png` replaces the wood colour. The relief stays the vanilla wood chest normal map; the painted `delivery-chest_n.png` warped the model. The open lid uses the same material |
| 2026-10-27 | Chest marks (to confirm) | Collection and delivery keep the wood chest. A sign canvas floats above each one, without the board and without a Sign component, so it cannot be edited. Collection is a bright emerald up arrow (`#5EFFC0`). Delivery is a bright sky-blue down arrow (`#6ED4FF`). The face colour of the text is forced white so the arrow is not dulled. The mark's centre sits another 10 cm closer to the lid. It rises 5 cm over one second and returns over the next. `art/reference/collection.png` and `delivery.png` replace the wood colour. The collection bones and resin are off |
| 2026-10-27 | Tomb vein pulse (to confirm) | The existing purple emission colour fades over a 5 second loop: off at 0%, full from 30% to 70% (2 seconds), off at 100%. The same material is recoloured; no second glow is added |
| 2026-10-27 | Chest costs and hover (to confirm) | Collection: Wood ×10, AncientSeed ×2. Delivery: Wood ×10, GreydwarfEye ×10. Both stay on the workbench. The hover no longer repeats the chest name in the role line. The return countdown sits 50 cm lower |
| 2026-10-27 | Tribute offers (to confirm) | The five offer buttons use three rows and two wider columns, so Amber Pearl and Silver Necklace are not clipped |
| 2026-10-27 | Tomb veins, v1 (to confirm) | Skull stains stay as they are. The purple mask is one 128×128 emission texture on one material copy, replaced in place each try, not stacked. Another growth fills more of the remaining black tips and stays off the skull texels. The earlier backup is kept |
| 2026-10-26 | Tomb skull stain test (to confirm) | The tomb stays the original Grausten chest, 20% larger. Colour, relief and metal stay the vanilla maps. The purple mask is painted onto the chest's own glow and is not blurred, so it does not smear onto the skull faces |
| 2026-10-26 | Tomb vein glow, next try (to confirm) | The backed-up branch mask is kept. The live mask grows only onto front and lid texels the skulls do not use, so the purple can fill more of the grain without lighting the skull faces |
| 2026-10-28 | Filter and delivery panels (HB-062, HB-081, confirmed) | One typeahead above the list. Typing filters the suggestions; clicking one adds it and hides the suggestions until the field is focused again; the typed text stays. Filter rows are accept-only, one item per line, with an X that removes it. No Add and no Remove. Clear all, plus a corner X that closes every Haugbui panel. Scroll when the list is taller than the panel. Delivery uses the same search. An empty list says there is nothing to deliver. A new line shows how many of that item are already in the chest and an empty requested field. Clear all is available when the list is not empty. Save is disabled only while a requested field is empty. Shift+E stays. The vanilla chest UI has Edit filters; the delivery chest UI has Edit requests |
| 2026-10-28 | Look textures in the Thunderstore package (HB-T-088) — **to confirm** | Do not commit `art/reference/`. Those PNGs are edited vanilla textures. The package is built on the machine that has them and must contain `textures/graustenchest_veins.png`, `Runetable_d.png`, `collection.png` and `delivery.png`. The pack script stops if any of the four is missing. A clone without the files still builds and falls back to the vanilla maps |
| 2026-10-28 | Delivery editor is a draft until Save (HB-081, confirmed) | Clear all empties the list on screen and does not write until Save. Closing without Save keeps the stored requests. An empty list can be saved, which clears them. A new row's requested field starts empty; a line already stored shows its amount. Saving keeps what was already delivered. Save is disabled only while a requested field is empty; a filled field that is not a valid amount is refused when Save is pressed |
| 2026-10-28 | Offence and defence rows (HB-142, confirmed) | `OffenceMultiplier` and `DefenceMultiplier` replace the shared `CombatMultiplier` row. Both stay at 1.0, 1.2, 1.4, 1.6, 1.8, 2.0 until logged fights at level 2 and level 6 are accepted. Nothing is guessed ahead of those fights. The Altar adds no resistance |
| 2026-11-02 | Offence curve (HB-142, confirmed 2026-11-03) | The troll is no longer an offence target. Level 6 offence stays 2.0, the measured troll fight. Level 2 offence is 0.8. The step is 0.30, so the row is 0.5, 0.8, 1.1, 1.4, 1.7, 2.0. A step of 0.24 from 0.8 ends at 1.76 and would change level 6. Level 1 is 0.5, one step below level 2; level 1 never fights |
| 2026-11-02 | Defence curve (HB-142, confirmed) | Defence uses the same row as offence: 0.5, 0.8, 1.1, 1.4, 1.7, 2.0. Level 6 still takes half damage. Level 2 divides incoming damage by 0.8. The Altar adds no resistance |
| 2026-11-02 | Close-fight margin (HB-142, confirmed 2026-11-03) | A calibration fight passes on a win when the last hit dealt and the last hit taken match that level's multipliers. A loss passes when the enemy has at most two of the Haugbui's hits left and the same per-hit check holds. Who lands the last blow does not fail the fight |
| 2026-10-29 | Filter stack cap, hover count, Save closes (HB-062, HB-081, confirmed) | Save on the delivery editor and the production panel closes the panel when the write succeeds. A refused save stays open. Turning production on does not close it. The filter still saves immediately and has no Save button. Each filter row has a stack cap: how many inventory stacks of that prefab the chest may hold. Empty, or 0, means no cap beyond the chest. The Haugbui does not remove stacks already over the cap and adds nothing more once over it. At the cap, an open stack can still be filled. Headers are Item and Stacks (pt-BR: Item and Pilhas). The chest hover shows "1 item in the filter" or "N items in the filter" instead of the names. Suggestion lists stay limited to items the character has picked up |
| 2026-11-02 | Material piles (HB-176, confirmed) | After M11, not in 0.0.11. Tomb toggle, default off. A pile is a player-built piece whose recipe is one stack of one transportable material. The Haugbui dismantles it for a delivery or a production job and receives the refund before the piece is removed. What does not fit is a ground drop, then collected in parts. Per tomb: piles before chests, or chests first and piles only when the chests are short. Carried items stay first. Ground order is unchanged. Furniture, chests and buildings are not piles. A ward the owner cannot use blocks the pile |
| 2026-11-02 | Upgrade beams (HB-012, confirmed) | The connection line shows only while that tomb's panel is open or its storage window is open. Standing nearby does not show it |
| 2026-11-02 | Coin offer button (confirmed) | The Coins button spends 5 coins and adds 5 credits (5 times the coin value, which is 1). Fewer than 5 does nothing. The label is "5 Coins +5" / "5 Moedas +5". The other valuables still offer one at a time |
| 2026-11-02 | Servant name on the creature (HB-139, confirmed) | Renaming writes the live creature's name immediately. It no longer waits for the next spawn |
| 2026-11-02 | Delivery amount colour (HB-081, confirmed 2026-11-03) | The number already in the chest is yellow while it is below the requested amount and green when it is equal or greater. An empty or invalid requested field leaves that number white |
| 2026-11-02 | Interaction swing (confirmed) | The pickup gesture still plays the attack clip, and that clip's hit is ignored. A real fight still uses StartAttack and still deals damage. The training dummy is never a target, including during a raid |
| 2026-11-02 | Resting status (confirmed) | While the tomb is set to rest, the status is "{name} is resting in peace." / "{name} descansa em paz." |
| 2026-11-02 | Leave one item (HB-084, confirmed 2026-11-03) | Every storage container except a Collection Chest and tomb storage keeps one of each prefab when a delivery or a production job takes from it. The tomb is sorted empty on purpose. A filter that lists the prefab may be emptied of it. A filter that does not list it still keeps one |
| 2026-11-02 | Edit filters placement (confirmed 2026-11-03) | The button hangs just under the chest panel's bottom-right, so a tall chest's last row stays clear |
| 2026-11-03 | Carry cycle (HB-031, confirmed) | The bag unloads at 80% of its weight, or when nothing else fits, or when nothing is left to pick up. An item that does not fit is skipped; a part that fits is taken. Unloading feeds machines from the bag, then stores the rest. A Delivery Chest stays in front and one trip loads every open line that fits. Done food is pulled at once from the stone oven, the cooking station, the iron cooking station and the frost foundry |
| 2026-11-04 | Empty the bag every 3 minutes (HB-031, confirmed) | It does not keep a queue. When the current task finishes, it picks the most urgent chore. Every 3 real minutes, and first after it leaves the tomb, that next chore is emptying the bag into chests. The clock starts again only when the emptying finishes. The task already under way is not cut. A fight comes first only when the Longsword is bound and combat is on. Items no chest will take stay carried, and that still finishes the emptying |
| 2026-11-03 | Frost foundry (HB-121, confirmed) | The frost foundry burns a finished item the same way as the cooking stations. A done item there is pulled immediately |
| 2026-11-03 | Hatching eggs and livestock food (HB-092) — **to confirm** | A single egg the game can hatch stays on the ground. That covers a hen egg and an asksvin egg on `EggGrow`, including while it is still cold, and an asksvin egg on `EggHatch`. A stack of eggs is still collected. Food is left only while a tamed animal that eats it is within that animal's own search range. The same item farther away, and anything in a chest, is still collected. This also applies to ground items taken for a job |
| 2026-11-03 | Copy and paste lists (HB-062, HB-082, confirmed 2026-11-03) | One filter list and one request list, remembered until the game closes. Paste replaces the whole list. A filter paste saves immediately. A request paste writes immediately, keeps what was already delivered, and leaves the panel open. Copying an empty list is allowed, and pasting it clears the other chest. Copy Request stays off while a requested field is empty. While either editor is open, Shift+C copies and Shift+V pastes, and those presses do not toggle walk or auto loot |
| 2026-11-03 | Copy and paste from the chest hover (HB-062, HB-082, confirmed) | The hover of a vanilla chest and of a Delivery Chest shows Shift+C and Shift+V. Looking at that chest, the keys copy and paste its filter or its request list. The same press does not toggle walk or auto loot |
| 2026-11-03 | Placement notices (HB-026, HB-041, confirmed 2026-11-03) | The altar explanation and the Longsword warning show when the player places that piece. They do not show again when the piece reattaches to a rebuilt tomb |
| 2026-11-03 | Find the Haugbui (confirmed 2026-11-07) | Looking at the tomb, Shift+F turns the player and the camera toward that tomb's Haugbui and draws its silhouette through other objects for 5 seconds. The tomb hover shows Shift+F. The key does not run while the tomb panel is open, and it does not also run whatever F is bound to. If it is inside the tomb, a message says so |
| 2026-11-07 | Find Haugbui on the tomb panel (confirmed; the panel closes — **to confirm**) | The tomb panel has a Find Haugbui button above the storage buttons. It does the same look and the same 5 second silhouette, then closes the panel so the mark can be seen. If the Haugbui is inside the tomb, the same message is shown. Shift+F is the tomb hover, not this panel |
| 2026-11-03 | Custom models and a dedicated server (confirmed) | Own 3D models stay out of v1. The edited combined pieces are the v1 look. A dedicated server is not in v1; a later 1.1 or 1.2 only if it needs no breaking change |
| 2026-11-03 | Object damage does not scare it (HB-144) — **to confirm** | A hit with no creature behind it is ignored. That covers the eitr refinery's cloud, refined eitr, a spike wall, and the same kind of hit from fire or smoke. It does not flee, does not play the alert, and does not cancel the trip, so ore already in the bag is still fed. A creature's hit, and a player's hit, still send it home |
| 2026-11-04 | Keep lit (HB-177, confirmed) | M13, not M11 or M12. Every refillable Fireplace starts off, and the hover shows the off line until Shift+E. Shift+E only toggles that piece. On: "[Shift+E] keep lit (on)" / "[Shift+E] manter aceso (ligado)". Off: the same line with off / desligado. Refuel when the shown fuel is 1 or 0, filling it to the top with that fire's own fuel. Zero is included. Smelters, kilns, ovens and the shield generator are not included |
| 2026-11-07 | Reset a finished station job (HB-112) — **to confirm** | Reset zeros supplied and produced. The target, the recipe, the mode and whether the job is on stay as they are. The same finite amount runs again. Items already in the station are not removed. Zeroing only the produced count would leave the job finished, because the supplied inputs already cover the target |
| 2026-11-07 | Away mode keeps the area loaded (HB-125, confirmed) | A checkbox on the tomb, default off, keeps that Haugbui's area loaded while the game is running, and the existing Haugbui keeps working. Closing the game still pauses the world. The label is "Keep working while you are away" / "Continua trabalhando enquanto você está longe". The warning is "Keeps this Haugbui's area loaded while you are away. Uses more CPU and can slow the game." / "Mantém a área deste Haugbui carregada enquanto você está longe. Usa mais CPU e pode deixar o jogo lento." |
| 2026-11-07 | Away checkbox starts off on old tombs (HB-128) — **to confirm** | The away bit used to be part of the default toggles and nothing read it, so every saved tomb had it on. Tomb saves older than schema 4, and servant slots older than schema 2, load with that bit off. A new tomb starts off |
| 2026-11-07 | Away master switch and unused tick (HB-128) — **to confirm** | `AwayMode.Enabled` stays default on and is a master switch. The checkbox is the per-tomb choice. Both must be on. `AwayMode.TickSeconds` stays in the config so older files load. Nothing reads it |
| 2026-11-07 | Kept area matches a player bubble (HB-125) — **to confirm** | The kept area is the same near-simulation distance the game uses around a player, centered on the tomb. One new zone is generated per tick. There is no separate count of what was done while away |
| 2026-11-07 | Away area only while he may be out (HB-125, confirmed) | The checkbox does not keep the area loaded while Rest is on, while he is dead and the revive wait has not finished, or by day when the tomb has no Altar. It loads again when he is allowed out: night, or day with the Altar, once the wait is over |
| 2026-11-07 | Away area through the work window (HB-125) — **to confirm** | The area stays loaded for the whole time he is allowed out, including while he is inside because there is nothing to do or tribute is unpaid, so a job can still appear. It also stays loaded while he flies home, and drops once he has entered. At dusk the zones stream back in over a few seconds. The warning is "Keeps this Haugbui's area loaded while he can be out and you are away. Uses more CPU and can slow the game." / "Mantém a área deste Haugbui carregada enquanto ele pode estar fora e você está longe. Usa mais CPU e pode deixar o jogo lento." |

Open: every row marked **to confirm**.
