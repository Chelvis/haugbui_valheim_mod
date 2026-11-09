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
    public class DeliveryLedgerTests
    {
        [Test]
        public void Spec_example_can_be_entered()
        {
            var ledger = new DeliveryLedger();
            Assert.That(ledger.Request("Wood", 100), Is.EqualTo(RequestEditResult.Ok));
            Assert.That(ledger.Request("Stone", 200), Is.EqualTo(RequestEditResult.Ok));
            Assert.That(ledger.Request("Resin", 50), Is.EqualTo(RequestEditResult.Ok));
            Assert.That(ledger.Request("Iron", 30), Is.EqualTo(RequestEditResult.Ok));
            Assert.That(ledger.Lines.Select(l => l.Remaining), Is.EqualTo(new[] { 100, 200, 50, 30 }));
        }

        [Test]
        public void Never_counts_more_than_requested()
        {
            var ledger = new DeliveryLedger();
            ledger.Request("Wood", 10);
            Assert.That(ledger.RecordDelivered("Wood", 7), Is.EqualTo(7));
            Assert.That(ledger.RecordDelivered("Wood", 7), Is.EqualTo(3));
            Assert.That(ledger.Remaining("Wood"), Is.EqualTo(0));
            Assert.That(ledger.Lines, Is.Empty, "a fulfilled quota leaves the list");
            Assert.That(ledger.RecordDelivered("Wood", 1), Is.EqualTo(0));
        }

        [Test]
        public void Saving_a_new_amount_starts_the_quota_over()
        {
            var ledger = new DeliveryLedger();
            ledger.Request("Wood", 10);
            ledger.RecordDelivered("Wood", 10);
            Assert.That(ledger.Lines, Is.Empty);
            Assert.That(ledger.Request("Wood", 20), Is.EqualTo(RequestEditResult.Ok));
            DeliveryRequest line = ledger.Lines.Single();
            Assert.That(line.Delivered, Is.EqualTo(0));
            Assert.That(line.Requested, Is.EqualTo(20));
            Assert.That(ledger.Remaining("Wood"), Is.EqualTo(20));

            ledger.RecordDelivered("Wood", 4);
            Assert.That(ledger.SetRequested(0, 20), Is.EqualTo(RequestEditResult.Ok));
            Assert.That(ledger.Lines.Single().Delivered, Is.EqualTo(0));
            Assert.That(ledger.Remaining("Wood"), Is.EqualTo(20), "the new 20 is not reduced by the old delivery");
        }

        [Test]
        public void Deleting_a_line_forgets_what_it_had_delivered()
        {
            var ledger = new DeliveryLedger();
            ledger.Request("Wood", 10);
            ledger.RecordDelivered("Wood", 4);
            Assert.That(ledger.Replace(new RequestDraft[0]), Is.EqualTo(RequestEditResult.Ok));
            Assert.That(ledger.Request("Wood", 10), Is.EqualTo(RequestEditResult.Ok));
            Assert.That(ledger.Lines.Single().Delivered, Is.EqualTo(0));
            Assert.That(ledger.Remaining("Wood"), Is.EqualTo(10));

            ledger.RecordDelivered("Wood", 6);
            Assert.That(ledger.Replace(new[] { new RequestDraft("Wood", 10) }), Is.EqualTo(RequestEditResult.Ok));
            Assert.That(ledger.Lines.Single().Delivered, Is.EqualTo(0));
            Assert.That(ledger.Remaining("Wood"), Is.EqualTo(10));
        }

        [Test]
        public void Keep_amount_follows_the_chest_and_ignores_delivered()
        {
            var ledger = new DeliveryLedger();
            Assert.That(ledger.Request("Wood", 10, keepAmount: true), Is.EqualTo(RequestEditResult.Ok));
            DeliveryRequest line = ledger.Lines.Single();
            Assert.That(line.KeepAmount, Is.True);
            Assert.That(DeliveryLedger.Owed(line, 0), Is.EqualTo(10));
            Assert.That(DeliveryLedger.Owed(line, 4), Is.EqualTo(6));
            Assert.That(DeliveryLedger.Owed(line, 10), Is.EqualTo(0));
            Assert.That(DeliveryLedger.Owed(line, 12), Is.EqualTo(0), "a surplus is not a debt, and it is not removed here");
            Assert.That(ledger.RecordDelivered("Wood", 6), Is.EqualTo(6));
            Assert.That(ledger.Lines.Single().Delivered, Is.EqualTo(0));
            Assert.That(DeliveryLedger.Owed(ledger.Lines.Single(), 0), Is.EqualTo(10), "history does not reduce a keep-amount line");
            Assert.That(ledger.Lines.Count, Is.EqualTo(1));
        }

        [Test]
        public void Request_for_an_open_item_edits_that_line()
        {
            var ledger = new DeliveryLedger();
            ledger.Request("Wood", 10);
            ledger.RecordDelivered("Wood", 3);
            ledger.Request("Wood", 25);
            Assert.That(ledger.Lines.Count, Is.EqualTo(1));
            Assert.That(ledger.Lines[0].Requested, Is.EqualTo(25));
            Assert.That(ledger.Lines[0].Delivered, Is.EqualTo(0));
        }

        [Test]
        public void A_fulfilled_quota_is_removed_and_the_next_line_starts_at_zero()
        {
            var ledger = new DeliveryLedger();
            ledger.Request("Wood", 5);
            ledger.RecordDelivered("Wood", 5);
            Assert.That(ledger.Lines, Is.Empty);
            ledger.Request("Wood", 3);
            Assert.That(ledger.Lines.Count, Is.EqualTo(1));
            Assert.That(ledger.Lines.Single().Delivered, Is.EqualTo(0));
            Assert.That(ledger.Lines.Single().Remaining, Is.EqualTo(3));
            Assert.That(ledger.ClearCompleted(), Is.EqualTo(0));
        }

        [Test]
        public void Replace_starts_over_and_drops_lines_left_out()
        {
            var ledger = new DeliveryLedger();
            ledger.Request("Wood", 100);
            ledger.RecordDelivered("Wood", 40);
            ledger.Request("Stone", 10);
            Assert.That(ledger.Replace(new[] { new RequestDraft("Wood", 0) }), Is.EqualTo(RequestEditResult.InvalidAmount));
            Assert.That(ledger.Lines.Select(l => l.ItemPrefab), Is.EqualTo(new[] { "Wood", "Stone" }));
            Assert.That(ledger.Lines.Single(l => l.ItemPrefab == "Wood").Delivered, Is.EqualTo(40), "a refused edit keeps the old count");

            Assert.That(ledger.Replace(new[] { new RequestDraft("Wood", 30), new RequestDraft("Resin", 5, keepAmount: true) }), Is.EqualTo(RequestEditResult.Ok));
            DeliveryRequest wood = ledger.Lines.Single(l => l.ItemPrefab == "Wood");
            Assert.That(wood.Delivered, Is.EqualTo(0));
            Assert.That(wood.Requested, Is.EqualTo(30));
            Assert.That(wood.KeepAmount, Is.False);
            Assert.That(ledger.Lines.Any(l => l.ItemPrefab == "Stone"), Is.False);
            DeliveryRequest resin = ledger.Lines.Single(l => l.ItemPrefab == "Resin");
            Assert.That(resin.Requested, Is.EqualTo(5));
            Assert.That(resin.KeepAmount, Is.True);

            Assert.That(ledger.Replace(new RequestDraft[0]), Is.EqualTo(RequestEditResult.Ok));
            Assert.That(ledger.Lines, Is.Empty);

            var tooMany = new List<RequestDraft>();
            for (int i = 0; i < DeliveryLedger.MaxLines + 1; i++) tooMany.Add(new RequestDraft("Item" + i, 1));
            ledger.Request("Wood", 1);
            Assert.That(ledger.Replace(tooMany), Is.EqualTo(RequestEditResult.TooManyLines));
            Assert.That(ledger.Lines.Single().ItemPrefab, Is.EqualTo("Wood"));
        }

        [Test]
        public void Limits_and_cancel()
        {
            var ledger = new DeliveryLedger();
            for (int i = 0; i < DeliveryLedger.MaxLines; i++) Assert.That(ledger.Request("Item" + i, 1), Is.EqualTo(RequestEditResult.Ok));
            Assert.That(ledger.Request("Extra", 1), Is.EqualTo(RequestEditResult.TooManyLines));
            Assert.That(ledger.Request("Item0", 0), Is.EqualTo(RequestEditResult.InvalidAmount));
            Assert.That(ledger.Request("", 5), Is.EqualTo(RequestEditResult.InvalidItem));
            Assert.That(ledger.Cancel(0), Is.EqualTo(RequestEditResult.Ok));
            Assert.That(ledger.Cancel(42), Is.EqualTo(RequestEditResult.NoSuchLine));
            Assert.That(ledger.Lines.Count, Is.EqualTo(DeliveryLedger.MaxLines - 1));
        }

        [Test]
        public void Survives_serialization()
        {
            var ledger = new DeliveryLedger();
            ledger.Request("Wood", 100);
            ledger.RecordDelivered("Wood", 40);
            ledger.Request("Resin", 8, keepAmount: true);
            byte[] blob = DeliveryRequestsSerializer.Instance.Serialize(ledger.ToList());
            Assert.That(DeliveryRequestsSerializer.Instance.TryDeserialize(blob, out List<DeliveryRequest> read), Is.EqualTo(BlobReadStatus.Ok));
            var back = new DeliveryLedger(read);
            Assert.That(back.Remaining("Wood"), Is.EqualTo(60));
            DeliveryRequest resin = back.Lines.Single(l => l.ItemPrefab == "Resin");
            Assert.That(resin.KeepAmount, Is.True);
            Assert.That(DeliveryLedger.Owed(resin, 3), Is.EqualTo(5));
        }

        [Test]
        public void Reserved_totals_sum_open_lines_of_every_chest()
        {
            var a = new DeliveryLedger();
            a.Request("Wood", 100);
            a.RecordDelivered("Wood", 30);
            var b = new DeliveryLedger();
            b.Request("Wood", 5);
            b.Request("Stone", 2);
            b.RecordDelivered("Stone", 2);
            b.Request("Resin", 9, keepAmount: true);
            Dictionary<string, int> reserved = DeliveryLedger.Reserved(new[]
            {
                (a, (Func<string, int>)null),
                (b, (Func<string, int>)(prefab => prefab == "Resin" ? 4 : 0)),
            });
            Assert.That(reserved["Wood"], Is.EqualTo(75));
            Assert.That(reserved["Resin"], Is.EqualTo(5));
            Assert.That(reserved.ContainsKey("Stone"), Is.False);
        }
    }

    public class DeliveryPlannerTests
    {
        private static ContainerSnapshot Target(int slots, params ItemStack[] items) => Items.Chest("delivery", 0, slots, items);

        [Test]
        public void Never_takes_more_than_remaining()
        {
            ContainerSnapshot source = Items.Chest("s", 5, 10, Items.Make("Wood", 50), Items.Make("Wood", 50));
            DeliveryTrip trip = DeliveryPlanner.Plan("Wood", 30, Target(10), new CarriedInventory(8, 1000), new[] { source }, Vec3.Zero);
            Assert.That(trip.Takes.Sum(t => t.Item.Amount), Is.EqualTo(30));
            Assert.That(trip.Total, Is.EqualTo(30));
        }

        [Test]
        public void Insufficient_stock_delivers_what_exists()
        {
            ContainerSnapshot a = Items.Chest("a", 5, 10, Items.Make("Iron", 12, maxStack: 30));
            ContainerSnapshot b = Items.Chest("b", 9, 10, Items.Make("Iron", 6, maxStack: 30));
            DeliveryTrip trip = DeliveryPlanner.Plan("Iron", 30, Target(10), new CarriedInventory(8, 1000), new[] { a, b }, Vec3.Zero);
            Assert.That(trip.Total, Is.EqualTo(18));
            Assert.That(trip.Takes.Select(t => t.Source), Is.EqualTo(new[] { "a", "b" }), "nearest first");
        }

        [Test]
        public void Chest_room_and_carry_limits_cap_the_trip()
        {
            ContainerSnapshot source = Items.Chest("s", 5, 10, Items.Make("Stone", 50), Items.Make("Stone", 50), Items.Make("Stone", 50));
            ContainerSnapshot full = Target(1, Items.Make("Stone", 45));
            Assert.That(DeliveryPlanner.Plan("Stone", 200, full, new CarriedInventory(8, 1000), new[] { source }, Vec3.Zero).Total, Is.EqualTo(5));
            var weak = new CarriedInventory(8, 60);
            Assert.That(DeliveryPlanner.Plan("Stone", 200, Target(10), weak, new[] { source }, Vec3.Zero).Total, Is.EqualTo(60));
        }

        [Test]
        public void Cheated_items_keep_their_own_stack_in_the_chest()
        {
            ItemStack cheated = Items.Make("Wood", 10);
            cheated.Cheated = true;
            ContainerSnapshot source = Items.Chest("s", 5, 10, cheated);
            ContainerSnapshot target = Target(1, Items.Make("Wood", 20));
            Assert.That(DeliveryPlanner.Plan("Wood", 10, target, new CarriedInventory(8, 1000), new[] { source }, Vec3.Zero).Total, Is.EqualTo(0),
                "the only slot holds clean wood");
            DeliveryTrip trip = DeliveryPlanner.Plan("Wood", 10, Target(2, Items.Make("Wood", 20)), new CarriedInventory(8, 1000), new[] { source }, Vec3.Zero);
            Assert.That(trip.Puts.Single().Cheated && trip.Total == 10);
        }

        [Test]
        public void Carried_items_count_first()
        {
            var carried = new CarriedInventory(8, 1000);
            carried.Add(Items.Make("Resin", 20));
            ContainerSnapshot source = Items.Chest("s", 5, 10, Items.Make("Resin", 50));
            DeliveryTrip trip = DeliveryPlanner.Plan("Resin", 25, Target(10), carried, new[] { source }, Vec3.Zero);
            Assert.That(trip.Takes.Single().Item.Amount, Is.EqualTo(5));
            Assert.That(trip.Total, Is.EqualTo(25));
        }

        [Test]
        public void One_trip_loads_every_line_that_fits()
        {
            ContainerSnapshot source = Items.Chest("s", 1, 10, Items.Make("Wood", 20), Items.Make("Stone", 20));
            DeliveryTrip trip = DeliveryPlanner.Combine(new[] { ("Wood", 10), ("Stone", 10) }, Target(10),
                new CarriedInventory(4, 100), new[] { source }, Vec3.Zero);
            Assert.That(trip.Total, Is.EqualTo(20));
            Assert.That(trip.Takes.Sum(t => t.Item.Amount), Is.EqualTo(20));
            Assert.That(trip.Takes.Sum(t => t.Item.Prefab == "Wood" ? t.Item.Amount : 0), Is.EqualTo(10));
        }

        [Test]
        public void A_full_bag_does_not_take_another_delivery_line()
        {
            var carried = new CarriedInventory(2, 10);
            carried.Add(Items.Make("Wood", 10));
            ContainerSnapshot source = Items.Chest("s", 1, 10, Items.Make("Stone", 20));
            DeliveryTrip trip = DeliveryPlanner.Combine(new[] { ("Wood", 10), ("Stone", 10) }, Target(10), carried,
                new[] { source }, Vec3.Zero);
            Assert.That(trip.Puts.Where(p => p.Prefab == "Wood").Sum(p => p.Amount), Is.EqualTo(10));
            Assert.That(trip.Takes.Sum(t => t.Item.Amount), Is.EqualTo(0));
        }

        [Test]
        public void Ordinary_chest_keeps_one_unless_the_filter_lists_it()
        {
            ContainerSnapshot ordinary = Items.Chest("s", 5, 10, Items.Make("Wood", 5));
            ordinary.KeepSeed = true;
            Assert.That(DeliveryPlanner.Plan("Wood", 30, Target(10), new CarriedInventory(8, 1000), new[] { ordinary }, Vec3.Zero).Total, Is.EqualTo(4));

            ContainerSnapshot last = Items.Chest("last", 5, 10, Items.Make("Wood", 1));
            last.KeepSeed = true;
            Assert.That(DeliveryPlanner.Plan("Wood", 30, Target(10), new CarriedInventory(8, 1000), new[] { last }, Vec3.Zero).Total, Is.EqualTo(0));

            ContainerSnapshot listed = Items.Chest("listed", 5, 10, Items.Make("Wood", 3));
            listed.KeepSeed = true;
            listed.Filter = new List<string> { "Wood" };
            Assert.That(DeliveryPlanner.Plan("Wood", 30, Target(10), new CarriedInventory(8, 1000), new[] { listed }, Vec3.Zero).Total, Is.EqualTo(3));

            ContainerSnapshot other = Items.Chest("other", 5, 10, Items.Make("Wood", 2));
            other.KeepSeed = true;
            other.Filter = new List<string> { "Stone" };
            Assert.That(DeliveryPlanner.Plan("Wood", 30, Target(10), new CarriedInventory(8, 1000), new[] { other }, Vec3.Zero).Total, Is.EqualTo(1));

            ContainerSnapshot emptied = Items.Chest("collection", 5, 10, Items.Make("Wood", 3));
            Assert.That(DeliveryPlanner.Plan("Wood", 30, Target(10), new CarriedInventory(8, 1000), new[] { emptied }, Vec3.Zero).Total, Is.EqualTo(3));
        }

        [Test]
        public void Delivery_chest_only_receives_and_never_gives()
        {
            var facts = new ContainerFacts { PlacedByPlayer = true, CreatorId = 1, Privacy = ContainerPrivacy.Public, HaugbuiKind = StructureKind.DeliveryChest };
            Assert.That(ContainerRules.Check(facts, 1, ContainerUse.DeliveryTarget), Is.EqualTo(ContainerVerdict.Allowed));
            Assert.That(ContainerRules.Check(facts, 1, ContainerUse.StorageSource), Is.EqualTo(ContainerVerdict.HaugbuiRole));
            Assert.That(ContainerRules.Check(facts, 1, ContainerUse.StorageDestination), Is.EqualTo(ContainerVerdict.HaugbuiRole));
            facts.HaugbuiKind = StructureKind.None;
            Assert.That(ContainerRules.Check(facts, 1, ContainerUse.DeliveryTarget), Is.EqualTo(ContainerVerdict.HaugbuiRole));
            facts.HaugbuiKind = StructureKind.Tomb;
            Assert.That(ContainerRules.Check(facts, 1, ContainerUse.StorageSource), Is.EqualTo(ContainerVerdict.HaugbuiRole), "tomb storage is never a delivery source");
        }
    }
}
