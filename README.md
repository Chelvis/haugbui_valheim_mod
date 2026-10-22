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

> [!WARNING]
> **Pre-alpha — not playable yet.** Version 0.0.9 contains the project foundation, the tomb and its five upgrades
> (with placeholder looks), the Haugbui itself (emerges at night, returns at dawn, dies and returns at a later dusk,
> tomb panel with toggles), its jobs — sorting its tomb storage and Collection Chests into chests that already
> hold the same item, bringing requested items to Delivery Chests, picking up ground items, harvesting ripe crops,
> beehives and birds' nests, running simple production stations (smelters, kilns, windmill, spinning wheel, eitr
> refinery, fermenter, ovens and cooking stations, fuel for hot tubs and shield generators) and working while the
> player sleeps — the tribute it asks for each shift, permanent servant slots per player with territories, opt-in
> combat with the Longsword (it defends its radius, or flees to the tomb when combat is off), a world marker that
> warns when tombs disappear, an opt-in world-folder backup (off by default), and an in-game
> self-test. The gameplay described below is the planned design, not shipped features.

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
| 🌙 | **Sleep & away** | Works through the night you skip by sleeping, and keeps your base logistics running while you are far away. |

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

## Safe for your world

Removing a mod should not cost you your base. Haugbui is designed so that a world opened without it keeps
everything: its structures are saved as ordinary vanilla objects (the mod only changes how they look), it adds no
custom items, and the servant itself is never saved — his state lives in the tomb. A warning appears on load if
anything Haugbui-related is missing.

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
| M10–M11 | Full UI, performance, release | ⏳ |
| M12 | Away mode (release 1.1) | ⏳ |

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
