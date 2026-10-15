using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Coordination;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;
using Haugbui.Core.Storage;

namespace Haugbui.Brain
{
    internal static class TaskKinds
    {
        public const string Deposit = "deposit";
        public const string SortTombStorage = "sort_tomb";
        public const string Deliver = "deliver";
        public const string Collect = "collect";
        public const string Harvest = "harvest";
        public const string Gather = "gather";
        public const string ProductionOutputs = "production_outputs";
        public const string ProductionFeed = "production_feed";
        public const string ProductionOperate = "production_operate";

        // ARCHITECTURE §5.1: deposit → production outputs → production feeding → delivery → harvest → collection chest → ground items.
        // A done cooking item burns if left, so taking it off beats even the deposit.
        public const int ProductionUrgentPriority = 105;
        public const int DepositPriority = 100;
        public const int ProductionOperatePriority = 85;
        public const int ProductionOutputsPriority = 80;
        public const int ProductionFeedPriority = 75;
        public const int DeliverPriority = 70;
        public const int HarvestPriority = 60;
        public const int SortTombStoragePriority = 50;
        public const int CollectPriority = 45;
        public const int GatherPriority = 40;
    }

    /// <summary>HB-T-046: carried items go into chests that already hold them; no destination → kept, retried later.</summary>
    internal sealed class DepositProvider : ITaskProvider<WorkContext>
    {
        public bool IsEnabled(WorkContext context) => !context.Carried.IsEmpty;

        public void Collect(WorkContext context, List<TaskCandidate> output)
        {
            foreach (ItemStack stack in Distinct(context.Carried.Stacks))
            {
                if (context.NoDestination.IsWaiting(stack.KindKey, context.Now)) continue;
                List<StorageAllocation> plan = StorageSelector.Plan(stack, stack.Amount, context.Destinations, context.From);
                if (plan.Count == 0 && CollectionFallback.Enabled && context.Tomb.Worker.FallbackKinds.Contains(stack.KindKey))
                    plan = CollectionFallback.Plan(context, stack, stack.Amount);
                if (plan.Count == 0)
                {
                    context.NoDestination.Fail(stack.KindKey, context.Now);
                    continue;
                }
                ContainerSnapshot first = context.Destinations.Concat(context.CollectionChests).First(d => d.Id == plan[0].ContainerId);
                ItemStack item = stack.Copy(stack.Amount);
                output.Add(new TaskCandidate
                {
                    Kind = TaskKinds.Deposit,
                    Priority = TaskKinds.DepositPriority,
                    Distance = Vec3.Distance(context.From, first.Position),
                    Build = () => new TaskPlan(TaskKinds.Deposit, plan.Select(a => new TaskStep(StepKind.Put, a.ContainerId, item, a.Amount))),
                });
            }
        }

        /// <summary>One entry per distinct item data, amounts summed.</summary>
        internal static List<ItemStack> Distinct(IEnumerable<ItemStack> stacks)
        {
            var result = new List<ItemStack>();
            foreach (ItemStack stack in stacks)
            {
                ItemStack same = result.FirstOrDefault(r => r.StacksWith(stack));
                if (same != null) same.Amount += stack.Amount;
                else result.Add(stack.Copy(stack.Amount));
            }
            return result;
        }
    }

    /// <summary>
    /// HB-044/HB-T-046: tomb storage is the first source after emerging. Takes only what has a destination now and only
    /// as much as the destinations and the carry limits allow.
    /// </summary>
    internal sealed class SortTombStorageProvider : ITaskProvider<WorkContext>
    {
        public bool IsEnabled(WorkContext context) => context.TombStorage != null && context.TombStorage.Items.Count > 0;

        public void Collect(WorkContext context, List<TaskCandidate> output)
        {
            List<TaskStep> steps = TakeSteps(context, context.TombStorage);
            if (steps.Count == 0) return;
            output.Add(new TaskCandidate
            {
                Kind = TaskKinds.SortTombStorage,
                Priority = TaskKinds.SortTombStoragePriority,
                Distance = Vec3.Distance(context.From, context.TombStorage.Position),
                Keys = new[] { "take:" + context.TombStorage.Id },
                Build = () => new TaskPlan(TaskKinds.SortTombStorage, steps),
            });
        }

        /// <summary>
        /// Takes from <paramref name="source"/> only what has a destination now (HB-065) and only as much as the
        /// destinations and the carry limits allow. Equipment is never taken (HB-072).
        /// </summary>
        internal static List<TaskStep> TakeSteps(WorkContext context, ContainerSnapshot source)
        {
            var simulated = new CarriedInventory(context.Carried.Slots, context.Carried.Capacity);
            simulated.Restore(context.Carried.Stacks.Select(s => s.Copy(s.Amount)));
            var steps = new List<TaskStep>();
            foreach (ItemStack stack in DepositProvider.Distinct(source.Items))
            {
                if (!Creature.ItemBridge.IsTransportable(stack)) continue;
                if (context.NoDestination.IsWaiting(stack.KindKey, context.Now)) continue;
                int room = StorageSelector.Plan(stack, stack.Amount, context.Destinations, context.From).Sum(a => a.Amount);
                if (room <= 0)
                {
                    context.NoDestination.Fail(stack.KindKey, context.Now);
                    continue;
                }
                int take = simulated.Add(stack.Copy(System.Math.Min(room, stack.Amount)));
                if (take > 0) steps.Add(new TaskStep(StepKind.Take, source.Id, stack.Copy(take), take));
            }
            return steps;
        }
    }

    /// <summary>HB-T-049: Collection Chests in the radius are emptied into chests that hold the same items.</summary>
    internal sealed class CollectionProvider : ITaskProvider<WorkContext>
    {
        public bool IsEnabled(WorkContext context) =>
            context.CollectionChests.Count > 0 && (context.Probe || context.Now >= context.NextCollectionScan);

        public void Collect(WorkContext context, List<TaskCandidate> output)
        {
            bool found = false;
            foreach (ContainerSnapshot chest in context.CollectionChests)
            {
                List<TaskStep> steps = SortTombStorageProvider.TakeSteps(context, chest);
                if (steps.Count == 0) continue;
                found = true;
                output.Add(new TaskCandidate
                {
                    Kind = TaskKinds.Collect,
                    Priority = TaskKinds.CollectPriority,
                    Distance = Vec3.Distance(context.From, chest.Position),
                    Keys = new[] { "take:" + chest.Id },
                    Build = () => new TaskPlan(TaskKinds.Collect, steps),
                });
            }
            // HB-071: look again after one collection interval; while there is work, every planning tick.
            if (!found && !context.Probe) context.Tomb.Worker.NextCollectionScan = context.Now + HaugbuiPlugin.Instance.Settings.CollectionIntervalSeconds.Value;
        }
    }

    /// <summary>
    /// HB-T-053: one trip per open request line: take the remaining amount from storage and Collection Chests, put it
    /// into the Delivery Chest; the worker counts what went in. The line key keeps two Haugbuis off the same line.
    /// </summary>
    internal sealed class DeliveryProvider : ITaskProvider<WorkContext>
    {
        public bool IsEnabled(WorkContext context) => context.DeliveryChests.Count > 0;

        public void Collect(WorkContext context, List<TaskCandidate> output)
        {
            foreach ((ContainerSnapshot snapshot, Structures.DeliveryChest chest) in context.DeliveryChests)
            {
                foreach (Core.Persistence.DeliveryRequest line in chest.Ledger.OpenLines.ToList())
                {
                    var probe = new ItemStack { Prefab = line.ItemPrefab };
                    if (!Creature.ItemBridge.IsTransportable(probe)) continue;
                    DeliveryTrip trip = DeliveryPlanner.Plan(line.ItemPrefab, line.Remaining, snapshot, context.Carried,
                        context.Sources.Where(s => s.Id != snapshot.Id), context.From);
                    if (trip.Total <= 0) continue;
                    var steps = trip.Takes.Select(t => new TaskStep(StepKind.Take, t.Source, t.Item, t.Item.Amount))
                        .Concat(trip.Puts.Select(p => new TaskStep(StepKind.Put, snapshot.Id, p, p.Amount)))
                        .ToList();
                    string firstTarget = steps[0].Target;
                    ContainerSnapshot first = firstTarget == snapshot.Id ? snapshot : context.Sources.First(s => s.Id == firstTarget);
                    output.Add(new TaskCandidate
                    {
                        Kind = TaskKinds.Deliver,
                        Priority = TaskKinds.DeliverPriority,
                        Distance = Vec3.Distance(context.From, first.Position),
                        Keys = new[] { $"deliver:{snapshot.Id}:{line.ItemPrefab}" },
                        Build = () => new TaskPlan(TaskKinds.Deliver, steps),
                    });
                }
            }
        }
    }
}
