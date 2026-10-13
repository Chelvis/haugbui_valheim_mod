using System;
using System.Collections.Generic;

namespace Haugbui.Core.Creature
{
    public enum HaugbuiState
    {
        Dormant,
        Emerging,
        Idle,
        Traveling,
        Interacting,
        Combat,
        Fleeing,
        Returning,
        Entering,
        Dead
    }

    /// <summary>
    /// HB-043/142. Interrupt priority: Dead &gt; Combat &gt; Returning &gt; tasks. An Interacting step is atomic: a return
    /// requested during it waits until the step completes.
    /// </summary>
    public sealed class HaugbuiStateMachine
    {
        private static readonly Dictionary<HaugbuiState, HaugbuiState[]> Allowed = new Dictionary<HaugbuiState, HaugbuiState[]>
        {
            [HaugbuiState.Dormant] = new[] { HaugbuiState.Emerging, HaugbuiState.Idle },
            [HaugbuiState.Emerging] = new[] { HaugbuiState.Idle, HaugbuiState.Returning },
            [HaugbuiState.Idle] = new[] { HaugbuiState.Traveling, HaugbuiState.Combat, HaugbuiState.Fleeing, HaugbuiState.Returning },
            [HaugbuiState.Traveling] = new[] { HaugbuiState.Idle, HaugbuiState.Interacting, HaugbuiState.Combat, HaugbuiState.Fleeing, HaugbuiState.Returning },
            [HaugbuiState.Interacting] = new[] { HaugbuiState.Idle, HaugbuiState.Traveling, HaugbuiState.Returning },
            [HaugbuiState.Combat] = new[] { HaugbuiState.Idle, HaugbuiState.Fleeing, HaugbuiState.Returning },
            [HaugbuiState.Fleeing] = new[] { HaugbuiState.Idle, HaugbuiState.Returning },
            [HaugbuiState.Returning] = new[] { HaugbuiState.Entering, HaugbuiState.Combat, HaugbuiState.Idle },
            [HaugbuiState.Entering] = new[] { HaugbuiState.Dormant },
            [HaugbuiState.Dead] = new[] { HaugbuiState.Dormant },
        };

        private readonly Func<double> _clock;

        public HaugbuiStateMachine(Func<double> clock, HaugbuiState initial = HaugbuiState.Dormant)
        {
            _clock = clock;
            State = initial;
            EnteredAt = clock();
        }

        public HaugbuiState State { get; private set; }
        public double EnteredAt { get; private set; }
        public double TimeInState => _clock() - EnteredAt;
        public bool ReturnPending { get; private set; }

        /// <summary>True while the creature exists in the world.</summary>
        public bool IsOut => State != HaugbuiState.Dormant && State != HaugbuiState.Dead;

        public bool IsWorking => State == HaugbuiState.Traveling || State == HaugbuiState.Interacting;

        public event Action<HaugbuiState, HaugbuiState> Changed;

        public static bool CanTransition(HaugbuiState from, HaugbuiState to) =>
            to == HaugbuiState.Dead ? from != HaugbuiState.Dormant && from != HaugbuiState.Dead
                                    : Allowed.TryGetValue(from, out HaugbuiState[] targets) && Array.IndexOf(targets, to) >= 0;

        public bool TryEnter(HaugbuiState to)
        {
            if (!CanTransition(State, to)) return false;
            if (ReturnPending && (to == HaugbuiState.Traveling || to == HaugbuiState.Interacting)) return false;
            HaugbuiState from = State;
            State = to;
            EnteredAt = _clock();
            if (to == HaugbuiState.Returning || to == HaugbuiState.Dead || to == HaugbuiState.Dormant) ReturnPending = false;
            Changed?.Invoke(from, to);
            return true;
        }

        /// <summary>Asks the creature to go home; returns true when it is already on its way or inside.</summary>
        public bool RequestReturn()
        {
            switch (State)
            {
                case HaugbuiState.Dormant:
                case HaugbuiState.Dead:
                case HaugbuiState.Returning:
                case HaugbuiState.Entering:
                    return true;
                case HaugbuiState.Interacting:
                case HaugbuiState.Combat:
                    ReturnPending = true;
                    return false;
                default:
                    return TryEnter(HaugbuiState.Returning);
            }
        }

        public void CancelReturn() => ReturnPending = false;

        /// <summary>Ends an Interacting step or a fight; goes home if a return is pending.</summary>
        public void CompleteStep()
        {
            if (State != HaugbuiState.Interacting && State != HaugbuiState.Combat) return;
            if (ReturnPending) TryEnter(HaugbuiState.Returning);
            else TryEnter(HaugbuiState.Idle);
        }

        public bool Kill() => TryEnter(HaugbuiState.Dead);

        /// <summary>Restores a saved state; only Dormant, Dead and Idle (out) are persisted.</summary>
        public void Restore(HaugbuiState saved)
        {
            State = saved == HaugbuiState.Dead ? HaugbuiState.Dead : saved == HaugbuiState.Dormant ? HaugbuiState.Dormant : HaugbuiState.Idle;
            EnteredAt = _clock();
            ReturnPending = false;
        }

        /// <summary>The subset saved in the tomb.</summary>
        public static HaugbuiState Persisted(HaugbuiState state) =>
            state == HaugbuiState.Dormant || state == HaugbuiState.Dead ? state
            : state == HaugbuiState.Entering ? HaugbuiState.Dormant
            : HaugbuiState.Idle;
    }
}
