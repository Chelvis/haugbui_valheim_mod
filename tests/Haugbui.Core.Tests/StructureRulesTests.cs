using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Geometry;
using Haugbui.Core.Structures;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class StructureRulesTests
    {
        private const long Me = 42;
        private const long Someone = 7;
        private const float BedDistance = 50f;
        private const float Spacing = 5f;
        private const float Range = 8f;

        private static IEnumerable<StructureKind[]> AllUpgradeSubsets()
        {
            var upgrades = TombProgression.Upgrades;
            for (int mask = 0; mask < 1 << upgrades.Count; mask++)
                yield return upgrades.Where((_, i) => (mask & (1 << i)) != 0).ToArray();
        }

        [TestCaseSource(nameof(AllUpgradeSubsets))]
        public void Level_and_abilities_for_every_upgrade_subset(StructureKind[] subset)
        {
            TombProgress forward = TombProgression.Compute(subset);
            TombProgress reversed = TombProgression.Compute(subset.Reverse());

            Assert.That(forward.Level, Is.EqualTo(1 + subset.Length));
            Assert.That(reversed.Level, Is.EqualTo(forward.Level));
            Assert.That(forward.Has(TombAbilities.Combat), Is.EqualTo(subset.Contains(StructureKind.Longsword)));
            Assert.That(forward.Has(TombAbilities.FullTime), Is.EqualTo(subset.Contains(StructureKind.Altar)));
        }

        [Test]
        public void Duplicates_and_non_upgrades_do_not_count()
        {
            TombProgress progress = TombProgression.Compute(new[]
            {
                StructureKind.Offering, StructureKind.Offering, StructureKind.Tomb, StructureKind.CollectionChest
            });

            Assert.That(progress.Level, Is.EqualTo(2));
        }

        [Test]
        public void Longsword_alone_grants_combat_at_level_two()
        {
            TombProgress progress = TombProgression.Compute(new[] { StructureKind.Longsword });

            Assert.That(progress.Level, Is.EqualTo(2));
            Assert.That(progress.Abilities, Is.EqualTo(TombAbilities.Combat));
        }

        private static BedInfo Bed(float x, long owner) => new BedInfo(new Vec3(x, 0, 0), owner);

        [TestCase(49f, Me, PlacementResult.Ok)]
        [TestCase(50f, Me, PlacementResult.Ok)]
        [TestCase(51f, Me, PlacementResult.NeedOwnBed)]
        [TestCase(10f, Someone, PlacementResult.NeedOwnBed)]
        [TestCase(10f, 0L, PlacementResult.NeedOwnBed)]
        public void Tomb_needs_own_bed_within_distance(float bedX, long owner, PlacementResult expected)
        {
            PlacementResult result = PlacementRules.CheckTomb(Vec3.Zero, Me, new[] { Bed(bedX, owner) },
                Enumerable.Empty<Vec3>(), BedDistance, Spacing);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void No_bed_at_all_is_rejected()
        {
            Assert.That(PlacementRules.CheckTomb(Vec3.Zero, Me, Enumerable.Empty<BedInfo>(), Enumerable.Empty<Vec3>(),
                BedDistance, Spacing), Is.EqualTo(PlacementResult.NeedOwnBed));
        }

        [TestCase(4f, PlacementResult.TombTooClose)]
        [TestCase(6f, PlacementResult.Ok)]
        public void Tombs_keep_minimum_spacing(float otherX, PlacementResult expected)
        {
            PlacementResult result = PlacementRules.CheckTomb(Vec3.Zero, Me, new[] { Bed(10, Me) },
                new[] { new Vec3(otherX, 0, 0) }, BedDistance, Spacing);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void Upgrade_needs_a_tomb_in_range()
        {
            var far = new TombInfo("far", new Vec3(9, 0, 0));

            Assert.That(PlacementRules.CheckUpgrade(StructureKind.Offering, Vec3.Zero, new[] { far }, Range, out TombInfo bind),
                Is.EqualTo(PlacementResult.NeedTomb));
            Assert.That(bind, Is.Null);
        }

        [Test]
        public void Upgrade_binds_to_the_nearest_tomb_without_that_kind()
        {
            var near = new TombInfo("near", new Vec3(2, 0, 0), new[] { StructureKind.Offering });
            var other = new TombInfo("other", new Vec3(-6, 0, 0));

            PlacementResult result = PlacementRules.CheckUpgrade(StructureKind.Offering, Vec3.Zero, new[] { near, other },
                Range, out TombInfo bind);

            Assert.That(result, Is.EqualTo(PlacementResult.Ok));
            Assert.That(bind.Guid, Is.EqualTo("other"));
        }

        [Test]
        public void Duplicate_upgrade_kind_is_rejected()
        {
            var tomb = new TombInfo("t", new Vec3(2, 0, 0), new[] { StructureKind.Altar });

            Assert.That(PlacementRules.CheckUpgrade(StructureKind.Altar, Vec3.Zero, new[] { tomb }, Range, out _),
                Is.EqualTo(PlacementResult.UpgradeExists));
        }

        [Test]
        public void Orphan_upgrades_rebind_to_a_rebuilt_tomb()
        {
            var rebuilt = new TombInfo("new", Vec3.Zero);
            var upgrades = new[]
            {
                new UpgradeInfo("u1", StructureKind.Offering, new Vec3(3, 0, 0), "destroyed"),
                new UpgradeInfo("u2", StructureKind.Offering, new Vec3(4, 0, 0), "destroyed"),
                new UpgradeInfo("u3", StructureKind.Altar, new Vec3(20, 0, 0), "destroyed"),
                new UpgradeInfo("u4", StructureKind.Longsword, new Vec3(1, 0, 0), ""),
            };

            IReadOnlyDictionary<string, string> changes = PlacementRules.RebindOrphans(upgrades, new[] { rebuilt }, Range);

            Assert.That(changes.Keys, Is.EquivalentTo(new[] { "u1", "u4" }), "one Offering per tomb; u3 is out of range");
            Assert.That(changes.Values.Distinct(), Is.EqualTo(new[] { "new" }));
        }

        [Test]
        public void Bound_upgrades_are_not_rebound()
        {
            var tomb = new TombInfo("t", Vec3.Zero, new[] { StructureKind.Offering });
            var upgrades = new[] { new UpgradeInfo("u1", StructureKind.Offering, new Vec3(1, 0, 0), "t") };

            Assert.That(PlacementRules.RebindOrphans(upgrades, new[] { tomb }, Range), Is.Empty);
        }

        [Test]
        public void Only_bound_upgrades_in_range_count()
        {
            var tomb = new TombInfo("t", Vec3.Zero);
            var upgrades = new[]
            {
                new UpgradeInfo("a", StructureKind.Offering, new Vec3(1, 0, 0), "t"),
                new UpgradeInfo("b", StructureKind.Altar, new Vec3(9, 0, 0), "t"),
                new UpgradeInfo("c", StructureKind.Longsword, new Vec3(1, 0, 0), "other"),
            };

            Assert.That(PlacementRules.CountedUpgrades(tomb, upgrades, Range), Is.EqualTo(new[] { StructureKind.Offering }));
        }

        [TestCase(PlacementResult.Ok, null)]
        [TestCase(PlacementResult.NeedOwnBed, "haugbui_msg_need_bed")]
        [TestCase(PlacementResult.UpgradeExists, "haugbui_msg_upgrade_exists")]
        public void Results_map_to_message_keys(PlacementResult result, string key)
        {
            Assert.That(PlacementMessages.KeyFor(result), Is.EqualTo(key));
        }
    }
}
