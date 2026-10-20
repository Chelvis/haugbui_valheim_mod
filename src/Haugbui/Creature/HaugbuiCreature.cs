using System;
using System.Collections.Generic;
using HarmonyLib;
using Haugbui.Core.Creature;
using Haugbui.Diagnostics;
using Haugbui.Integration;
using UnityEngine;

namespace Haugbui.Creature
{
    /// <summary>
    /// HB-T-028/029: the creature side. Vanilla AI never runs; the tomb's state machine drives it through MoveTo/Stop.
    /// Only the ZDO owner moves it; every peer shows the visuals from the state in its ZDO.
    /// </summary>
    public sealed class HaugbuiCreature : MonoBehaviour
    {
        public const float ArriveDistance = 1.0f;
        private const float Acceleration = 4f;
        private const float TravelAltitude = 1.5f;
        private const float MinFlySpeed = 0.05f;
        private const float TurnDegreesPerSecond = 360f;
        private const float MinHeadingSpeed = 0.3f;

        private static readonly List<HaugbuiCreature> Instances = new List<HaugbuiCreature>();

        private ZNetView _view;
        private Character _character;
        private Rigidbody _body;
        private Vector3? _target;
        private Collider _targetCollider;
        private float _speed;
        private Vector3 _lastPosition;

        public static IReadOnlyList<HaugbuiCreature> All => Instances;

        internal Character Character => _character;
        internal ZNetView View => _view;
        internal CreatureVisuals Visuals { get; private set; }
        internal float MaxSpeed { get; set; } = 1f;
        internal bool HasTarget => _target != null || _targetCollider != null;
        internal bool Arrived { get; private set; }
        internal float DistanceToTarget { get; private set; }
        internal float MaxObservedSpeed { get; private set; }
        internal float MaxFrameJump { get; private set; }

        internal string TombGuid => _view != null && _view.IsValid() ? _view.GetZDO().GetString(ZdoKeys.TombGuid) : string.Empty;

        internal event Action Died;

        /// <summary>Damage it took after every game modifier, and who dealt it (null for none).</summary>
        internal event Action<float, Character> Damaged;

        /// <summary>HB-142: set by the tomb from its level; applied to every hit it deals and takes.</summary>
        internal float DealtFactor { get; set; } = 1f;
        internal float TakenFactor { get; set; } = 1f;

        /// <summary>While set and nearly still, it turns to face this point and looks at it (combat).</summary>
        internal Vector3? Facing { get; set; }

        internal Humanoid Humanoid => _character as Humanoid;

        internal int HitsDealt { get; private set; }
        internal float LastDealtBase { get; private set; }
        internal float LastDealtScaled { get; private set; }
        internal int HitsTaken { get; private set; }
        internal float LastTakenIncoming { get; private set; }
        internal float LastTakenScaled { get; private set; }

        internal void ScaleDealt(HitData hit)
        {
            LastDealtBase = hit.GetTotalDamage();
            hit.m_damage.Modify(DealtFactor);
            LastDealtScaled = hit.GetTotalDamage();
            HitsDealt++;
        }

        internal void ScaleTaken(HitData hit)
        {
            LastTakenIncoming = hit.GetTotalDamage();
            hit.m_damage.Modify(TakenFactor);
            LastTakenScaled = hit.GetTotalDamage();
            HitsTaken++;
        }

        private void Awake()
        {
            _view = GetComponent<ZNetView>();
            _character = GetComponent<Character>();
            _body = GetComponent<Rigidbody>();
            if (_view == null || !_view.IsValid()) return;

            BaseAI ai = GetComponent<BaseAI>();
            if (ai != null) ai.enabled = false;
            CreaturePrefab.MakeIncorporeal(_character);
            // Character.m_tamed is not serialized, so the prefab's value never reaches an instance; the ZDO key keeps it
            // for every peer (Character.IsTamed). Tamed: players see a friendly green health bar (BaseAI.IsEnemy).
            _character.m_tamed = true;
            if (_view.IsOwner()) _view.GetZDO().Set(ZDOVars.s_tamed, true);
            _character.m_onDeath += () => Died?.Invoke();
            _character.m_onDamaged += (damage, attacker) => Damaged?.Invoke(damage, attacker);
            Visuals = gameObject.AddComponent<CreatureVisuals>();
            _lastPosition = transform.position;
            Instances.Add(this);
        }

        private void OnDestroy() => Instances.Remove(this);

        internal static HaugbuiCreature Find(string tombGuid)
        {
            foreach (HaugbuiCreature creature in Instances)
                if (creature != null && creature.TombGuid == tombGuid) return creature;
            return null;
        }

        internal void Bind(string tombGuid) => _view.GetZDO().Set(ZdoKeys.TombGuid, tombGuid);

        internal void SetState(HaugbuiState state)
        {
            if (_view.IsOwner()) _view.GetZDO().Set(ZdoKeys.CreatureState, (int)state);
        }

        internal void MoveTo(Vector3 target)
        {
            _target = target;
            _targetCollider = null;
            Arrived = false;
        }

        /// <summary>Arrival is measured to the collider's nearest point, so large stations count from their edge.</summary>
        internal void MoveTo(Collider target)
        {
            _targetCollider = target;
            _target = target.bounds.center;
            Arrived = false;
        }

        /// <summary>Sleep fast mode only (HB-046): the screen is faded, so it jumps to the next step instead of flying.</summary>
        internal void Teleport(Vector3 position)
        {
            Stop();
            transform.position = position;
            if (_body != null)
            {
                _body.position = position;
                _body.linearVelocity = Vector3.zero;
            }
            _lastPosition = position;
        }

        internal void Stop()
        {
            _target = null;
            _targetCollider = null;
            Facing = null;
            Arrived = false;
            _speed = 0;
            if (_character != null) _character.SetMoveDir(Vector3.zero);
        }

        private void Update()
        {
            if (_character == null || _view == null || !_view.IsValid() || !_view.IsOwner() || _character.IsDead()) return;

            Vector3 position = transform.position;
            float dt = Time.deltaTime;
            if (!HasTarget)
            {
                _character.SetMoveDir(Vector3.zero);
                return;
            }

            Vector3 goal = _targetCollider != null ? _targetCollider.ClosestPoint(position) : _target.Value;
            DistanceToTarget = Vector3.Distance(position, goal);
            if (DistanceToTarget <= ArriveDistance)
            {
                Arrived = true;
                _speed = 0;
                _character.SetMoveDir(Vector3.zero);
                return;
            }
            Arrived = false;

            Vector3 aim = goal;
            if (DistanceToTarget > 4f)
                aim.y = Mathf.Max(goal.y, ZoneSystem.instance.GetGroundHeight(position) + TravelAltitude);

            _speed = Steering.NextSpeed(_speed, DistanceToTarget, MaxSpeed, Acceleration, dt);
            float speed = Mathf.Max(MinFlySpeed, _speed);
            _character.m_flySlowSpeed = speed;
            _character.m_flyFastSpeed = speed;
            _character.SetMoveDir((aim - position).normalized);
            _character.SetRun(true);
        }

        /// <summary>
        /// Faces where it flies: yaw only, smoothly, toward the horizontal velocity, or toward the goal while slow. The
        /// vanilla flight turn (Character.UpdateRotation at the prefab's m_flyTurnSpeed) did not turn the Haugbui.
        /// </summary>
        private void LateUpdate()
        {
            if (_character == null || _view == null || !_view.IsValid() || !_view.IsOwner() || _character.IsDead()) return;
            Vector3 heading = _body != null ? _body.linearVelocity : Vector3.zero;
            heading.y = 0f;
            Vector3 look = heading;
            if (heading.magnitude < MinHeadingSpeed)
            {
                Vector3 goal;
                if (Facing != null) goal = Facing.Value;
                else if (HasTarget) goal = _targetCollider != null ? _targetCollider.ClosestPoint(transform.position) : _target.Value;
                else return;
                heading = goal - transform.position;
                heading.y = 0f;
                if (heading.magnitude < 0.05f) return;
                // A melee swing takes only its vertical aim from the look direction (Attack.GetMeleeAttackDir).
                look = Facing != null ? goal - transform.position : heading;
            }
            Quaternion want = Quaternion.LookRotation(heading.normalized);
            Quaternion yaw = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            transform.rotation = Quaternion.RotateTowards(yaw, want, TurnDegreesPerSecond * Time.deltaTime);
            _character.SetLookDir(look);
        }

        /// <summary>Horizontal angle between where it faces and a point.</summary>
        internal float AngleTo(Vector3 point)
        {
            Vector3 to = point - transform.position;
            to.y = 0f;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            return to.sqrMagnitude < 0.0001f ? 0f : Vector3.Angle(forward, to);
        }

        /// <summary>Angle between where it faces and where it moves, both flattened; 0 while nearly still.</summary>
        internal float HeadingError
        {
            get
            {
                if (_body == null) return 0f;
                Vector3 velocity = _body.linearVelocity;
                velocity.y = 0f;
                if (velocity.magnitude < 0.5f) return 0f;
                Vector3 forward = transform.forward;
                forward.y = 0f;
                return Vector3.Angle(forward, velocity);
            }
        }

        /// <summary>
        /// Speed is sampled per physics step: the body moves only in fixed steps, so a render frame shorter than one
        /// step can contain a whole step of motion and per-frame speed reads up to 50% high.
        /// </summary>
        private void FixedUpdate()
        {
            if (_character == null || _view == null || !_view.IsValid() || !_view.IsOwner()) return;
            Vector3 position = transform.position;
            float jump = Vector3.Distance(position, _lastPosition);
            MaxFrameJump = Mathf.Max(MaxFrameJump, jump);
            MaxObservedSpeed = Mathf.Max(MaxObservedSpeed, jump / Time.fixedDeltaTime);
            _lastPosition = position;
        }

        internal void ResetTelemetry()
        {
            MaxObservedSpeed = 0;
            MaxFrameJump = 0;
            _lastPosition = transform.position;
        }
    }

    [HarmonyPatch]
    internal static class CreatureAIPatches
    {
        /// <summary>Belt and braces: the AI component is disabled, but nothing vanilla may drive a Haugbui.</summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.UpdateAI))]
        private static bool SkipVanillaAI(BaseAI __instance, ref bool __result)
        {
            if (__instance.GetComponent<HaugbuiCreature>() == null) return true;
            __result = false;
            return false;
        }

    }
}
