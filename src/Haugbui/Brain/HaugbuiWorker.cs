using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Coordination;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;
using Haugbui.Core.Storage;
using Haugbui.Creature;
using Haugbui.Diagnostics;
using Haugbui.Integration;
using Haugbui.Registry;
using Haugbui.Structures;
using UnityEngine;
using Reservations = Haugbui.Coordination.Reservations;

namespace Haugbui.Brain
{
    /// <summary>
    /// HB-T-045/046: plans tasks for one tomb's Haugbui and executes their steps through the tomb's state machine.
    /// A step flies within reach of its target (Traveling), then moves items in one frame (Interacting).
    /// </summary>
    internal sealed class HaugbuiWorker
    {
        private const float CoverageIntervalSeconds = 10f;
        private const float CoverageSampleStep = 8f;
        private const float MinTravelTimeout = 15f;

        private readonly TombController _tomb;
        private readonly TaskPlanner<WorkContext> _planner;
        private readonly WorkContext _context = new WorkContext();
        private readonly WorkContext _probe = new WorkContext();
        private readonly CarriedEndpoint _carried;
        private const float OwnDropSeconds = 300f;

        private readonly Dictionary<string, ItemDrop> _ownDrops = new Dictionary<string, ItemDrop>();
        private readonly Dictionary<string, float> _ownDropsUntil = new Dictionary<string, float>();
        private MonoBehaviour _target;
        private float _travelDeadline;
        private float _nextCoverage;

        public HaugbuiWorker(TombController tomb)
        {
            _tomb = tomb;
            _planner = new TaskPlanner<WorkContext>(Reservations.Table, new ITaskProvider<WorkContext>[]
            {
                new DepositProvider(),
                new DeliveryProvider(),
                new OwnDropsProvider(),
                new HarvestProvider(),
                new SortTombStorageProvider(),
                new CollectionProvider(),
                new GroundItemProvider(),
            });
            _carried = new CarriedEndpoint("carried:" + tomb.Guid, tomb.Carried);
        }

        public TaskPlan Plan { get; private set; }
        public RetryBackoff NoDestination { get; } = new RetryBackoff(2, 60);
        public float Coverage { get; private set; } = 1f;
        public bool PartiallyLoaded => Coverage < LoadedCoverage.FullyLoaded;
        public string LastAbort { get; private set; }
        public int Aborts { get; private set; }
        public int Takes { get; private set; }
        public int Puts { get; private set; }
        public int Deliveries { get; private set; }
        public int Harvests { get; private set; }
        public int PickUps { get; private set; }

        /// <summary>Drops of its own harvests still on the ground (HB-093); session only.</summary>
        public IReadOnlyDictionary<string, ItemDrop> OwnDrops => _ownDrops;
        public IAccessPolicy Policy { get; set; } = SinglePlayerAccessPolicy.Instance;

        /// <summary>World time of the next Collection Chest check (HB-071).</summary>
        public double NextCollectionScan { get; set; }

        /// <summary>Self-test isolation for checks that need an idle Haugbui.</summary>
        public bool Paused { get; set; }

        private string Holder => _tomb.Guid;

        /// <summary>Panel text: what it is doing, or why it keeps items.</summary>
        public string Activity
        {
            get
            {
                if (Plan != null)
                {
                    switch (Plan.Kind)
                    {
                        case TaskKinds.Deposit: return "$haugbui_activity_storing";
                        case TaskKinds.Deliver: return "$haugbui_activity_delivering";
                        case TaskKinds.Collect: return "$haugbui_activity_collecting";
                        case TaskKinds.Harvest: return "$haugbui_activity_harvesting";
                        case TaskKinds.Gather: return "$haugbui_activity_gathering";
                        default: return "$haugbui_activity_sorting";
                    }
                }
                if (!_tomb.Carried.IsEmpty && _tomb.Machine.IsOut) return "$haugbui_activity_no_destination";
                return null;
            }
        }

        /// <summary>Scheduler tick (PlanningIntervalSeconds): renew leases, plan when idle, refresh the loaded coverage.</summary>
        public void Tick()
        {
            if (!_tomb.IsOwner || _tomb.State == null) return;
            if (Time.time >= _nextCoverage) RefreshCoverage();
            PruneOwnDrops();
            if (!_tomb.CanWork) return;
            if (Plan != null) Reservations.Table.Renew(Holder);
            else PlanNow();
        }

        public void RefreshCoverage()
        {
            _nextCoverage = Time.time + CoverageIntervalSeconds;
            Coverage = LoadedArea.Coverage(_tomb.transform.position, _tomb.Attributes.Radius, CoverageSampleStep);
        }

        public void PlanNow()
        {
            if (Plan != null || Paused || !_tomb.CanWork || _tomb.Machine.State != HaugbuiState.Idle || _tomb.Machine.ReturnPending) return;
            _context.Refresh(_tomb, Policy);
            Plan = _planner.Next(_context, Holder);
            if (Plan != null) Log.Debug($"Tomb {_tomb.Guid}: plan {Plan}");
        }

        /// <summary>HB-167: would it find work right now? Plans from the tomb with a separate context and reserves nothing.</summary>
        public bool HasWork()
        {
            if (_tomb.State == null) return false;
            _probe.Refresh(_tomb, Policy, probe: true);
            return _planner.HasCandidates(_probe);
        }

        /// <summary>Called every frame by the tomb while the creature is out; returns true while a task drives the creature.</summary>
        public bool Step()
        {
            if (Plan == null) return false;
            HaugbuiStateMachine machine = _tomb.Machine;
            HaugbuiCreature creature = _tomb.Creature;
            if (creature == null || !(machine.State == HaugbuiState.Idle || machine.State == HaugbuiState.Traveling))
            {
                Abort("interrupted (" + machine.State + ")");
                return false;
            }

            TaskStep step = Plan.Current;
            if (step == null)
            {
                Finish();
                return false;
            }

            if (machine.State == HaugbuiState.Idle)
            {
                if (machine.ReturnPending)
                {
                    Abort("returning");
                    return false;
                }
                _target = Resolve(step.Target);
                if (_target == null && IsOptional(step))
                {
                    // Someone else took that drop or crop; the rest of the trip still makes sense.
                    Plan.Advance();
                    return true;
                }
                if (_target == null)
                {
                    // HB-T-047: an unloaded or removed target is unavailable, never "empty".
                    Abort("target not loaded: " + step.Target);
                    return false;
                }
                if (!machine.TryEnter(HaugbuiState.Traveling)) return true;
                creature.MaxSpeed = _tomb.Attributes.MaxSpeed;
                Collider collider = MainCollider(_target);
                if (collider != null) creature.MoveTo(collider);
                else creature.MoveTo(_target.transform.position);
                float distance = Vector3.Distance(creature.transform.position, _target.transform.position);
                _travelDeadline = Time.time + Mathf.Max(MinTravelTimeout, 3f * distance / Mathf.Max(0.5f, creature.MaxSpeed));
                return true;
            }

            if (_target == null && IsOptional(step))
            {
                machine.TryEnter(HaugbuiState.Idle);
                creature.Stop();
                Plan.Advance();
                return true;
            }
            if (_target == null)
            {
                Abort("target unloaded while travelling");
                return false;
            }
            if (Time.time > _travelDeadline)
            {
                Abort("target out of reach");
                return false;
            }
            if (!creature.Arrived) return true;

            if (!machine.TryEnter(HaugbuiState.Interacting))
            {
                Abort("cannot interact (" + machine.State + ")");
                return false;
            }
            bool moved = Execute(step, _target);
            machine.CompleteStep();
            creature.Stop();
            Plan.Advance();
            if (!Plan.IsDone) return true;
            Finish();
            // Chain the next task at once, but only after progress, so a step that keeps failing cannot spin.
            if (moved) PlanNow();
            return Plan != null;
        }

        /// <returns>True when items moved. A step that moved nothing puts its item kind on the retry backoff.</returns>
        private bool Execute(TaskStep step, MonoBehaviour target)
        {
            switch (step.Kind)
            {
                case StepKind.PickUp:
                    return PickUp((ItemDrop)target) > 0;
                case StepKind.Harvest:
                    return Harvest(target);
                default:
                    return Transfer(step, new ContainerEndpoint((Container)target));
            }
        }

        /// <summary>HB-103: the target's own action; its drops go straight into the carried inventory as far as they fit.</summary>
        private bool Harvest(MonoBehaviour target)
        {
            List<ItemDrop> drops = HarvestGateway.Harvest(target);
            if (drops.Count == 0) return false;
            Harvests++;
            foreach (ItemDrop drop in drops)
            {
                PickUp(drop);
                if (!DropEndpoint.IsLive(drop)) continue;
                string id = DropEndpoint.IdOf(drop);
                _ownDrops[id] = drop;
                _ownDropsUntil[id] = Time.time + OwnDropSeconds;
            }
            _tomb.Creature?.Visuals?.Pulse();
            return true;
        }

        private int PickUp(ItemDrop drop)
        {
            if (!DropEndpoint.IsLive(drop)) return 0;
            ItemStack item = DropEndpoint.Read(drop);
            TransferRecord record = Reservations.Transfers.Move(new DropEndpoint(drop), _carried, item, item.Amount);
            if (record.Moved <= 0) return 0;
            PickUps++;
            _tomb.Creature?.Visuals?.Pulse();
            return record.Moved;
        }

        private void PruneOwnDrops()
        {
            if (_ownDrops.Count == 0) return;
            foreach (string id in _ownDrops.Keys.ToList())
            {
                if (DropEndpoint.IsLive(_ownDrops[id]) && Time.time < _ownDropsUntil[id]) continue;
                _ownDrops.Remove(id);
                _ownDropsUntil.Remove(id);
            }
        }

        private static bool IsOptional(TaskStep step) => step.Kind == StepKind.PickUp || step.Kind == StepKind.Harvest;

        private bool Transfer(TaskStep step, ContainerEndpoint target)
        {
            TransferRecord record = step.Kind == StepKind.Take
                ? Reservations.Transfers.Move(target, _carried, step.Item, step.Amount)
                : Reservations.Transfers.Move(_carried, target, step.Item, step.Amount);
            bool delivery = Plan.Kind == TaskKinds.Deliver;
            if (record.Moved <= 0)
            {
                // A delivery step that moved nothing says nothing about storage destinations.
                if (!delivery) NoDestination.Fail(step.Item.KindKey, TimeSource.Current.Now);
                return false;
            }
            if (step.Kind == StepKind.Take) Takes++;
            else
            {
                Puts++;
                if (delivery)
                {
                    DeliveryChest chest = target.Container.GetComponent<DeliveryChest>();
                    int counted = chest != null ? chest.RecordDelivered(step.Item.Prefab, record.Moved) : 0;
                    Deliveries += counted;
                    if (counted < record.Moved)
                        Log.Debug($"Tomb {_tomb.Guid}: delivered {record.Moved} {step.Item.Prefab}, ledger counted {counted}.");
                }
                else NoDestination.Succeed(step.Item.KindKey);
            }
            _tomb.Creature?.Visuals?.Pulse();
            return true;
        }

        public void Abort(string reason)
        {
            if (Plan == null) return;
            LastAbort = reason;
            Aborts++;
            Log.Debug($"Tomb {_tomb.Guid}: task {Plan.Kind} stopped: {reason}");
            Finish();
            if (_tomb.Machine.State == HaugbuiState.Traveling)
            {
                _tomb.Machine.TryEnter(HaugbuiState.Idle);
                _tomb.Creature?.Stop();
            }
        }

        /// <summary>Death, entering the tomb, despawn: nothing is held for a Haugbui that is not out.</summary>
        public void ReleaseAll()
        {
            Plan = null;
            _target = null;
            Reservations.Table.ReleaseAll(Holder);
        }

        private void Finish()
        {
            _planner.Finish(Plan, Holder);
            Plan = null;
            _target = null;
        }

        private MonoBehaviour Resolve(string id)
        {
            if (!_context.Targets.TryGetValue(id, out MonoBehaviour target) || target == null) return null;
            ZNetView view = ViewOf(target);
            if (view == null || !view.IsValid() || ZNetScene.instance.FindInstance(view.GetZDO()) == null) return null;
            return target;
        }

        private static ZNetView ViewOf(MonoBehaviour target)
        {
            switch (target)
            {
                case Container container: return container.m_nview;
                case ItemDrop drop: return drop.m_nview;
                default: return HarvestGateway.ViewOf(target);
            }
        }

        private static Collider MainCollider(Component target) =>
            target.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger)
                .OrderByDescending(c => c.bounds.size.sqrMagnitude).FirstOrDefault();
    }
}
