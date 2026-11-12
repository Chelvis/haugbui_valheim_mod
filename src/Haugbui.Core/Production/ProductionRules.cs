using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;
using Haugbui.Core.Harvest;
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

        /// <summary>Counts collected outputs, never more than this job can claim; returns what was counted.</summary>
        public static int RecordProduced(StationJobState job, int products, int yield)
        {
            // A kiln with no input still makes a counted product. The cap is the target, not the inputs fed.
            int cap = string.IsNullOrEmpty(job.InputPrefab)
                ? job.Mode == JobMode.Infinite ? int.MaxValue : Math.Max(0, job.Target)
                : Planned(job, yield);
            int counted = Math.Max(0, Math.Min(products, cap - job.Produced));
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

        /// <summary>
        /// Starts the same batch again. Supplied has to go too: a finite job is finished once the inputs
        /// already cover the target, even if the collected count is put back to zero.
        /// </summary>
        public static void ResetProgress(StationJobState job)
        {
            if (job == null) return;
            job.Supplied = 0;
            job.Produced = 0;
        }

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

    /// <summary>
    /// Inputs come from what it already carries, then eligible ground drops, then storage minus what Delivery Chests
    /// still wait for, then material piles when <paramref name="piles"/> is passed. Pass no drops and the ground is
    /// ignored. Reservations and the leave-one rule apply to chests only.
    /// </summary>
    public static class SupplyPlanner
    {
        /// <param name="cheated">Flag the station needs (HB-061); null lets the planner choose, clean first.</param>
        /// <param name="carry">Simulated carried inventory; takes are added to it.</param>
        /// <param name="ground">Eligible drops. Null ignores the ground.</param>
        /// <param name="bagOnly">The bag is unloading: use what it already holds. Chests, drops and piles wait until it is stored.</param>
        /// <param name="piles">Piles that may be dismantled. Null or empty leaves every pile standing.</param>
        public static SupplyPick Pick(string prefab, int wanted, bool? cheated, CarriedInventory carry,
            IReadOnlyCollection<ContainerSnapshot> sources, Vec3 from, int reserved, IEnumerable<GroundItem> ground = null,
            bool bagOnly = false, IEnumerable<MaterialPile> piles = null)
        {
            var pick = new SupplyPick();
            if (wanted <= 0 || string.IsNullOrEmpty(prefab)) return pick;
            var drops = ground?.Where(d => d?.Item != null && d.Item.Prefab == prefab).ToList() ?? new List<GroundItem>();
            var stack = piles?.Where(p => p?.Refund != null && p.Refund.Prefab == prefab).ToList() ?? new List<MaterialPile>();
            int carriedClean = Carried(carry, prefab, false);
            int carriedCheated = Carried(carry, prefab, true);
            int stockClean = Stock(sources, prefab, false);
            int stockCheated = Stock(sources, prefab, true);
            int groundClean = drops.Where(d => !d.Item.Cheated).Sum(d => d.Item.Amount);
            int groundCheated = drops.Where(d => d.Item.Cheated).Sum(d => d.Item.Amount);
            int pileClean = stack.Where(p => !p.Refund.Cheated).Sum(p => p.Refund.Amount);
            int pileCheated = stack.Where(p => p.Refund.Cheated).Sum(p => p.Refund.Amount);
            bool flag = cheated ?? (carriedClean + stockClean + groundClean + pileClean <= 0
                && carriedCheated + stockCheated + groundCheated + pileCheated > 0);
            pick.Cheated = flag;

            pick.FromCarried = Math.Min(wanted, flag ? carriedCheated : carriedClean);
            if (bagOnly) return pick;
            int unmet = wanted - pick.FromCarried;
            foreach (GroundItem drop in drops.Where(d => d.Item.Cheated == flag)
                         .OrderBy(d => Vec3.Distance(from, d.Position)).ThenBy(d => d.Id, StringComparer.Ordinal))
            {
                if (unmet <= 0) break;
                int n = carry.Add(drop.Item.Copy(Math.Min(unmet, drop.Item.Amount)));
                if (n <= 0) continue;
                pick.Takes.Add(new PlannedTake(drop.Id, drop.Item.Copy(n)));
                unmet -= n;
            }

            int free = Math.Max(0, stockClean + stockCheated - Math.Max(0, reserved));
            int chestLeft = Math.Min(unmet, Math.Min(free, flag ? stockCheated : stockClean));
            foreach (ContainerSnapshot source in sources.OrderBy(s => Vec3.Distance(from, s.Position)).ThenBy(s => s.Id, StringComparer.Ordinal))
            {
                if (chestLeft <= 0) break;
                int budget = source.Takeable(prefab);
                foreach (ItemStack item in DepositPlanner.Merge(source.Items.Where(i => i.Prefab == prefab && i.Cheated == flag), Enumerable.Empty<ItemStack>()))
                {
                    if (chestLeft <= 0) break;
                    if (budget <= 0) break;
                    int n = carry.Add(item.Copy(Math.Min(chestLeft, Math.Min(item.Amount, budget))));
                    if (n <= 0) continue;
                    pick.Takes.Add(new PlannedTake(source.Id, item.Copy(n)));
                    budget -= n;
                    chestLeft -= n;
                    unmet -= n;
                }
            }

            var opened = new HashSet<string>();
            while (unmet > 0)
            {
                PlannedTake? pile = PileSource.Open(stack, prefab, flag, carry, from, opened);
                if (pile == null) break;
                pick.Takes.Add(pile.Value);
                unmet -= Math.Min(unmet, pile.Value.Item.Amount);
            }
            return pick;
        }

        public static int Stock(IEnumerable<ContainerSnapshot> sources, string prefab, bool cheated) =>
            sources.SelectMany(s => s.Items).Where(i => i.Prefab == prefab && i.Cheated == cheated).Sum(i => i.Amount);

        private static int Carried(CarriedInventory carry, string prefab, bool cheated) =>
            carry.Stacks.Where(s => s.Prefab == prefab && s.Cheated == cheated).Sum(s => s.Amount);
    }
}
