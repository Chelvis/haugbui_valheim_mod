using System.Collections.Generic;
using Haugbui.Core.Combat;
using Haugbui.Core.Creature;
using Haugbui.Core.Progression;
using Haugbui.Creature;
using Haugbui.Diagnostics;
using Haugbui.Integration;
using UnityEngine;

namespace Haugbui.Brain
{
    /// <summary>
    /// HB-T-071..073: the combat interrupt of one tomb's Haugbui. Scans about once a second (immediately on damage,
    /// when a raid starts or ends, or when it is hurt with no target) for the nearest creature attacking a player, a
    /// Haugbui or the base (any creature that is not tamed while a raid reaches the radius), decides fight / flee /
    /// back to work (CombatRules) and drives the creature while it fights or flees. Work is aborted on interrupt and
    /// re-planned afterwards; carried items stay carried.
    /// </summary>
    internal sealed class CombatTaskProvider
    {
        private const float ScanSeconds = 1f;
        private const float AlertCooldownSeconds = 10f;
        private const float NearTombMeters = 6f;
        private const float WaitSpeed = 1f;

        private readonly TombController _tomb;
        private readonly ThreatTracker _threat = new ThreatTracker();
        private readonly Dictionary<string, Character> _attackers = new Dictionary<string, Character>();
        private readonly CombatActuator _actuator = new CombatActuator();
        private float _nextScan;
        private float _nextAlert;
        private bool _weaponLogged;

        public CombatTaskProvider(TombController tomb) => _tomb = tomb;

        public Character Target { get; private set; }

        /// <summary>A raid reaches the operational radius (last scan): every creature that is not tamed is a target.</summary>
        public bool Raid { get; private set; }
        public int Engagements { get; private set; }
        public int Flights { get; private set; }
        public int Alerts { get; private set; }
        public int AttacksStarted => _actuator.AttacksStarted;
        public bool Threatened { get; private set; }

        public void OnDamaged(float damage, Character attacker)
        {
            string id = CombatGateway.IdOf(attacker);
            if (id != null) _attackers[id] = attacker;
            _threat.Damaged(Time.timeAsDouble, id);
            _nextScan = 0f;
        }

        /// <summary>Death, entering the tomb, despawn: nothing carries over to the next emergence.</summary>
        public void Reset()
        {
            _threat.Clear();
            _attackers.Clear();
            Target = null;
            Raid = false;
            Threatened = false;
        }

        /// <summary>Every frame while the creature is out, before the worker's step.</summary>
        public void Step(bool sleeping)
        {
            HaugbuiCreature creature = _tomb.Creature;
            HaugbuiStateMachine machine = _tomb.Machine;
            if (creature == null || !_tomb.CanWork) return;

            bool enabled = _tomb.CombatEnabled;
            HaugbuiAttributes attributes = _tomb.Attributes;
            bool raid = CombatGateway.RaidReaches(_tomb.transform.position, attributes.Radius);
            if (raid != Raid)
            {
                Raid = raid;
                _nextScan = 0f;
                Log.Info($"Tomb {_tomb.Guid}: {(raid ? "raid in range, attacking every creature that is not tamed" : "raid over")}.");
            }

            Threatened = _threat.Threatened(Time.timeAsDouble, Status);
            if (_threat.Attackers == 0) _attackers.Clear();

            if (!enabled || sleeping) Target = null;
            else if (Time.time >= _nextScan || (Threatened && Target == null))
            {
                _nextScan = Time.time + ScanSeconds;
                Target = Nearest(creature, attributes.Radius);
            }
            else if (Target != null && !IsValid(Target, creature, attributes.Radius)) Target = null;

            var input = new CombatInput
            {
                State = machine.State,
                Enabled = enabled,
                Paused = sleeping,
                ReturnPending = machine.ReturnPending,
                HasTarget = Target != null,
                Threatened = Threatened,
            };
            switch (CombatRules.Decide(input))
            {
                case CombatAction.Engage:
                    Engage(creature);
                    break;
                case CombatAction.Flee:
                    _tomb.Worker.Abort("fleeing");
                    if (machine.TryEnter(HaugbuiState.Fleeing))
                    {
                        Flights++;
                        Log.Debug($"Tomb {_tomb.Guid}: hurt, flying to the tomb.");
                    }
                    break;
                case CombatAction.Disengage:
                    machine.CompleteStep();
                    creature.Stop();
                    Log.Debug($"Tomb {_tomb.Guid}: fight over ({machine.State}).");
                    break;
                case CombatAction.StopFleeing:
                    if (machine.TryEnter(HaugbuiState.Idle)) creature.Stop();
                    break;
            }

            if (machine.State == HaugbuiState.Combat && Target != null) _actuator.Fight(creature, Target, attributes.MaxSpeed);
            else if (machine.State == HaugbuiState.Fleeing)
            {
                bool home = Vector3.Distance(creature.transform.position, _tomb.transform.position) < NearTombMeters;
                creature.MaxSpeed = home ? Mathf.Min(WaitSpeed, attributes.MaxSpeed) : attributes.MaxSpeed;
                creature.Facing = null;
                if (!creature.HasTarget || creature.Arrived) creature.MoveTo(_tomb.HoverPoint());
            }
        }

        private void Engage(HaugbuiCreature creature)
        {
            _tomb.Worker.Abort("combat");
            if (!_tomb.Machine.TryEnter(HaugbuiState.Combat)) return;
            Engagements++;
            if (!_weaponLogged)
            {
                _weaponLogged = true;
                Log.Info($"Tomb {_tomb.Guid}: Haugbui weapon {CombatGateway.Describe(CombatGateway.Weapon(creature.Humanoid))}; " +
                         $"level {_tomb.Attributes.Level}: hits ×{creature.DealtFactor:0.##}, damage taken ×{creature.TakenFactor:0.##}.");
            }
            if (Time.time >= _nextAlert)
            {
                _nextAlert = Time.time + AlertCooldownSeconds;
                BaseAI ai = creature.GetComponent<BaseAI>();
                if (ai != null && !CreatureVisuals.LowQuality)
                {
                    ai.m_alertedEffects.Create(creature.transform.position, Quaternion.identity);
                    Alerts++;
                }
            }
            Log.Debug($"Tomb {_tomb.Guid}: engaging {Target.m_name} at {Vector3.Distance(Target.transform.position, creature.transform.position):0.0} m.");
        }

        private Character Nearest(HaugbuiCreature creature, float radius)
        {
            Vector3 tomb = _tomb.transform.position;
            Vector3 from = creature.transform.position;
            float radiusSq = radius * radius;
            Character best = null;
            float bestSq = float.MaxValue;
            foreach (Character character in Character.GetAllCharacters())
            {
                if (character == null || (character.transform.position - tomb).sqrMagnitude > radiusSq) continue;
                if (!IsValid(character, creature, radius)) continue;
                float sq = (character.transform.position - from).sqrMagnitude;
                if (sq >= bestSq) continue;
                best = character;
                bestSq = sq;
            }
            return best;
        }

        /// <param name="raid">Judge as during a raid; null uses the state of the last scan.</param>
        internal TargetVerdict Judge(Character character, bool? raid = null)
        {
            HaugbuiCreature creature = _tomb.Creature;
            return TargetPolicy.Judge(Facts(character, creature != null ? creature.Character : null), _tomb.Attributes.Radius, raid ?? Raid);
        }

        private bool IsValid(Character character, HaugbuiCreature creature, float radius) =>
            character != null && TargetPolicy.IsValid(Facts(character, creature.Character), radius, Raid);

        private TargetFacts Facts(Character character, Character haugbui) =>
            CombatGateway.Facts(character, haugbui, _tomb.transform.position,
                _threat.HitRecently(CombatGateway.IdOf(character), Time.timeAsDouble));

        private AttackerStatus Status(string id)
        {
            if (!_attackers.TryGetValue(id, out Character attacker) || attacker == null || attacker.IsDead()
                || !LoadedArea.IsLoaded(attacker.transform.position)
                || Vector3.Distance(attacker.transform.position, _tomb.transform.position) > _tomb.Attributes.Radius)
            {
                _attackers.Remove(id);
                return AttackerStatus.Gone;
            }
            return CombatGateway.IsTargeting(attacker, _tomb.Creature != null ? _tomb.Creature.Character : null)
                ? AttackerStatus.TargetingIt
                : AttackerStatus.Present;
        }
    }
}
