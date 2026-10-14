using System.Linq;
using Haugbui.Core.Creature;
using Haugbui.Core.Storage;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class StackingRulesTests
    {
        [TearDown]
        public void Restore() => StackingRules.StrictItemData = true;

        private static ItemStack Cheated(int amount)
        {
            ItemStack stack = Items.Make("Wood", amount);
            stack.Cheated = true;
            return stack;
        }

        [Test]
        public void Strict_mode_keeps_cheated_and_clean_apart()
        {
            ContainerSnapshot chest = Items.Chest("x", 0, 1, Items.Make("Wood", 30));
            Assert.That(chest.RoomFor(Cheated(5)), Is.EqualTo(0));
            Assert.That(chest.Accept(Cheated(5), 5), Is.EqualTo(0));
            Assert.That(chest.Items.Single().Cheated, Is.False);
        }

        [Test]
        public void Vanilla_mode_a_cheated_item_makes_the_stack_cheated()
        {
            StackingRules.StrictItemData = false;
            ContainerSnapshot chest = Items.Chest("x", 0, 1, Items.Make("Wood", 30));
            Assert.That(chest.RoomFor(Cheated(5)), Is.EqualTo(20));
            Assert.That(chest.Accept(Cheated(5), 5), Is.EqualTo(5));
            Assert.That(chest.Items.Single().Amount, Is.EqualTo(35));
            Assert.That(chest.Items.Single().Cheated, Is.True);
        }

        [Test]
        public void Vanilla_mode_never_clears_a_cheated_stack()
        {
            StackingRules.StrictItemData = false;
            ContainerSnapshot chest = Items.Chest("x", 0, 1, Cheated(30));
            Assert.That(chest.Accept(Items.Make("Wood", 5), 5), Is.EqualTo(5));
            Assert.That(chest.Items.Single().Cheated, Is.True);
        }

        [Test]
        public void Vanilla_mode_keeps_the_stack_data_and_still_needs_same_quality_and_world_level()
        {
            StackingRules.StrictItemData = false;
            ItemStack tagged = Items.Make("Wood", 10);
            tagged.CustomData["k"] = "v";
            ContainerSnapshot chest = Items.Chest("x", 0, 1, Items.Make("Wood", 30));
            Assert.That(chest.Accept(tagged, 10), Is.EqualTo(10));
            Assert.That(chest.Items.Single().CustomData, Is.Empty, "the stack keeps its own custom data");

            ContainerSnapshot quality = Items.Chest("q", 0, 1, Items.Make("Wood", 30, quality: 2));
            Assert.That(quality.RoomFor(Items.Make("Wood", 5)), Is.EqualTo(0));
        }

        [Test]
        public void Carried_inventory_stays_strict_in_both_modes()
        {
            StackingRules.StrictItemData = false;
            var carried = new CarriedInventory(2, 1000);
            carried.Add(Items.Make("Wood", 5));
            carried.Add(Cheated(5));
            Assert.That(carried.Stacks.Count, Is.EqualTo(2));
            Assert.That(carried.Stacks.Count(s => s.Cheated), Is.EqualTo(1));
        }
    }
}
