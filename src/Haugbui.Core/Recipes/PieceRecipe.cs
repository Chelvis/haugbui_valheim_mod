using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Haugbui.Core.Recipes
{
    public readonly struct Ingredient
    {
        public Ingredient(string item, int amount)
        {
            Item = item;
            Amount = amount;
        }

        public string Item { get; }
        public int Amount { get; }

        public override string ToString() => $"{Item}:{Amount.ToString(CultureInfo.InvariantCulture)}";
    }

    public sealed class PieceRecipe
    {
        public PieceRecipe(string pieceId, string station, IReadOnlyList<Ingredient> ingredients)
        {
            PieceId = pieceId;
            Station = station;
            Ingredients = ingredients;
        }

        public string PieceId { get; }
        public string Station { get; }
        public IReadOnlyList<Ingredient> Ingredients { get; }

        public string IngredientsToConfig() => string.Join(",", Ingredients.Select(i => i.ToString()));

        /// <summary>Parses "Item:Amount,Item:Amount". Returns false and an error message on any malformed entry.</summary>
        public static bool TryParseIngredients(string text, out IReadOnlyList<Ingredient> ingredients, out string error)
        {
            ingredients = Array.Empty<Ingredient>();
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "empty ingredient list";
                return false;
            }

            var result = new List<Ingredient>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in text.Split(','))
            {
                string entry = raw.Trim();
                string[] parts = entry.Split(':');
                if (parts.Length != 2 || parts[0].Trim().Length == 0)
                {
                    error = $"'{entry}' is not Item:Amount";
                    return false;
                }
                string item = parts[0].Trim();
                if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount <= 0)
                {
                    error = $"'{entry}' has an invalid amount";
                    return false;
                }
                if (!seen.Add(item))
                {
                    error = $"'{item}' is listed twice";
                    return false;
                }
                result.Add(new Ingredient(item, amount));
            }

            ingredients = result;
            return true;
        }
    }

    public static class PieceIds
    {
        public const string Tomb = "haugbui_tomb";
        public const string Offering = "haugbui_upgrade_offering";
        public const string PersonalTreasure = "haugbui_upgrade_treasure";
        public const string SilverBracelets = "haugbui_upgrade_bracelets";
        public const string Longsword = "haugbui_upgrade_longsword";
        public const string Altar = "haugbui_upgrade_altar";
        public const string CollectionChest = "haugbui_chest_collection";
        public const string DeliveryChest = "haugbui_chest_delivery";
    }

    public static class StationPrefabs
    {
        public const string Workbench = "piece_workbench";
        public const string Forge = "forge";
        public const string GaldrTable = "piece_magetable";
    }

    /// <summary>Default costs from PRODUCT_REQUIREMENTS §3, §4.2, §9 and §10.</summary>
    public static class DefaultRecipes
    {
        public static IReadOnlyList<PieceRecipe> All { get; } = new[]
        {
            Recipe(PieceIds.Tomb, StationPrefabs.Workbench,
                "Stone:20,TrophySkeleton:5,SurtlingCore:2"),
            Recipe(PieceIds.Offering, StationPrefabs.Workbench,
                "CookedMeat:5,CookedDeerMeat:3,Carrot:10,Thistle:5,Honey:10,Wood:10"),
            Recipe(PieceIds.PersonalTreasure, StationPrefabs.Forge,
                "Coins:150,Ruby:3,AmberPearl:3,Amber:5,Iron:10"),
            Recipe(PieceIds.SilverBracelets, StationPrefabs.Forge,
                "Silver:10,Bronze:5,WolfClaw:3"),
            Recipe(PieceIds.Longsword, StationPrefabs.Forge,
                "BlackMetal:20,Chain:2,TrophyDraugrElite:1"),
            Recipe(PieceIds.Altar, StationPrefabs.GaldrTable,
                "BlackMarble:20,Eitr:10,WitheredBone:20,Ectoplasm:10"),
            Recipe(PieceIds.CollectionChest, StationPrefabs.Workbench,
                "Wood:10,AncientSeed:2"),
            Recipe(PieceIds.DeliveryChest, StationPrefabs.Workbench,
                "Wood:10,GreydwarfEye:10"),
        };

        private static PieceRecipe Recipe(string pieceId, string station, string ingredients)
        {
            if (!PieceRecipe.TryParseIngredients(ingredients, out IReadOnlyList<Ingredient> parsed, out string error))
                throw new InvalidOperationException($"Default recipe {pieceId}: {error}");
            return new PieceRecipe(pieceId, station, parsed);
        }
    }
}
