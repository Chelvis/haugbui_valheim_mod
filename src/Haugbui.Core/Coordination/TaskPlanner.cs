using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Creature;

namespace Haugbui.Core.Coordination
{
    public enum StepKind
    {
        /// <summary>Move items from the target into the carried inventory.</summary>
        Take,

        /// <summary>Move carried items into the target.</summary>
        Put,

        /// <summary>Pick up a ground item into the carried inventory.</summary>
        PickUp,

        /// <summary>Use the target's own pick/extract action, then pick up what it dropped.</summary>
        Harvest,

        /// <summary>Put carried inputs into a production station through its own RPC.</summary>
        Feed,

        /// <summary>Put carried fuel into a production station through its own RPC.</summary>
        Fuel
    }

    /// <summary>One atomic interaction; the executor first flies within reach of <see cref="Target"/>.</summary>
    public sealed class TaskStep
    {
        public TaskStep(StepKind kind, string target, ItemStack item, int amount)
        {
            Kind = kind;
            Target = target;
            Item = item;
            Amount = amount;
        }

        public StepKind Kind { get; }
        public string Target { get; }
        public ItemStack Item { get; }
        public int Amount { get; }

        public override string ToString() => $"{Kind} {Item?.Prefab} x{Amount} @ {Target}";
    }

    public sealed class TaskPlan
    {
        private int _index;

        public TaskPlan(string kind, IEnumerable<TaskStep> steps)
        {
            Kind = kind;
            Steps = steps.ToList();
        }

        public string Kind { get; internal set; }
        public IReadOnlyList<TaskStep> Steps { get; }
        public IReadOnlyList<string> Keys { get; internal set; } = Array.Empty<string>();

        /// <summary>What the plan works for, e.g. the station whose job it serves.</summary>
        public string Tag { get; set; } = string.Empty;

        public TaskStep Current => _index < Steps.Count ? Steps[_index] : null;
        public bool IsDone => _index >= Steps.Count;
        public int CompletedSteps => _index;

        public void Advance()
        {
            if (_index < Steps.Count) _index++;
        }

        public override string ToString() => $"{Kind}: {string.Join(", ", Steps)}";
    }

    public sealed class TaskCandidate
    {
        public string Kind { get; set; } = string.Empty;
        public int Priority { get; set; }

        /// <summary>Tie-break among equal priorities: nearer first.</summary>
        public float Distance { get; set; }

        public IReadOnlyList<string> Keys { get; set; } = Array.Empty<string>();

        /// <summary>Builds the steps once the keys are reserved; null or empty means "nothing to do after all".</summary>
        public Func<TaskPlan> Build { get; set; }
    }

    public interface ITaskProvider<in TContext>
    {
        bool IsEnabled(TContext context);

        /// <summary>Cheap: adds candidates, does not reserve or change anything.</summary>
        void Collect(TContext context, List<TaskCandidate> output);
    }

    /// <summary>ARCHITECTURE §5.1: highest priority candidate whose reservation keys are all free wins.</summary>
    public sealed class TaskPlanner<TContext>
    {
        private readonly ReservationTable _reservations;
        private readonly List<ITaskProvider<TContext>> _providers;
        private readonly List<TaskCandidate> _candidates = new List<TaskCandidate>();

        public TaskPlanner(ReservationTable reservations, IEnumerable<ITaskProvider<TContext>> providers)
        {
            _reservations = reservations;
            _providers = providers.ToList();
        }

        /// <summary>Candidates seen by the last <see cref="Next"/> call (diagnostics).</summary>
        public IReadOnlyList<TaskCandidate> LastCandidates => _candidates;

        public TaskPlan Next(TContext context, string holder)
        {
            _candidates.Clear();
            foreach (ITaskProvider<TContext> provider in _providers)
                if (provider.IsEnabled(context)) provider.Collect(context, _candidates);

            foreach (TaskCandidate candidate in _candidates.OrderByDescending(c => c.Priority).ThenBy(c => c.Distance))
            {
                if (!_reservations.TryReserveAll(candidate.Keys, holder)) continue;
                TaskPlan plan = candidate.Build?.Invoke();
                if (plan == null || plan.Steps.Count == 0)
                {
                    foreach (string key in candidate.Keys) _reservations.Release(key, holder);
                    continue;
                }
                plan.Kind = candidate.Kind;
                plan.Keys = candidate.Keys;
                return plan;
            }
            return null;
        }

        /// <summary>Whether any provider has something to do now; reserves nothing (HB-167 work check).</summary>
        public bool HasCandidates(TContext context)
        {
            var found = new List<TaskCandidate>();
            foreach (ITaskProvider<TContext> provider in _providers)
            {
                if (!provider.IsEnabled(context)) continue;
                provider.Collect(context, found);
                if (found.Count > 0) return true;
            }
            return false;
        }

        public void Finish(TaskPlan plan, string holder)
        {
            if (plan == null) return;
            foreach (string key in plan.Keys) _reservations.Release(key, holder);
        }
    }
}
