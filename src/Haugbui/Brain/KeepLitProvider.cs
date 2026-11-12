using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Coordination;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;
using Haugbui.Core.Harvest;
using Haugbui.Core.Production;
using Haugbui.Core.Storage;
using Haugbui.Integration;
using Haugbui.Registry;
using UnityEngine;

namespace Haugbui.Brain
{
    /// <summary>Refills a fireplace marked keep-lit when its shown fuel is 0 or 1. The production toggle does not gate it.</summary>
    internal sealed class KeepLitProvider : ITaskProvider<WorkContext>
    {
        private readonly List<Piece> _pieces = new List<Piece>();

        public bool IsEnabled(WorkContext context) => true;

        public void Collect(WorkContext context, List<TaskCandidate> output)
        {
            _pieces.Clear();
            Piece.GetAllPiecesInRadius(context.Center, context.Radius, _pieces);
            foreach (Piece piece in _pieces)
            {
                Fireplace fire = piece.GetComponent<Fireplace>() ?? piece.GetComponentInChildren<Fireplace>();
                if (!KeepLit.IsRefillable(fire) || !KeepLit.IsOn(fire)) continue;
                if (!context.Policy.WardsAllow(fire.transform.position, context.OwnerId) || !context.Mine(fire.transform.position)) continue;
                float stored = KeepLit.Fuel(fire);
                int units = FireFuel.UnitsToFill(stored, (int)fire.m_maxFuel);
                if (units <= 0) continue;
                string id = KeepLit.IdOf(fire);
                string fuelName = KeepLit.FuelPrefab(fire);
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(fuelName)) continue;

                var carry = new CarriedInventory(context.Carried.Slots, context.Carried.Capacity);
                carry.Restore(context.Carried.Stacks.Select(s => s.Copy(s.Amount)));
                IReadOnlyList<GroundItem> ground = context.GroundItems ? SupplyDrops.Of(context) : null;
                bool bagOnly = CarryCycle.ShouldUnload(context.Carried.Weight, context.Carried.Capacity);
                SupplyPick pick = SupplyPlanner.Pick(fuelName, units, false, carry, context.Sources, context.From,
                    context.Reserved(fuelName), ground, bagOnly, context.Piles);
                if (pick.Total <= 0) continue;

                var dropIds = new HashSet<string>();
                if (ground != null) foreach (GroundItem drop in ground) dropIds.Add(drop.Id);
                var steps = new List<TaskStep>();
                var keys = new List<string> { "fire:" + id };
                SupplySteps.AppendTakes(context, pick.Takes, dropIds, steps, keys);
                int add = System.Math.Min(units, pick.Total);
                steps.Add(new TaskStep(StepKind.Refuel, id, new ItemStack { Prefab = fuelName }, add));
                context.Targets[id] = fire;

                string first = steps[0].Target;
                MaterialPile pile = context.Piles.FirstOrDefault(p => p.Id == first);
                Vec3 firstPosition = first == id ? WorldRegistry.ToVec3(fire.transform.position)
                    : pile != null ? pile.Position
                    : dropIds.Contains(first) ? ground.First(d => d.Id == first).Position
                    : context.Sources.First(s => s.Id == first).Position;
                output.Add(new TaskCandidate
                {
                    Kind = TaskKinds.KeepLit,
                    Priority = TaskKinds.ProductionFeedPriority,
                    Distance = Vec3.Distance(context.From, firstPosition),
                    Keys = keys,
                    Build = () => new TaskPlan(TaskKinds.KeepLit, steps),
                });
            }
        }
    }
}
