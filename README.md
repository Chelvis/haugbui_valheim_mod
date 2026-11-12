<div align="center">

# ᚺ Haugbui

**Summon an undead servant that helps with the boring tasks in your base.**

*A Valheim mod: build a burial tomb and a spectral mound-dweller rises to sort your chests, fetch what you ask for,
harvest your fields and keep your smelters running — while you go adventuring.*

![Valheim](https://img.shields.io/badge/Valheim-1.0.20-2f4f4f)
![BepInEx](https://img.shields.io/badge/BepInEx-5.4.23-6a5acd)
![Jötunn](https://img.shields.io/badge/J%C3%B6tunn-2.30.2-8b0000)
![Platform](https://img.shields.io/badge/platform-Windows-0078d6)
![Status](https://img.shields.io/badge/status-pre--alpha-orange)

</div>

> [!NOTE]
> Version **0.0.13** passed its self-test and is not tagged yet. Tag `v0.0.12` stays where it is.
> Leia em português: [README.pt-BR.md](README.pt-BR.md).

---

## The legend

In Norse lore, a *haugbúi* is the dead one who dwells inside his burial mound, guarding what was buried with him.
Build him a tomb near your bed, honour him with offerings, and he will serve your household from beyond the grave.

Once a Haugbui binds itself to you, there is no getting rid of it: it joins you to prove its worth and become
worthy of Valhalla, no longer bound to its tomb or the earthly world. If its tomb is destroyed, it returns as soon as
a new one is built.

He is not a walking corpse: he is a translucent, floating spirit that drifts through walls and doors. By night he
works. At dawn he returns to his mound — unless you give him reason to walk in daylight.

## What he will do

| | Task | How it works |
|---|---|---|
| 📦 | **Sort your storage** | Puts items into chests that **already hold that item** — empty chests are never touched, so your layout stays yours. |
| 🧺 | **Collection Chest** | Drop anything into it; he puts it away. |
| 📜 | **Delivery Chest** | Ask for *"40 Wood, 20 Resin"*; he gathers it from your chests and brings it. |
| 🌾 | **Ground items & harvest** | Picks up loose items, harvests ripe crops, empties beehives and birds' nests. |
| 🔥 | **Production** | Feeds smelters, kilns, the windmill, spinning wheel, eitr refinery, fermenters and cooking stations — with finite ("make 100 iron") or endless jobs. |
| ⚔️ | **Defence** *(opt-in)* | Given his king's sword, he defends his burial place. |
| 🌙 | **Sleep** | Works through the night you skip by sleeping. Work while you are far from the base is not in this version. |

He is not free: each shift is paid with a **tribute** of coins, amber or gems offered at his tomb — only
when there is work to do.

He never creates or destroys items, never touches equipment (weapons, armour, tools), respects wards and private
chests, and puts every item in its destination *before* removing it from its source.

## Raising the tomb

The tomb is built from Black Forest burial-chamber loot and must stand near your bed. Five upgrades — **built in any
order** — raise its level and strengthen the servant:

| Upgrade | Biome | Grants |
|---|---|---|
| 🍖 **Offering** — roasted boar meat, honey, thistle | Black Forest | +1 level |
| 💰 **Personal Treasure** — coins, rubies, amber, iron | Swamp | +1 level |
| 💍 **Silver Bracelets** — silver arm rings | Mountains | +1 level |
| 🗡️ **Fallen King's Longsword** — black metal | Plains | +1 level, **combat** |
| 🔮 **Altar of Darkness** — eitr, black marble | Mistlands | +1 level, **works in daylight**, dark aura |

Each level raises carry weight, speed, work radius, inventory slots and health. Production unlocks at level 2.
All costs and numbers are configurable.

## Install

1. Install [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) 5.4.2351 and
   [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/) 2.30.2 with r2modman (or another BepInEx manager).
2. Install Haugbui the same way, or copy the Release `Haugbui` folder into `BepInEx\plugins`.
3. Start the game. The hammer gains a **Haugbui** tab.

Normal play needs no config edits. The optional in-game editor is
[Configuration Manager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/) (F1). It is not included.

## Safe for your world

Structures are saved as ordinary vanilla pieces. The mod changes how they look and stores its own data in extra
keys on those pieces. It adds no custom items. The servant is not saved as a creature; his state lives on the tomb.
Opening the world without the DLL keeps the pieces, their containers and the items inside them. The servant, the
custom look and the automation are absent until the DLL is back. A warning appears on load when tombs that the
world recorded are missing.

Quitting, removing the DLL, loading, saving and putting the DLL back has **not** been done on disk yet. Until that
round trip is observed, treat a backup as the restore path. The opt-in world-folder backup is off by default
(`BackupWorldOnLoad`).

| Kept without the DLL | Lost without the DLL |
|---|---|
| The vanilla piece, its container and items | The servant, the custom look, the automation |
| Haugbui keys on that piece (level, toggles, requests, jobs, carried items) | Those keys, if you deconstruct the piece while the mod is missing |

## How far he can work

He only touches objects that are loaded near you. At the default simulation distance that is about 128 m. At the
lowest setting it is about 64 m. A larger work radius does not reach unloaded chests; the tomb panel says when the
radius is only partly loaded. He does not keep working in a base you have left. That is a later version.

## Other logistics mods

Do not let another mod move the same items. AzuAutoStore, MultiUserChest and Automatics can take a stack in the
same moment and duplicate or lose it. Give those mods a different set of chests, or leave them off.

## Roadmap

| Milestone | Scope | Status |
|---|---|---|
| M1 | Foundation, configuration, localization, diagnostics, technical spikes | ✅ (0.0.1) |
| M2 | Tomb, upgrades, levels, persistence | ✅ (0.0.2) |
| M3 | The servant: flight, day/night, death and resurrection | ✅ (0.0.3) |
| M4 | Inventory, safe transfers, storage sorting, reservations between Haugbuis | ✅ (0.0.4) |
| M5 | Collection and Delivery chests, request editor; moved items never become cheated | ✅ (0.0.5) |
| M6 | Ground items, harvesting, tribute per shift, servant slots, death and new-tomb timing | ✅ (0.0.6) |
| M7 | Production automation, sleep fast mode, territories, optional strict item data | ✅ (0.0.7) |
| M8 | Combat: defend the radius, level scaling, flee home when combat is off | ✅ (0.0.8) |
| M9 | World safety: marker and warning, opt-in backup, blob integrity, save/reload checks | ✅ (0.0.9) |
| M10 | Tomb panel, hovers, chest filter, localization, structure looks and build-menu icons | ✅ (0.0.10) |
| M11 | Carry cycle, timed bag emptying, chest-list copy, object damage ignored | ✅ (0.0.11) |
| M12 | Away mode: an opted-in tomb keeps its area loaded | ✅ (0.0.12) |
| M13 | Material piles, keep-lit fires, finite Frigid kiln | ✅ (0.0.13, self-test 2026-11-12) |
| 1.1 | Shift billing: emerge only on the boundary, or pay for the time left | ⏳ after v1.0 |
| 1.2 | Task schedules: collect a batch, then deliver it | ⏳ after v1.0 |

The full plan lives in [`docs/`](docs):
[requirements](docs/PRODUCT_REQUIREMENTS.md) ·
[architecture](docs/ARCHITECTURE.md) ·
[technical feasibility](docs/TECHNICAL_FEASIBILITY.md) ·
[backlog](docs/IMPLEMENTATION_BACKLOG.json) ·
[development guide](docs/DEVELOPMENT_GUIDE.md)

Contributors and AI agents: start with [`AGENTS.md`](AGENTS.md) and the [handover](docs/HANDOVER.md).

## Requirements

- Valheim **1.0.20** (Windows)
- [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) 5.4.2351
- [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/) 2.30.2

Single-player first. Multiplayer is out of scope for 1.0, but nothing in the design rules it out.

## Building from source

```powershell
git clone https://github.com/Chelvis/haugbui_valheim_mod.git
cd haugbui_valheim_mod
copy Environment.props.example Environment.props   # set VALHEIM_INSTALL and MOD_DEPLOYPATH
dotnet build Haugbui.sln -c Debug                   # Debug builds deploy to MOD_DEPLOYPATH
dotnet test tests/Haugbui.Core.Tests                # domain tests, no game needed
```

Game assemblies are referenced from your Valheim install and publicized at build time; nothing from the game is
stored in this repository. See the [development guide](docs/DEVELOPMENT_GUIDE.md) for details, including building
on macOS.

## Console commands

Open the console with **F5** (launch with `-console`).

| Command | What it does |
|---|---|
| `haugbui_status` | Plugin, game API and recipe validation status |
| `haugbui_dump_prefabs` | Writes station, piece, container, item and recipe data to `BepInEx/haugbui_prefabs.json` |
| `haugbui_dump_tomb` | Prints the nearest tomb's saved state, level and bound upgrades |
| `haugbui_reservations` | Task reservations and each Haugbui's current task, carried items and waiting item kinds |
| `haugbui_ledger` | The last item transfers (source and destination counts before/after) |
| `haugbui_registry` | World registry counts compared with a full scene scan |
| `haugbui_selftest` | Debug builds only: in-game integration tests with a report and pictures (see the development guide). Optional `and_exit` closes the game when the report is written |
| `haugbui_spike_*` | Debug builds only: technical spikes |

## Languages

English and Brazilian Portuguese (Português do Brasil). Translations live in
[`src/Haugbui/Translations`](src/Haugbui/Translations); more languages are welcome once the mod is playable.

## Contributing

The project is not open to contributions yet. Once a tested, playable version exists, issues and pull requests will
be welcome.

## Credits

- [BepInEx](https://github.com/BepInEx/BepInEx) and [Jötunn](https://github.com/Valheim-Modding/Jotunn), the modding
  foundations this mod stands on.
- *Valheim Companions – Dvergr Tidier*, whose "destination first, never lose an item" rule Haugbui adopts.

Haugbui is a fan project, not affiliated with or endorsed by Iron Gate AB or Coffee Stain.
