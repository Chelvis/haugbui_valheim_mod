using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;
using Haugbui.Core.Persistence;
using Haugbui.Core.Storage;

namespace Haugbui.Core.Production
{
    public enum JobPhase
    {
        Disabled,
        Feeding,

        /// <summary>Finite job: everything is supplied, outputs are still being made or collected.</summary>
        WaitingOutputs,
        Complete
    }

    /// <summary>HB-111/112/114: finite target = products of this job; infinite runs until disabled.</summary>
    public static class ProductionJob
    {
        public static int Planned(StationJobState job, int yield) => job.Supplied * Math.Max(1, yield);

        /// <summary>Inputs the job still accepts; int.MaxValue for infinite jobs.</summary>
        public static int InputsWanted(StationJobState job, int yield)
        {
            if (job == null || !job.Enabled) return 0;
            if (job.Mode == JobMode.Infinite) return int.MaxValue;
            yield = Math.Max(1, yield);
            int missing = job.Target - Planned(job, yield);
            return missing <= 0 ? 0 : (missing + yield - 1) / yield;
        }

        public static void RecordSupplied(StationJobState job, int inputs) => job.Supplied += Math.Max(0, inputs);

        /// <summary>Counts collected outputs, never more than the job's inputs can have made; returns what was counted.</summary>
        public static int RecordProduced(StationJobState job, int products, int yield)
        {
            int counted = Math.Max(0, Math.Min(products, Planned(job, yield) - job.Produced));
            job.Produced += counted;
            return counted;
        }

        /// <param name="stationIdle">Nothing of the job is queued, processing or waiting at the output point.</param>
        public static JobPhase Phase(StationJobState job, int yield, bool stationIdle)
        {
            if (job == null || !job.Enabled) return JobPhase.Disabled;
            if (job.Mode == JobMode.Infinite) return JobPhase.Feeding;
            if (job.Produced >= job.Target) return JobPhase.Complete;
            if (InputsWanted(job, yield) > 0) return JobPhase.Feeding;
            // Someone else took outputs: once the station has nothing left of the job, it is done.
            return stationIdle ? JobPhase.Complete : JobPhase.WaitingOutputs;
        }

        /// <summary>Products still to make; -1 for infinite jobs.</summary>
        public static int Remaining(StationJobState job) =>
            job.Mode == JobMode.Infinite ? -1 : Math.Max(0, job.Target - job.Produced);

        /// <summary>Panel edit: a new recipe starts a new count; other edits keep the progress.</summary>
        public static void Edit(StationJobState job, bool enabled, string input, string output, JobMode mode, int target)
        {
            if (job.InputPrefab != input || job.OutputPrefab != output)
            {
                job.Supplied = 0;
                job.Produced = 0;
            }
            job.Enabled = enabled;
            job.InputPrefab = input ?? string.Empty;
            job.OutputPrefab = output ?? string.Empty;
            job.Mode = mode;
            job.Target = Math.Max(0, target);
        }
    }

    public enum OwnershipVerdict
    {
        Own,
        Claim,
        Takeover,
        Foreign
    }

    /// <summary>HB-118/157: one owning tomb per job, persisted; another may take over once the owner stopped seeing it.</summary>
    public static class JobOwnership
    {
        /// <summary>The owner refreshes OwnerSeen at most this often, so the station ZDO is not rewritten every tick.</summary>
        public const double SeenRefreshSeconds = 60;

        /// <param name="ownerPresent">The owning tomb is loaded and able to work the station, whatever it last saw.</param>
        public static OwnershipVerdict Check(StationJobState job, string tomb, double now, double takeoverSeconds, bool ownerPresent = false)
        {
            if (string.IsNullOrEmpty(job.TombGuid)) return OwnershipVerdict.Claim;
            if (job.TombGuid == tomb) return OwnershipVerdict.Own;
            if (ownerPresent) return OwnershipVerdict.Foreign;
            if (double.IsNaN(job.OwnerSeen) || now - job.OwnerSeen >= takeoverSeconds) return OwnershipVerdict.Takeover;
            return OwnershipVerdict.Foreign;
        }

        /// <returns>True when the job changed and must be saved.</returns>
        public static bool Touch(StationJobState job, string tomb, double now)
        {
            if (job.TombGuid == tomb && !double.IsNaN(job.OwnerSeen) && now - job.OwnerSeen < SeenRefreshSeconds && now >= job.OwnerSeen)
                return false;
            job.TombGuid = tomb;
            job.OwnerSeen = now;
            return true;
        }
    }

    /// <summary>How much to put into a station on one trip.</summary>
    public static class StationFill
    {
        /// <summary>Queue room, capped by the job; a trip is only worth it when half the queue is free or it is empty.</summary>
        public static int Inputs(int queued, int maxQueue, int wanted)
        {
            int free = Math.Max(0, maxQueue - queued);
            if (free == 0 || wanted <= 0) return 0;
            if (queued > 0 && free * 2 < maxQueue && wanted > free) return 0;
            return Math.Min(free, wanted);
        }

        /// <summary>
        /// Whole fuel units the station accepts (vanilla adds one only while fuel ≤ max − 1), enough to burn the queue
        /// after this trip; fuel-only stations (<paramref name="fuelPerProduct"/> ≤ 0) are kept topped up.
        /// </summary>
        public static int Fuel(float fuel, int maxFuel, int queuedAfter, float fuelPerProduct)
        {
            if (maxFuel <= 0) return 0;
            int room = Math.Max(0, (int)Math.Floor(maxFuel - fuel + 1e-4));
            if (fuelPerProduct <= 0) return fuel <= maxFuel / 2f ? room : 0;
            int need = (int)Math.Ceiling(queuedAfter * fuelPerProduct - fuel - 1e-4);
            return Math.Max(0, Math.Min(room, need));
        }
    }

    public sealed class SupplyPick
    {
        public bool Cheated { get; set; }
        public int FromCarried { get; set; }
        public List<PlannedTake> Takes { get; } = new List<PlannedTake>();
        public int Total => FromCarried + Takes.Sum(t => t.Item.Amount);
    }

    /// <summary>HB-113/087: inputs come from storage minus what Delivery Chests still wait for; carried items first.</summary>
    public static class SupplyPlanner
    {
        /// <param name="cheated">Flag the station needs (HB-061); null lets the planner choose, clean first.</param>
        /// <param name="carry">Simulated carried inventory; takes are added to it.</param>
        public static SupplyPick Pick(string prefab, int wanted, bool? cheated, CarriedInventory carry,
            IReadOnlyCollection<ContainerSnapshot> sources, Vec3 from, int reserved)
        {
            var pick = new SupplyPick();
            if (wanted <= 0 || string.IsNullOrEmpty(prefab)) return pick;
            int carriedClean = Carried(carry, prefab, false);
            int carriedCheated = Carried(carry, prefab, true);
            int stockClean = Stock(sources, prefab, false);
            int stockCheated = Stock(sources, prefab, true);
            bool flag = cheated ?? (carriedClean + stockClean <= 0 && carriedCheated + stockCheated > 0);
            pick.Cheated = flag;

            pick.FromCarried = Math.Min(wanted, flag ? carriedCheated : carriedClean);
            int free = Math.Max(0, stockClean + stockCheated - Math.Max(0, reserved));
            int left = Math.Min(wanted - pick.FromCarried, Math.Min(free, flag ? stockCheated : stockClean));
            foreach (ContainerSnapshot source in sources.OrderBy(s => Vec3.Distance(from, s.Position)).ThenBy(s => s.Id, StringComparer.Ordinal))
            {
                foreach (ItemStack stack in DepositPlanner.Merge(source.Items.Where(i => i.Prefab == prefab && i.Cheated == flag), Enumerable.Empty<ItemStack>()))
                {
                    if (left <= 0) return pick;
                    int n = carry.Add(stack.Copy(Math.Min(left, stack.Amount)));
                    if (n <= 0) continue;
                    pick.Takes.Add(new PlannedTake(source.Id, stack.Copy(n)));
                    left -= n;
                }
            }
            return pick;
        }

        public static int Stock(IEnumerable<ContainerSnapshot> sources, string prefab, bool cheated) =>
            sources.SelectMany(s => s.Items).Where(i => i.Prefab == prefab && i.Cheated == cheated).Sum(i => i.Amount);

        private static int Carried(CarriedInventory carry, string prefab, bool cheated) =>
            carry.Stacks.Where(s => s.Prefab == prefab && s.Cheated == cheated).Sum(s => s.Amount);
    }
}
