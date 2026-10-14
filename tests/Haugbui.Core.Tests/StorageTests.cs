using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;
using Haugbui.Core.Persistence;
using Haugbui.Core.Storage;
using Haugbui.Core.Structures;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    internal static class Items
    {
        public static ItemStack Make(string prefab, int amount, int maxStack = 50, float weight = 1f, int quality = 1) =>
            new ItemStack { Prefab = prefab, Amount = amount, MaxStack = maxStack, UnitWeight = weight, Quality = quality };

        public static ContainerSnapshot Chest(string id, float x, int slots, params ItemStack[] items) =>
            new ContainerSnapshot { Id = id, Position = new Vec3(x, 0, 0), Slots = slots, Items = items.ToList() };
    }

    public class StorageSelectorTests
    {
        private static readonly Vec3 Origin = Vec3.Zero;

        [Test]
        public void Empty_chest_is_never_chosen()
        {
            ContainerSnapshot empty = Items.Chest("empty", 1, 10);
            Assert.That(StorageSelector.Plan(Items.Make("Wood", 10), 10, new[] { empty }, Origin), Is.Empty);
        }

        [Test]
        public void Wood_never_goes_to_a_stone_only_chest()
        {
            ContainerSnapshot stone = Items.Chest("stone", 1, 10, Items.Make("Stone", 5));
            ContainerSnapshot wood = Items.Chest("wood", 30, 10, Items.Make("Wood", 1));
            List<StorageAllocation> plan = StorageSelector.Plan(Items.Make("Wood", 10), 10, new[] { stone, wood }, Origin);
            Assert.That(plan.Select(a => a.ContainerId), Is.EqualTo(new[] { "wood" }));
        }

        [Test]
        public void Different_quality_is_not_the_same_item()
        {
            ContainerSnapshot q2 = Items.Chest("q2", 1, 10, Items.Make("Wood", 1, quality: 2));
            Assert.That(StorageSelector.Plan(Items.Make("Wood", 5), 5, new[] { q2 }, Origin), Is.Empty);
        }

        [Test]
        public void Whole_fit_beats_a_nearer_chest_that_fits_part()
        {
            ContainerSnapshot near = Items.Chest("near", 1, 1, Items.Make("Wood", 45));
            ContainerSnapshot far = Items.Chest("far", 40, 5, Items.Make("Wood", 1));
            Assert.That(StorageSelector.Plan(Items.Make("Wood", 20), 20, new[] { near, far }, Origin).First().ContainerId, Is.EqualTo("far"));
        }

        [Test]
        public void Topping_up_an_open_stack_beats_starting_a_new_one()
        {
            ContainerSnapshot fullStacks = Items.Chest("full", 1, 10, Items.Make("Wood", 50));
            ContainerSnapshot openStack = Items.Chest("open", 20, 10, Items.Make("Wood", 30));
            Assert.That(StorageSelector.Plan(Items.Make("Wood", 10), 10, new[] { fullStacks, openStack }, Origin).Single().ContainerId,
                Is.EqualTo("open"));
        }

        [Test]
        public void Nearest_wins_between_equal_chests()
        {
            ContainerSnapshot a = Items.Chest("a", 12, 10, Items.Make("Wood", 10));
            ContainerSnapshot b = Items.Chest("b", 3, 10, Items.Make("Wood", 10));
            Assert.That(StorageSelector.Plan(Items.Make("Wood", 5), 5, new[] { a, b }, Origin).Single().ContainerId, Is.EqualTo("b"));
        }

        [Test]
        public void Overflow_is_split_across_matching_chests_without_exceeding_capacity()
        {
            ContainerSnapshot a = Items.Chest("a", 1, 2, Items.Make("Wood", 40));            // room 10 + 50
            ContainerSnapshot b = Items.Chest("b", 2, 1, Items.Make("Wood", 20));            // room 30
            ContainerSnapshot empty = Items.Chest("c", 0, 10);
            List<StorageAllocation> plan = StorageSelector.Plan(Items.Make("Wood", 200), 200, new[] { a, b, empty }, Origin);
            Assert.That(plan.Sum(p => p.Amount), Is.EqualTo(90));
            Assert.That(plan.Single(p => p.ContainerId == "a").Amount, Is.EqualTo(60));
            Assert.That(plan.Single(p => p.ContainerId == "b").Amount, Is.EqualTo(30));
            Assert.That(plan.Any(p => p.ContainerId == "c"), Is.False);
        }

        [Test]
        public void Full_matching_chest_is_not_a_candidate()
        {
            ContainerSnapshot full = Items.Chest("full", 1, 1, Items.Make("Wood", 50));
            Assert.That(StorageSelector.IsCandidate(full, Items.Make("Wood", 1)), Is.False);
        }

        [Test]
        public void Room_counts_matching_stacks_of_the_same_quality_and_free_slots()
        {
            ContainerSnapshot chest = Items.Chest("x", 0, 4, Items.Make("Wood", 45), Items.Make("Wood", 45, quality: 2), Items.Make("Stone", 3));
            Assert.That(chest.RoomFor(Items.Make("Wood", 1)), Is.EqualTo(5 + 50));
        }
    }

    public class ItemStackDataTests
    {
        [Test]
        public void Copy_and_serializer_keep_cheated_crafter_and_custom_data()
        {
            ItemStack stack = Items.Make("MeadHealthMinor", 3);
            stack.Cheated = true;
            stack.CrafterId = 42;
            stack.CrafterName = "Kelvin";
            stack.CustomData["k"] = "v";
            ItemStack copy = stack.Copy(2);
            copy.CustomData["other"] = "x";
            Assert.That(stack.CustomData.Count, Is.EqualTo(1), "copy owns its dictionary");

            Assert.That(ItemStacksSerializer.Instance.TryDeserialize(ItemStacksSerializer.Instance.Serialize(new List<ItemStack> { stack }),
                out List<ItemStack> read), Is.EqualTo(BlobReadStatus.Ok));
            ItemStack back = read.Single();
            Assert.That(back.StacksWith(stack), Is.True);
            Assert.That(back.Cheated && back.CrafterId == 42 && back.CrafterName == "Kelvin" && back.CustomData["k"] == "v");
        }

        [Test]
        public void Version_1_blobs_still_load()
        {
            var writer = new BlobWriter(1);
            writer.Write(1).Write("Wood").Write(12).Write(1).Write(0).Write(0);
            Assert.That(ItemStacksSerializer.Instance.TryDeserialize(writer.ToArray(), out List<ItemStack> read), Is.EqualTo(BlobReadStatus.Ok));
            Assert.That(read.Single().Amount, Is.EqualTo(12));
            Assert.That(read.Single().Cheated, Is.False);
            Assert.That(read.Single().CustomData, Is.Not.Null);
        }

        [Test]
        public void Different_data_does_not_merge_in_the_carried_inventory()
        {
            var inventory = new CarriedInventory(3, 1000);
            ItemStack cheated = Items.Make("Wood", 5);
            cheated.Cheated = true;
            inventory.Add(Items.Make("Wood", 5));
            inventory.Add(cheated);
            Assert.That(inventory.Stacks.Count, Is.EqualTo(2));
            Assert.That(inventory.Remove(cheated, 5), Is.EqualTo(5));
            Assert.That(inventory.Stacks.Single().Cheated, Is.False, "the identical stack is removed first");
        }
    }

    /// <summary>In-memory container with the game's stacking rule and optional injected failures.</summary>
    internal sealed class FakeContainer : IItemEndpoint
    {
        private readonly List<ItemStack> _items = new List<ItemStack>();
        private readonly int _slots;

        public FakeContainer(string id, int slots)
        {
            Id = id;
            _slots = slots;
        }

        public string Id { get; }
        public bool InUse { get; set; }
        public Random FailRandom { get; set; }
        public double AddShortfallRate { get; set; }
        public int? RemoveCap { get; set; }
        public IReadOnlyList<ItemStack> Items => _items;

        public string Unavailable() => InUse ? "in use" : null;
        public int Count(ItemStack item) => _items.Where(i => i.SameKind(item)).Sum(i => i.Amount);

        public int RoomFor(ItemStack item)
        {
            int max = Math.Max(1, item.MaxStack);
            return _items.Where(i => i.SameKind(item)).Sum(i => max - i.Amount) + (_slots - _items.Count) * max;
        }

        public int Add(ItemStack item)
        {
            if (InUse) throw new InvalidOperationException("modified while in use");
            int left = Math.Min(item.Amount, RoomFor(item));
            if (FailRandom != null && FailRandom.NextDouble() < AddShortfallRate) left = FailRandom.Next(0, left + 1);
            int added = left;
            int max = Math.Max(1, item.MaxStack);
            foreach (ItemStack stack in _items.Where(i => i.SameKind(item)))
            {
                int put = Math.Min(left, max - stack.Amount);
                stack.Amount += put;
                // Vanilla stacking taints the stack.
                if (item.Cheated) stack.Cheated = true;
                left -= put;
            }
            while (left > 0)
            {
                int put = Math.Min(left, max);
                _items.Add(item.Copy(put));
                left -= put;
            }
            return added;
        }

        public int Remove(ItemStack item, int amount)
        {
            if (InUse) throw new InvalidOperationException("modified while in use");
            if (RemoveCap.HasValue) amount = Math.Min(amount, RemoveCap.Value);
            int left = amount;
            foreach (ItemStack stack in _items.Where(i => i.StacksWith(item)).Concat(_items.Where(i => i.SameKind(item) && !i.StacksWith(item))).ToList())
            {
                int take = Math.Min(left, stack.Amount);
                stack.Amount -= take;
                left -= take;
            }
            _items.RemoveAll(i => i.Amount <= 0);
            return amount - left;
        }
    }

    /// <summary>Injects failures between the add and the remove: the source throws or gives less than asked.</summary>
    internal sealed class FlakySource : IItemEndpoint
    {
        private readonly IItemEndpoint _inner;
        private readonly Random _random;
        private readonly double _rate;

        public FlakySource(IItemEndpoint inner, Random random, double rate)
        {
            _inner = inner;
            _random = random;
            _rate = rate;
        }

        public string Id => _inner.Id;
        public string Unavailable() => _inner.Unavailable();
        public int Count(ItemStack item) => _inner.Count(item);
        public int RoomFor(ItemStack item) => _inner.RoomFor(item);
        public int Add(ItemStack item) => _inner.Add(item);

        public int Remove(ItemStack item, int amount)
        {
            if (_random.NextDouble() >= _rate) return _inner.Remove(item, amount);
            if (_random.Next(2) == 0) throw new InvalidOperationException("injected");
            return _inner.Remove(item, _random.Next(0, amount));
        }
    }

    public class TransferEngineTests
    {
        private static readonly string[] Prefabs = { "Wood", "Stone", "Resin", "Flint" };

        [Test]
        public void Moves_destination_first_and_reports_amounts()
        {
            var a = new FakeContainer("a", 4);
            var b = new FakeContainer("b", 4);
            a.Add(Items.Make("Wood", 30));
            TransferRecord r = new TransferEngine().Move(a, b, Items.Make("Wood", 0), 20);
            Assert.That(r.Status, Is.EqualTo(TransferStatus.Moved));
            Assert.That(a.Count(Items.Make("Wood", 0)), Is.EqualTo(10));
            Assert.That(b.Count(Items.Make("Wood", 0)), Is.EqualTo(20));
            Assert.That(r.Conserved);
        }

        [Test]
        public void Never_modifies_a_container_in_use()
        {
            var a = new FakeContainer("a", 4);
            var b = new FakeContainer("b", 4) { InUse = true };
            a.Add(Items.Make("Wood", 30));
            var engine = new TransferEngine();
            Assert.That(engine.Move(a, b, Items.Make("Wood", 0), 5).Status, Is.EqualTo(TransferStatus.DestinationUnavailable));
            a.InUse = true;
            b.InUse = false;
            Assert.That(engine.Move(a, b, Items.Make("Wood", 0), 5).Status, Is.EqualTo(TransferStatus.SourceUnavailable));
            Assert.That(b.Items, Is.Empty);
        }

        [Test]
        public void Offers_only_what_fits()
        {
            var a = new FakeContainer("a", 4);
            var b = new FakeContainer("b", 1);
            a.Add(Items.Make("Wood", 100));
            b.Add(Items.Make("Wood", 45));
            TransferRecord r = new TransferEngine().Move(a, b, Items.Make("Wood", 0), 100);
            Assert.That(r.Moved, Is.EqualTo(5));
            Assert.That(a.Count(Items.Make("Wood", 0)), Is.EqualTo(95));
        }

        [Test]
        public void Cheated_flag_travels_with_the_item()
        {
            var a = new FakeContainer("a", 4);
            var carried = new CarriedInventory(2, 1000);
            ItemStack cheated = Items.Make("Wood", 10);
            cheated.Cheated = true;
            a.Add(cheated);
            new TransferEngine().Move(a, new CarriedEndpoint("carried", carried), a.Items[0], 10);
            Assert.That(carried.Stacks.Single().Cheated, Is.True);
        }

        [Test]
        public void Failed_source_removal_rolls_back_the_destination()
        {
            var a = new FakeContainer("a", 4);
            var b = new FakeContainer("b", 4);
            a.Add(Items.Make("Wood", 30));
            a.RemoveCap = 5;
            TransferRecord r = new TransferEngine().Move(a, b, Items.Make("Wood", 0), 20);
            Assert.That(r.Status, Is.EqualTo(TransferStatus.RolledBack));
            Assert.That(r.Moved, Is.EqualTo(5));
            Assert.That(a.Count(Items.Make("Wood", 0)), Is.EqualTo(25));
            Assert.That(b.Count(Items.Make("Wood", 0)), Is.EqualTo(5));
        }

        [Test]
        public void Ten_thousand_random_transfers_with_injected_failures_conserve_totals()
        {
            var random = new Random(20261013);
            var containers = Enumerable.Range(0, 4).Select(i => new FakeContainer("c" + i, 3 + i)).ToList();
            var carried = new CarriedInventory(3, 150);
            var endpoints = new List<IItemEndpoint>(containers) { new CarriedEndpoint("carried", carried) };
            foreach (FakeContainer c in containers)
                foreach (string prefab in Prefabs)
                    c.Add(Items.Make(prefab, random.Next(1, 60)));
            Dictionary<string, int> expected = Totals(containers, carried);

            foreach (FakeContainer c in containers)
            {
                c.FailRandom = random;
                c.AddShortfallRate = 0.05;
            }

            var engine = new TransferEngine();
            int moved = 0, rolledBack = 0;
            for (int i = 0; i < 10000; i++)
            {
                IItemEndpoint source = endpoints[random.Next(endpoints.Count)];
                IItemEndpoint destination = endpoints[random.Next(endpoints.Count)];
                if (source == destination) continue;
                foreach (FakeContainer c in containers) c.InUse = random.NextDouble() < 0.05;
                ItemStack item = Items.Make(Prefabs[random.Next(Prefabs.Length)], 0);
                item.Cheated = random.Next(10) == 0;
                TransferRecord r = engine.Move(new FlakySource(source, random, 0.1), destination, item, random.Next(1, 80));
                Assert.That(r.Conserved, Is.True, r.ToString());
                if (r.Status == TransferStatus.Moved) moved++;
                if (r.Status == TransferStatus.RolledBack) rolledBack++;
            }
            foreach (FakeContainer c in containers) c.InUse = false;

            Assert.That(Totals(containers, carried), Is.EqualTo(expected));
            Assert.That(moved, Is.GreaterThan(1000));
            Assert.That(rolledBack, Is.GreaterThan(10), "failures were injected");
            Assert.That(carried.Weight, Is.LessThanOrEqualTo(150f));
        }

        private static Dictionary<string, int> Totals(IEnumerable<FakeContainer> containers, CarriedInventory carried) =>
            containers.SelectMany(c => c.Items).Concat(carried.Stacks)
                .GroupBy(s => s.Prefab).ToDictionary(g => g.Key, g => g.Sum(s => s.Amount));
    }

    public class AccessRuleTests
    {
        private const long Owner = 1;
        private const long Stranger = 2;

        private static ContainerFacts Chest() =>
            new ContainerFacts { PlacedByPlayer = true, CreatorId = Owner, Privacy = ContainerPrivacy.Public };

        [Test]
        public void Exclusion_rule_table()
        {
            var cases = new (string Name, Func<ContainerFacts, ContainerFacts> Change, ContainerVerdict Expected)[]
            {
                ("plain chest", f => f, ContainerVerdict.Allowed),
                ("obliterator with a root override", f => { f.IsObliterator = true; f.IsVehicle = true; return f; }, ContainerVerdict.Obliterator),
                ("player tombstone", f => { f.IsPlayerTombstone = true; return f; }, ContainerVerdict.PlayerTombstone),
                ("cart or ship", f => { f.IsVehicle = true; return f; }, ContainerVerdict.Vehicle),
                ("obliterator", f => { f.IsObliterator = true; return f; }, ContainerVerdict.Obliterator),
                ("auto destroy empty", f => { f.AutoDestroyEmpty = true; return f; }, ContainerVerdict.AutoDestroyEmpty),
                ("dungeon chest", f => { f.PlacedByPlayer = false; f.CreatorId = 0; return f; }, ContainerVerdict.NotPlayerBuilt),
                ("tomb storage", f => { f.HaugbuiKind = StructureKind.Tomb; return f; }, ContainerVerdict.HaugbuiRole),
                ("collection chest", f => { f.HaugbuiKind = StructureKind.CollectionChest; return f; }, ContainerVerdict.HaugbuiRole),
                ("delivery chest", f => { f.HaugbuiKind = StructureKind.DeliveryChest; return f; }, ContainerVerdict.HaugbuiRole),
                ("own private chest", f => { f.Privacy = ContainerPrivacy.Private; return f; }, ContainerVerdict.Allowed),
                ("stranger's private chest", f => { f.Privacy = ContainerPrivacy.Private; f.CreatorId = Stranger; return f; }, ContainerVerdict.Private),
                ("stranger's public chest", f => { f.CreatorId = Stranger; return f; }, ContainerVerdict.Allowed),
                ("group chest", f => { f.Privacy = ContainerPrivacy.Group; return f; }, ContainerVerdict.Private),
            };
            foreach (var c in cases)
                Assert.That(ContainerRules.Check(c.Change(Chest()), Owner, ContainerUse.StorageDestination), Is.EqualTo(c.Expected), c.Name);
        }

        [Test]
        public void Collection_chest_is_a_source_but_never_a_destination()
        {
            ContainerFacts facts = Chest();
            facts.HaugbuiKind = StructureKind.CollectionChest;
            Assert.That(ContainerRules.Check(facts, Owner, ContainerUse.StorageSource), Is.EqualTo(ContainerVerdict.Allowed));
            Assert.That(ContainerRules.Check(facts, Owner, ContainerUse.StorageDestination), Is.EqualTo(ContainerVerdict.HaugbuiRole));
        }

        [Test]
        public void Wards()
        {
            var foreign = new WardFacts { Enabled = true, Covers = true, CreatorId = Stranger };
            var own = new WardFacts { Enabled = true, Covers = true, CreatorId = Owner };
            var permitted = new WardFacts { Enabled = true, Covers = true, CreatorId = Stranger, OwnerPermitted = true };
            var off = new WardFacts { Enabled = false, Covers = true, CreatorId = Stranger };
            var far = new WardFacts { Enabled = true, Covers = false, CreatorId = Stranger };

            Assert.That(WardAccess.Allows(new WardFacts[0], Owner), Is.True, "no ward");
            Assert.That(WardAccess.Allows(new[] { foreign }, Owner), Is.False, "foreign ward");
            Assert.That(WardAccess.Allows(new[] { own }, Owner), Is.True, "own ward");
            Assert.That(WardAccess.Allows(new[] { permitted }, Owner), Is.True, "permitted");
            Assert.That(WardAccess.Allows(new[] { off, far }, Owner), Is.True, "disabled or not covering");
            Assert.That(WardAccess.Allows(new[] { foreign, own }, Owner), Is.True, "like vanilla, one permitting ward is enough");
        }
    }
}
