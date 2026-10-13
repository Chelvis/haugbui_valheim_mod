using System;
using System.Collections.Generic;
using System.Linq;

namespace Haugbui.Core.Recipes
{
    public enum RecipeStatus
    {
        Valid,
        ConfigInvalidUsingDefault,
        Disabled
    }

    public sealed class RecipeValidation
    {
        public RecipeValidation(PieceRecipe recipe, RecipeStatus status, IReadOnlyList<string> problems)
        {
            Recipe = recipe;
            Status = status;
            Problems = problems;
        }

        /// <summary>The recipe to register; null when the piece is disabled.</summary>
        public PieceRecipe Recipe { get; }
        public RecipeStatus Status { get; }
        public IReadOnlyList<string> Problems { get; }
    }

    public static class RecipeValidator
    {
        /// <param name="configuredIngredients">Config text per piece id; null or missing means "use default".</param>
        public static IReadOnlyDictionary<string, RecipeValidation> Validate(
            IEnumerable<PieceRecipe> defaults,
            Func<string, string> configuredIngredients,
            Func<string, bool> itemExists,
            Func<string, bool> stationExists)
        {
            var result = new Dictionary<string, RecipeValidation>();
            foreach (PieceRecipe fallback in defaults)
                result[fallback.PieceId] = ValidateOne(fallback, configuredIngredients(fallback.PieceId), itemExists, stationExists);
            return result;
        }

        private static RecipeValidation ValidateOne(
            PieceRecipe fallback, string configured, Func<string, bool> itemExists, Func<string, bool> stationExists)
        {
            var problems = new List<string>();
            PieceRecipe candidate = fallback;
            bool usedFallbackForConfig = false;

            if (configured != null && configured != fallback.IngredientsToConfig())
            {
                if (PieceRecipe.TryParseIngredients(configured, out IReadOnlyList<Ingredient> parsed, out string error))
                {
                    candidate = new PieceRecipe(fallback.PieceId, fallback.Station, parsed);
                }
                else
                {
                    problems.Add($"config text invalid ({error}); using default");
                    usedFallbackForConfig = true;
                }
            }

            List<string> missing = MissingNames(candidate, itemExists, stationExists);
            if (missing.Count > 0 && candidate != fallback)
            {
                problems.Add($"configured recipe uses unknown names ({string.Join(", ", missing)}); using default");
                candidate = fallback;
                usedFallbackForConfig = true;
                missing = MissingNames(candidate, itemExists, stationExists);
            }

            if (missing.Count > 0)
            {
                problems.Add($"unknown names in default recipe ({string.Join(", ", missing)}); piece disabled");
                return new RecipeValidation(null, RecipeStatus.Disabled, problems);
            }

            return new RecipeValidation(candidate,
                usedFallbackForConfig ? RecipeStatus.ConfigInvalidUsingDefault : RecipeStatus.Valid, problems);
        }

        private static List<string> MissingNames(PieceRecipe recipe, Func<string, bool> itemExists, Func<string, bool> stationExists)
        {
            List<string> missing = recipe.Ingredients.Select(i => i.Item).Where(item => !itemExists(item)).ToList();
            if (!stationExists(recipe.Station)) missing.Add(recipe.Station);
            return missing;
        }
    }
}
