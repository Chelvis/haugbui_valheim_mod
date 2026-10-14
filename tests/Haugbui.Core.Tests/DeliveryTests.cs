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
            Assert.That(DeliveryLedger.IsComplete(ledger.Lines.Single()));
            Assert.That(ledger.RecordDelivered("Wood", 1), Is.EqualTo(0));
            Assert.That(ledger.Lines.Single().Delivered, Is.EqualTo(10));
        }

        [Test]
        public void Editing_below_delivered_completes_the_line()
        {
            var ledger = new DeliveryLedger();
            ledger.Request("Stone", 200);
            ledger.RecordDelivered("Stone", 80);
            Assert.That(ledger.SetRequested(0, 50), Is.EqualTo(RequestEditResult.Ok));
            DeliveryRequest line = ledger.Lines.Single();
            Assert.That(line.Requested == 80 && line.Delivered == 80 && DeliveryLedger.IsComplete(line));
            Assert.That(ledger.SetRequested(0, 120), Is.EqualTo(RequestEditResult.Ok));
            Assert.That(ledger.Remaining("Stone"), Is.EqualTo(40), "raising it reopens the line");
        }

        [Test]
        public void Request_for_an_open_item_edits_that_line()
        {
            var ledger = new DeliveryLedger();
            ledger.Request("Wood", 10);
            ledger.Request("Wood", 25);
            Assert.That(ledger.Lines.Count, Is.EqualTo(1));
            Assert.That(ledger.Lines[0].Requested, Is.EqualTo(25));
        }

        [Test]
        public void Completed_line_stays_until_cleared_and_a_new_one_can_be_opened()
        {
            var ledger = new DeliveryLedger();
            ledger.Request("Wood", 5);
            ledger.RecordDelivered("Wood", 5);
            ledger.Request("Wood", 3);
            Assert.That(ledger.Lines.Count, Is.EqualTo(2));
            Assert.That(ledger.ClearCompleted(), Is.EqualTo(1));
            Assert.That(ledger.Lines.Single().Remaining, Is.EqualTo(3));
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
            byte[] blob = DeliveryRequestsSerializer.Instance.Serialize(ledger.ToList());
            Assert.That(DeliveryRequestsSerializer.Instance.TryDeserialize(blob, out List<DeliveryRequest> read), Is.EqualTo(BlobReadStatus.Ok));
            var back = new DeliveryLedger(read);
            Assert.That(back.Remaining("Wood"), Is.EqualTo(60));
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
            Dictionary<string, int> reserved = DeliveryLedger.Reserved(new[] { a, b });
            Assert.That(reserved["Wood"], Is.EqualTo(75));
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
