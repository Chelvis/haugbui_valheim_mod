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
  BoneFragments ×10, SurtlingCore ×1, GreydwarfEye ×5; requires a nearby Workbench.
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
  extensions); the hover text and the tomb panel show the current level only ("Level n") [Decision 2026-10-19].
- **HB-013** Level and abilities are recomputed when upgrades are placed/destroyed and on load. Attribute changes apply
  immediately (HP max raises current HP proportionally). Losing the Longsword or Altar removes that ability at once
  (combat stops; a daytime Haugbui returns to its tomb).

### 4.2 Upgrades (costs are tunable defaults, rationale in §4.3)

| Upgrade | Visual | Mandatory per brief | Default recipe | Station | Biome |
|---|---|---|---|---|---|
| **HB-020 Offering** | plate with roasted boar meat and a mead mug | boar meat, honey, a Black Forest ingredient | CookedMeat ×5, Honey ×10, Thistle ×5, Wood ×10 | Workbench | Black Forest |
| **HB-021 Personal Treasure** | reinforced iron chest with coins, rubies, amber | coins, rubies, amber pearls, iron | Coins ×150, Ruby ×3, AmberPearl ×3, Amber ×5, Iron ×10 | Forge | Swamp |
| **HB-022 Silver Bracelets** | small stand with silver arm rings | silver | Silver ×10, WolfPelt ×2, FineWood ×4 | Forge | Mountains |
| **HB-023 Fallen King's Longsword** | black-metal two-handed longsword on a wall mount | black metal | BlackMetal ×15, LinenThread ×5, TrophyDraugrElite ×1 | Forge | Plains |
| **HB-024 Altar of Darkness** | dark altar radiating purple energy | eitr, black marble | BlackMarble ×20, Eitr ×10, BoneFragments ×20, Ectoplasm ×10 | Galdr table | Mistlands |

All item and station names above were verified in game on 1.0.17 (startup validation 8/8 and `haugbui_dump_prefabs`,
2026-10-13). All recipe ingredients
and stations are validated at startup against `ObjectDB`/`ZNetScene` (backlog HB-T-012); a missing name disables
that piece with a logged error instead of breaking the game.

### 4.3 Cost design

The brief only fixed each upgrade's mandatory ingredient; the rest is a balance proposal (product owner delegated it,
2026-10-13), meant to be tuned in playtests. Principles:

- **The tomb is a burial-chamber reward.** Tomb, Collection Chest and Delivery Chest use Black Forest burial-chamber
  loot (bone fragments, surtling core) and early materials only, so level 1 is useful as soon as the player clears
  the first crypt — fitting for a mound-dweller.
- **One upgrade per biome.** Each upgrade's mandatory material belongs to a different biome, so the Haugbui grows
  with the playthrough. Since order is free, the player picks which bonus comes first once they own the materials.
- **The first upgrade is cheap.** Any upgrade reaches level 2, which unlocks production. The Offering is the
  cheapest (Black Forest, workbench), so automation arrives around the time the player builds the first smelter.
- **Ability upgrades cost a trophy or rare material.** Combat (Longsword) needs a Draugr Elite trophy — the "fallen
  king" — and full-time work (Altar) needs Mistlands eitr: the two strongest effects arrive late.
- **Quantities stay below the matching vanilla station** of the same tier (e.g. the Longsword costs less black metal
  than a black-metal weapon set), so the upgrade feels like decoration that pays off, not a resource sink.
- Ghost-themed extras (bone fragments, ectoplasm) keep the undead flavour without adding new farming.

- **HB-025** Upgrades have no gameplay interaction other than hover info; they can be deconstructed (refund = recipe).
- **HB-026 Combat warning.** When the Longsword binds to a tomb, a modal popup explains: the Haugbui will defend its
  burial place at all costs and may damage your buildings during combat. Combat stays **OFF** until the player enables
  it in the tomb panel; the first enable requires confirming the same warning (stored as `combat_ack`).

## 5. Attributes

- **HB-030** Progression (defaults, all configurable):

| Attribute | L1 | L2 | L3 | L4 | L5 | L6 |
|---|---:|---:|---:|---:|---:|---:|
| Carry capacity (weight units) | 50 | 70 | 90 | 110 | 130 | 150 |
| Max movement speed (m/s) | 2.0 | 2.8 | 3.6 | 4.4 | 5.2 | 6.0 |
| Operational radius (m) | 50 | 60 | 70 | 80 | 90 | 100 |
| Inventory slots | 2 | 3 | 4 | 5 | 6 | 6 |
| HP | 20 | 40 | 60 | 80 | 100 | 120 |
| Combat multiplier (attack damage ×, incoming damage ÷) | 1.0 | 1.2 | 1.4 | 1.6 | 1.8 | 2.0 |

- **HB-031** Carry capacity uses vanilla item weight (`m_weight × stack`). Weight and slots limit how many units of a stack fit. A stack that does not fit whole is taken in part; the rest stays and is collected on later trips. A single unit heavier than the capacity is never taken.
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
  chest is empty, and refuses everything else. First UI: typeahead multi-select and clear-all; icons later.
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
  Wood ×10, BoneFragments ×5, Resin ×5 (wood chest = Wood ×10); Workbench.
- **HB-071** Every collection cycle (config, default 10 s while working) a Haugbui whose radius contains it takes
  eligible items that have a valid destination and distributes them per §8.1.
- **HB-072** Ineligible items (equipment) stay in the chest untouched.

## 10. Delivery Chest

- **HB-080** Buildable chest, **exactly 10 slots**, distinct look. Cost (tunable, §4.3): Wood ×15, BoneFragments ×10,
  GreydwarfEye ×5, Resin ×5; Workbench.
- **HB-081** `E` opens the chest normally; `Shift+E` (alt-interact) opens the request editor.
- **HB-082** A request line = {item, requested quantity}. Up to 10 lines [Decision]. Item picker lists transportable
  items the player has discovered.
- **HB-083** The ledger tracks `delivered` per line. Remaining = requested − delivered. The Haugbui never fetches
  more than remaining, never removes more from a source than it delivers.
- **HB-084** Sources: storage containers and Collection Chests in radius (never other Delivery Chests or tomb
  storage). Insufficient stock → deliver what exists and keep the line open.
- **HB-085** Lines can be edited (requested < delivered → line completes) or cancelled; completed lines show as done
  until cleared.
- **HB-086** Multiple Delivery Chests are independent; requests are stored on each chest.
- **HB-087** Items reserved by open requests (remaining quantities) are not consumed by production jobs.
- **HB-088** Delivery stops when the chest has no room; player removing items does not reduce `delivered`.

## 11. Ground item collection

- **HB-090** Toggle per tomb, default ON.
- **HB-091** Collects transportable items lying within the radius that have a valid destination.
- **HB-092** Skips: items younger than 10 s, items within 3 m of a player, items currently owned/being picked up by
  someone else, items reserved by another Haugbui, item-stand/piece items.
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
  when it leaves may burn, as for a player — the panel warns about this.
- **HB-122** v1 automates only simple production machines (a station that converts or consumes items placed in it:
  the Smelter, CookingStation and Fermenter families). It never crafts **recipes** at any `CraftingStation`;
  recipe crafting is a V2 candidate. [Decision 2026-10-13, product owner]

### 13.2 Behaviour

- **HB-110** `Shift+E` on a supported station opens the automation panel (per-station settings).
- **HB-111** Settings: enabled, recipe (input → output from the station's own conversion list), mode Finite/Infinite,
  target quantity (finite). Persisted on the station.
- **HB-112** Finite target = number of **products generated by this job** (not storage totals). Progress shows
  supplied inputs, products collected, remaining. Persists across saves.
- **HB-113** The Haugbui supplies inputs and fuel from storage containers (§8 sources, minus delivery reservations),
  keeps the station topped up without exceeding capacity, and collects outputs from the station's output point into
  storage (§8.1; retained if no destination). **Ground** [Decision 2026-10-19]: only while the ground-items toggle is
  on. Then the order is carried items, then eligible ground drops, then storage chests. Toggle off ignores everything
  on the ground, including job inputs and fuel.
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

Planned for release 1.1 (backlog M12), with a feasibility spike in M1; can move into 1.0 if the spike is clean.

- **HB-125** While the game is running but the tomb's area is not loaded (the player is far away), the tomb keeps
  working on world data without a visible Haugbui: sorts tomb storage and Collection Chests into storage chests,
  fulfils Delivery Chest requests, and refills smelter/kiln/refinery inputs and fuel within capacity.
- **HB-126** Away mode follows the same rules as the visible Haugbui (night/Altar, toggles, storage rule, wards,
  destination-first) and is throttled by simulated travel time, so it is never faster than the visible Haugbui.
- **HB-127** Not done in away mode: harvesting, collecting station outputs, cooking, combat. The game freezes
  those objects while unloaded and catches up when the player returns; the visible Haugbui then collects.
- **HB-128** Config `AwayMode.Enabled`, default ON. The tomb panel shows what was done while away (summary counters).
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
  so a Haugbui with combat is at least level 2. Offence and defence become two configurable rows. Until they are
  calibrated, the shared 1.0×–2.0× row stays. Defence target: level 1 handles Meadows animals and struggles against
  Black Forest monsters other than greylings, up to level 6 surviving the offence target. Offence target: level 2
  kills greydwarfs and shamans and has some chance against one greydwarf brute; level 6 kills three cultists, or two
  fenrings, or one troll, in a row; levels 3–5 are even steps between those ends [Decision 2026-10-19].
- **HB-143** Collateral damage to player structures is possible by design (weapon hits pieces near the enemy).
- **HB-144** Toggle OFF: never initiates attacks. When it **takes damage** (being targeted alone does not count), it
  flees to its tomb and waits there **without entering** (entering would end the shift and charge tribute again,
  HB-167); when no attacker remains it resumes work (re-planned). Carried items stay carried during the flight
  (HB-045) [Decision 2026-10-16]. An attacker remains while it is alive, loaded and inside the operational radius and
  either targets the Haugbui or hit it in the last 10 s; the Haugbui also waits until 5 s have passed without damage.
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
| 2026-10-19 | HP regen (HB-035, confirmed) | Resting in the tomb: full in 600 world seconds (about 10 real minutes; a sleep skip completes it). Outside: 1% of max HP every 10 real seconds (about 16.7 minutes from empty to full); a sleep skip does not heal it. No regen while dead. Both rates are config. The rest default is applied; outdoor regen is HB-T-116 |
| 2026-10-19 | Level display (HB-012) | Tomb panel and hover show the current level only ("Level 2"), not "2/6" |

Open: none.
