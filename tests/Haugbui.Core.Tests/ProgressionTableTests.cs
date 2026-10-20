using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Progression;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class ProgressionTableTests
    {
        private static string Csv(float[] values) => ProgressionTable.ToCsv(values);
        private static string Csv(int[] values) => ProgressionTable.ToCsv(values);

        private static ProgressionTable Parse(
            string carry = null, string speed = null, string radius = null, string slots = null, string hp = null, string combat = null)
        {
            return ProgressionTable.FromCsv(
                carry ?? Csv(ProgressionTable.DefaultCarryCapacity),
                speed ?? Csv(ProgressionTable.DefaultMaxSpeed),
                radius ?? Csv(ProgressionTable.DefaultRadius),
                slots ?? Csv(ProgressionTable.DefaultSlots),
                hp ?? Csv(ProgressionTable.DefaultMaxHealth),
                combat ?? Csv(ProgressionTable.DefaultCombatMultiplier),
                out _);
        }

        [Test]
        public void Defaults_match_the_requirements_table()
        {
            HaugbuiAttributes l1 = ProgressionTable.Default.ForLevel(1);
            HaugbuiAttributes l6 = ProgressionTable.Default.ForLevel(6);

            Assert.That(l1.CarryCapacity, Is.EqualTo(50));
            Assert.That(l1.MaxSpeed, Is.EqualTo(1f));
            Assert.That(Enumerable.Range(1, 6).Select(l => ProgressionTable.Default.ForLevel(l).MaxSpeed),
                Is.EqualTo(new[] { 1f, 2f, 3f, 4f, 5f, 6f }));
            Assert.That(l1.Radius, Is.EqualTo(50));
            Assert.That(l1.Slots, Is.EqualTo(2));
            Assert.That(l1.MaxHealth, Is.EqualTo(20));

            Assert.That(l6.CarryCapacity, Is.EqualTo(150));
            Assert.That(l6.MaxSpeed, Is.EqualTo(6.0f));
            Assert.That(l6.Radius, Is.EqualTo(100));
            Assert.That(l6.Slots, Is.EqualTo(6));
            Assert.That(l6.MaxHealth, Is.EqualTo(120));

            Assert.That(Enumerable.Range(1, 6).Select(l => ProgressionTable.Default.ForLevel(l).CombatMultiplier),
                Is.EqualTo(new[] { 1.0f, 1.2f, 1.4f, 1.6f, 1.8f, 2.0f }));
        }

        [Test]
        public void Combat_multiplier_column_is_configurable_and_falls_back_alone()
        {
            Assert.That(Parse(combat: "1,1,1,1,1,3").ForLevel(6).CombatMultiplier, Is.EqualTo(3f));

            ProgressionTable bad = ProgressionTable.FromCsv(
                Csv(ProgressionTable.DefaultCarryCapacity), Csv(ProgressionTable.DefaultMaxSpeed), "1,2,3,4,5,6",
                Csv(ProgressionTable.DefaultSlots), Csv(ProgressionTable.DefaultMaxHealth), "1,2,0,4,5,6",
                out IReadOnlyList<string> warnings);
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.StartWith("CombatMultiplier"));
            Assert.That(bad.ForLevel(6).CombatMultiplier, Is.EqualTo(2f));
            Assert.That(bad.ForLevel(6).Radius, Is.EqualTo(6f));
        }

        [TestCase(0, 1)]
        [TestCase(-3, 1)]
        [TestCase(7, 6)]
        public void Level_is_clamped(int requested, int expected)
        {
            Assert.That(ProgressionTable.Default.ForLevel(requested).Level, Is.EqualTo(expected));
        }

        [Test]
        public void Default_csv_round_trips_without_warnings()
        {
            ProgressionTable.FromCsv(
                Csv(ProgressionTable.DefaultCarryCapacity), Csv(ProgressionTable.DefaultMaxSpeed),
                Csv(ProgressionTable.DefaultRadius), Csv(ProgressionTable.DefaultSlots),
                Csv(ProgressionTable.DefaultMaxHealth), Csv(ProgressionTable.DefaultCombatMultiplier), out IReadOnlyList<string> warnings);

            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void Custom_values_are_applied()
        {
            ProgressionTable table = Parse(radius: "10, 20, 30, 40, 50, 60.5");

            Assert.That(table.ForLevel(3).Radius, Is.EqualTo(30));
            Assert.That(table.ForLevel(6).Radius, Is.EqualTo(60.5f));
        }

        [TestCase("1,2,3")]
        [TestCase("1,2,3,4,5,6,7")]
        [TestCase("1,2,x,4,5,6")]
        [TestCase("1,2,-3,4,5,6")]
        [TestCase("1,2,0,4,5,6")]
        [TestCase("")]
        public void Invalid_column_falls_back_to_defaults_and_warns(string radius)
        {
            ProgressionTable table = ProgressionTable.FromCsv(
                Csv(ProgressionTable.DefaultCarryCapacity), Csv(ProgressionTable.DefaultMaxSpeed), radius,
                Csv(ProgressionTable.DefaultSlots), Csv(ProgressionTable.DefaultMaxHealth),
                Csv(ProgressionTable.DefaultCombatMultiplier), out IReadOnlyList<string> warnings);

            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.StartWith("Radius"));
            Assert.That(table.ForLevel(2).Radius, Is.EqualTo(60));
        }

        [Test]
        public void Invalid_column_does_not_reset_other_columns()
        {
            ProgressionTable table = Parse(carry: "1,2,3,4,5,6", radius: "bad");

            Assert.That(table.ForLevel(1).CarryCapacity, Is.EqualTo(1));
            Assert.That(table.ForLevel(1).Radius, Is.EqualTo(50));
        }

        [Test]
        public void Fractional_slots_are_rejected()
        {
            ProgressionTable table = ProgressionTable.FromCsv(
                Csv(ProgressionTable.DefaultCarryCapacity), Csv(ProgressionTable.DefaultMaxSpeed),
                Csv(ProgressionTable.DefaultRadius), "2,3,4.5,5,6,6", Csv(ProgressionTable.DefaultMaxHealth), Csv(ProgressionTable.DefaultCombatMultiplier),
                out IReadOnlyList<string> warnings);

            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(table.ForLevel(3).Slots, Is.EqualTo(4));
        }
    }
}
