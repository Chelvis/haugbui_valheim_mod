using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;
using Haugbui.Core.Persistence;
using Haugbui.Core.Production;
using Haugbui.Core.Storage;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class ProductionJobTests
    {
        private static StationJobState Finite(int target) => new StationJobState
        {
            Enabled = true, InputPrefab = "IronOre", OutputPrefab = "Iron", Mode = JobMode.Finite, Target = target
        };

        [Test]
        public void Finite_job_wants_inputs_until_supplied_covers_the_target()
        {
            StationJobState job = Finite(100);
            Assert.That(ProductionJob.InputsWanted(job, 1), Is.EqualTo(100));
            ProductionJob.RecordSupplied(job, 60);
            Assert.That(ProductionJob.InputsWanted(job, 1), Is.EqualTo(40));
            ProductionJob.RecordSupplied(job, 40);
            Assert.That(ProductionJob.InputsWanted(job, 1), Is.EqualTo(0));
            Assert.That(ProductionJob.Phase(job, 1, stationIdle: false), Is.EqualTo(JobPhase.WaitingOutputs));
        }

        [Test]
        public void Finite_job_completes_when_outputs_are_collected()
        {
            StationJobState job = Finite(3);
            ProductionJob.RecordSupplied(job, 3);
            Assert.That(ProductionJob.RecordProduced(job, 2, 1), Is.EqualTo(2));
            Assert.That(ProductionJob.Phase(job, 1, false), Is.EqualTo(JobPhase.WaitingOutputs));
            Assert.That(ProductionJob.RecordProduced(job, 1, 1), Is.EqualTo(1));
            Assert.That(ProductionJob.Phase(job, 1, false), Is.EqualTo(JobPhase.Complete));
            Assert.That(ProductionJob.Remaining(job), Is.EqualTo(0));
        }

        [Test]
        public void Produced_never_exceeds_what_the_inputs_can_make()
        {
            StationJobState job = Finite(10);
            ProductionJob.RecordSupplied(job, 2);
            Assert.That(ProductionJob.RecordProduced(job, 5, 1), Is.EqualTo(2));
            Assert.That(job.Produced, Is.EqualTo(2));
        }

        [Test]
        public void Yield_above_one_rounds_inputs_up()
        {
            StationJobState job = Finite(10);
            Assert.That(ProductionJob.InputsWanted(job, 6), Is.EqualTo(2));
            ProductionJob.RecordSupplied(job, 1);
            Assert.That(ProductionJob.InputsWanted(job, 6), Is.EqualTo(1));
            ProductionJob.RecordSupplied(job, 1);
            Assert.That(ProductionJob.InputsWanted(job, 6), Is.EqualTo(0));
            Assert.That(ProductionJob.RecordProduced(job, 12, 6), Is.EqualTo(12));
            Assert.That(ProductionJob.Phase(job, 6, true), Is.EqualTo(JobPhase.Complete));
        }

        [Test]
        public void Infinite_job_always_feeds_while_enabled()
        {
            StationJobState job = Finite(0);
            job.Mode = JobMode.Infinite;
            ProductionJob.RecordSupplied(job, 1000);
            Assert.That(ProductionJob.InputsWanted(job, 1), Is.EqualTo(int.MaxValue));
            Assert.That(ProductionJob.Phase(job, 1, true), Is.EqualTo(JobPhase.Feeding));
            Assert.That(ProductionJob.Remaining(job), Is.EqualTo(-1));
            job.Enabled = false;
            Assert.That(ProductionJob.InputsWanted(job, 1), Is.EqualTo(0));
            Assert.That(ProductionJob.Phase(job, 1, true), Is.EqualTo(JobPhase.Disabled));
        }

        [Test]
        public void Lost_outputs_complete_the_job_once_the_station_is_idle()
        {
            StationJobState job = Finite(5);
            ProductionJob.RecordSupplied(job, 5);
            ProductionJob.RecordProduced(job, 3, 1);
            Assert.That(ProductionJob.Phase(job, 1, stationIdle: false), Is.EqualTo(JobPhase.WaitingOutputs));
            Assert.That(ProductionJob.Phase(job, 1, stationIdle: true), Is.EqualTo(JobPhase.Complete));
        }

        [Test]
        public void Editing_the_target_keeps_progress_and_a_new_recipe_resets_it()
        {
            StationJobState job = Finite(10);
            ProductionJob.RecordSupplied(job, 10);
            ProductionJob.RecordProduced(job, 10, 1);
            Assert.That(ProductionJob.Phase(job, 1, true), Is.EqualTo(JobPhase.Complete));

            ProductionJob.Edit(job, true, "IronOre", "Iron", JobMode.Finite, 15);
            Assert.That(job.Produced, Is.EqualTo(10));
            Assert.That(ProductionJob.InputsWanted(job, 1), Is.EqualTo(5));

            ProductionJob.Edit(job, true, "CopperOre", "Copper", JobMode.Finite, 15);
            Assert.That(new[] { job.Supplied, job.Produced }, Is.EqualTo(new[] { 0, 0 }));
            Assert.That(ProductionJob.InputsWanted(job, 1), Is.EqualTo(15));
        }

        [Test]
        public void Lowering_the_target_below_supplied_stops_feeding()
        {
            StationJobState job = Finite(10);
            ProductionJob.RecordSupplied(job, 6);
            ProductionJob.Edit(job, true, "IronOre", "Iron", JobMode.Finite, 4);
            Assert.That(ProductionJob.InputsWanted(job, 1), Is.EqualTo(0));
        }
    }

    public class JobOwnershipTests
    {
        [Test]
        public void Unowned_job_is_claimed_and_own_job_is_kept()
        {
            var job = new StationJobState();
            Assert.That(JobOwnership.Check(job, "a", 0, 300), Is.EqualTo(OwnershipVerdict.Claim));
            Assert.That(JobOwnership.Touch(job, "a", 0), Is.True);
            Assert.That(JobOwnership.Check(job, "a", 10, 300), Is.EqualTo(OwnershipVerdict.Own));
        }

        [Test]
        public void Second_tomb_ignores_a_job_seen_recently_by_its_owner()
        {
            var job = new StationJobState { TombGuid = "a", OwnerSeen = 100 };
            Assert.That(JobOwnership.Check(job, "b", 399, 300), Is.EqualTo(OwnershipVerdict.Foreign));
            Assert.That(JobOwnership.Check(job, "b", 400, 300), Is.EqualTo(OwnershipVerdict.Takeover));
            Assert.That(JobOwnership.Check(job, "b", 5000, 300, ownerPresent: true), Is.EqualTo(OwnershipVerdict.Foreign));
        }

        [Test]
        public void Owner_refreshes_seen_time_only_once_a_minute()
        {
            var job = new StationJobState { TombGuid = "a", OwnerSeen = 100 };
            Assert.That(JobOwnership.Touch(job, "a", 130), Is.False);
            Assert.That(JobOwnership.Touch(job, "a", 160), Is.True);
            Assert.That(job.OwnerSeen, Is.EqualTo(160));
            Assert.That(JobOwnership.Touch(job, "b", 161), Is.True);
            Assert.That(job.TombGuid, Is.EqualTo("b"));
        }

        [Test]
        public void Job_without_seen_time_from_another_tomb_can_be_taken_over()
        {
            var job = new StationJobState { TombGuid = "a" };
            Assert.That(JobOwnership.Check(job, "b", 0, 300), Is.EqualTo(OwnershipVerdict.Takeover));
        }
    }

    public class StationFillTests
    {
        [TestCase(0, 10, int.MaxValue, 10)]
        [TestCase(4, 10, int.MaxValue, 6)]
        [TestCase(6, 10, int.MaxValue, 0)]
        [TestCase(6, 10, 3, 3)]
        [TestCase(10, 10, 5, 0)]
        [TestCase(0, 10, 0, 0)]
        public void Inputs_fill_the_queue_in_worthwhile_trips(int queued, int max, int wanted, int expected) =>
            Assert.That(StationFill.Inputs(queued, max, wanted), Is.EqualTo(expected));

        [Test]
        public void Fuel_covers_the_queue_without_exceeding_capacity()
        {
            Assert.That(StationFill.Fuel(0, 20, 10, 2), Is.EqualTo(20));
            Assert.That(StationFill.Fuel(0, 20, 3, 2), Is.EqualTo(6));
            Assert.That(StationFill.Fuel(5.5f, 20, 10, 2), Is.EqualTo(14));
            Assert.That(StationFill.Fuel(19.2f, 20, 10, 2), Is.EqualTo(0));
            Assert.That(StationFill.Fuel(4, 20, 2, 2), Is.EqualTo(0));
            Assert.That(StationFill.Fuel(0, 0, 10, 2), Is.EqualTo(0));
        }

        [Test]
        public void Fuel_only_stations_top_up_below_half()
        {
            Assert.That(StationFill.Fuel(4, 10, 0, 0), Is.EqualTo(6));
            Assert.That(StationFill.Fuel(5.5f, 10, 0, 0), Is.EqualTo(0));
        }
    }

    public class SupplyPlannerTests
    {
        private static ItemStack Ore(int n, bool cheated = false) =>
            new ItemStack { Prefab = "IronOre", Amount = n, MaxStack = 30, UnitWeight = 1, Cheated = cheated };

        private static ContainerSnapshot Chest(string id, float x, params ItemStack[] items) =>
            new ContainerSnapshot { Id = id, Position = new Vec3(x, 0, 0), Slots = 10, Items = items.ToList() };

        private static CarriedInventory Carry() => new CarriedInventory(4, 300);

        [Test]
        public void Takes_nearest_first_and_never_more_than_wanted()
        {
            var sources = new List<ContainerSnapshot> { Chest("far", 20, Ore(10)), Chest("near", 2, Ore(4)) };
            SupplyPick pick = SupplyPlanner.Pick("IronOre", 6, null, Carry(), sources, new Vec3(0, 0, 0), 0);
            Assert.That(pick.Takes.Select(t => (t.Source, t.Item.Amount)), Is.EqualTo(new[] { ("near", 4), ("far", 2) }));
            Assert.That(pick.Total, Is.EqualTo(6));
            Assert.That(pick.Cheated, Is.False);
        }

        [Test]
        public void Delivery_reservations_are_left_in_storage()
        {
            var sources = new List<ContainerSnapshot> { Chest("a", 1, Ore(10)) };
            Assert.That(SupplyPlanner.Pick("IronOre", 10, null, Carry(), sources, new Vec3(0, 0, 0), 7).Total, Is.EqualTo(3));
            Assert.That(SupplyPlanner.Pick("IronOre", 10, null, Carry(), sources, new Vec3(0, 0, 0), 10).Total, Is.EqualTo(0));
        }

        [Test]
        public void One_flag_per_trip_clean_first_and_the_required_flag_is_honoured()
        {
            var sources = new List<ContainerSnapshot> { Chest("a", 1, Ore(3, cheated: true), Ore(2)) };
            SupplyPick free = SupplyPlanner.Pick("IronOre", 10, null, Carry(), sources, new Vec3(0, 0, 0), 0);
            Assert.That(free.Cheated, Is.False);
            Assert.That(free.Total, Is.EqualTo(2));
            Assert.That(free.Takes.All(t => !t.Item.Cheated));

            SupplyPick cheated = SupplyPlanner.Pick("IronOre", 10, true, Carry(), sources, new Vec3(0, 0, 0), 0);
            Assert.That(cheated.Total, Is.EqualTo(3));
            Assert.That(cheated.Takes.All(t => t.Item.Cheated));

            var onlyCheated = new List<ContainerSnapshot> { Chest("a", 1, Ore(3, cheated: true)) };
            Assert.That(SupplyPlanner.Pick("IronOre", 10, null, Carry(), onlyCheated, new Vec3(0, 0, 0), 0).Cheated, Is.True);
            Assert.That(SupplyPlanner.Pick("IronOre", 10, false, Carry(), onlyCheated, new Vec3(0, 0, 0), 0).Total, Is.EqualTo(0));
        }

        [Test]
        public void Carried_items_count_first_and_carry_limits_apply()
        {
            CarriedInventory carry = new CarriedInventory(1, 300);
            carry.Add(Ore(5));
            var sources = new List<ContainerSnapshot> { Chest("a", 1, Ore(30)) };
            SupplyPick pick = SupplyPlanner.Pick("IronOre", 40, null, carry, sources, new Vec3(0, 0, 0), 0);
            Assert.That(pick.FromCarried, Is.EqualTo(5));
            Assert.That(pick.Takes.Sum(t => t.Item.Amount), Is.EqualTo(25));
        }
    }
}
