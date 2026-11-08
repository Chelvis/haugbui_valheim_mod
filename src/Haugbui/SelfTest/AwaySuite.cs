#if DEBUG
using System.Collections;
using Haugbui.Core.Creature;
using Haugbui.Core.GameApi;
using Haugbui.Core.Persistence;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>
    /// Moves the player outside the simulation bubble. An unchecked tomb unloads. A checked one unloads by day,
    /// reloads when the night starts, and its Haugbui emerges and empties the tomb from there.
    /// </summary>
    internal sealed class AwaySuite
    {
        private static readonly Vector3[] Directions =
        {
            Vector3.right, Vector3.forward, Vector3.left, Vector3.back,
        };

        private readonly SelfTestContext _t;
        private readonly FakeTime _time = new FakeTime();
        private Vector3 _home;
        private Vector3 _optedPos;
        private Vector3 _plainPos;
        private Vector3 _lookout;
        private float _span;
        private ZDOID _optedId;
        private ZDOID _plainId;
        private ZDOID _chestId;
        private bool _ready;
        private TombController _opted;
        private Container _tombStorage;
        private Container _chest;

        public AwaySuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Away";
            if (!_t.Check("away mode api", GameApiSelfCheck.IsAvailable(Features.RemoteWork))) yield break;
            var enabled = HaugbuiPlugin.Instance.Settings.AwayModeEnabled;
            bool was = enabled.Value;
            Player player = _t.Player;
            _home = player.transform.position;
            TimeSource.Override = _time;
            _time.Fraction = FakeTime.Noon;
            try
            {
                yield return Setup();
                if (!_ready) yield break;
                yield return PlacePlainFar();
                if (_plainId.IsNone()) yield break;
                yield return WorkFromFar(enabled);
            }
            finally
            {
                enabled.Value = was;
                TimeSource.Override = null;
                if (_opted != null && _opted.State != null && _opted.State.Has(TombToggles.AwayMode))
                    _opted.SetToggle(TombToggles.AwayMode, false, out _);
                Discard(_chestId);
                Discard(_plainId);
                Discard(_optedId);
                Pin(_home, true);
                KeepLoaded.Invalidate();
            }
        }

        private IEnumerator Setup()
        {
            Player player = _t.Player;
            SimulationDistance distance = ZNet.instance.GetSyncedSimulationDistance();
            _span = (distance.TotalSimulationDistance + 4) * 64f;
            Vector3 arena = SelfTestContext.FindDryArena(player.transform.position + player.transform.forward * 14f, 8f);
            HaugbuiStructure tomb = _t.PlaceStructure(StructureKind.Tomb, arena);
            yield return SelfTestContext.WaitFrames(3);
            _opted = tomb != null ? tomb.GetComponent<TombController>() : null;
            _tombStorage = tomb != null ? tomb.GetComponent<Container>() : null;
            GameObject chestObject = _t.PlaceVanilla("piece_chest_wood", SelfTestContext.OnGround(arena + new Vector3(5f, 0f, 4f)), Quaternion.identity);
            _chest = chestObject != null ? chestObject.GetComponentInChildren<Container>() : null;
            if (!_t.Check("tomb and chest placed", tomb != null && tomb.Zdo != null && _opted != null && _tombStorage != null && _chest != null)) yield break;
            _optedPos = _opted.transform.position;
            _optedId = tomb.Zdo.m_uid;
            _chestId = IdOf(chestObject);

            _opted.SetToggle(TombToggles.GroundItems, false, out _);
            _opted.SetToggle(TombToggles.Harvesting, false, out _);
            _opted.SetToggle(TombToggles.Production, false, out _);
            _ready = _t.Check("checkbox starts off", !_opted.State.Has(TombToggles.AwayMode) && tomb.Zdo.GetInt(ZdoKeys.KeepLoaded) == 0)
                && _t.Check("checkbox turns on", _opted.SetToggle(TombToggles.AwayMode, true, out _))
                && _t.Check("daytime is not an anchor", tomb.Zdo.GetInt(ZdoKeys.KeepLoaded) == 1 && !KeepLoaded.Anchored(_optedPos));
            if (!_ready) yield break;

            _time.Fraction = FakeTime.Night;
            _ready = _t.Check("a checked tomb anchors at night", KeepLoaded.Anchored(_optedPos))
                && _t.Check("rest turns on", _opted.SetToggle(TombToggles.Resting, true, out _))
                && _t.Check("rest in tomb is not an anchor", !KeepLoaded.Anchored(_optedPos));
            if (!_ready) yield break;

            _time.Fraction = FakeTime.Noon;
            _ready = _t.Check("rest turns off", _opted.SetToggle(TombToggles.Resting, false, out _))
                && _t.Check("daytime stays unanchored", !KeepLoaded.Anchored(_optedPos));
            if (!_ready) yield break;

            _opted.State.DeathTime = TimeSource.Current.Now;
            _opted.Machine.Restore(HaugbuiState.Dead);
            _opted.TickNow();
            _ready = _t.Check("death cooldown is not an anchor", !KeepLoaded.Anchored(_optedPos));
            _opted.State.DeathTime = double.NaN;
            _opted.Machine.Restore(HaugbuiState.Dormant);
            _opted.TickNow();
            if (!_ready) yield break;

            Add(_chest, "Wood", 1);
            Add(_tombStorage, "Wood", 8);
            _ready = _t.Check("wood is waiting in the tomb", Count(_tombStorage, "Wood") == 8 && Count(_chest, "Wood") == 1);
        }

        private IEnumerator PlacePlainFar()
        {
            float water = ZoneSystem.instance.m_waterLevel + 0.5f;
            foreach (Vector3 direction in Directions)
            {
                Vector3 target = _optedPos + direction * _span;
                yield return Arrive(target);
                Vector3 spot = SelfTestContext.OnGround(new Vector3(target.x, 0f, target.z));
                if (spot.y <= water || Vector3.Distance(spot, _optedPos) < _span - 80f) continue;
                HaugbuiStructure plain = _t.PlaceStructure(StructureKind.Tomb, spot);
                yield return SelfTestContext.WaitFrames(3);
                if (plain == null || plain.Zdo == null) continue;
                _plainPos = plain.transform.position;
                _plainId = plain.Zdo.m_uid;
                TombController controller = plain.GetComponent<TombController>();
                _t.Check("far tomb stays unchecked",
                    controller != null && !controller.State.Has(TombToggles.AwayMode) && plain.Zdo.GetInt(ZdoKeys.KeepLoaded) == 0
                    && !KeepLoaded.Anchored(_plainPos));
                float until = Time.time + 8f;
                bool unloaded = !Alive(_optedId);
                while (Time.time < until && !unloaded)
                {
                    Pin(target, false);
                    if (!Alive(_optedId)) unloaded = true;
                    yield return null;
                }
                _t.Check("opted-in tomb unloads by day while the player is away", unloaded);
                yield break;
            }
            _t.Fail("place an unchecked tomb outside the bubble", "no dry ground far enough from the opted-in tomb");
        }

        private IEnumerator WorkFromFar(BepInEx.Configuration.ConfigEntry<bool> enabled)
        {
            _lookout = _optedPos + new Vector3(0f, 0f, _span);
            if (Vector3.Distance(_lookout, _plainPos) < _span * 0.8f)
                _lookout = _optedPos + new Vector3(0f, 0f, -_span);
            if (!_t.Check("opted-in tomb is unloaded before the night", !Alive(_optedId))) yield break;
            Pin(_lookout, false);
            _time.Fraction = FakeTime.Night;

            bool sawWood = false;
            bool viewGone = false;
            bool zoneGone = false;
            bool sorted = false;
            float end = Time.time + 45f;
            while (Time.time < end && !(sawWood && viewGone && zoneGone && sorted))
            {
                Pin(_lookout, false);
                float away = Vector3.Distance(_t.Player.transform.position, _optedPos);
                bool far = away > _span * 0.75f && !ZNetScene.InActiveArea(_optedPos, _t.Player.transform.position);
                int left = Count(Box(_optedId), "Wood");
                int stored = Count(Box(_chestId), "Wood");
                TombController live = Live();
                if (far && left > 0) sawWood = true;
                if (!Alive(_plainId)) viewGone = true;
                if (!ZoneSystem.instance.IsZoneLoaded(_plainPos)) zoneGone = true;
                if (far && sawWood && left == 0 && stored == 9 && live != null && live.Creature != null) sorted = true;
                yield return null;
            }

            float meters = Vector3.Distance(_t.Player.transform.position, _optedPos);
            _t.Check("player is far from the Haugbui",
                meters > _span * 0.75f && !ZNetScene.InActiveArea(_optedPos, _t.Player.transform.position), $"{meters:0} m");
            _t.Check("unchecked tomb unloads while the player is away", viewGone);
            _t.Check("unchecked tomb's zone unloads", zoneGone);
            _t.Check("the job was still open after the player left", sawWood);
            TombController back = Live();
            _t.Check("sorted the tomb while the player was away", sorted,
                $"tomb {Count(Box(_optedId), "Wood")}, chest {Count(Box(_chestId), "Wood")}, creature {(back != null && back.Creature != null)}");

            enabled.Value = false;
            KeepLoaded.Invalidate();
            float off = Time.time + 5f;
            bool optedGone = false;
            while (Time.time < off && !optedGone)
            {
                Pin(_lookout, false);
                if (!Alive(_optedId)) optedGone = true;
                yield return null;
            }
            _t.Check("master switch unloads the tomb while the player is away", optedGone);
        }

        private IEnumerator Arrive(Vector3 flat)
        {
            float end = Time.time + 25f;
            while (Time.time < end)
            {
                Pin(flat, false);
                if (ZoneSystem.instance.IsZoneLoaded(flat)) yield break;
                yield return null;
            }
        }

        private void Pin(Vector3 position, bool keepHeight)
        {
            Player player = _t.Player;
            if (player == null) return;
            if (!keepHeight)
            {
                if (ZoneSystem.instance != null && ZoneSystem.instance.IsZoneLoaded(position))
                    position.y = ZoneSystem.instance.GetGroundHeight(position) + 1f;
                else
                    position.y = 40f;
            }
            player.transform.position = position;
            Rigidbody body = player.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = position;
                body.linearVelocity = Vector3.zero;
            }
            if (ZNet.instance != null) ZNet.instance.SetReferencePosition(position);
        }

        private Container Box(ZDOID id)
        {
            if (id.IsNone() || ZDOMan.instance == null || ZNetScene.instance == null) return null;
            ZDO zdo = ZDOMan.instance.GetZDO(id);
            if (zdo == null) return null;
            ZNetView view = ZNetScene.instance.FindInstance(zdo);
            return view != null ? view.GetComponent<Container>() ?? view.GetComponentInChildren<Container>() : null;
        }

        private TombController Live()
        {
            if (_optedId.IsNone() || ZDOMan.instance == null || ZNetScene.instance == null) return null;
            ZDO zdo = ZDOMan.instance.GetZDO(_optedId);
            if (zdo == null) return null;
            ZNetView view = ZNetScene.instance.FindInstance(zdo);
            return view != null ? view.GetComponent<TombController>() : null;
        }

        private static bool Alive(ZDOID id)
        {
            if (id.IsNone() || ZDOMan.instance == null || ZNetScene.instance == null) return false;
            ZDO zdo = ZDOMan.instance.GetZDO(id);
            return zdo != null && ZNetScene.instance.FindInstance(zdo) != null;
        }

        private static void Discard(ZDOID id)
        {
            if (id.IsNone() || ZDOMan.instance == null) return;
            ZDO zdo = ZDOMan.instance.GetZDO(id);
            if (zdo == null) return;
            ZNetView view = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
            if (view != null) ZNetScene.instance.Destroy(view.gameObject);
            else ZDOMan.instance.DestroyZDO(zdo);
        }

        private static ZDOID IdOf(GameObject go)
        {
            if (go == null) return ZDOID.None;
            ZNetView view = go.GetComponent<ZNetView>() ?? go.GetComponentInParent<ZNetView>();
            return view != null && view.IsValid() ? view.GetZDO().m_uid : ZDOID.None;
        }

        private static void Add(Container container, string prefab, int amount)
        {
            GameObject item = ObjectDB.instance.GetItemPrefab(prefab);
            container.GetInventory().AddItem(item, amount);
        }

        private static int Count(Container container, string prefab) =>
            container == null ? -1 : ItemBridge.CountPrefab(container.GetInventory(), prefab);
    }
}
#endif
