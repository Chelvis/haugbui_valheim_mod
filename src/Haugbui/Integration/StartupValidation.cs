using System.Collections.Generic;
using System.Linq;
using Haugbui.Config;
using Haugbui.Core.Recipes;
using Haugbui.Diagnostics;
using Jotunn.Managers;

namespace Haugbui.Integration
{
    /// <summary>Validates Haugbui piece recipes against the loaded game before pieces are registered.</summary>
    internal static class StartupValidation
    {
        public static IReadOnlyDictionary<string, RecipeValidation> Results { get; private set; } =
            new Dictionary<string, RecipeValidation>();

        public static IReadOnlyDictionary<string, RecipeValidation> Run(HaugbuiConfig config)
        {
            Results = RecipeValidator.Validate(
                DefaultRecipes.All,
                config.GetRecipeIngredients,
                item => PrefabManager.Cache.GetPrefab<ItemDrop>(item) != null,
                station => PrefabManager.Cache.GetPrefab<CraftingStation>(station) != null);

            int valid = Results.Values.Count(r => r.Status == RecipeStatus.Valid);
            Log.Info($"Recipe validation: {valid}/{Results.Count} recipes valid.");
            foreach (KeyValuePair<string, RecipeValidation> entry in Results.Where(r => r.Value.Status != RecipeStatus.Valid))
            {
                string line = $"Recipe {entry.Key}: {string.Join("; ", entry.Value.Problems)}";
                if (entry.Value.Status == RecipeStatus.Disabled) Log.Error(line);
                else Log.Warning(line);
            }
            return Results;
        }

        public static bool IsPieceEnabled(string pieceId) =>
            Results.TryGetValue(pieceId, out RecipeValidation result) && result.Status != RecipeStatus.Disabled;
    }
}
