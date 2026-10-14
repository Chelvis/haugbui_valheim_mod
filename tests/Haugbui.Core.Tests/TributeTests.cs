using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Core.Tribute;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class WorldClockTests
    {
        private const double Day = 1800;

        [TestCase(0.25f)]
        [TestCase(0.5f)]
        [TestCase(0.75f)]
        [TestCase(0.9f)]
        [TestCase(0.1f)]
        public void Unscale_inverts_the_game_rescale(float fraction) =>
            Assert.That(WorldClock.Rescale(WorldClock.Unscale(fraction)), Is.EqualTo(fraction).Within(1e-6));

        [TestCase(0.10, true)]
        [TestCase(0.14, true)]
        [TestCase(0.16, false)]
        [TestCase(0.84, false)]
        [TestCase(0.86, true)]
        public void Night_is_thirty_percent_of_the_raw_day(double raw, bool night) =>
            Assert.That(WorldClock.IsNight((10 + raw) * Day, Day), Is.EqualTo(night));

        [Test]
        public void Shift_ids_day_night_and_rollover()
        {
            Assert.That(WorldClock.ShiftId(10.5 * Day, Day), Is.EqualTo(20), "day shift of day 10");
            Assert.That(WorldClock.ShiftId(10.9 * Day, Day), Is.EqualTo(21), "night shift of day 10");
            Assert.That(WorldClock.ShiftId(11.05 * Day, Day), Is.EqualTo(21), "after midnight: still the night that began on day 10");
            Assert.That(WorldClock.ShiftId(11.2 * Day, Day), Is.EqualTo(22));
            Assert.That(WorldClock.IsNightShift(21) && !WorldClock.IsNightShift(22));
            Assert.That(WorldClock.ShiftId(0.05 * Day, Day), Is.EqualTo(-1), "first night of a new world");
            Assert.That(WorldClock.IsNightShift(-1));
        }

        [Test]
        public void Shift_bounds()
        {
            Assert.That(WorldClock.ShiftStart(21, Day), Is.EqualTo(10.85 * Day).Within(1e-6));
            Assert.That(WorldClock.ShiftEnd(21, Day), Is.EqualTo(11.15 * Day).Within(1e-6));
            Assert.That(WorldClock.ShiftStart(20, Day), Is.EqualTo(10.15 * Day).Within(1e-6));
            Assert.That(WorldClock.ShiftEnd(20, Day), Is.EqualTo(10.85 * Day).Within(1e-6));
            Assert.That(WorldClock.ShiftEnd(-1, Day), Is.EqualTo(0.15 * Day).Within(1e-6));
        }

        [Test]
        public void Next_dusk_is_always_in_the_future()
        {
            Assert.That(WorldClock.NextDusk(10.5 * Day, Day), Is.EqualTo(10.85 * Day).Within(1e-6));
            Assert.That(WorldClock.NextDusk(10.9 * Day, Day), Is.EqualTo(11.85 * Day).Within(1e-6));
            Assert.That(WorldClock.NextDusk(11.05 * Day, Day), Is.EqualTo(11.85 * Day).Within(1e-6));
            Assert.That(WorldClock.DuskAtOrAfter(10.85 * Day, Day), Is.EqualTo(10.85 * Day).Within(1e-6));
        }
    }

    public class TributeTests
    {
        [Test]
        public void Default_costs_and_values()
        {
            TributeTable table = TributeTable.Default;
            Assert.That(Enumerable.Range(1, 6).Select(table.CostFor), Is.EqualTo(new[] { 5, 5, 10, 10, 15, 15 }));
            Assert.That(table.Values.Select(v => $"{v.Key}:{v.Value}"),
                Is.EqualTo(new[] { "Coins:1", "Amber:5", "AmberPearl:10", "Ruby:20", "SilverNecklace:30" }));
            Assert.That(table.ValueOf("Wood"), Is.EqualTo(0));
        }

        [Test]
        public void Config_round_trips_and_falls_back()
        {
            TributeTable custom = TributeTable.FromConfig("1,2,3,4,5,6", "Coins:2,Ruby:40", out List<string> warnings);
            Assert.That(warnings, Is.Empty);
            Assert.That(custom.CostFor(3), Is.EqualTo(3));
            Assert.That(custom.ValueOf("Ruby"), Is.EqualTo(40));

            TributeTable broken = TributeTable.FromConfig("1,2,x", "Coins", out warnings);
            Assert.That(warnings.Count, Is.EqualTo(2));
            Assert.That(broken.CostFor(6), Is.EqualTo(15));
            Assert.That(broken.ValueOf("SilverNecklace"), Is.EqualTo(30));

            string defaults = TributeTable.ValuesToConfig(TributeTable.DefaultValues);
            Assert.That(TributeTable.FromConfig(TributeTable.CostsToCsv(TributeTable.DefaultCosts), defaults, out warnings).Values.Count, Is.EqualTo(5));
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void Balance_spends_cheated_value_first()
        {
            var balance = new TributeBalance();
            balance.Offer(10, cheated: true);
            balance.Offer(20, cheated: false);
            Assert.That(balance.TryCharge(15), Is.True);
            Assert.That((balance.Total, balance.Cheated, balance.Clean), Is.EqualTo((15, 0, 15)));
            Assert.That(balance.TryCharge(16), Is.False, "never below zero");
            Assert.That(balance.TakeAll(), Is.EqualTo((15, 0)));
            Assert.That(balance.Total, Is.EqualTo(0));
        }

        [Test]
        public void Refund_keeps_the_cheated_part_cheated()
        {
            var balance = new TributeBalance();
            balance.Offer(30, cheated: true);
            balance.Offer(5, cheated: false);
            balance.TryCharge(10);
            Assert.That(balance.TakeAll(), Is.EqualTo((5, 20)), "clean coins never exceed what was offered clean");
        }

        [Test]
        public void Shift_is_charged_once_and_only_with_work()
        {
            Assert.That(ShiftRules.Decide(true, 21, WorldClock.NoShift, hasWork: false, balance: 100, cost: 5), Is.EqualTo(ShiftDecision.NoWork));
            Assert.That(ShiftRules.Decide(true, 21, WorldClock.NoShift, hasWork: true, balance: 100, cost: 5), Is.EqualTo(ShiftDecision.Charge));
            Assert.That(ShiftRules.Decide(true, 21, 21, hasWork: true, balance: 0, cost: 5), Is.EqualTo(ShiftDecision.AlreadyPaid));
            Assert.That(ShiftRules.Decide(true, 23, 21, hasWork: true, balance: 4, cost: 5), Is.EqualTo(ShiftDecision.NoTribute));
            Assert.That(ShiftRules.Decide(false, 23, 21, hasWork: false, balance: 0, cost: 5), Is.EqualTo(ShiftDecision.Free));
            Assert.That(ShiftRules.MayWork(ShiftDecision.NoTribute) || ShiftRules.MayWork(ShiftDecision.NoWork), Is.False);
        }

        [Test]
        public void Old_tomb_blobs_load_with_no_balance_and_no_slot()
        {
            byte[] v2 = new BlobWriter(2).Write("g").Write(7L).Write((int)TombToggles.Defaults).Write(-1f).Write(double.NaN).Write(3)
                .Write(0).Write(1f).Write(2f).Write(3f).Write(10.0).ToArray();
            Assert.That(TombStateSerializer.Instance.TryDeserialize(v2, out TombState state), Is.EqualTo(BlobReadStatus.Ok));
            Assert.That((state.Balance, state.CheatedBalance, state.SlotIndex), Is.EqualTo((0, 0, -1)));
            Assert.That(state.LastPaidShift, Is.EqualTo(WorldClock.NoShift));
            Assert.That(double.IsNaN(state.StartTime), "old tombs keep working at once");
        }

        [Test]
        public void Tomb_state_v3_round_trips()
        {
            var state = new TombState { Guid = "g", SlotIndex = 2, Balance = 35, CheatedBalance = 5, LastPaidShift = 4321, StartTime = 99.5 };
            TombStateSerializer.Instance.TryDeserialize(TombStateSerializer.Instance.Serialize(state), out TombState read);
            Assert.That((read.SlotIndex, read.Balance, read.CheatedBalance, read.LastPaidShift, read.StartTime),
                Is.EqualTo((2, 35, 5, 4321L, 99.5)));
        }
    }

    public class ServantSlotTests
    {
        [Test]
        public void Names()
        {
            Assert.That(ServantSlots.DefaultName(0), Is.EqualTo("Haugbui"));
            Assert.That(ServantSlots.DefaultName(1), Is.EqualTo("Haugbui #2"));
            Assert.That(ServantSlots.DefaultName(2), Is.EqualTo("Haugbui #3"));
        }

        [Test]
        public void Encoding_survives_lower_casing()
        {
            var slot = new ServantSlot
            {
                Index = 1, Name = "Haugbui #2 Ætt", TombGuid = "ABCdef", DeathTime = 1234.5, Toggles = TombToggles.Harvesting
            };
            string text = ServantSlots.Encode(slot);
            Assert.That(text, Is.EqualTo(text.ToLowerInvariant()));
            Assert.That(text, Does.Not.Contain(" "));
            Assert.That(ServantSlots.Decode(text.ToLowerInvariant(), out ServantSlot read), Is.EqualTo(BlobReadStatus.Ok));
            Assert.That((read.Index, read.Name, read.TombGuid, read.DeathTime, read.Toggles),
                Is.EqualTo((1, "Haugbui #2 Ætt", "ABCdef", 1234.5, TombToggles.Harvesting)));
            Assert.That(ServantSlots.Decode("zz", out _), Is.EqualTo(BlobReadStatus.Corrupt));
            Assert.That(ServantSlots.Decode("", out _), Is.EqualTo(BlobReadStatus.Missing));
        }

        [Test]
        public void Binding_takes_the_lowest_vacant_slot()
        {
            var slots = new Dictionary<int, ServantSlot>
            {
                [0] = new ServantSlot { Index = 0, TombGuid = "a" },
                [1] = new ServantSlot { Index = 1, TombGuid = "" , DeathTime = 50 },
                [2] = new ServantSlot { Index = 2, TombGuid = "c" },
            };
            Assert.That(ServantSlots.LowestVacant(slots, 3, g => true), Is.EqualTo(1));
            slots[1].TombGuid = "b";
            Assert.That(ServantSlots.LowestVacant(slots, 3, g => true), Is.EqualTo(-1), "fourth tomb refused at the default limit");
            Assert.That(ServantSlots.LowestVacant(slots, 4, g => true), Is.EqualTo(3), "slots exist from the start");
            Assert.That(ServantSlots.LowestVacant(slots, 3, g => g != "a"), Is.EqualTo(0), "a slot whose tomb is gone is vacant");
        }

        [Test]
        public void Key_names_parse_back()
        {
            string key = ServantSlots.KeyName(-42, 2);
            Assert.That(key, Is.EqualTo(key.ToLowerInvariant()));
            Assert.That(ServantSlots.TryParseIndex(key, -42, out int index) && index == 2);
            Assert.That(ServantSlots.TryParseIndex(key, 42, out _), Is.False);
        }
    }
}
