using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Coordination;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;
using Haugbui.Core.Harvest;
using Haugbui.Core.Persistence;
using Haugbui.Core.Production;
using Haugbui.Core.Storage;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Integration.StationAdapters;
using Haugbui.Registry;
using UnityEngine;

namespace Haugbui.Brain
{
    /// <summary>One station of the area with its job, as seen at one planning tick.</summary>
    internal sealed class StationWork
    {
        public MonoBehaviour Station;
        public IStationAdapter Adapter;
        public string Id;
        public StationJobState Job;
        public PersistentSlot<StationJobState> Slot;
        public StationRecipe Recipe;
        public StationState State;
        public Vec3 Position;
        public readonly List<ItemDrop> Outputs = new List<ItemDrop>();
    }

    /// <summary>Panel and hover text per station, written by the Haugbui that works it (HB-115/120).</summary>
    internal static class ProductionStatus
    {
        private static readonly Dictionary<string, (string Text, float At)> Lines = new Dictionary<string, (string, float)>();
        private const float FreshSeconds = 30f;

        public static void Set(string station, string text) => Lines[station] = (text, Time.time);

        /// <summary>Null when no Haugbui looked at the station recently.</summary>
        public static string Get(string station) =>
            Lines.TryGetValue(station, out (string Text, float At) line) && Time.time - line.At < FreshSeconds ? line.Text : null;

        public static void Clear() => Lines.Clear();
    }

    /// <summary>
    /// HB-T-063/064/065: feeds stations whose job this tomb owns (inputs from storage minus delivery reservations, fuel
    /// within capacity) and collects their outputs at the output point, also with the ground toggle off (HB-093).
    /// </summary>
    internal sealed class ProductionProvider : ITaskProvider<WorkContext>
    {
        /// <summary>HB-121: a cooking slot is loaded only if the item is done this long before the Haugbui must head home.</summary>
        public const double CookingMarginSeconds = 30;

        private readonly List<GroundItem> _candidates = new List<GroundItem>();
        private readonly Dictionary<string, ItemDrop> _drops = new Dictionary<string, ItemDrop>();

        public bool IsEnabled(WorkContext context) => context.Production && context.Stations.Count > 0;

        public void Collect(WorkContext context, List<TaskCandidate> output)
        {
            foreach (StationWork work in context.Stations)
            {
                Operate(context, work, output);
                CollectOutputs(context, work, output);
                Feed(context, work, output);
            }
        }

        /// <summary>HB-T-066/068: tap a ready fermenter, take done items off a cooking station; only the job's own items.</summary>
        private static void Operate(WorkContext context, StationWork work, List<TaskCandidate> output)
        {
            StationState state = work.State;
            if (state.ReadyActions <= 0 || state.NextReady == null) return;
            bool ours = state.NextReady == work.Recipe.Input || state.NextReady == work.Recipe.Output || state.NextReady == state.BurntItem;
            if (!ours) return;
            var step = new TaskStep(StepKind.Operate, work.Id, new ItemStack { Prefab = work.Recipe.Output }, state.ReadyActions);
            string station = work.Id;
            output.Add(new TaskCandidate
            {
                Kind = TaskKinds.ProductionOperate,
                Priority = state.Urgent ? TaskKinds.ProductionUrgentPriority : TaskKinds.ProductionOperatePriority,
                Distance = Vec3.Distance(context.From, work.Position),
                Keys = new[] { "station:" + work.Id },
                Build = () => new TaskPlan(TaskKinds.ProductionOperate, new[] { step }) { Tag = station },
            });
        }

        private void CollectOutputs(WorkContext context, StationWork work, List<TaskCandidate> output)
        {
            if (work.Outputs.Count == 0) return;
            _candidates.Clear();
            _drops.Clear();
            foreach (ItemDrop drop in work.Outputs)
            {
                string id = DropEndpoint.IdOf(drop);
                if (!context.IsFree(GatherKeys.Drop(id))) continue;
                _candidates.Add(new GroundItem { Id = id, Position = WorldRegistry.ToVec3(drop.transform.position), Item = DropEndpoint.Read(drop) });
                _drops[id] = drop;
            }
            if (_candidates.Count == 0) return;
            List<GroundItem> route = PickupRoute.Plan(_candidates, context.From, context.Carried);
            if (route.Count == 0) return;
            foreach (GroundItem stop in route) context.Targets[stop.Id] = _drops[stop.Id];
            List<TaskStep> steps = route.Select(s => new TaskStep(StepKind.PickUp, s.Id, s.Item, s.Item.Amount)).ToList();
            string station = work.Id;
            output.Add(new TaskCandidate
            {
                Kind = TaskKinds.ProductionOutputs,
                Priority = TaskKinds.ProductionOutputsPriority,
                Distance = Vec3.Distance(context.From, work.Position),
                Keys = route.Select(s => GatherKeys.Drop(s.Id)).ToArray(),
                Build = () => new TaskPlan(TaskKinds.ProductionOutputs, steps) { Tag = station },
            });
        }

        private static void Feed(WorkContext context, StationWork work, List<TaskCandidate> output)
        {
            StationState state = work.State;
            StationJobState job = work.Job;
            int yield = Mathf.Max(1, work.Recipe.Yield);
            string reason = Status(context, work, yield);
            if (reason != null)
            {
                if (!context.Probe) ProductionStatus.Set(work.Id, reason);
                return;
            }

            var carry = new CarriedInventory(context.Carried.Slots, context.Carried.Capacity);
            carry.Restore(context.Carried.Stacks.Select(s => s.Copy(s.Amount)));

            // HB-117: another input in the queue is the player's; only fuel goes in, so it finishes and the job resumes.
            bool manual = state.QueuedInputs.Any(i => i != work.Recipe.Input);
            SupplyPick inputs = new SupplyPick();
            int wanted = 0;
            // HB-121: without the Altar, a cooking slot is loaded only if it can come off before the Haugbui heads home.
            bool tooLate = work.Recipe.CookSeconds > 0 && work.Recipe.CookSeconds + CookingMarginSeconds > context.WorkSecondsLeft;
            if (!manual && !tooLate && !string.IsNullOrEmpty(work.Recipe.Input))
            {
                wanted = StationFill.Inputs(state.Queued, state.MaxQueue, ProductionJob.InputsWanted(job, yield));
                inputs = SupplyPlanner.Pick(work.Recipe.Input, wanted, state.RequiredCheated, carry, context.Sources, context.From,
                    context.Reserved(work.Recipe.Input));
            }

            SupplyPick fuel = new SupplyPick();
            string fuelPrefab = null;
            int fuelWanted = StationFill.Fuel(state.Fuel, state.MaxFuel, state.Queued + inputs.Total, state.FuelPerProduct);
            if (fuelWanted > 0)
            {
                // Fuel is consumed without a flag (RPC_AddFuel), so cheated fuel would come back clean (HB-061).
                foreach (string prefab in state.FuelPrefabs)
                {
                    fuel = SupplyPlanner.Pick(prefab, fuelWanted, false, carry, context.Sources, context.From, context.Reserved(prefab));
                    fuelPrefab = prefab;
                    if (fuel.Total > 0) break;
                }
            }

            // An empty queue with no input to give needs no fuel either; fuel alone only keeps a running queue burning.
            bool fuelOnlyTrip = inputs.Total <= 0 && state.Queued == 0 && work.Adapter.Kind(work.Station) != StationKind.FuelOnly;
            if (inputs.Total <= 0 && (fuel.Total <= 0 || fuelOnlyTrip))
            {
                if (context.Probe) return;
                if (manual) ProductionStatus.Set(work.Id, "$haugbui_job_manual_use");
                else if (tooLate) ProductionStatus.Set(work.Id, "$haugbui_job_too_close_to_dawn");
                else if (wanted > 0) ProductionStatus.Set(work.Id, Waiting(work.Recipe.InputItem));
                else if (fuelWanted > 0 && fuel.Total <= 0 && state.FuelPrefabs.Count > 0) ProductionStatus.Set(work.Id, Waiting(FuelItem(state.FuelPrefabs[0])));
                else ProductionStatus.Set(work.Id, wanted == 0 && ProductionJob.InputsWanted(job, yield) == 0 ? "$haugbui_job_finishing" : "$haugbui_job_working");
                return;
            }

            var steps = new List<TaskStep>();
            steps.AddRange(inputs.Takes.Concat(fuel.Takes).Select(t => new TaskStep(StepKind.Take, t.Source, t.Item, t.Item.Amount)));
            if (fuel.Total > 0)
                steps.Add(new TaskStep(StepKind.Fuel, work.Id, new ItemStack { Prefab = fuelPrefab, Cheated = false }, fuel.Total));
            if (inputs.Total > 0)
                steps.Add(new TaskStep(StepKind.Feed, work.Id, new ItemStack { Prefab = work.Recipe.Input, Cheated = inputs.Cheated }, inputs.Total));
            context.Targets[work.Id] = work.Station;
            if (!context.Probe) ProductionStatus.Set(work.Id, manual ? "$haugbui_job_manual_use" : "$haugbui_job_working");

            string first = steps[0].Target;
            Vec3 firstPosition = first == work.Id ? work.Position : context.Sources.First(s => s.Id == first).Position;
            string station = work.Id;
            output.Add(new TaskCandidate
            {
                Kind = TaskKinds.ProductionFeed,
                Priority = TaskKinds.ProductionFeedPriority,
                Distance = Vec3.Distance(context.From, firstPosition),
                Keys = new[] { "station:" + work.Id },
                Build = () => new TaskPlan(TaskKinds.ProductionFeed, steps) { Tag = station },
            });
        }

        /// <summary>Why the station gets nothing now; null when it may be fed.</summary>
        private static string Status(WorkContext context, StationWork work, int yield)
        {
            StationState state = work.State;
            JobPhase phase = ProductionJob.Phase(work.Job, yield, !state.Busy && work.Outputs.Count == 0);
            if (phase == JobPhase.Complete) return "$haugbui_job_complete";
            if (phase == JobPhase.Disabled) return "$haugbui_job_disabled";
            if (state.Blocked != null) return state.Blocked;
            if (!ProductionGate.Known(context.OwnerId, work.Recipe)) return "$haugbui_job_unknown_recipe";
            return null;
        }

        private static string Waiting(ItemDrop item) =>
            item != null ? "$haugbui_job_waiting_for " + item.m_itemData.m_shared.m_name : "$haugbui_job_waiting";

        private static ItemDrop FuelItem(string prefab) => ObjectDB.instance?.GetItemPrefab(prefab)?.GetComponent<ItemDrop>();
    }

    /// <summary>HB-T-069/070: which tombs may produce, and which recipes the owner may use.</summary>
    internal static class ProductionGate
    {
        public static bool Unlocked(TombController tomb) =>
            tomb.Structure.Progress.Level >= HaugbuiPlugin.Instance.Settings.ProductionUnlockLevel.Value;

        public static bool Enabled(TombController tomb) => Unlocked(tomb) && tomb.State.Has(TombToggles.Production);

        /// <summary>
        /// HB-116: input and output must be known materials of the owner. Only the local player's knowledge is
        /// readable; for another owner the recipe was already checked when the job was set up.
        /// </summary>
        public static bool Known(long ownerId, StationRecipe recipe)
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.GetPlayerID() != ownerId) return true;
            return Known(player, recipe);
        }

        /// <summary>Fuel-only stations (no input, e.g. the frost kiln) need no discovery: the player uses them freely too.</summary>
        public static bool Known(Player player, StationRecipe recipe) =>
            recipe.InputItem == null
            || player.IsMaterialKnown(recipe.InputItem.m_itemData.m_shared.m_name)
            && (recipe.OutputItem == null || player.IsMaterialKnown(recipe.OutputItem.m_itemData.m_shared.m_name));
    }
}
