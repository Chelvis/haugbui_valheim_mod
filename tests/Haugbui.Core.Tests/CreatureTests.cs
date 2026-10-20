using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class ItemRulesTests
    {
        private static readonly GameItemType[] Accepted =
        {
            GameItemType.Material, GameItemType.Consumable, GameItemType.Trophy, GameItemType.Misc, GameItemType.Fish,
            GameItemType.AmmoNonEquipable
        };

        [Test]
        public void Classification_matrix_over_every_item_type()
        {
            foreach (GameItemType type in Enum.GetValues(typeof(GameItemType)))
            {
                Assert.That(ItemRules.IsTransportable(type, equipable: true), Is.False, type + " equipable");
                Assert.That(ItemRules.IsTransportable(type, equipable: false), Is.EqualTo(Accepted.Contains(type)), type.ToString());
            }
        }
    }

    public class CarriedInventoryTests
    {
        private static ItemStack Wood(int amount) => new ItemStack { Prefab = "Wood", Amount = amount, UnitWeight = 2f, MaxStack = 50 };
        private static ItemStack Stone(int amount) => new ItemStack { Prefab = "Stone", Amount = amount, UnitWeight = 2f, MaxStack = 50 };
        private static ItemStack Resin(int amount) => new ItemStack { Prefab = "Resin", Amount = amount, UnitWeight = 0.3f, MaxStack = 100 };

        [Test]
        public void Weight_cap_limits_accepted_amount()
        {
            var inventory = new CarriedInventory(slots: 6, capacity: 50);
            Assert.That(inventory.Add(Wood(40)), Is.EqualTo(25));
            Assert.That(inventory.Weight, Is.EqualTo(50f));
            Assert.That(inventory.CanAccept(Wood(1)), Is.EqualTo(0));
        }

        [Test]
        public void Slot_cap_limits_distinct_stacks()
        {
            var inventory = new CarriedInventory(slots: 2, capacity: 1000);
            Assert.That(inventory.Add(Wood(10)), Is.EqualTo(10));
            Assert.That(inventory.Add(Stone(10)), Is.EqualTo(10));
            Assert.That(inventory.Add(Resin(5)), Is.EqualTo(0));
            Assert.That(inventory.Add(Wood(45)), Is.EqualTo(40), "fills the existing stack only");
        }

        [Test]
        public void Overflowing_a_stack_uses_a_new_slot()
        {
            var inventory = new CarriedInventory(slots: 3, capacity: 1000);
            inventory.Add(Wood(70));
            Assert.That(inventory.Stacks.Select(s => s.Amount), Is.EqualTo(new[] { 50, 20 }));
        }

        [Test]
        public void Restore_ignores_caps_so_a_level_drop_keeps_items()
        {
            var inventory = new CarriedInventory(slots: 1, capacity: 1);
            inventory.Restore(new[] { Wood(50), Stone(50) });
            Assert.That(inventory.Stacks.Sum(s => s.Amount), Is.EqualTo(100));
            Assert.That(inventory.CanAccept(Wood(1)), Is.EqualTo(0));
        }

        [Test]
        public void Changed_fires_on_add_and_take()
        {
            var inventory = new CarriedInventory(2, 100);
            int changes = 0;
            inventory.Changed += () => changes++;
            inventory.Add(Wood(1));
            inventory.TakeAll();
            inventory.TakeAll();
            Assert.That(changes, Is.EqualTo(2));
        }

        [Test]
        public void Stacks_round_trip()
        {
            var stacks = new List<ItemStack> { Wood(3), new ItemStack { Prefab = "TrophyDeer", Amount = 1, Quality = 2, Variant = 1, WorldLevel = 3 } };
            ItemStacksSerializer.Instance.TryDeserialize(ItemStacksSerializer.Instance.Serialize(stacks), out List<ItemStack> read);
            Assert.That(read.Select(s => (s.Prefab, s.Amount, s.Quality, s.Variant, s.WorldLevel)),
                Is.EqualTo(new[] { ("Wood", 3, 1, 0, 0), ("TrophyDeer", 1, 2, 1, 3) }));
        }
    }

    public class DepositPlannerTests
    {
        [Test]
        public void Split_conserves_totals_with_full_storage()
        {
            var items = new List<ItemStack>
            {
                new ItemStack { Prefab = "Wood", Amount = 30 }, new ItemStack { Prefab = "Stone", Amount = 20 }
            };
            int room = 35;
            List<ItemStack> overflow = DepositPlanner.Deposit(items, item =>
            {
                int take = Math.Min(room, item.Amount);
                room -= take;
                return take;
            });

            Assert.That(overflow.Select(s => (s.Prefab, s.Amount)), Is.EqualTo(new[] { ("Stone", 15) }));
            Assert.That(35 + overflow.Sum(s => s.Amount), Is.EqualTo(50));
        }

        [Test]
        public void Nothing_accepted_overflows_everything()
        {
            var items = new List<ItemStack> { new ItemStack { Prefab = "Wood", Amount = 7 } };
            Assert.That(DepositPlanner.Deposit(items, _ => 0).Single().Amount, Is.EqualTo(7));
        }

        [Test]
        public void Merge_combines_equal_stacks()
        {
            List<ItemStack> merged = DepositPlanner.Merge(
                new[] { new ItemStack { Prefab = "Wood", Amount = 2 } },
                new[] { new ItemStack { Prefab = "Wood", Amount = 3 }, new ItemStack { Prefab = "Wood", Amount = 1, Quality = 2 } });
            Assert.That(merged.Select(s => (s.Amount, s.Quality)), Is.EqualTo(new[] { (5, 1), (1, 2) }));
        }
    }

    public class StateMachineTests
    {
        private double _now;
        private HaugbuiStateMachine Machine(HaugbuiState initial = HaugbuiState.Dormant) => new HaugbuiStateMachine(() => _now, initial);

        [Test]
        public void Normal_night_cycle()
        {
            HaugbuiStateMachine sm = Machine();
            Assert.That(sm.TryEnter(HaugbuiState.Emerging), Is.True);
            Assert.That(sm.TryEnter(HaugbuiState.Idle), Is.True);
            Assert.That(sm.TryEnter(HaugbuiState.Traveling), Is.True);
            Assert.That(sm.TryEnter(HaugbuiState.Interacting), Is.True);
            sm.CompleteStep();
            Assert.That(sm.State, Is.EqualTo(HaugbuiState.Idle));
            Assert.That(sm.RequestReturn(), Is.True);
            Assert.That(sm.TryEnter(HaugbuiState.Entering), Is.True);
            Assert.That(sm.TryEnter(HaugbuiState.Dormant), Is.True);
            Assert.That(sm.IsOut, Is.False);
        }

        [Test]
        public void Returning_can_turn_back_when_it_should_stay_out()
        {
            HaugbuiStateMachine sm = Machine(HaugbuiState.Idle);
            sm.RequestReturn();
            Assert.That(sm.TryEnter(HaugbuiState.Idle), Is.True);
            Assert.That(sm.TryEnter(HaugbuiState.Traveling), Is.True);
        }

        [Test]
        public void Illegal_transitions_are_refused()
        {
            HaugbuiStateMachine sm = Machine();
            Assert.That(sm.TryEnter(HaugbuiState.Traveling), Is.False);
            Assert.That(sm.Kill(), Is.False, "nothing to kill while dormant");
            Assert.That(HaugbuiStateMachine.CanTransition(HaugbuiState.Entering, HaugbuiState.Idle), Is.False);
            Assert.That(HaugbuiStateMachine.CanTransition(HaugbuiState.Dead, HaugbuiState.Idle), Is.False);
        }

        [Test]
        public void Return_waits_for_the_atomic_step()
        {
            HaugbuiStateMachine sm = Machine(HaugbuiState.Idle);
            sm.TryEnter(HaugbuiState.Traveling);
            sm.TryEnter(HaugbuiState.Interacting);
            Assert.That(sm.RequestReturn(), Is.False);
            Assert.That(sm.State, Is.EqualTo(HaugbuiState.Interacting));
            sm.CompleteStep();
            Assert.That(sm.State, Is.EqualTo(HaugbuiState.Returning));
        }

        [Test]
        public void Fight_in_progress_ends_into_the_return()
        {
            HaugbuiStateMachine sm = Machine(HaugbuiState.Idle);
            Assert.That(sm.TryEnter(HaugbuiState.Combat), Is.True);
            Assert.That(sm.RequestReturn(), Is.False);
            Assert.That(sm.State, Is.EqualTo(HaugbuiState.Combat));
            Assert.That(sm.TryEnter(HaugbuiState.Traveling), Is.False);
            sm.CompleteStep();
            Assert.That(sm.State, Is.EqualTo(HaugbuiState.Returning));
        }

        [Test]
        public void No_new_fight_once_a_return_is_pending_or_under_way()
        {
            HaugbuiStateMachine returning = Machine(HaugbuiState.Idle);
            returning.RequestReturn();
            Assert.That(returning.State, Is.EqualTo(HaugbuiState.Returning));
            Assert.That(returning.TryEnter(HaugbuiState.Combat), Is.False);
            Assert.That(returning.TryEnter(HaugbuiState.Fleeing), Is.False);

            HaugbuiStateMachine pending = Machine(HaugbuiState.Idle);
            pending.TryEnter(HaugbuiState.Combat);
            pending.RequestReturn();
            pending.TryEnter(HaugbuiState.Fleeing);
            Assert.That(pending.TryEnter(HaugbuiState.Combat), Is.False);
        }

        [Test]
        public void Flight_can_turn_into_a_fight_and_back_to_work()
        {
            HaugbuiStateMachine sm = Machine(HaugbuiState.Traveling);
            Assert.That(sm.TryEnter(HaugbuiState.Fleeing), Is.True);
            Assert.That(sm.TryEnter(HaugbuiState.Combat), Is.True);
            sm.CompleteStep();
            Assert.That(sm.State, Is.EqualTo(HaugbuiState.Idle));
            Assert.That(HaugbuiStateMachine.CanTransition(HaugbuiState.Interacting, HaugbuiState.Combat), Is.False, "never splits a step");
            Assert.That(HaugbuiStateMachine.CanTransition(HaugbuiState.Fleeing, HaugbuiState.Entering), Is.False, "waits outside the tomb");
        }

        [Test]
        public void Pending_return_blocks_new_tasks()
        {
            HaugbuiStateMachine sm = Machine(HaugbuiState.Idle);
            sm.TryEnter(HaugbuiState.Combat);
            sm.RequestReturn();
            Assert.That(sm.TryEnter(HaugbuiState.Fleeing), Is.True, "fleeing is not a task");
            sm.CancelReturn();
            sm.TryEnter(HaugbuiState.Idle);
            Assert.That(sm.TryEnter(HaugbuiState.Traveling), Is.True);
        }

        [Test]
        public void Death_from_any_out_state()
        {
            foreach (HaugbuiState state in Enum.GetValues(typeof(HaugbuiState)))
            {
                HaugbuiStateMachine sm = Machine(state);
                bool expected = state != HaugbuiState.Dormant && state != HaugbuiState.Dead;
                Assert.That(sm.Kill(), Is.EqualTo(expected), state.ToString());
            }
        }

        [Test]
        public void Persisted_subset_and_restore()
        {
            foreach (HaugbuiState state in Enum.GetValues(typeof(HaugbuiState)))
            {
                HaugbuiState saved = HaugbuiStateMachine.Persisted(state);
                Assert.That(saved, Is.AnyOf(HaugbuiState.Dormant, HaugbuiState.Idle, HaugbuiState.Dead));
                HaugbuiStateMachine sm = Machine();
                sm.Restore(saved);
                Assert.That(sm.State, Is.EqualTo(saved));
            }
        }

        [Test]
        public void Time_in_state_uses_the_clock()
        {
            _now = 10;
            HaugbuiStateMachine sm = Machine();
            _now = 12.5;
            Assert.That(sm.TimeInState, Is.EqualTo(2.5));
            int changes = 0;
            sm.Changed += (_, __) => changes++;
            sm.TryEnter(HaugbuiState.Emerging);
            Assert.That(sm.TimeInState, Is.EqualTo(0));
            Assert.That(changes, Is.EqualTo(1));
        }
    }

    public class PresencePolicyTests
    {
        private const double Day = 1800;
        private const long Today = 100;

        private static double TimeAt(float fraction, long day = Today) => (day + WorldClock.Unscale(fraction)) * Day;

        private static PresenceInput At(float fraction, bool fullTime = false) => new PresenceInput
        {
            Now = TimeAt(fraction),
            DayLengthSeconds = Day,
            FullTime = fullTime,
            DeathTime = double.NaN,
            CooldownSeconds = Day,
            DawnReturnLeadSeconds = 60,
            StartTime = double.NaN,
        };

        [TestCase(0.8f, true)]
        [TestCase(0.0f, true)]
        [TestCase(0.1f, true)]
        [TestCase(0.2f, false)] // 54 s of world time before dawn, inside the 60 s lead
        [TestCase(0.24f, false)]
        [TestCase(0.5f, false)]
        [TestCase(0.74f, false)]
        public void Without_altar_only_at_night_before_the_dawn_lead(float fraction, bool expected)
        {
            Assert.That(PresencePolicy.ShouldBeOut(At(fraction)), Is.EqualTo(expected));
        }

        [Test]
        public void Altar_works_at_noon_and_its_removal_sends_it_back()
        {
            Assert.That(PresencePolicy.Decide(At(0.5f, fullTime: true), creatureOut: false), Is.EqualTo(PresenceDecision.Emerge));
            Assert.That(PresencePolicy.Decide(At(0.5f, fullTime: false), creatureOut: true), Is.EqualTo(PresenceDecision.Return));
            Assert.That(PresencePolicy.Decide(At(0.5f), creatureOut: false), Is.EqualTo(PresenceDecision.StayInside));
            Assert.That(PresencePolicy.Decide(At(0.9f), creatureOut: true), Is.EqualTo(PresenceDecision.StayOut));
        }

        [Test]
        public void Resting_keeps_it_inside_even_with_the_altar()
        {
            PresenceInput input = At(0.9f, fullTime: true);
            input.Resting = true;
            Assert.That(PresencePolicy.Decide(input, creatureOut: true), Is.EqualTo(PresenceDecision.Return));
        }

        [Test]
        public void Death_at_night_without_altar_returns_at_the_dusk_after_the_cooldown()
        {
            PresenceInput input = At(0.9f);
            input.Dead = true;
            input.DeathTime = input.Now;
            double expected = (Today + 1 + WorldClock.EveningRaw) * Day + Day;
            Assert.That(PresencePolicy.ReturnTime(input), Is.EqualTo(expected).Within(1e-6),
                "the cooldown ends at night the next day, so it waits for the dusk after that");

            input.Now = TimeAt(0.9f, Today + 1) + 10;
            Assert.That(PresencePolicy.ShouldBeOut(input), Is.False, "cooldown over in the middle of a night: not yet");
            input.Now = expected + 1;
            Assert.That(PresencePolicy.ShouldBeOut(input), Is.True, "back at the next dusk");
        }

        [Test]
        public void Death_by_day_without_altar_returns_at_that_evening_after_the_cooldown()
        {
            PresenceInput input = At(0.3f);
            input.Dead = true;
            input.DeathTime = input.Now;
            Assert.That(PresencePolicy.ReturnTime(input), Is.EqualTo((Today + 1 + WorldClock.EveningRaw) * Day).Within(1e-6));
        }

        [Test]
        public void Death_with_altar_returns_at_cooldown_end()
        {
            PresenceInput input = At(0.9f, fullTime: true);
            input.Dead = true;
            input.DeathTime = input.Now;
            Assert.That(PresencePolicy.ReturnTime(input), Is.EqualTo(input.Now + Day));
            input.Now += Day - 1;
            Assert.That(PresencePolicy.ShouldBeOut(input), Is.False);
            input.Now += 2;
            Assert.That(PresencePolicy.ShouldBeOut(input), Is.True);
        }

        [Test]
        public void New_tomb_waits_for_the_next_dusk_with_or_without_altar()
        {
            PresenceInput input = At(0.9f, fullTime: true);
            input.StartTime = WorldClock.NextDusk(input.Now, Day);
            Assert.That(input.StartTime, Is.EqualTo((Today + 1 + WorldClock.EveningRaw) * Day).Within(1e-6));
            Assert.That(PresencePolicy.ShouldBeOut(input), Is.False);
            input.Now = input.StartTime + 1;
            Assert.That(PresencePolicy.ShouldBeOut(input), Is.True);
        }

        [Test]
        public void Rebuilding_during_cooldown_is_never_faster_than_waiting()
        {
            PresenceInput input = At(0.5f, fullTime: true);
            input.Dead = true;
            input.DeathTime = input.Now - 100;
            input.StartTime = WorldClock.NextDusk(input.Now, Day);
            double back = PresencePolicy.ReturnTime(input);
            Assert.That(back, Is.GreaterThan(input.StartTime));
            Assert.That(PresencePolicy.WaitRemaining(input), Is.EqualTo(back - input.Now).Within(1e-6), "the later of the two");
        }

        [Test]
        public void Day_length_changes_move_the_dusk()
        {
            PresenceInput input = At(0.5f);
            input.DayLengthSeconds = 1200;
            input.Now = 100.5 * 1200;
            Assert.That(WorldClock.NextDusk(input.Now, 1200), Is.EqualTo(100.85 * 1200).Within(1e-6));
        }

        [Test]
        public void Shift_timer_counts_down_the_shift_or_up_to_the_next()
        {
            PresenceInput night = At(0.9f);
            ShiftTimer timer = PresencePolicy.Timer(night);
            Assert.That(timer.InShift);
            Assert.That(timer.Seconds, Is.EqualTo((Today + 1 + WorldClock.MorningRaw) * Day - 60 - night.Now).Within(1e-6));

            ShiftTimer day = PresencePolicy.Timer(At(0.5f));
            Assert.That(day.InShift, Is.False);
            Assert.That(day.Seconds, Is.EqualTo((Today + WorldClock.EveningRaw) * Day - At(0.5f).Now).Within(1e-6));

            PresenceInput altar = At(0.5f, fullTime: true);
            ShiftTimer altarDay = PresencePolicy.Timer(altar);
            Assert.That(altarDay.InShift);
            Assert.That(altarDay.Seconds, Is.EqualTo((Today + WorldClock.EveningRaw) * Day - altar.Now).Within(1e-6), "Altar day shift ends at dusk");
        }

        [Test]
        public void Cooldown_math_on_world_time()
        {
            Assert.That(PresencePolicy.CooldownRemaining(500, double.NaN, 3600), Is.EqualTo(0));
            Assert.That(PresencePolicy.CooldownRemaining(1500, 1000, 3600), Is.EqualTo(3100));
            Assert.That(PresencePolicy.CooldownRemaining(1000 + 1800 * 3, 1000, 3600), Is.EqualTo(0), "sleep skips advance world time");
            Assert.That(PresencePolicy.CooldownRemaining(900, 1000, 3600), Is.EqualTo(3600), "rolled-back time never exceeds a full cooldown");
        }

        [Test]
        public void Seconds_to_dawn_on_world_time()
        {
            Assert.That(PresencePolicy.SecondsToDawn(At(0.76f)), Is.EqualTo((Today + 1 + WorldClock.MorningRaw) * Day - At(0.76f).Now).Within(0.01));
            Assert.That(PresencePolicy.SecondsToDawn(At(0.0f)), Is.EqualTo(0.15 * Day).Within(0.01), "after midnight");
            Assert.That(PresencePolicy.SecondsToDawn(At(0.5f)), Is.EqualTo(0));
        }

        [TestCase(0, "0:00")]
        [TestCase(59.2, "1:00")]
        [TestCase(3600, "1:00:00")]
        [TestCase(3725, "1:02:05")]
        public void Countdown_format(double seconds, string expected)
        {
            Assert.That(PresencePolicy.FormatCountdown(seconds), Is.EqualTo(expected));
        }
    }

    public class HealthAndSteeringTests
    {
        [Test]
        public void Regeneration_is_linear_and_capped()
        {
            Assert.That(HealthMath.Regenerate(0, 100, 1800, 3600), Is.EqualTo(50f));
            Assert.That(HealthMath.Regenerate(90, 100, 1800, 3600), Is.EqualTo(100f));
            Assert.That(HealthMath.Regenerate(10, 100, -5, 3600), Is.EqualTo(10f));
        }

        [Test]
        public void Level_change_keeps_the_health_fraction()
        {
            Assert.That(HealthMath.Rescale(10, 20, 40), Is.EqualTo(20f));
            Assert.That(HealthMath.Rescale(60, 60, 20), Is.EqualTo(20f));
            Assert.That(HealthMath.Rescale(5, 0, 40), Is.EqualTo(40f));
        }

        [Test]
        public void Speed_never_exceeds_max_and_brakes_near_target()
        {
            float speed = 0;
            for (int i = 0; i < 200; i++)
            {
                speed = Steering.NextSpeed(speed, distance: 100, maxSpeed: 4, acceleration: 3, dt: 0.02f);
                Assert.That(speed, Is.LessThanOrEqualTo(4f));
            }
            Assert.That(speed, Is.EqualTo(4f));
            Assert.That(Steering.NextSpeed(4, distance: 0.5f, maxSpeed: 4, acceleration: 3, dt: 0.02f), Is.LessThan(4f));
        }

        [Test]
        public void Step_never_overshoots_or_jumps()
        {
            Assert.That(Steering.StepLength(6, distance: 0.05f, dt: 0.02f, maxStep: 1), Is.EqualTo(0.05f));
            Assert.That(Steering.StepLength(6, distance: 50, dt: 0.5f, maxStep: 1), Is.EqualTo(1f));
        }
    }

    public class TombStateV2Tests
    {
        [Test]
        public void Version_1_blobs_still_load()
        {
            var writer = new BlobWriter(1);
            writer.Write("g").Write(7L).Write((int)TombToggles.Defaults).Write(12f).Write(double.NaN).Write(3);
            Assert.That(TombStateSerializer.Instance.TryDeserialize(writer.ToArray(), out TombState state), Is.EqualTo(BlobReadStatus.Ok));
            Assert.That(state.LevelCache, Is.EqualTo(3));
            Assert.That(state.CreatureState, Is.EqualTo((int)HaugbuiState.Dormant));
            Assert.That(float.IsNaN(state.PositionX), Is.True);
        }

        [Test]
        public void Version_2_fields_round_trip()
        {
            var state = new TombState { CreatureState = (int)HaugbuiState.Idle, PositionX = 1, PositionY = 2, PositionZ = 3, HealthTime = 42 };
            TombStateSerializer.Instance.TryDeserialize(TombStateSerializer.Instance.Serialize(state), out TombState read);
            Assert.That((read.CreatureState, read.PositionX, read.PositionY, read.PositionZ, read.HealthTime),
                Is.EqualTo(((int)HaugbuiState.Idle, 1f, 2f, 3f, 42.0)));
        }
    }
}
