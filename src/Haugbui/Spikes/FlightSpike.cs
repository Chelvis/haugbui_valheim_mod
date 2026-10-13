#if DEBUG
using System.Collections.Generic;
using Haugbui.Diagnostics;
using Haugbui.Diagnostics.Commands;
using Jotunn.Managers;
using UnityEngine;

namespace Haugbui.Spikes
{
    /// <summary>
    /// HB-T-008: a non-persistent Wraith clone with its AI disabled, flying through buildings and terrain bumps by
    /// excluding those layers from its physics contacts while keeping Valheim's own flight motion and hit detection.
    /// </summary>
    internal static class FlightSpike
    {
        public const string PrefabName = "HaugbuiSpikeWraith";

        // Contacts with these layers are ignored, so the body passes through them; raycast-based hits still work.
        private static readonly string[] PassThroughLayers =
        {
            "Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "blocker", "vehicle", "item"
        };

        public static void RegisterPrefab()
        {
            PrefabManager.OnVanillaPrefabsAvailable += CreatePrefab;
        }

        private static void CreatePrefab()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= CreatePrefab;
            GameObject clone = PrefabManager.Instance.CreateClonedPrefab(PrefabName, "Wraith");
            if (clone == null)
            {
                Log.Error("Flight spike: vanilla prefab 'Wraith' not found");
                return;
            }

            clone.GetComponent<ZNetView>().m_persistent = false;
            CharacterDrop drops = clone.GetComponent<CharacterDrop>();
            if (drops != null) Object.DestroyImmediate(drops);
            clone.GetComponent<Character>().m_name = "Haugbui (spike)";
            PrefabManager.Instance.AddPrefab(clone);
        }

        public static void MakeIncorporeal(Character character)
        {
            int mask = LayerMask.GetMask(PassThroughLayers);
            Collider collider = character.GetCollider();
            if (collider != null) collider.excludeLayers = mask;
            Rigidbody body = character.GetComponent<Rigidbody>();
            if (body != null) body.excludeLayers = mask;

            BaseAI ai = character.GetComponent<BaseAI>();
            if (ai != null) ai.enabled = false;
        }
    }

    internal sealed class FlightSpikeCommand : HaugbuiCommand
    {
        private static readonly List<Vector3> Waypoints = new List<Vector3>();
        private static FlightSpikeController _active;

        public override string Name => "haugbui_spike_fly";
        public override string Help =>
            "SPIKE HB-T-008: spawn [speed] | point | clearpoints | go | follow | stop | report | despawn";
        public override bool IsCheat => true;

        protected override void Execute(string[] args)
        {
            Player player = Player.m_localPlayer;
            switch (args.Length > 0 ? args[0] : "")
            {
                case "spawn":
                    Spawn(player, args.Length > 1 && float.TryParse(args[1], out float speed) ? speed : 4f);
                    break;
                case "point":
                    Waypoints.Add(player.transform.position + Vector3.up);
                    Print($"Waypoint {Waypoints.Count} at {player.transform.position}");
                    break;
                case "clearpoints":
                    Waypoints.Clear();
                    Print("Waypoints cleared.");
                    break;
                case "go":
                    if (RequireActive()) _active.Patrol(Waypoints);
                    break;
                case "follow":
                    if (RequireActive()) _active.Follow(player);
                    break;
                case "stop":
                    if (RequireActive()) _active.Stop();
                    break;
                case "report":
                    if (RequireActive()) Print(_active.Report());
                    break;
                case "despawn":
                    if (_active != null) ZNetScene.instance.Destroy(_active.gameObject);
                    _active = null;
                    Print("Despawned.");
                    break;
                default:
                    Print(Help);
                    break;
            }
        }

        private void Spawn(Player player, float speed)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(FlightSpike.PrefabName);
            if (prefab == null)
            {
                Print("Spike prefab not registered; see the log.");
                return;
            }
            if (_active != null) ZNetScene.instance.Destroy(_active.gameObject);

            Vector3 position = player.transform.position + player.transform.forward * 3f + Vector3.up * 1.5f;
            GameObject instance = Object.Instantiate(prefab, position, Quaternion.identity);
            Character character = instance.GetComponent<Character>();
            character.m_flySlowSpeed = speed;
            character.m_flyFastSpeed = speed;
            FlightSpike.MakeIncorporeal(character);
            _active = instance.AddComponent<FlightSpikeController>();
            Print($"Spawned at {position}, speed {speed} m/s. Use 'follow' and walk through walls, or 'point' x N then 'go'.");
        }

        private bool RequireActive()
        {
            if (_active != null) return true;
            Print("Run 'spawn' first.");
            return false;
        }
    }

    internal sealed class FlightSpikeController : MonoBehaviour
    {
        private const float ArriveDistance = 1f;
        private const float HoverHeight = 1.5f;
        private const float StuckSeconds = 3f;

        private Character _character;
        private List<Vector3> _path = new List<Vector3>();
        private int _index;
        private Player _follow;

        private float _bestDistance = float.MaxValue;
        private float _lastProgressTime;
        private int _stuckEvents;
        private int _hits;
        private float _minClearance = float.MaxValue;
        private float _flownMeters;
        private Vector3 _lastPosition;

        private void Awake()
        {
            _character = GetComponent<Character>();
            _character.m_onDamaged += (damage, attacker) => _hits++;
            _lastPosition = transform.position;
        }

        public void Patrol(List<Vector3> waypoints)
        {
            _path = new List<Vector3>(waypoints);
            _index = 0;
            _follow = null;
            ResetProgress();
        }

        public void Follow(Player player)
        {
            _follow = player;
            _path.Clear();
            ResetProgress();
        }

        public void Stop()
        {
            _follow = null;
            _path.Clear();
            _character.SetMoveDir(Vector3.zero);
        }

        public string Report() =>
            $"Flown {_flownMeters:0} m, stuck events {_stuckEvents}, hits taken {_hits}, " +
            $"min clearance over terrain {(_minClearance == float.MaxValue ? 0 : _minClearance):0.00} m, " +
            $"health {_character.GetHealth():0}/{_character.GetMaxHealth():0}";

        private void Update()
        {
            if (_character == null || _character.IsDead()) return;

            Vector3 position = transform.position;
            _flownMeters += Vector3.Distance(position, _lastPosition);
            _lastPosition = position;
            float ground = ZoneSystem.instance.GetGroundHeight(position);
            _minClearance = Mathf.Min(_minClearance, position.y - ground);

            if (!TryGetTarget(out Vector3 target))
            {
                _character.SetMoveDir(Vector3.zero);
                return;
            }

            target.y = Mathf.Max(target.y, ZoneSystem.instance.GetGroundHeight(target) + HoverHeight);
            Vector3 toTarget = target - position;
            float distance = toTarget.magnitude;
            if (distance <= ArriveDistance)
            {
                if (_follow == null) _index++;
                ResetProgress();
                _character.SetMoveDir(Vector3.zero);
                return;
            }

            _character.SetMoveDir(toTarget / distance);
            _character.SetRun(true);
            TrackProgress(distance);
        }

        private bool TryGetTarget(out Vector3 target)
        {
            if (_follow != null)
            {
                target = _follow.transform.position - _follow.transform.forward * 3f + Vector3.up * HoverHeight;
                return true;
            }
            if (_path.Count > 0)
            {
                if (_index >= _path.Count) _index = 0;
                target = _path[_index];
                return true;
            }
            target = default;
            return false;
        }

        private void TrackProgress(float distance)
        {
            if (distance < _bestDistance - 0.5f)
            {
                _bestDistance = distance;
                _lastProgressTime = Time.time;
            }
            else if (Time.time - _lastProgressTime > StuckSeconds && _follow == null)
            {
                _stuckEvents++;
                Log.Warning($"Flight spike: STUCK at {transform.position} heading to waypoint {_index + 1}");
                _index++;
                ResetProgress();
            }
        }

        private void ResetProgress()
        {
            _bestDistance = float.MaxValue;
            _lastProgressTime = Time.time;
        }
    }
}
#endif
