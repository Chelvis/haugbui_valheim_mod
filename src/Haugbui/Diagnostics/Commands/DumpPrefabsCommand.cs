using System.IO;
using System.Linq;
using Haugbui.Core.Diagnostics;
using Haugbui.Core.Recipes;
using UnityEngine;

namespace Haugbui.Diagnostics.Commands
{
    /// <summary>
    /// Writes the prefab data the planning docs cannot get from code (asset values such as capacities, conversions and
    /// recipes) to BepInEx/haugbui_prefabs.json. Read-only.
    /// </summary>
    internal sealed class DumpPrefabsCommand : HaugbuiCommand
    {
        public const string FileName = "haugbui_prefabs.json";

        public override string Name => "haugbui_dump_prefabs";
        public override string Help => "Haugbui: write station, container, item and recipe data to BepInEx/" + FileName;

        protected override void Execute(string[] args)
        {
            if (ObjectDB.instance == null || ZNetScene.instance == null || ObjectDB.instance.m_items.Count == 0)
            {
                Print("Load a world first.");
                return;
            }

            var json = new JsonWriter().BeginObject();
            json.Property("game", Version.GetVersionString());
            json.Property("haugbui", HaugbuiPlugin.PluginVersion);
            WriteItems(json);
            WriteStations(json);
            WriteRecipes(json);
            WriteHaugbuiNames(json);
            json.EndObject();

            string path = Path.Combine(BepInEx.Paths.BepInExRootPath, FileName);
            File.WriteAllText(path, json.ToString());
            Print($"Wrote {path}");
        }

        private static void WriteItems(JsonWriter json)
        {
            json.Name("items").BeginArray();
            foreach (GameObject prefab in ObjectDB.instance.m_items.Where(p => p != null).OrderBy(p => p.name))
            {
                ItemDrop drop = prefab.GetComponent<ItemDrop>();
                if (drop == null) continue;
                ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
                json.BeginObject()
                    .Property("prefab", prefab.name)
                    .Property("token", shared.m_name)
                    .Property("name", Localization.instance.Localize(shared.m_name))
                    .Property("type", shared.m_itemType.ToString())
                    .Property("equipable", drop.m_itemData.IsEquipable())
                    .Property("weight", shared.m_weight)
                    .Property("maxStack", shared.m_maxStackSize)
                    .Property("teleportable", shared.m_teleportable)
                    .EndObject();
            }
            json.EndArray();
        }

        private static void WriteStations(JsonWriter json)
        {
            var prefabs = ZNetScene.instance.m_prefabs.Where(p => p != null).OrderBy(p => p.name).ToList();

            json.Name("smelters").BeginArray();
            foreach (GameObject prefab in prefabs)
            {
                Smelter s = prefab.GetComponent<Smelter>();
                if (s == null) continue;
                json.BeginObject()
                    .Property("prefab", prefab.name)
                    .Property("name", s.m_name)
                    .Property("fuel", s.m_fuelItem != null ? s.m_fuelItem.name : null)
                    .Property("maxOre", s.m_maxOre)
                    .Property("maxFuel", s.m_maxFuel)
                    .Property("fuelPerProduct", s.m_fuelPerProduct)
                    .Property("secPerProduct", s.m_secPerProduct)
                    .Property("spawnStack", s.m_spawnStack)
                    .Property("requiresRoof", s.m_requiresRoof)
                    .Property("windmill", s.m_windmill != null);
                json.Name("conversions").BeginArray();
                foreach (Smelter.ItemConversion c in s.m_conversion)
                    json.BeginObject().Property("from", PrefabName(c.m_from)).Property("to", PrefabName(c.m_to)).EndObject();
                json.EndArray().EndObject();
            }
            json.EndArray();

            json.Name("fermenters").BeginArray();
            foreach (GameObject prefab in prefabs)
            {
                Fermenter f = prefab.GetComponent<Fermenter>();
                if (f == null) continue;
                json.BeginObject()
                    .Property("prefab", prefab.name)
                    .Property("name", f.m_name)
                    .Property("duration", f.m_fermentationDuration);
                json.Name("conversions").BeginArray();
                foreach (Fermenter.ItemConversion c in f.m_conversion)
                {
                    json.BeginObject().Property("from", PrefabName(c.m_from)).Property("to", PrefabName(c.m_to))
                        .Property("produced", c.m_producedItems).EndObject();
                }
                json.EndArray().EndObject();
            }
            json.EndArray();

            json.Name("cookingStations").BeginArray();
            foreach (GameObject prefab in prefabs)
            {
                CookingStation c = prefab.GetComponent<CookingStation>();
                if (c == null) continue;
                json.BeginObject()
                    .Property("prefab", prefab.name)
                    .Property("name", c.m_name)
                    .Property("slots", c.m_slots != null ? c.m_slots.Length : 0)
                    .Property("useFuel", c.m_useFuel)
                    .Property("fuel", c.m_fuelItem != null ? c.m_fuelItem.name : null)
                    .Property("maxFuel", c.m_maxFuel)
                    .Property("requireFire", c.m_requireFire)
                    .Property("overcooked", PrefabName(c.m_overCookedItem));
                json.Name("conversions").BeginArray();
                foreach (CookingStation.ItemConversion conv in c.m_conversion)
                {
                    json.BeginObject().Property("from", PrefabName(conv.m_from)).Property("to", PrefabName(conv.m_to))
                        .Property("cookTime", conv.m_cookTime).EndObject();
                }
                json.EndArray().EndObject();
            }
            json.EndArray();

            json.Name("beehives").BeginArray();
            foreach (GameObject prefab in prefabs)
            {
                Beehive b = prefab.GetComponent<Beehive>();
                if (b == null) continue;
                json.BeginObject()
                    .Property("prefab", prefab.name)
                    .Property("name", b.m_name)
                    .Property("item", PrefabName(b.m_honeyItem))
                    .Property("max", b.m_maxHoney)
                    .Property("secPerUnit", b.m_secPerUnit)
                    .EndObject();
            }
            json.EndArray();

            json.Name("craftingStations").BeginArray();
            foreach (GameObject prefab in prefabs)
            {
                CraftingStation c = prefab.GetComponent<CraftingStation>();
                if (c == null) continue;
                json.BeginObject().Property("prefab", prefab.name).Property("name", c.m_name).EndObject();
            }
            json.EndArray();

            json.Name("pieces").BeginArray();
            foreach (GameObject prefab in prefabs)
            {
                Piece piece = prefab.GetComponent<Piece>();
                if (piece == null) continue;
                json.BeginObject()
                    .Property("prefab", prefab.name)
                    .Property("token", piece.m_name)
                    .Property("category", piece.m_category.ToString())
                    .Property("station", piece.m_craftingStation != null ? piece.m_craftingStation.name : null);
                json.Name("components").BeginArray();
                foreach (Component component in prefab.GetComponents<Component>())
                {
                    if (component != null && !(component is Transform)) json.Value(component.GetType().Name);
                }
                json.EndArray();
                json.Name("resources").BeginArray();
                foreach (Piece.Requirement req in piece.m_resources)
                    json.BeginObject().Property("item", PrefabName(req.m_resItem)).Property("amount", req.m_amount).EndObject();
                json.EndArray().EndObject();
            }
            json.EndArray();

            json.Name("containers").BeginArray();
            foreach (GameObject prefab in prefabs)
            {
                Container c = prefab.GetComponent<Container>();
                if (c == null) continue;
                json.BeginObject()
                    .Property("prefab", prefab.name)
                    .Property("name", c.m_name)
                    .Property("width", c.m_width)
                    .Property("height", c.m_height)
                    .Property("privacy", c.m_privacy.ToString())
                    .Property("autoDestroyEmpty", c.m_autoDestroyEmpty)
                    .Property("isPiece", prefab.GetComponent<Piece>() != null)
                    .EndObject();
            }
            json.EndArray();
        }

        private static void WriteRecipes(JsonWriter json)
        {
            json.Name("recipes").BeginArray();
            foreach (Recipe r in ObjectDB.instance.m_recipes.Where(r => r != null && r.m_item != null && r.m_craftingStation != null))
            {
                json.BeginObject()
                    .Property("name", r.name)
                    .Property("item", r.m_item.name)
                    .Property("amount", r.m_amount)
                    .Property("station", r.m_craftingStation.name)
                    .Property("minStationLevel", r.m_minStationLevel)
                    .Property("enabled", r.m_enabled);
                json.Name("resources").BeginArray();
                foreach (Piece.Requirement req in r.m_resources)
                    json.BeginObject().Property("item", PrefabName(req.m_resItem)).Property("amount", req.m_amount).EndObject();
                json.EndArray().EndObject();
            }
            json.EndArray();
        }

        private static void WriteHaugbuiNames(JsonWriter json)
        {
            json.Name("haugbuiRecipeNames").BeginArray();
            foreach (string name in DefaultRecipes.All.SelectMany(r => r.Ingredients.Select(i => i.Item).Append(r.Station)).Distinct())
            {
                bool found = ObjectDB.instance.GetItemPrefab(name) != null || ZNetScene.instance.GetPrefab(name) != null;
                json.BeginObject().Property("name", name).Property("found", found).EndObject();
            }
            json.EndArray();
        }

        private static string PrefabName(Component component) => component != null ? component.name : null;
    }
}
