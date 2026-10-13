<div align="center">

# ᚺ Haugbui

**Summon an undead servant that helps with the boring tasks in your base.**

*A Valheim mod: build a burial tomb and a spectral mound-dweller rises to sort your chests, fetch what you ask for,
harvest your fields and keep your smelters running — while you go adventuring.*

![Valheim](https://img.shields.io/badge/Valheim-1.0.17-2f4f4f)
![BepInEx](https://img.shields.io/badge/BepInEx-5.4.23-6a5acd)
![Jötunn](https://img.shields.io/badge/J%C3%B6tunn-2.30.2-8b0000)
![Platform](https://img.shields.io/badge/platform-Windows-0078d6)
![Status](https://img.shields.io/badge/status-pre--alpha-orange)

</div>

> [!WARNING]
> **Pre-alpha — not playable yet.** Version 0.0.1 contains the project foundation (build, configuration,
> localization, diagnostics) and the technical spikes that de-risk the design. The gameplay described below is the
> planned design, not shipped features.

---

## The legend

In Norse lore, a *haugbúi* is the dead one who dwells inside his burial mound, guarding what was buried with him.
Build him a tomb near your bed, honour him with offerings, and he will serve your household from beyond the grave.

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
| M2 | Tomb, upgrades, levels, persistence | 🔨 in progress |
| M3 | The servant: flight, day/night, death and resurrection | ⏳ |
| M4–M6 | Storage sorting, Collection/Delivery chests, ground items, harvesting | ⏳ |
| M7 | Production automation, sleep fast mode | ⏳ |
| M8–M11 | Combat, world safety, full UI, performance, release | ⏳ |
| M12 | Away mode (release 1.1) | ⏳ |

The full plan lives in [`docs/`](docs):
[requirements](docs/PRODUCT_REQUIREMENTS.md) ·
[architecture](docs/ARCHITECTURE.md) ·
[technical feasibility](docs/TECHNICAL_FEASIBILITY.md) ·
[backlog](docs/IMPLEMENTATION_BACKLOG.json) ·
[development guide](docs/DEVELOPMENT_GUIDE.md)

## Requirements

- Valheim **1.0.17** (Windows)
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
| `haugbui_spike_*` | Debug builds only: technical spikes (see the development guide) |

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
