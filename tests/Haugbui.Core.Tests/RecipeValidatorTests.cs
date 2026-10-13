using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Recipes;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class RecipeValidatorTests
    {
        private static HashSet<string> AllStations() =>
            new HashSet<string> { StationPrefabs.Workbench, StationPrefabs.Forge, StationPrefabs.GaldrTable };

        private static HashSet<string> AllDefaultItems() =>
            new HashSet<string>(DefaultRecipes.All.SelectMany(r => r.Ingredients).Select(i => i.Item));

        private static IReadOnlyDictionary<string, RecipeValidation> Validate(
            HashSet<string> items, Dictionary<string, string> config = null, HashSet<string> stations = null)
        {
            stations = stations ?? AllStations();
            return RecipeValidator.Validate(
                DefaultRecipes.All,
                id => config != null && config.TryGetValue(id, out string text) ? text : null,
                items.Contains,
                stations.Contains);
        }

        [Test]
        public void All_defaults_are_valid_when_every_name_exists()
        {
            IReadOnlyDictionary<string, RecipeValidation> result = Validate(AllDefaultItems());

            Assert.That(result.Values.Select(v => v.Status), Is.All.EqualTo(RecipeStatus.Valid));
        }

        [Test]
        public void Unknown_item_disables_only_the_pieces_that_use_it()
        {
            HashSet<string> items = AllDefaultItems();
            items.Remove("WolfPelt");

            IReadOnlyDictionary<string, RecipeValidation> result = Validate(items);

            Assert.That(result[PieceIds.SilverBracelets].Status, Is.EqualTo(RecipeStatus.Disabled));
            Assert.That(result[PieceIds.SilverBracelets].Recipe, Is.Null);
            Assert.That(result.Where(kv => kv.Key != PieceIds.SilverBracelets).Select(kv => kv.Value.Status),
                Is.All.EqualTo(RecipeStatus.Valid));
        }

        [Test]
        public void Unknown_station_disables_the_piece()
        {
            HashSet<string> stations = AllStations();
            stations.Remove(StationPrefabs.GaldrTable);

            IReadOnlyDictionary<string, RecipeValidation> result = Validate(AllDefaultItems(), stations: stations);

            Assert.That(result[PieceIds.Altar].Status, Is.EqualTo(RecipeStatus.Disabled));
            Assert.That(result[PieceIds.Altar].Problems.Single(), Does.Contain(StationPrefabs.GaldrTable));
        }

        [Test]
        public void Valid_config_override_is_used()
        {
            var config = new Dictionary<string, string> { [PieceIds.Tomb] = "Stone:5" };

            RecipeValidation tomb = Validate(AllDefaultItems(), config)[PieceIds.Tomb];

            Assert.That(tomb.Status, Is.EqualTo(RecipeStatus.Valid));
            Assert.That(tomb.Recipe.IngredientsToConfig(), Is.EqualTo("Stone:5"));
        }

        [TestCase("Stone")]
        [TestCase("NoSuchItem:3")]
        public void Bad_config_override_falls_back_to_default(string text)
        {
            var config = new Dictionary<string, string> { [PieceIds.Tomb] = text };

            RecipeValidation tomb = Validate(AllDefaultItems(), config)[PieceIds.Tomb];

            Assert.That(tomb.Status, Is.EqualTo(RecipeStatus.ConfigInvalidUsingDefault));
            Assert.That(tomb.Recipe.IngredientsToConfig(),
                Is.EqualTo(DefaultRecipes.All.Single(r => r.PieceId == PieceIds.Tomb).IngredientsToConfig()));
            Assert.That(tomb.Problems, Has.Count.EqualTo(1));
        }
    }
}
