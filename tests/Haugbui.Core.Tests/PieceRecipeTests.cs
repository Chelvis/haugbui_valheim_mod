using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Recipes;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class PieceRecipeTests
    {
        [Test]
        public void Parses_ingredient_list()
        {
            bool ok = PieceRecipe.TryParseIngredients(" Stone:20 , BoneFragments:10", out IReadOnlyList<Ingredient> parsed, out _);

            Assert.That(ok, Is.True);
            Assert.That(parsed.Select(i => i.ToString()), Is.EqualTo(new[] { "Stone:20", "BoneFragments:10" }));
        }

        [TestCase("")]
        [TestCase("Stone")]
        [TestCase("Stone:0")]
        [TestCase("Stone:-1")]
        [TestCase("Stone:abc")]
        [TestCase(":5")]
        [TestCase("Stone:1,Stone:2")]
        [TestCase("Stone:1:2")]
        public void Rejects_malformed_lists(string text)
        {
            bool ok = PieceRecipe.TryParseIngredients(text, out _, out string error);

            Assert.That(ok, Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void Every_piece_has_a_default_recipe()
        {
            string[] pieces =
            {
                PieceIds.Tomb, PieceIds.Offering, PieceIds.PersonalTreasure, PieceIds.SilverBracelets,
                PieceIds.Longsword, PieceIds.Altar, PieceIds.CollectionChest, PieceIds.DeliveryChest
            };

            Assert.That(DefaultRecipes.All.Select(r => r.PieceId), Is.EquivalentTo(pieces));
        }

        [Test]
        public void Default_recipes_keep_the_mandatory_ingredients_of_the_brief()
        {
            Dictionary<string, PieceRecipe> byId = DefaultRecipes.All.ToDictionary(r => r.PieceId);

            AssertHas(byId[PieceIds.Offering], "CookedMeat", "Honey", "Thistle");
            AssertHas(byId[PieceIds.PersonalTreasure], "Coins", "Ruby", "AmberPearl", "Iron");
            AssertHas(byId[PieceIds.SilverBracelets], "Silver");
            AssertHas(byId[PieceIds.Longsword], "BlackMetal");
            AssertHas(byId[PieceIds.Altar], "Eitr", "BlackMarble");
        }

        [Test]
        public void Config_text_round_trips()
        {
            foreach (PieceRecipe recipe in DefaultRecipes.All)
            {
                PieceRecipe.TryParseIngredients(recipe.IngredientsToConfig(), out IReadOnlyList<Ingredient> parsed, out _);
                Assert.That(parsed.Select(i => i.ToString()), Is.EqualTo(recipe.Ingredients.Select(i => i.ToString())));
            }
        }

        private static void AssertHas(PieceRecipe recipe, params string[] items)
        {
            Assert.That(recipe.Ingredients.Select(i => i.Item), Is.SupersetOf(items), recipe.PieceId);
        }
    }
}
