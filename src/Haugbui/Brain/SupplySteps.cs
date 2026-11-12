using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Coordination;
using Haugbui.Core.Creature;
using Haugbui.Core.Harvest;
using Haugbui.Core.Storage;
using UnityEngine;

namespace Haugbui.Brain
{
    internal static class SupplySteps
    {
        public static void AppendTakes(WorkContext context, IEnumerable<PlannedTake> takes, ICollection<string> dropIds,
            List<TaskStep> steps, List<string> keys)
        {
            if (takes == null) return;
            foreach (PlannedTake take in takes)
            {
                MaterialPile pile = context.Piles.FirstOrDefault(p => p.Id == take.Source);
                if (pile != null)
                {
                    ItemStack refund = pile.Refund.Copy(pile.Refund.Amount);
                    steps.Add(new TaskStep(StepKind.Dismantle, pile.Id, refund, refund.Amount));
                    keys.Add("pile:" + pile.Id);
                    continue;
                }
                bool drop = dropIds != null && dropIds.Contains(take.Source);
                steps.Add(new TaskStep(drop ? StepKind.PickUp : StepKind.Take, take.Source, take.Item, take.Item.Amount));
                if (!drop) continue;
                context.ClaimedDrops.Add(take.Source);
                keys.Add(GatherKeys.Drop(take.Source));
                if (context.SupplyDropTargets.TryGetValue(take.Source, out ItemDrop item)) context.Targets[take.Source] = item;
            }
        }
    }
}
