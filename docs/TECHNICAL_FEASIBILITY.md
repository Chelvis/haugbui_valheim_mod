# Haugbui — Technical Feasibility Report

Status: planning baseline · Target game: **Valheim 1.0.20** (Windows) · Date: 2026-10-12. Findings below were verified on
1.0.17 unless noted; on 2026-10-16 the 1.0.20 assemblies were decompiled into `.reference/` and the GameApiManifest
check passes against them.

## 0. How this report was produced

| Source | Use | Label in this document |
|---|---|---|
| Decompiled `assembly_valheim.dll` from the developer's own Windows install (`Version.CurrentVersion = 1.0.17`, `Version.cs:168`). Kept in `.reference/` (git-ignored, never redistributed). | Primary source for every API claim | **[V]** verified in 1.0.17 code (file:line when useful) |
| Jötunn docs / prefab lists generated from Valheim 1.0.7, official patch notes, Thunderstore pages, open-source mods updated for 1.0 | Prefab names, asset data, toolchain, prior art | **[W]** verified on the web (secondary) |
| Reasoning from verified code, not yet exercised in game | Design assumptions | **[I]** inferred — must be confirmed by an in-game spike |
| Not confirmed anywhere | Open risk | **[U]** unverified |

Prefab *data* (component field values inside asset bundles) is not visible in decompiled C#. Where a value comes from a
pre-1.0 export it is marked as such. Everything marked [I]/[U] has a validation ticket in `IMPLEMENTATION_BACKLOG.json`.

---

## 1. Game version and toolchain

| Item | Finding | Label |
|---|---|---|
| Game version | Valheim **1.0.20** since M7 (`Version.CurrentVersion = 1.0.20`, plugin 0.0.7 loads and its API self-check passes); planning was done on **1.0.17** (patch 2026-10-06). 1.0 released 2026-09-09 (Deep North biome, end of Early Access, new chunked save system). | [V] / [W] |
| Engine | Unity 6 (6000.0.x). Exact build to be read from `BepInEx\LogOutput.log`. | [W] / [U] build number |
| Scripting backend | Mono (BepInEx 5 works). | [I] |
| Game assemblies | `<Valheim>\valheim_Data\Managed\`: `assembly_valheim`, `assembly_utils`, `assembly_guiutils`, `gui_framework`, `SoftReferenceableAssets`, `Splatform`, `PlayFab*`, `UnityEngine.*`, `Unity.TextMeshPro`, `Unity.InputSystem`. | [V] (file list) |
| Mod loader | `denikson-BepInExPack_Valheim` **5.4.2351** (BepInEx 5.4.23.5, UnityDoorstop 4.5.0, Unity 6 support). Already installed on the developer's machine (`winhttp.dll`, `doorstop_config.ini`). | [W] |
| Modding library | **Jötunn 2.30.2** (`JotunnLib` NuGet, net462). 2.30.0 = "updated for Valheim 1.0.7". No release after 1.0.16/1.0.17 → compatibility with 1.0.17 must be smoke-tested. | [W] / [U] |
| Template | `Valheim-Modding/JotunnModStub` (net48, publicizer via `DoPrebuild.props`, `publish.ps1`). Pins outdated Jötunn 2.26/2.27. **Not used** (M1 decision): its prebuild writes publicized DLLs into the game folder and needs the game installed; Haugbui uses `BepInEx.AssemblyPublicizer.MSBuild` + compile-only NuGet refs instead, so it also builds on macOS from `.reference/Managed`. | [V] (JotunnLib 2.30.2 `build/*.props` read) |
| Harmony | HarmonyX shipped in `BepInEx\core\0Harmony.dll` (exact version to read from the file). | [U] |

**Conclusion:** BepInEx 5 + Jötunn 2.30.2 + Harmony is the appropriate and only mainstream stack. No other dependency is
required. Optional runtime tools: `shudnal-ConfigurationManager` (in-game F1 config; the Azumatt package is deprecated).

### 1.1 Valheim 1.0 breaking changes relevant to this mod [V]

Most open-source examples online target 0.217–0.221 and are **wrong for 1.0**:

- `Container` RPCs renamed: `RPC_RequestOpen`, `RPC_OpenResponse`, `RPC_RequestStack`, `RPC_StackResponse`,
  `RPC_RequestTakeAll`, `RPC_TakeAllResponse`, `RPC_Discovered` (`Container.cs:75-81`). Container items are now a
  `byte[]` under `ZDOVars.s_items` (was base64 string).
- "Cheated" item tracking: `ItemData.m_cheated`; `Smelter.RPC_AddOre(string prefabName, bool cheated)`,
  `CookingStation.RPC_AddItem(string, bool)`, `Fermenter.RPC_AddItem(int nameHash, bool)`; `Inventory.AddItem(...,
  bool cheated, ...)`. Merging a cheated item taints the destination stack.
- `Hoverable` gained `float GetHoverOffset()`.
- `Player.PlacementStatus` gained `DeepSnow`, `NoSnow`; `Player.PlacePiece(..., bool cheated)`.
- `Character.Faction` gained `DeepNorth`.
- `ZDOMan.FindSectorObjects` takes `SimulationDistance`; loaded area is a graphics setting (§9.4).
- World saves are folders of chunk files (`_main.N.db2`, `.fwl2`, `.chunks`, `*.chunk`); only dirty chunks are rewritten.

---

## 2. Reusable prefabs and assets

### 2.1 Creature base: Wraith vs Ghost

| | Wraith | Ghost |
|---|---|---|
| Flying character (`Character.m_flying`) | **yes** (fly slow 2.5 / fast 5) [W, 0.221 export] | **no** — floats visually but walks on navmesh (`HumanoidNoSwim`) [W, 0.221 export] |
| Faction | Undead | Undead |
| Default attack | `wraith_melee` | `Ghost_attack` |
| Visuals | `wraith` body material + `evil_smoke` particles (`wraith_smoke`) on head/hands/spine | `Ghost_mat`, smoke/trail particles, point light |
| Drops | Chain, TrophyWraith | Ectoplasm, TrophyGhost |

**Recommendation:** clone **Wraith** with Jötunn (`new CustomCreature("Haugbui", "Wraith", config)`), then strip/override:
`CharacterDrop` (no drops), `MonsterAI` behaviour (see §5), daylight despawn (not applied unless `SpawnSystem` sets it —
cloned creatures do not spawn naturally [W]), tint materials toward a translucent spectral look, scale down slightly.
Re-verify all prefab values against 1.0.17 at runtime in the first spike (values above come from a 0.221.12 export).

### 2.2 Tomb visual: player tombstone

- Prefab `Player_tombstone` [W]: ZNetView, Rigidbody, `TombStone`, `Container` (8×4), ZSyncTransform, `Floating`,
  `FloatingTerrain`.
- `TombStone` **destroys itself when empty** (`UpdateDespawn`, owner-only, every 2 s, `m_nview.Destroy()`), grants the
  looting buff, floats in water and snaps back to its spawn point [V, `TombStone.cs:153-196`].
- **Do not reuse the prefab as-is.** Reuse only its **mesh/materials** as the tomb's visual child.

### 2.3 Storage, VFX/SFX and items

- Vanilla containers: `piece_chest_wood` (5×2 = 10 slots [U, verify prefab data]), `piece_chest` (reinforced),
  `piece_chest_private`, `piece_chest_blackmetal`. `piece_chest_wood` is a natural host for the 10-slot Collection and
  Delivery chests (§9.2).
- VFX/SFX that exist in 1.0.7 [W]: `vfx_ghost_spawn`, `vfx_ghost_death`, `vfx_wraith_death`, `vfx_wraith_hit`,
  `sfx_wraith_idle`, `sfx_wraith_alerted`, `sfx_ghost_idle`, `vfx_bonepile_destroyed`, `sfx_bonepile_destroyed`,
  `vfx_UndeadBurn`, `sfx_UndeadBurn_Start`, `vfx_DraugrSpawn`, `fx_summon_skeleton_spawn`, `vfx_spawn`, `vfx_odin_despawn`,
  `fx_DvergerMage_Support_start` (purple), `fx_DvergerMage_Nova_ring`, `vfx_Potion_eitr_minor`, `vfx_smelter_addore`,
  `sfx_smelter_add`, `fx_tombstone_destroyed`. Names that do **not** exist: `vfx_WraithDeath`, `vfx_Potion_eitr`,
  `vfx_undead_burn`. Evil bone pile prefab: `BonePileSpawner` / `BonePileSpawner_swamp` (child VFX names [U]).
- Item prefab names [W, Jötunn 1.0.7 list]: `RawMeat` (boar meat), `CookedMeat`, `Honey`, `Thistle`, `Blueberries`,
  `Coins`, `Ruby`, `AmberPearl`, `Amber`, `Iron`, `Silver`, `BlackMetal`, `Eitr`, `BlackMarble`, `Feathers`, `MeadTasty`
  (and `MeadBaseTasty`), `TrollHide`, `FineWood`, `Bronze`, `Coal`, `Resin`, `Wood`, `Stone`.

### 2.4 Custom assets required

| Asset | Plan |
|---|---|
| Haugbui creature | Cloned Wraith + material tint + optional emissive. No new mesh needed. |
| Tomb | Kitbash: tombstone mesh + stones/bones (Jötunn `KitbashManager`) on a host prefab. |
| Offering | Kitbash: cooked meat + mead mug item meshes on a plate/table piece. |
| Personal Treasure | Kitbash: iron-banded chest mesh + coin/ruby/amber visuals. |
| Silver Bracelets | Kitbash: small stand + silver material rings (simple primitive torus if no vanilla ring mesh). [U] |
| Fallen King's Longsword | Kitbash: black-metal sword mesh (`SwordBlackmetal` visual) on an item-stand-like wall mount. |
| Altar of Darkness | Kitbash: black marble pieces + `BonePileSpawner`-like smoke + purple Dvergr mage VFX. |
| Altar aura on Haugbui | Reuse particle systems (purple Dvergr VFX, wraith smoke) with reduced emission. |
| Countdown label | World-space TextMeshPro + vanilla `Billboard` component. |
| Icons | Rendered with Jötunn `RenderManager` from the kitbashed prefabs. |

A Unity asset bundle is **optional**: everything above can be kitbashed at runtime. A bundle becomes necessary only if
bespoke meshes (e.g. bracelets) are wanted.

---

## 3. Inventory, items and containers

| Topic | Finding | Label |
|---|---|---|
| Inventory | `Inventory(string name, Sprite bkg, int w, int h)`; `AddItem` overloads (prefab+amount clamps to one stack); `CanAddItem` ignores weight; `CountItems(string sharedName)` uses `$item_*` token; `Save/Load(ZPackage)` format v109. `AddItem(..., dropIfFullInv=true)` drops at `Player.m_localPlayer` — pass `false`. | [V] |
| Item classification | `ItemDrop.ItemData.SharedData.m_itemType` enum (Material, Consumable, Trophy, Misc, Fish, AmmoNonEquipable, Customization, … Trinket=24) and `ItemData.IsEquipable()` (Tool, weapons, shield, armor, ammo, torch, utility, trinket). | [V] |
| Item identity | `m_dropPrefab.name` (prefab name) + `m_quality` + `m_worldLevel`; stacking matches name/quality/worldLevel. | [V] |
| Container | Fields `m_width/m_height/m_privacy/m_checkGuardStone/m_autoDestroyEmpty/m_wagon`; `IsInUse()`; `Load()` skips while in use; saves only `if (!m_loading && IsOwner())`. **No static container list.** | [V] |
| Ownership | Only the ZDO owner's edits persist; `RPC_RequestOpen` transfers ownership to the requester. In single-player the local player is the owner of all loaded ZDOs. | [V] / [I] |
| Ground items | `ItemDrop.s_instances` (private static list), `m_autoPickup`, `Pickup(Humanoid)`, `RequestOwn`, `CanPickup` (owner + 0.5 s), `DropItem(...)` static. Items despawn after 3600 s only if outside a base and no player within 25 m (`ItemDrop.cs:1403`). | [V] |

**Feasible.** The storage rule "deposit only where at least one item of the same type already exists" is easy with
`Inventory.CountItems`/`GetAllItems`. Prior art (AzuAutoStore "Must Have Existing Item") confirms the gameplay pattern.

**Equipment exclusion** uses `m_itemType` + `IsEquipable()` (allow-list: Material, Consumable, Trophy, Misc, Fish,
AmmoNonEquipable; deny everything equipable, `Customization`, `None`). No name-based filtering.

---

## 4. Production stations

All verified in 1.0.17 code [V]. Prefab names and values verified with `haugbui_dump_prefabs` on 1.0.17 (2026-10-13;
dump kept in `.reference/dumps/`, not committed) [V].

| Station class | Prefabs | Input API | Output | Notes |
|---|---|---|---|---|
| `Smelter` | `smelter`, `blastfurnace`, `charcoal_kiln`, `windmill`, `piece_spinningwheel`, `eitrrefinery` (+ fuel-only `piece_bathtub`, `piece_FrostKiln`) | `InvokeRPC("RPC_AddOre", prefabName, cheated)`, `InvokeRPC("RPC_AddFuel")` | **Spawns `ItemDrop` on the ground at `m_outputPoint`.** Windmill (`m_spawnStack`) accumulates then drops a stack (`RPC_EmptyProcessed` forces it). | RPCs do **not** check capacity: caller must check `GetQueueSize() < m_maxOre`, `GetFuel() <= m_maxFuel-1`. Conversions are 1 input → 1 output (`m_conversion` list). No recipe-knowledge gate. Spinning wheel needs roof; windmill output scales with wind. |
| `CookingStation` | `piece_cookingstation`, `piece_cookingstation_iron`, `piece_oven`, `piece_FrostFoundry` | `RPC_AddItem(string, bool)`, `RPC_AddFuel` | `RPC_RemoveDoneItem` spawns ground items | **Items burn** (`m_overCookedItem`) if left; `SpawnItem` dereferences `Player.m_localPlayer` (dedicated-server NRE risk). |
| `Fermenter` | `fermenter` | `RPC_AddItem(int prefabNameHash, bool)` (only when Empty) | `RPC_Tap` (Ready) → `m_producedItems` ground items | 2400 s duration; must not be covered/"Exposed". |
| `ShieldGenerator` | `piece_shieldgenerator` | `RPC_AddFuel` (no max check) | — | Fuel maintenance only. |
| `Incinerator` | `incinerator` (Obliterator) | — | — | **Never automate** (destroys items). |
| `CraftingStation` + `Recipe` | `piece_cauldron` (and later workbench, forge, etc.) | No RPC: vanilla crafting is instant, done by the player through `InventoryGui` (resources removed, product added to the player inventory) | Product goes to the crafter's inventory | Cauldron is a **crafting station**, not a processing machine ([V], dump). Automation = Haugbui performs the recipe itself at ≤1 m: checks `Player.IsRecipeKnown`, station level (`m_minStationLevel`), fire requirement (`CraftingStation.CheckUsable`), removes `m_resources` and adds `m_amount` products to its carried inventory in one frame. Real "unlocked recipe" rule applies here. **Not in v1** (product decision 2026-10-13: no recipe crafting at any station); V2 candidate. [I] |

### 4.1 Prefab values from the 1.0.17 dump [V]

| Prefab | Kind | Fuel (max, per product) | Queue max | Sec/product | Notes |
|---|---|---|---|---|---|
| `smelter` | Smelter | Coal (20, 2) | 10 | 30 | Copper/Tin/Silver ore, Iron ore, Iron/Bronze/Copper scrap |
| `blastfurnace` | Smelter | Coal (20, 2) | 10 | 30 | BlackMetalScrap, FlametalOreNew (listed twice), **GoldOre → Gold** (new) |
| `charcoal_kiln` | Smelter | — | 25 | 15 | Wood, FineWood, RoundLog → Coal |
| `windmill` | Smelter | — | 50 | 10 | Barley → BarleyFlour, **OatSeeds → Oat, Oat → OatFlour** (new); `spawnStack` |
| `piece_spinningwheel` | Smelter | — | 40 | 30 | Flax → LinenThread; needs roof |
| `eitrrefinery` | Smelter | Sap (20, 1) | 20 | 40 | Softtissue → Eitr |
| `piece_bathtub` | Smelter, fuel only | Wood (10, 1) | 0 | 5000 | no conversions |
| `piece_FrostKiln` | Smelter, fuel only | Ice (25, 5) | 0 | 30 | single conversion with **no input** → FrozenFuel: produces from fuel; output must be collected |
| `piece_cookingstation` / `_iron` | CookingStation | no fuel, needs fire | 2 / 5 slots | per item | burns to Coal |
| `piece_oven` | CookingStation | Wood (10) | 4 slots | per item | |
| `piece_FrostFoundry` | CookingStation | FrozenFuel (20) | 1 slot | per item | 30 conversions |
| `fermenter` | Fermenter | — | 1 | 2400 | 20 conversions, **6** products per base (not the code default 4) |
| `piece_beehive` / `piece_birdnest` | Beehive | — | max 4 | 1200 | Honey / Feathers |

Crafting stations: `piece_workbench` (88 recipes), `forge` (74), `blackforge` (144), `piece_magetable` (34),
`piece_artisanstation` (7), `piece_stonecutter` (1), `UpgradeStation`, and three food stations: **`piece_cauldron`**
(41: soups, jerky, stews…), **`piece_MeadCauldron`** "Mead ketill" (21: all mead bases and barley wine base) and
**`piece_preptable`** "food preparation table" (28: doughs, feast materials, raw fish dishes). Mead bases are no
longer made at the cauldron. None of these recipe stations is automated in v1.

`piece_chest_wood` is 5×2 = 10 slots [V], as assumed for the Collection/Delivery chest hosts.

Feasible with a **station adapter** abstraction (§ARCHITECTURE). Two important consequences:

1. **Outputs are ground items.** "Retrieve finished products" means picking up `ItemDrop`s near the station's output
   point. This is independent of the Ground Item Collection toggle (production pickup is part of the job).
2. **No "unlocked recipe" concept for smelting** in vanilla [V]. The requirement "use only recipes unlocked by the player"
   is mapped to: the conversion must exist in the station's `m_conversion`, and the input/output item must be *known* to
   the tomb owner (`Player.IsKnownMaterial($item_token)`) — checked when the job is configured by the player (local
   player is the owner in single-player). [I]

---

## 5. Creature, movement and combat

| Topic | Finding | Label |
|---|---|---|
| AI ownership | `BaseAI.UpdateAI` runs only on the ZDO owner. | [V] |
| Flying move | `BaseAI.MoveTo` skips the navmesh when `m_flying`, but `MoveAndAvoid`/`CanMove` raycast against `Default, static_solid, Default_small, piece, terrain, vehicle` → vanilla flyers **are blocked by walls**. | [V] |
| Pass-through-walls | Requires our own movement controller (owner-only) that moves the body kinematically, plus colliders that ignore `piece`/`static_solid`/`terrain` (Unity `Collider.excludeLayers`, available in Unity 2022.2+/6). Layer matrix itself not visible in code. | [I] / [U] |
| Underworld check | `Character.UnderWorldCheck` teleports a character >1 m below ground to ground+0.5 every 5 s. Flight altitude must stay ≥ ground. | [V] |
| Daylight despawn | `MonsterAI.SetDespawnInDay` only set by `SpawnSystem`; not applied to creatures we spawn. | [V] |
| Targeting | `BaseAI.IsEnemy(a,b)` static. A **tamed** character never targets players or other tamed creatures; tamed creatures skip static (building) targets. `MonsterAI.m_targetCreature`/`SetTarget` are private. | [V] |
| Health | `GetHealth/SetHealth (owner)/SetMaxHealth/GetMaxHealth`, ZDO `health`/`max_health`; `m_onDeath` action; `OnDeath` destroys via `ZNetScene`. | [V] |
| Damage | `Character.Damage(HitData)` → `RPC_Damage` applied on owner. `WearNTear.Damage` damages pieces; pieces flash wards when damaged by characters. | [V] |

**Decision (2026-10-13):** the product owner accepted either incorporeal or physics-respecting movement, whichever is
easier. **Incorporeal flight is the easier and more robust option**: a physics-respecting worker needs pathfinding
inside player bases, and the vanilla navmesh does not link doors or stairs through floor openings, treats doors as
walls and is not reliably rebuilt when the player builds — the #1 source of bugs in worker mods (Vikings For Hire,
Hearthfolk, Valheim Villages; feasibility §11). Flying straight through geometry removes pathfinding entirely.

**Feasible, with custom movement.** The Haugbui keeps `Humanoid` (for health, hit reception, attack pipeline via
`m_defaultItems`/`StartAttack`) but its `MonsterAI` is neutralised (Harmony skip of `UpdateAI` for Haugbui instances, or
replace with a minimal `BaseAI` subclass). A dedicated controller drives position/rotation on the owner; `ZSyncTransform`
replicates it.

**Combat targeting** uses a stricter filter than `IsEnemy` (which would also make it hunt wild deer): valid targets are
non-tamed, non-player, non-Haugbui characters driven by `MonsterAI` (not `AnimalAI`), alive, inside the operational area,
in loaded zones. Collateral damage to pieces comes naturally from area/weapon hits and is part of the design.

---

## 6. Construction, upgrades, beds and wards

| Topic | Finding | Label |
|---|---|---|
| Placement rules | `Player.UpdatePlacementGhost(bool)` (private) sets private `m_placementStatus`; `TryPlacePiece` messages failures via `Message(Center, "$msg_*")`. Mods add rules with a postfix on `UpdatePlacementGhost` that sets the status + a hover message. | [V] / [I] |
| Built-in spacing | `Piece.m_blockingPieces` + `m_blockRadius` → `PlacementStatus.MoreSpace`. | [V] |
| Workbench levels | `CraftingStation.GetLevel() = 1 + extension count`; `StationExtension.FindExtensions` matches station `m_name` within `m_maxStationDistance` (default 5 m), distinct piece names unless `m_stack`. **Pitfalls:** `CraftingStation.Interact` opens the crafting UI; any CraftingStation becomes a build-range station; level is 0 while unloaded. | [V] |
| Beds | `Bed` owner in ZDO `owner`/`ownerName`; `IsMine/GetOwner` private; `IsCurrent()` public; no static list. Spawn point is stored in the player profile per world (`PlayerProfile.GetCustomSpawnPoint`). | [V] |
| Wards | `PrivateArea.CheckAccess(Vector3, float radius=0, bool flash=true, bool wardCheck=false)` evaluates the **local player**; `m_allAreas` private static; `IsPermitted(long)`, `IsInside` private. Containers already check wards on interaction. | [V] |
| Container privacy | `PrivacySetting {Private, Group, Public}`; private chests only for creator; `Group` always false. | [V] |

**Decision:** implement **our own tomb-level computation** that mimics the workbench (proximity, connection beam VFX,
level shown on hover) instead of `CraftingStation`/`StationExtension`. Reasons: avoid the crafting UI and build-range side
effects, support ordered upgrades, and keep the pieces compatible with the host-prefab strategy (§9.2).

**Bed rule** is feasible: on placement, scan `Bed` components within 50 m (physics overlap on `piece` layer) whose ZDO
`owner` equals the placing player's ID. In single-player that is the local player.

**Ward rule:** single-player → `PrivateArea.CheckAccess(point, 0, flash:false)` is exact (owner = local player). For
future multiplayer, reimplement the check for an arbitrary owner ID by iterating `m_allAreas` (publicized) —
designed behind `IAccessPolicy` from day one.

---

## 7. Harvesting

| Target | API | Output | Label |
|---|---|---|---|
| Crops | `Pickable` with `m_harvestable == true` (e.g. `Pickable_Carrot`), ready = `!GetPicked()` and `CanBePicked()`; growing crops are `Plant` (no Pickable yet) | `InvokeRPC("RPC_Pick", 0)` → ground drops; crop object destroyed (no respawn) | [V] / [W] |
| Beehive | `Beehive` (`piece_beehive`), level in ZDO `level` (getter private) | `InvokeRPC("RPC_Extract")` → `level` honey ground drops | [V] |
| **Bird nest** | `piece_birdnest` ("Birds' Nest", **new in 1.0**) uses the **Beehive** component with `m_honeyItem = Feathers`, max 4, 1200 s/unit | Same as beehive | [W] (component data from 1.0 dump) |
| Sap collector | `SapCollector`, `RPC_Extract` | ground drops | [V] (optional extra) |

**All harvesting requirements are feasible using the game's own RPCs**, so normal drop tables and effects apply.
`Pickable.RPC_Pick` uses `Player.m_localPlayer` for effects (NRE on a dedicated server) — irrelevant for single-player,
documented for multiplayer.

Daylight affects only beehive VFX, not production [V].

---

## 8. Day/night and time

| Topic | Finding | Label |
|---|---|---|
| Day/night | `EnvMan.IsDay()` / `IsNight()` static flags; day = smoothed day fraction in [0.25, 0.75]. | [V] |
| Weather | `EnvMan.IsDaylight()` returns false in environments with `m_alwaysDark` → **weather-dependent; must not be used**. `IsDay/IsNight` are weather-independent. | [V] |
| Day length | `EnvMan.m_dayLengthSec` (code default 1200, prefab value reportedly 1800 s = 30 min, day ≈ 21 min / night ≈ 9 min). Read at runtime. | [V] code / [W] value |
| Dawn anticipation | `EnvMan.GetDayFraction()` public → the Haugbui starts returning at a configurable fraction before 0.25. | [V] |
| World time | `ZNet.GetTimeSeconds()` persisted in the world save (`m_netTime`), restored on load; advances only while the world runs; sleeping skips to morning. | [V] |

### 8.1 Resurrection cooldown: real time vs world time

The requirement says "one real-world hour ≈ two Valheim days". These are only equal **while the game runs** (1800 s/day).

| Clock | Pros | Cons |
|---|---|---|
| **World time** (`ZNet.GetTimeSeconds`) — recommended | Consistent with the game's own timers (smelters, beehives); survives saves; immune to PC clock changes; multiplayer-consistent; matches "≈ 2 Valheim days" | Does not advance while the game is closed; sleeping advances it ~9 game-min in seconds |
| Real time (`DateTime.UtcNow`) | Advances while the game is closed | Manipulable via system clock; diverges per machine in multiplayer; inconsistent with in-game time |

**Decision (2026-10-13, product owner): world time.** Store `deathTime` and `cooldownSeconds` (default 3600 world
seconds) in the tomb ZDO. No real-time option in v1.

---

## 9. Persistence and world safety (critical)

### 9.1 What the game does with mod data when the mod is missing

| Data | Behaviour without the mod | Label |
|---|---|---|
| ZDO of a **custom prefab** (custom piece/creature) | Kept on load ("Found N ZDOs with unknown prefabs. Will load anyway", `ZDOMan.cs:423`) **but destroyed by the host/server as soon as its zone is instantiated** ("Destroyed invalid prefab ZDO", `ZNetScene.cs:225-234`). Next save of that chunk drops it **permanently**. Zones never visited keep it. | [V] |
| **Custom ZDO keys on a vanilla prefab** (e.g. keys we add to a smelter) | Preserved: `ZDO.Save` serialises every key, no filtering. | [V] (`ZDO.cs` save path) |
| **Custom item prefabs inside a container** | Skipped on load ("Failed to find item prefab", `Inventory.cs:966`); lost the next time that container is saved by its owner. | [V] / [I] |
| **Non-persistent ZDOs** | Never saved (`ZDOMan.cs:1638` filters `Persistent`). | [V] |
| Global keys (`ZoneSystem.SetGlobalKey`) | Unknown keys stored as `NonServerOption` strings and saved with the world. | [V] / [I] |
| Blocking world load without the mod | **Impossible**: a plugin cannot run code when it is not loaded. | — |

### 9.2 Mitigation strategy (recommended): host prefabs + marker keys

1. **No custom items.** All recipes use vanilla items, so no container can ever hold an unknown item.
2. **The Haugbui creature is non-persistent.** It is a runtime projection of its tomb: HP, carried inventory, death time
   and cooldown live in the **tomb's ZDO**. Nothing about the creature is ever saved, so nothing can be destroyed.
3. **Every Haugbui structure persists as a vanilla prefab** ("host") carrying a marker key (`haugbui_kind`) and all its
   state in custom ZDO keys. The build-menu piece is a Jötunn custom prefab used only as the placement ghost; on placement
   it is immediately replaced by the host prefab (same position/rotation/creator). When the mod is loaded, a Harmony
   postfix on the host's `Awake` recognises the marker and attaches Haugbui behaviour and visuals.
   - Collection/Delivery chests → host `piece_chest_wood` (10 slots natively).
   - Tomb → a vanilla container host (its container is the tomb's protected storage / overflow).
   - Upgrades → an inert vanilla decorative host chosen in the spike.
   - Without the mod: they appear as ordinary vanilla objects; **items and all Haugbui keys are kept**; reinstalling the
     mod restores everything.
   - With the mod: the `Awake` postfix **changes the appearance** — it disables the host's renderers, attaches the
     Haugbui model (kitbash/asset bundle), adjusts colliders, name and hover text. The host only defines what the
     object degrades to; players never see it while the mod runs. Constraint: the host's ZDO prefab hash and
     component set stay (e.g. a `Container`), so the host must be chosen to be compatible with the Haugbui role.
4. **World marker:** global key `haugbui <version>` plus a tomb count; on load, the mod compares the marker with the tombs
   it finds and shows a prominent warning if structures disappeared. The scan is `ZDOMan.m_objectsByID`. On 1.0.20
   `LoadChunks` still loads every chunk into that dictionary, so a tomb whose zone is not instantiated is still counted.
5. **Backups:** Valheim keeps its own automatic backups (folder per backup in 1.0 [W]). The mod's opt-in copy uses
   `World.GetSaveDirectory(FileHelpers.FileSource)` (1.0.20), which is `worlds_local/<name>/` for a local world, and
   refuses to copy while `ZNet.IsSaving()` is true. Cloud and legacy saves are skipped.

**Spike HB-T-007 (2026-10-13, Windows, 1.0.17) [V]:** a vanilla `piece_chest_wood` tagged with `haugbui_kind` and
`haugbui_guid` got its new look from a `Container.Awake` postfix on every load (log: "applied look … 5 meshes copied"),
kept its 5 items and the same GUID across save/quit/reload, and loaded as a plain chest without the DLL (reported by
the product owner). The copied tombstone also showed its floating raft (`TombStone.m_floater`); fixed by skipping that
subtree. Not covered yet: placement swap from the placeholder piece and deconstruction refund (M2, HB-T-014).

**Feasibility status: [V] go for the appearance and survival part; placement flow still [I]** (ticket M1). Risks: host-prefab swap on placement, deconstruction refunds
(host refunds vanilla cost; patch for marked pieces), other mods treating hosts as normal chests. **Fallback:** ordinary
Jötunn custom prefabs + world marker + backups + documented risk (structures and their contents are lost if the world
is opened and played near them without the mod).

### 9.3 Exactly what is and is not protected (with the recommended strategy)

| Protected | Not protected |
|---|---|
| Structures (as vanilla objects), their containers and items, all Haugbui keys (levels, toggles, cooldown, requests, jobs), carried inventory (in tomb ZDO), station automation settings | Visuals/behaviour while the mod is missing; vanilla deconstruction refunds only the host's vanilla cost; if a player deconstructs a host without the mod, the tomb state is gone (items fall out normally) |
| Worlds saved without the mod, then reopened with it | Anything another mod deletes; manual edits; Valheim save corruption bugs (e.g. fixed in 1.0.17 "chunk splitting could remove all objects") |

The in-process missing-mod simulation (augmentation suppressed, then the object reloaded from its ZDO) covers every
structure, a tomb that holds items, a delivery request and a station job. The on-disk round trip — quit, remove
`Haugbui.dll`, load, save, put the DLL back — is still the product owner's observation. This table stays the
host-prefab design until that observation is written in. Do not treat the simulation as that observation.

### 9.4 Loaded area limits the operational radius

Objects only exist as GameObjects inside the simulation area around the player, a graphics setting (0–6, default 2 =
5×5 zones of 64 m → **≥128 m around the player**; level 0 only ≥64 m) [V, `SimulationDistance.cs`]. Consequences:

- GameObjects (and therefore the physical Haugbui) exist only near the player. Same limitation as all prior-art mods.
- With the player standing at the tomb, a 100 m radius is covered at the default setting, **not** at the lowest setting.
- The Haugbui must treat unloaded containers/stations as unavailable, never as empty.

#### 9.4.0 Sleeping (time skip) [V]

`Game.UpdateSleeping` (Game.cs:963) calls `EnvMan.SkipToMorning` (EnvMan.cs:977) when everyone is in bed in the
afternoon or at night. The skip is not a jump: `UpdateTimeSkip` advances `ZNet` time by `dt × m_timeSkipSpeed` each
fixed update, with `m_timeSkipSpeed = remaining / 12`, so the skip lasts ~12 real seconds and ends at day fraction
**0.15** — still night (`CalculateNight`: fraction ≥ 0.75 or ≤ 0.25). Objects stay loaded (the player is in bed at the
base) and vanilla stations process the elapsed time during those frames. `EnvMan.IsTimeSkipping()` is public.
→ A sleep **fast mode** is feasible: while skipping, the Haugbui executes tasks without flight, consuming a budget of
simulated time from the world-time delta of each frame (PRD HB-046). Harvesting and output collection work too,
because the objects exist. Approximation: work is spread over 12 s instead of the real night, so station queues
refilled late in the skip process less than they would have.

#### 9.4.1 Working while the player is away

Verified facts [V]:
- **All ZDOs of the world are in memory** once the world loads (`ZDOMan.LoadChunks` loads every chunk into the
  dictionary and sectors). Only GameObject instantiation is limited to the simulation area.
- **Changing any ZDO marks its chunk dirty for saving**, loaded or not (`ZDO.IncreaseDataRevision` →
  `ZDOMan.SetDirtySector`, `ZDOMan.cs:760`).
- Container contents are pure ZDO data (`s_items` byte[]), so they can be read/written without instantiation.
- The whole engine is built around **one reference position per peer** (`ZNet.GetReferencePosition` drives
  `ZoneSystem.CreateLocalZones`, `ZNetScene.CreateDestroyObjects`, `ZDOMan.ReleaseNearbyZDOS`, `SlowUpdater`).
- Vanilla stations and crops do **not** progress while unloaded; they catch up from timestamps when loaded again
  (smelter accumulator capped at 3600 s; plants/beehives from stored times).

| Option | What it gives | Cost / risk | Verdict |
|---|---|---|---|
| A. Keep the base loaded ("chunk loader" anchored on the tomb) | Physical Haugbui works anywhere | Patching zone creation, object creation/removal, ZDO ownership and slow updates for a second centre; terrain must be generated; doubles loaded area (CPU/physics); fragile across game updates | **Rejected for v1** (possible research item for a future dedicated-server version) |
| B. **Remote work on world data** ("away mode") | When the tomb is unloaded, the tomb runs an abstract worker on ZDO data: sorts tomb storage and Collection Chests into storage chests, fulfils Delivery Chest requests, tops up smelter/kiln queues and fuel within capacity | Must replicate a few private vanilla writes (e.g. smelter queue keys) and keep the same transfer rules; travel time simulated from distance/speed; night/level rules still apply | **Recommended** as a separate milestone (M12), spike in M1 |
| C. Do nothing | — | Servant idle while away | Baseline |

**Spike HB-T-090 (2026-10-13) [V]:** with the player far away (`goto`), +5 Resin were written to an unloaded chest's
`s_items` and 2 CopperOre + 4 Coal to an unloaded smelter's queue/fuel keys, then saved. Back at the base the chest
held the 5 Resin, and the smelter had consumed exactly 2 ore and 4 coal (2 products × 2 fuel), i.e. it caught up and
processed the remote queue. **Go** for away mode. Scan timings not measured yet.

Not possible remotely without bypassing game mechanics (so not done): harvesting crops/hives/nests and collecting
station outputs — those require the objects to exist (drops are spawned as ground items), and the game itself
freezes them while away and catches up on return. When the player returns, the physical Haugbui collects the
accumulated outputs and harvests as usual.

---

## 10. UI and localization

| Topic | Finding | Label |
|---|---|---|
| Panels | Jötunn `GUIManager`: `CreateWoodpanel/Button/Text/InputField/Toggle/DropDown/ScrollView`, `CustomGUIFront`, `BlockInput(bool)`, `OnCustomGUIAvailable`. UGUI preferred over IMGUI. | [W] |
| Interaction | `Interactable.Interact(Humanoid, bool hold, bool alt)`, `Hoverable` (3 members in 1.0). Alt-interact = `$KEY_AltPlace` (Shift+E on keyboard). | [V] |
| Smelters | **Not `Interactable`**: interactions live on child `Switch` components (`m_onUse`/`m_onHover`). Automation entry point must patch `Switch` hover/interact for station switches. `CookingStation`, `Fermenter` are `Interactable`. | [V] |
| Popups | `UnifiedPopup.Push(new YesNoPopup(header, text, yes, no))`, `WarningPopup`. | [V] |
| Messages | `MessageHud.instance.ShowMessage(MessageType.Center/TopLeft, text, ...)`, `Player.Message(...)`. | [V] |
| Floating text | `Chat.SetNpcText` recreates the bubble each call → unsuitable for countdowns. Use a world-space TMP label + `Billboard`, updated ≤1 Hz. | [V] / [I] |
| Inventory UI | `InventoryGui.Show(Container)` does no ownership handshake — use the request flow. | [V] |
| Languages | `Portuguese_Brazilian`, `English` (constants in `assembly_guiutils/Localization.cs`). Missing key renders `[key]`. Jötunn loads `Translations/<Language>/*.json`, falls back to English. | [V] / [W] |

All feasible.

---

## 11. Prior art and lessons

No mod named "Haugbui" exists (Thunderstore full index, Nexus API) [W]. Closest: **Cheb's Necromancy** (worker
undead, pylons), **HiredHands** (Steward that runs stations), **Automatics**, **AzuAutoStore** ("must have existing
item"), **MultiUserChest** (owner-arbitrated chest transfers), **Hearthfolk** (settler logistics).

Documented failure modes we must design out:
- Item duplication/loss when editing a container you do not own, or two actors editing the same chest (AzuAutoStore,
  ServersideQoL, ValheimPlus). → owner-checked, single-frame transfers; skip containers in use.
- Lost assignments because runtime object IDs change on reload (Hearthfolk). → persist by own GUID in ZDO keys.
- Multi-second freezes from pathfinding/scans (Hearthfolk, Vikings For Hire). → no pathfinding, cached registries,
  time-sliced scans.
- Full destination chest blocking all workers. → per-item retry/backoff, alternative destinations.
- Navmesh failures inside bases are the #1 pain of worker mods. → incorporeal flight sidesteps it entirely.

**Valheim Companions – Dvergr Tidier** (Nexus 4409, released 2026-10-12; mod page text provided by the product
owner, not tested) [W]. Closest in scope: a dvergr that sorts chests within 20 m of a payment chest, BepInEx only
(no Jötunn), rescan every 30 s. What we take from it:
- "Destination before removal" for every move, and carried items saved to a chest when interrupted → adopted
  (ARCHITECTURE §7, PRD HB-044/HB-045).
- A dry-run command (`dsort plan`) and an orphan-creature cleanup (`dsort cleanup`) → `haugbui_plan`,
  `haugbui_cleanup` (HB-T-010).
- It walks physically on its **own walkable grid** (stairs, doors, background map build, teleport back when stuck)
  instead of Valheim pathfinding, with known limits (no ladders, portals or gaps; large radius slows map builds).
  This confirms that physical movement in bases needs a dedicated navigation system — the work incorporeal flight
  avoids.
- Untested compatibility with container-size, auto-stack and craft-from-container mods → same note for our README.
- Not adopted: per-stack coin cost, chest locking by contents (our rule is "existing item", PRD §8.1). Possible
  later: a sign tag that excludes a chest (its `#nosort`).

---

## 12. Requirement feasibility matrix

| # | Requirement | Verdict | Notes / alternative |
|---|---|---|---|
| 3.1 | Tomb from player tombstone | **Feasible with change** | Reuse tombstone *mesh*, not the `TombStone` component (self-destroys when empty). |
| 3.1 | Within 50 m of own bed | Feasible | Placement-ghost validation. |
| 3.1 | Multiple Haugbuis, no conflicts | Feasible | Central reservation service; minimum tomb spacing (config, default 5 m) only to avoid visual overlap. |
| 3.2 | 6 levels via physical upgrades, workbench-like | Feasible | Custom level counter (not `StationExtension`); upgrades in any order (decision 2026-10-13). |
| 3.3–3.7 | Upgrade ingredients | Feasible | All required items exist as vanilla prefabs. |
| 3.6 / 14 | Combat + warning + default-off toggle | Feasible | Custom target filter; `YesNoPopup` warning. |
| 3.7 | Altar visuals | Feasible | Kitbashed VFX; performance-capped particles. |
| 4 | Attribute table | Feasible | Centralised progression definition + config. |
| 4 | 1 m interaction distance | Feasible | Distance to the target's interaction point (collider closest point), not pivot. |
| 5 | Night-only without the Altar, weather-independent | Feasible | `IsDay/IsNight`, never `IsDaylight`. |
| 6 | Pass through walls, float | **Feasible with custom movement** | [I] collision exclusion to validate in spike. |
| 7 | Death, cooldown, countdown, no item loss | Feasible | State in tomb ZDO; world-time cooldown (open question). |
| 8 | Storage rule (existing item) | Feasible | — |
| 9/10 | Collection/Delivery chests (10 slots) | Feasible | Host `piece_chest_wood` (10 slots) [U data]. |
| 11 | Ground collection + toggle | Feasible | `ItemDrop` registry + reservations. |
| 12 | Crops, beehives, **bird nests** | Feasible | Bird nest exists in 1.0 (`piece_birdnest`, Beehive component). |
| 13 | Production automation | Feasible for Smelter family, Fermenter, ShieldGenerator (fuel), CookingStation (with dawn-aware loading to avoid burning), Incinerator **excluded**; recipe crafting at any crafting station out of v1. | Outputs are ground drops. "Unlocked recipes" = conversion list + known materials. |
| 13.5 | Unlock level | **Level 2** (decision 2026-10-13, configurable). | |
| 15 | Multi-Haugbui coordination | Feasible | Lease-based reservations, persistent job ownership. |
| 16 | Wards/ownership | Feasible (SP); designed for MP | `IAccessPolicy`. |
| 17 | Tomb management UI | Feasible | Jötunn GUI. |
| 18 | Persistence | Feasible | ZDO keys; see §9. |
| 18.1 | Missing-mod protection | **Partially feasible** | Cannot block loading; host-prefab strategy protects data [I spike]. |
| 19/20 | Config, pt-BR/en-US | Feasible | — |
| 21 | Performance | Feasible | Registries, time-slicing, staggered ticks. |
| — | Operational radius up to 100 m | **Limited** | Physical work only within loaded zones (≥128 m default, ≥64 m minimum setting). |
| — | Working while the player is away | **Partially feasible** | Remote logistics on ZDO data (§9.4.1 option B); no remote harvesting/output collection. |
| — | Multiplayer (future) | Feasible later | Owner-based execution; RPC paths with `Player.m_localPlayer` need workarounds on dedicated servers. |

---

## 13. Critical technical risks

| Risk | Impact | Mitigation |
|---|---|---|
| R1 Host-prefab strategy fails (placement swap, Awake augmentation) | Missing-mod protection weaker | Spike in M1; fallback to custom prefabs + marker + backups. |
| R2 Item duplication/loss in transfers | Save corruption, player trust | Single-frame transfers, owner checks, in-use checks, invariants test suite, debug ledger. |
| R3 Wall pass-through physics | Creature stuck / falls through terrain | Kinematic movement, altitude ≥ ground + margin, spike in M3. |
| R4 Jötunn 2.30.2 vs Valheim 1.0.17 | Build/load failure | Smoke test in M1; pin versions; watch Jötunn releases. |
| R5 Loaded-area limits | Haugbui ignores far targets | Clamp effective radius to loaded area; UI hint. |
| R6 Cooking stations burning food | Resource loss | Dawn-aware loading (no slot loaded that cannot be collected before the Haugbui goes in) + strict pickup priority; panel warns that manual items may burn. |
| R7 Performance with many containers/items | Frame drops | Registries, spatial filtering, time-sliced planning, config intervals. |
| R8 Game updates renaming RPCs/fields (as 1.0 did) | Breakage | Isolate all game calls in `Integration/` adapters; RPC names as constants; startup self-check that logs missing members. |
| R9 Compatibility with other logistics mods (AzuAutoStore, MultiUserChest, Automatics) | Duplicate moves | Mark Haugbui chests with an ignore flag (`MUC_Ignore`), document incompatibilities. |
