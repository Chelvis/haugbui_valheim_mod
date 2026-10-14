using System.Linq;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;
using Haugbui.Core.Harvest;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class GroundItemRulesTests
    {
        private static GroundItemFacts Eligible() => new GroundItemFacts
        {
            AgeSeconds = 60, NearestPlayerDistance = 20, Transportable = true, OwnedHere = true, WardAllows = true
        };

        [Test]
        public void Old_item_far_from_players_is_eligible() =>
            Assert.That(GroundItemRules.Check(Eligible()), Is.EqualTo(GroundItemVerdict.Eligible));

        [Test]
        public void Fresh_drop_is_not_taken()
        {
            GroundItemFacts facts = Eligible();
            facts.AgeSeconds = 9.9;
            Assert.That(GroundItemRules.Check(facts), Is.EqualTo(GroundItemVerdict.TooYoung));
        }

        [Test]
        public void Item_near_a_player_is_not_taken()
        {
            GroundItemFacts facts = Eligible();
            facts.NearestPlayerDistance = 2.9f;
            Assert.That(GroundItemRules.Check(facts), Is.EqualTo(GroundItemVerdict.PlayerNearby));
        }

        [Test]
        public void Equipment_pieces_wards_and_foreign_items_are_skipped()
        {
            GroundItemFacts facts = Eligible();
            facts.Transportable = false;
            Assert.That(GroundItemRules.Check(facts), Is.EqualTo(GroundItemVerdict.NotTransportable));
            facts = Eligible();
            facts.IsPiece = true;
            Assert.That(GroundItemRules.Check(facts), Is.EqualTo(GroundItemVerdict.Piece));
            facts = Eligible();
            facts.OwnedHere = false;
            Assert.That(GroundItemRules.Check(facts), Is.EqualTo(GroundItemVerdict.NotOwned));
            facts = Eligible();
            facts.WardAllows = false;
            Assert.That(GroundItemRules.Check(facts), Is.EqualTo(GroundItemVerdict.Ward));
        }
    }

    public class HarvestRulesTests
    {
        private static readonly HarvestOptions Defaults = new HarvestOptions();

        private static HarvestFacts Crop() => new HarvestFacts
        {
            Kind = HarvestTargetKind.Pickable, Cultivated = true, Ready = true, ProductTransportable = true, WardAllows = true
        };

        [Test]
        public void Ripe_crop_is_harvested() => Assert.That(HarvestRules.IsTarget(Crop(), Defaults));

        [Test]
        public void Picked_or_growing_crop_is_ignored()
        {
            HarvestFacts facts = Crop();
            facts.Ready = false;
            Assert.That(HarvestRules.IsTarget(facts, Defaults), Is.False);
        }

        [Test]
        public void Wild_pickables_only_with_the_option()
        {
            HarvestFacts facts = Crop();
            facts.Cultivated = false;
            Assert.That(HarvestRules.IsTarget(facts, Defaults), Is.False);
            Assert.That(HarvestRules.IsTarget(facts, new HarvestOptions { IncludeWildPickables = true }));
        }

        [Test]
        public void Pickables_that_anger_creatures_are_never_taken()
        {
            HarvestFacts facts = Crop();
            facts.Cultivated = false;
            facts.Aggravates = true;
            Assert.That(HarvestRules.IsTarget(facts, new HarvestOptions { IncludeWildPickables = true }), Is.False);
        }

        [Test]
        public void Hives_when_full_sap_only_with_the_option()
        {
            var hive = new HarvestFacts { Kind = HarvestTargetKind.Beehive, Ready = true, ProductTransportable = true, WardAllows = true };
            Assert.That(HarvestRules.IsTarget(hive, Defaults));
            hive.Ready = false;
            Assert.That(HarvestRules.IsTarget(hive, Defaults), Is.False);

            var sap = new HarvestFacts { Kind = HarvestTargetKind.SapCollector, Ready = true, ProductTransportable = true, WardAllows = true };
            Assert.That(HarvestRules.IsTarget(sap, Defaults), Is.False);
            Assert.That(HarvestRules.IsTarget(sap, new HarvestOptions { IncludeSapCollectors = true }));
        }

        [Test]
        public void Warded_targets_are_skipped()
        {
            HarvestFacts facts = Crop();
            facts.WardAllows = false;
            Assert.That(HarvestRules.IsTarget(facts, Defaults), Is.False);
        }
    }

    public class PickupRouteTests
    {
        private static GroundItem Drop(string id, float x, int amount = 1, string prefab = "Wood") => new GroundItem
        {
            Id = id, Position = new Vec3(x, 0, 0), Item = new ItemStack { Prefab = prefab, Amount = amount, UnitWeight = 1, MaxStack = 50 }
        };

        [Test]
        public void Collects_nearest_first_and_chains_nearby_drops()
        {
            var route = PickupRoute.Plan(new[] { Drop("far", 30), Drop("b", 8), Drop("a", 4) }, Vec3.Zero, new CarriedInventory(4, 100));
            Assert.That(route.Select(d => d.Id), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void First_stop_may_be_far()
        {
            var route = PickupRoute.Plan(new[] { Drop("far", 40) }, Vec3.Zero, new CarriedInventory(2, 100));
            Assert.That(route.Select(d => d.Id), Is.EqualTo(new[] { "far" }));
        }

        [Test]
        public void Skips_drops_that_do_not_fit_whole()
        {
            var carried = new CarriedInventory(1, 10);
            var route = PickupRoute.Plan(new[] { Drop("heavy", 1, 20), Drop("light", 2, 5) }, Vec3.Zero, carried);
            Assert.That(route.Select(d => d.Id), Is.EqualTo(new[] { "light" }));
        }

        [Test]
        public void Respects_slots_already_used()
        {
            var carried = new CarriedInventory(2, 100);
            carried.Add(new ItemStack { Prefab = "Stone", Amount = 1, UnitWeight = 1, MaxStack = 50 });
            var route = PickupRoute.Plan(new[] { Drop("a", 1), Drop("b", 2, 1, "Resin") }, Vec3.Zero, carried);
            Assert.That(route.Select(d => d.Id), Is.EqualTo(new[] { "a" }));
        }
    }
}
