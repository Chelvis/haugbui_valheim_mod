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

        public const int DepositPriority = 100;
        public const int SortTombStoragePriority = 50;
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
                if (plan.Count == 0)
                {
                    context.NoDestination.Fail(stack.KindKey, context.Now);
                    continue;
                }
                ContainerSnapshot first = context.Destinations.First(d => d.Id == plan[0].ContainerId);
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
            var simulated = new CarriedInventory(context.Carried.Slots, context.Carried.Capacity);
            simulated.Restore(context.Carried.Stacks.Select(s => s.Copy(s.Amount)));
            var steps = new List<TaskStep>();
            foreach (ItemStack stack in DepositProvider.Distinct(context.TombStorage.Items))
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
                if (take > 0) steps.Add(new TaskStep(StepKind.Take, context.TombStorage.Id, stack.Copy(take), take));
            }
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
    }
}
