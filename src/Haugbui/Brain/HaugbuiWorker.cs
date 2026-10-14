using System.Linq;
using Haugbui.Core.Coordination;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;
using Haugbui.Core.Storage;
using Haugbui.Creature;
using Haugbui.Diagnostics;
using Haugbui.Integration;
using Haugbui.Registry;
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
        private readonly CarriedEndpoint _carried;
        private Container _target;
        private float _travelDeadline;
        private float _nextCoverage;

        public HaugbuiWorker(TombController tomb)
        {
            _tomb = tomb;
            _planner = new TaskPlanner<WorkContext>(Reservations.Table, new ITaskProvider<WorkContext>[]
            {
                new DepositProvider(),
                new SortTombStorageProvider(),
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
        public IAccessPolicy Policy { get; set; } = SinglePlayerAccessPolicy.Instance;

        /// <summary>Self-test isolation for checks that need an idle Haugbui.</summary>
        public bool Paused { get; set; }

        private string Holder => _tomb.Guid;

        /// <summary>Panel text: what it is doing, or why it keeps items.</summary>
        public string Activity
        {
            get
            {
                if (Plan != null) return Plan.Kind == TaskKinds.Deposit ? "$haugbui_activity_storing" : "$haugbui_activity_sorting";
                if (!_tomb.Carried.IsEmpty && _tomb.Machine.IsOut) return "$haugbui_activity_no_destination";
                return null;
            }
        }

        /// <summary>Scheduler tick (PlanningIntervalSeconds): renew leases, plan when idle, refresh the loaded coverage.</summary>
        public void Tick()
        {
            if (!_tomb.IsOwner || _tomb.State == null) return;
            if (Time.time >= _nextCoverage) RefreshCoverage();
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
            bool moved = Execute(step, new ContainerEndpoint(_target));
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
        private bool Execute(TaskStep step, ContainerEndpoint target)
        {
            TransferRecord record = step.Kind == StepKind.Take
                ? Reservations.Transfers.Move(target, _carried, step.Item, step.Amount)
                : Reservations.Transfers.Move(_carried, target, step.Item, step.Amount);
            if (record.Moved <= 0)
            {
                NoDestination.Fail(step.Item.KindKey, TimeSource.Current.Now);
                return false;
            }
            if (step.Kind == StepKind.Take) Takes++;
            else
            {
                Puts++;
                NoDestination.Succeed(step.Item.KindKey);
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

        private Container Resolve(string id)
        {
            if (!_context.ById.TryGetValue(id, out Container container) || container == null) return null;
            ZNetView view = container.m_nview;
            if (view == null || !view.IsValid() || ZNetScene.instance.FindInstance(view.GetZDO()) == null) return null;
            return container;
        }

        private static Collider MainCollider(Component target) =>
            target.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger)
                .OrderByDescending(c => c.bounds.size.sqrMagnitude).FirstOrDefault();
    }
}
