#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Core.Production;
using Haugbui.Core.Storage;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Integration.StationAdapters;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>
    /// HB-T-079: transfers, one production job, one delivery and a death, with forced saves and an in-process reload.
    /// Item totals are compared with the transfer ledger. A real process kill is not part of this suite.
    /// </summary>
    internal sealed class TortureSuite
    {
        private const string Ore = "CopperOre";
        private const string Bar = "Copper";

        private readonly SelfTestContext _t;
        private readonly FakeTime _time = new FakeTime();
        private readonly List<string> _taught = new List<string>();
        private Vector3 _arena;
        private TombController _tomb;
        private Container _woodA, _woodB, _delivery, _ore, _coal, _bars;
        private Smelter _smelter;
        private int _duplicatesBefore;

        public TortureSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Torture";
            TimeSource.Override = _time;
            _duplicatesBefore = Coordination.Reservations.DuplicatesLogged;
            try
            {
                yield return Setup();
                if (_tomb == null) yield break;
                yield return Transfers();
                yield return Production();
                yield return Delivery();
                yield return Death();
                _t.Check("transfer ledger reports no duplicate",
                    Coordination.Reservations.DuplicatesLogged == _duplicatesBefore
                    && Coordination.Reservations.Ledger.All(record => record.Conserved));
            }
            finally
            {
                TombController.SkippingForTests = false;
                TimeSource.Override = null;
                Player player = _t.Player;
                if (player != null) foreach (string name in _taught) player.m_knownMaterial.Remove(name);
            }
        }

        private IEnumerator Setup()
        {
            Player player = _t.Player;
            _arena = SelfTestContext.FindDryArena(player.transform.position + player.transform.right * 12f, 8f);
            GameObject bed = _t.SpawnVanilla("bed", Ground(new Vector3(-6f, 0f, -4f)), Quaternion.identity);
            if (bed == null)
            {
                _t.Fail("spawn test bed", "prefab 'bed' not found");
                yield break;
            }
            yield return null;
            bed.GetComponent<Bed>().SetOwner(player.GetPlayerID(), player.GetPlayerName());
            _time.Fraction = FakeTime.Noon;

            HaugbuiStructure tomb = _t.PlaceStructure(StructureKind.Tomb, _arena);
            HaugbuiStructure offering = _t.PlaceStructure(StructureKind.Offering, Ground(new Vector3(2.2f, 0f, -2.2f)));
            HaugbuiStructure delivery = _t.PlaceStructure(StructureKind.DeliveryChest, Ground(new Vector3(4f, 0f, 2f)));
            _woodA = Chest(new Vector3(-6f, 0f, 3f));
            _woodB = Chest(new Vector3(-6f, 0f, 5f));
            _ore = Chest(new Vector3(2f, 0f, 5f));
            _coal = Chest(new Vector3(4f, 0f, 5f));
            _bars = Chest(new Vector3(6f, 0f, 5f));
            GameObject smelter = _t.PlaceVanilla("smelter", Ground(new Vector3(0f, 0f, 7f)), Quaternion.identity);
            _smelter = smelter != null ? smelter.GetComponentInChildren<Smelter>() : null;
            yield return SelfTestContext.WaitFrames(5);
            _delivery = delivery != null ? delivery.GetComponent<Container>() : null;
            _tomb = tomb != null ? tomb.GetComponent<TombController>() : null;
            if (!_t.Check("torture fixtures placed", _tomb != null && offering != null && _delivery != null && _woodA != null
                                                    && _woodB != null && _ore != null && _coal != null && _bars != null && _smelter != null))
            {
                _tomb = null;
                yield break;
            }
            _tomb.SetToggle(TombToggles.GroundItems, false, out _);
            _tomb.SetToggle(TombToggles.Harvesting, false, out _);
            _smelter.m_secPerProduct = 1f;
            Add(_woodA, "Wood", 20);
            Add(_ore, Ore, 2);
            Add(_coal, "Coal", 8);
            Teach(Ore);
            Teach(Bar);
            yield return SelfTestContext.WaitFor(() => _tomb.Structure.Progress.Level >= 2, 5f);
            _t.Check("level 2 for production", _tomb.Structure.Progress.Level >= 2, $"level {_tomb.Structure.Progress.Level}");
        }

        private IEnumerator Transfers()
        {
            int wood = Wood();
            var moving = new ItemStack { Prefab = "Wood", Amount = 8, WorldLevel = Game.m_worldLevel };
            ItemBridge.RefreshShared(moving);
            TransferRecord record = Coordination.Reservations.Transfers.Move(
                new ContainerEndpoint(_woodA), new ContainerEndpoint(_woodB), moving, 8);
            ZNet.instance.Save(false);
            _t.Check("transfer moved wood", record.Status == TransferStatus.Moved && record.Moved == 8 && record.Conserved, record.ToString());
            _t.Check("wood total after transfer", Wood() == wood, $"{Wood()} / {wood}");

            GameObject a = null, b = null;
            yield return _t.Reload(_woodA.gameObject, go => a = go);
            yield return _t.Reload(_woodB.gameObject, go => b = go);
            _woodA = a != null ? a.GetComponent<Container>() : null;
            _woodB = b != null ? b.GetComponent<Container>() : null;
            _t.Check("wood total after reload", _woodA != null && _woodB != null && Wood() == wood, Wood().ToString());
        }

        private IEnumerator Production()
        {
            if (_tomb == null || _smelter == null) yield break;
            int ore = Metal(Ore);
            int bars = Metal(Bar);
            int coal = Count(_coal, "Coal");
            yield return SetTime(FakeTime.Night);
            yield return SelfTestContext.WaitFor(() => _tomb.Machine.State == HaugbuiState.Idle, 15f);
            bool edited = StationJobs.Edit(_smelter, job =>
            {
                ProductionJob.Edit(job, true, Ore, Bar, JobMode.Finite, 1);
                job.TombGuid = _tomb.Guid;
                return true;
            });
            if (!_t.Check("production job saved", edited)) yield break;

            TombController.SkippingForTests = true;
            float end = Time.time + 90f;
            while (Time.time < end && !(Job() != null && Job().Produced >= 1 && _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty))
            {
                _time.Offset += 10;
                yield return null;
            }
            TombController.SkippingForTests = false;
            StationJobState jobNow = Job();
            ZNet.instance.Save(false);
            int produced = jobNow != null ? jobNow.Produced : 0;
            int supplied = jobNow != null ? jobNow.Supplied : 0;
            _t.Check("production job made one bar", produced >= 1 && supplied >= 1, $"supplied {supplied}, produced {produced}");

            GameObject again = null;
            yield return _t.Reload(_smelter.gameObject, go => again = go);
            _smelter = again != null ? again.GetComponentInChildren<Smelter>() : null;
            if (_smelter != null) _smelter.m_secPerProduct = 1f;
            StationJobState kept = Job();
            _t.Check("job progress survives reload", kept != null && kept.Supplied == supplied && kept.Produced == produced,
                kept == null ? "no job" : $"supplied {kept.Supplied}, produced {kept.Produced}");
            IStationAdapter adapter = _smelter != null ? StationAdapterRegistry.For(_smelter) : null;
            StationState station = adapter != null ? adapter.Read(_smelter) : null;
            int inside = station != null ? station.Queued + station.PendingOutputs : 0;
            int oreNow = Metal(Ore);
            int barsNow = Metal(Bar);
            // An empty bar chest is not a destination, so the bar waits in the tomb. It is still the same metal.
            _t.Check("ore and bars conserved", oreNow + barsNow + inside == ore + bars,
                $"ore {oreNow}, bars {barsNow}, station {inside}");
            _t.Check("coal was not created", Count(_coal, "Coal") + (station != null ? Mathf.CeilToInt(station.Fuel) : 0) <= coal,
                $"coal {Count(_coal, "Coal")}, fuel {station?.Fuel ?? 0}");
        }

        private IEnumerator Delivery()
        {
            if (_tomb == null || _delivery == null) yield break;
            int wood = Wood();
            DeliveryChest chest = _delivery.GetComponent<DeliveryChest>();
            if (!_t.Check("delivery request saved", chest != null && chest.Edit(ledger => ledger.Request("Wood", 5) == RequestEditResult.Ok))) yield break;

            yield return SetTime(FakeTime.Night);
            TombController.SkippingForTests = true;
            float end = Time.time + 60f;
            while (Time.time < end && chest.Ledger.Remaining("Wood") > 0)
            {
                _time.Offset += 10;
                yield return null;
            }
            TombController.SkippingForTests = false;
            ZNet.instance.Save(false);
            int remaining = chest.Ledger.Remaining("Wood");
            _t.Check("delivery completed", remaining == 0, $"remaining {remaining}");
            _t.Check("wood total after delivery", Wood() == wood, $"{Wood()} / {wood}");

            GameObject again = null;
            yield return _t.Reload(_delivery.gameObject, go => again = go);
            _delivery = again != null ? again.GetComponent<Container>() : null;
            DeliveryChest reloaded = _delivery != null ? _delivery.GetComponent<DeliveryChest>() : null;
            _t.Check("delivery ledger survives reload", reloaded != null && reloaded.Ledger.Remaining("Wood") == 0,
                reloaded == null ? "missing" : $"remaining {reloaded.Ledger.Remaining("Wood")}");
            _t.Check("wood total after delivery reload", Wood() == wood, Wood().ToString());
        }

        private IEnumerator Death()
        {
            if (_tomb == null) yield break;
            yield return SetTime(FakeTime.Night);
            yield return SelfTestContext.WaitFor(() => _tomb.Creature != null, 15f);
            if (!_t.Check("out before the death", _tomb.Creature != null, _tomb.Machine.State.ToString())) yield break;
            int resin = Resin();
            var resinStack = new ItemStack { Prefab = "Resin", Amount = 5, WorldLevel = Game.m_worldLevel, Quality = 1 };
            ItemBridge.RefreshShared(resinStack);
            _tomb.Carried.Add(resinStack);
            var hit = new HitData();
            hit.m_damage.m_damage = 100000f;
            _tomb.Creature.Character.Damage(hit);
            yield return SelfTestContext.WaitFor(() => _tomb.Machine.State == HaugbuiState.Dead, 5f);
            ZNet.instance.Save(false);
            double death = _tomb.State != null ? _tomb.State.DeathTime : double.NaN;
            _t.Check("death stored a cooldown", !double.IsNaN(death) && _tomb.WaitRemaining > 0, $"wait {_tomb.WaitRemaining:0}");
            _t.Check("resin moved to the tomb and conserved", Resin() == resin + 5 && _tomb.Carried.CountPrefab("Resin") == 0,
                $"resin {Resin()}, carried {_tomb.Carried.CountPrefab("Resin")}");

            GameObject again = null;
            yield return _t.Reload(_tomb.gameObject, go => again = go);
            _tomb = again != null ? again.GetComponent<TombController>() : null;
            _t.Check("cooldown survives reload", _tomb != null && _tomb.State != null && !double.IsNaN(_tomb.State.DeathTime)
                                                 && System.Math.Abs(_tomb.State.DeathTime - death) < 0.001,
                _tomb != null && _tomb.State != null ? _tomb.State.DeathTime.ToString() : "missing");
            _t.Check("resin survives reload", Resin() == resin + 5, Resin().ToString());
        }

        private IEnumerator SetTime(float fraction)
        {
            _time.Fraction = fraction;
            _tomb.TickNow();
            yield return SelfTestContext.WaitFrames(2);
        }

        private StationJobState Job() => _smelter != null ? StationJobs.Read(_smelter) : null;

        private int Metal(string prefab)
        {
            int n = Count(_ore, prefab) + Count(_bars, prefab);
            if (_tomb != null)
            {
                n += Count(_tomb.GetComponent<Container>(), prefab);
                if (_tomb.Carried != null) n += _tomb.Carried.CountPrefab(prefab);
                if (_tomb.Overflow != null) n += _tomb.Overflow.Where(s => s.Prefab == prefab).Sum(s => s.Amount);
            }
            foreach (ItemDrop drop in UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
            {
                if (drop == null || drop.m_itemData == null || Vector3.Distance(drop.transform.position, _arena) > 40f) continue;
                if (ItemBridge.PrefabName(drop.m_itemData) == prefab) n += drop.m_itemData.m_stack;
            }
            return n;
        }

        private int Wood() => Count(_woodA, "Wood") + Count(_woodB, "Wood") + Count(_delivery, "Wood")
                              + Count(_tomb != null ? _tomb.GetComponent<Container>() : null, "Wood")
                              + (_tomb != null && _tomb.Carried != null ? _tomb.Carried.CountPrefab("Wood") : 0);

        private int Resin() => Count(_tomb != null ? _tomb.GetComponent<Container>() : null, "Resin")
                               + (_tomb != null && _tomb.Carried != null ? _tomb.Carried.CountPrefab("Resin") : 0);

        private static int Count(Container container, string prefab) =>
            container == null ? 0 : ItemBridge.CountPrefab(container.GetInventory(), prefab);

        private Container Chest(Vector3 offset)
        {
            GameObject go = _t.PlaceVanilla("piece_chest_wood", Ground(offset), Quaternion.identity);
            return go != null ? go.GetComponent<Container>() : null;
        }

        private static void Add(Container container, string prefab, int amount)
        {
            var stack = new ItemStack { Prefab = prefab, Amount = amount, WorldLevel = Game.m_worldLevel };
            ItemBridge.RefreshShared(stack);
            ItemBridge.AddTo(container.GetInventory(), stack);
        }

        private void Teach(string prefab)
        {
            Player player = _t.Player;
            string name = ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
            if (player == null || name == null || player.IsMaterialKnown(name)) return;
            player.m_knownMaterial.Add(name);
            _taught.Add(name);
        }

        private Vector3 Ground(Vector3 offset) => SelfTestContext.OnGround(_arena + offset);
    }
}
#endif
