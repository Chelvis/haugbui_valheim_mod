#if DEBUG
using System.Collections;
using System.Linq;
using Haugbui.Brain;
using Haugbui.Coordination;
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
    /// <summary>HB-T-110: same-owner tomb spacing and territories where one owner's tomb areas overlap.</summary>
    internal sealed class TerritorySuite
    {
        private const float Apart = 20f;

        private readonly SelfTestContext _t;
        private Vector3 _arena;

        public TerritorySuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Territory";
            Player player = _t.Player;
            _arena = SelfTestContext.FindDryArena(player.transform.position - player.transform.forward * 12f, 9f);
            GameObject bed = _t.SpawnVanilla("bed", Ground(new Vector3(Apart / 2f, 0, -6)), Quaternion.identity);
            if (bed == null)
            {
                _t.Fail("spawn test bed", "prefab 'bed' not found");
                yield break;
            }
            yield return null;
            bed.GetComponent<Bed>().SetOwner(player.GetPlayerID(), player.GetPlayerName());

            HaugbuiStructure first = _t.PlaceStructure(StructureKind.Tomb, Ground(Vector3.zero));
            if (!_t.Check("first tomb placed", first != null)) yield break;
            yield return SelfTestContext.WaitFrames(2);
            PlacementResult refused = PlacementCheck.Check(StructureKind.Tomb, Ground(new Vector3(Apart, 0, 0)), player);
            _t.Check($"own tomb {Apart:0} m away refused (TombSpacingSameOwner {PlacementCheck.SameOwnerSpacing:0})",
                refused == PlacementResult.OwnTombTooClose, refused.ToString());

            float spacing = PlacementCheck.SameOwnerSpacing;
            PlacementCheck.SameOwnerSpacing = Apart / 2f;
            HaugbuiStructure second;
            try
            {
                second = _t.PlaceStructure(StructureKind.Tomb, Ground(new Vector3(Apart, 0, 0)));
            }
            finally
            {
                PlacementCheck.SameOwnerSpacing = spacing;
            }
            if (!_t.Check("second tomb placed with the spacing lowered for the test", second != null)) yield break;

            Container nearFirst = Chest(new Vector3(3, 0, 2));
            Container nearSecond = Chest(new Vector3(Apart - 3, 0, 2));
            HaugbuiStructure collection = _t.PlaceStructure(StructureKind.CollectionChest, Ground(new Vector3(Apart - 3, 0, -2)));
            yield return SelfTestContext.WaitFrames(3);
            TombController a = first.GetComponent<TombController>(), b = second.GetComponent<TombController>();
            if (!_t.Check("territory chests placed", nearFirst != null && nearSecond != null && collection != null && a != null && b != null)) yield break;

            string idFirst = ContainerEndpoint.IdOf(nearFirst), idSecond = ContainerEndpoint.IdOf(nearSecond);
            string idCollection = ContainerEndpoint.IdOf(collection.GetComponent<Container>());
            WorkContext ca = Context(a), cb = Context(b);
            _t.Check("both chests are inside both radii", Vector3.Distance(a.transform.position, nearSecond.transform.position) < a.Attributes.Radius
                                                          && Vector3.Distance(b.transform.position, nearFirst.transform.position) < b.Attributes.Radius);
            _t.Check("deposits go only to chests of its own territory",
                Has(ca.Destinations, idFirst) && !Has(ca.Destinations, idSecond) && Has(cb.Destinations, idSecond) && !Has(cb.Destinations, idFirst),
                $"A: {Ids(ca.Destinations)} · B: {Ids(cb.Destinations)}");
            _t.Check("storage chests stay shared as sources",
                Has(ca.Sources, idFirst) && Has(ca.Sources, idSecond) && Has(cb.Sources, idFirst) && Has(cb.Sources, idSecond),
                $"A: {Ids(ca.Sources)} · B: {Ids(cb.Sources)}");
            _t.Check("a Collection Chest belongs to the nearest tomb only",
                !ca.CollectionChests.Any(c => c.Id == idCollection) && cb.CollectionChests.Any(c => c.Id == idCollection));
            _t.Check("a crop or ground item position is split the same way",
                ca.Mine(nearFirst.transform.position) && !ca.Mine(nearSecond.transform.position) && cb.Mine(nearSecond.transform.position));

            WorkContext.TerritorySuspendedForTests = true;
            try
            {
                WorkContext shared = Context(a);
                _t.Check("TerritoryMode off: overlapping areas are shared again", Has(shared.Destinations, idFirst) && Has(shared.Destinations, idSecond));
            }
            finally
            {
                WorkContext.TerritorySuspendedForTests = false;
            }

            yield return ThreeTombs(a, b);
        }

        /// <summary>Three tombs of one owner. One ground stack is taken once, and a station job stays with the nearest.</summary>
        private IEnumerator ThreeTombs(TombController first, TombController second)
        {
            float spacing = PlacementCheck.SameOwnerSpacing;
            PlacementCheck.SameOwnerSpacing = 16f;
            HaugbuiStructure thirdPiece;
            try
            {
                thirdPiece = _t.PlaceStructure(StructureKind.Tomb, Ground(new Vector3(Apart * 2f, 0, 0)));
            }
            finally
            {
                PlacementCheck.SameOwnerSpacing = spacing;
            }
            if (!_t.Check("third tomb placed", thirdPiece != null)) yield break;
            TombController third = thirdPiece.GetComponent<TombController>();
            HaugbuiStructure[] offerings =
            {
                _t.PlaceStructure(StructureKind.Offering, Ground(new Vector3(0, 0, 3))),
                _t.PlaceStructure(StructureKind.Offering, Ground(new Vector3(Apart, 0, 3))),
                _t.PlaceStructure(StructureKind.Offering, Ground(new Vector3(Apart * 2f, 0, 3))),
            };
            GameObject smelterGo = _t.PlaceVanilla("smelter", Ground(new Vector3(3f, 0, -3f)), Quaternion.identity);
            Smelter smelter = smelterGo != null ? smelterGo.GetComponentInChildren<Smelter>() : null;
            Container ore = Seed(new Vector3(Apart, 0, 5f), "CopperOre", 1);
            Container full = Seed(new Vector3(Apart + 4f, 0, 5f), "Stone", 0);
            yield return SelfTestContext.WaitFrames(3);
            if (!_t.Check("three-tomb fixtures placed",
                    third != null && smelter != null && ore != null && full != null && offerings.All(o => o != null)))
                yield break;
            TombController[] tombs = { first, second, third };
            _t.Check("each offering binds to its own tomb",
                tombs.All(t => t.Attributes.Level >= 2),
                string.Join(", ", tombs.Select(t => t.Attributes.Level.ToString())));
            foreach (TombController tomb in tombs) tomb.SetToggle(TombToggles.Harvesting, false, out _);

            bool saved = StationJobs.Edit(smelter, job =>
            {
                ProductionJob.Edit(job, true, "CopperOre", "Copper", JobMode.Infinite, 0);
                job.TombGuid = first.Guid;
                job.OwnerSeen = TimeSource.Current.Now;
                return true;
            });
            string stationId = StationAdapterRegistry.IdOf(smelter);
            WorkContext near = Context(first), mid = Context(second), far = Context(third);
            StationJobState jobNow = StationJobs.Read(smelter);
            _t.Check("the smelter job stays with the nearest of the three",
                saved && jobNow != null && jobNow.TombGuid == first.Guid
                && near.Stations.Any(s => s.Id == stationId)
                && !mid.Stations.Any(s => s.Id == stationId)
                && !far.Stations.Any(s => s.Id == stationId),
                jobNow != null ? jobNow.TombGuid : "no job");

            if (full != null)
            {
                ChestFilterStore.Write(full.m_nview.GetZDO(), new[] { new ChestFilterLine("Stone", 1) });
                Add(full, "Stone", 50);
                WorkContext capped = Context(second);
                ContainerSnapshot snap = capped.Destinations.FirstOrDefault(d => d.Id == ContainerEndpoint.IdOf(full));
                var stone = new ItemStack { Prefab = "Stone", Amount = 1 };
                ItemBridge.RefreshShared(stone);
                int room = snap != null ? snap.RoomFor(stone) : -1;
                _t.Check("a full stack cap leaves no room", room == 0, room.ToString());
            }
            else _t.Fail("stack cap chest placed", "missing");

            ItemDrop pile = Drop("CopperOre", 8, new Vector3(Apart, 0, 8f));
            if (!_t.Check("ground stack dropped", pile != null)) yield break;
            var time = new FakeTime();
            TimeSource.Override = time;
            try
            {
                time.Fraction = FakeTime.Night;
                foreach (TombController tomb in tombs) tomb.TickNow();
                float end = Time.time + 50f;
                while (Time.time < end && (Live(pile) || tombs.Any(t => t.Carried.CountPrefab("CopperOre") > 0)))
                    yield return null;
            }
            finally
            {
                TimeSource.Override = null;
            }
            int stored = Count(ore, "CopperOre");
            int elsewhere = tombs.Sum(t => Count(t.GetComponent<Container>(), "CopperOre") + t.Carried.CountPrefab("CopperOre"));
            bool oneLease = !Reservations.Table.Leases.GroupBy(l => l.Key).Any(g => g.Select(l => l.Holder).Distinct().Count() > 1);
            _t.Check("one ground stack is taken by the nearest tomb only",
                !Live(pile) && stored == 9 && elsewhere == 0 && oneLease,
                $"chest {stored}, elsewhere {elsewhere}, drop {(Live(pile) ? "still there" : "gone")}");
        }

        private static WorkContext Context(TombController tomb)
        {
            var context = new WorkContext();
            context.Refresh(tomb, SinglePlayerAccessPolicy.Instance, probe: true);
            return context;
        }

        private static bool Has(System.Collections.Generic.IEnumerable<Core.Storage.ContainerSnapshot> list, string id) => list.Any(c => c.Id == id);

        private static string Ids(System.Collections.Generic.IEnumerable<Core.Storage.ContainerSnapshot> list) => string.Join(",", list.Select(c => c.Id));

        private Container Chest(Vector3 offset)
        {
            GameObject go = _t.PlaceVanilla("piece_chest_wood", Ground(offset), Quaternion.identity);
            Container container = go != null ? go.GetComponentInChildren<Container>() : null;
            if (container == null) return null;
            var stone = new ItemStack { Prefab = "Stone", Amount = 1, WorldLevel = Game.m_worldLevel };
            ItemBridge.RefreshShared(stone);
            ItemBridge.AddTo(container.GetInventory(), stone);
            return container;
        }

        private Vector3 Ground(Vector3 offset) => SelfTestContext.OnGround(_arena + offset);

        private Container Seed(Vector3 offset, string prefab, int amount)
        {
            GameObject go = _t.PlaceVanilla("piece_chest_wood", Ground(offset), Quaternion.identity);
            Container container = go != null ? go.GetComponentInChildren<Container>() : null;
            if (container != null && amount > 0) Add(container, prefab, amount);
            return container;
        }

        private static void Add(Container container, string prefab, int amount)
        {
            var stack = new ItemStack { Prefab = prefab, Amount = amount, WorldLevel = Game.m_worldLevel };
            ItemBridge.RefreshShared(stack);
            ItemBridge.AddTo(container.GetInventory(), stack);
        }

        private ItemDrop Drop(string prefab, int amount, Vector3 offset)
        {
            GameObject item = ObjectDB.instance.GetItemPrefab(prefab);
            if (item == null) return null;
            ItemDrop.ItemData data = item.GetComponent<ItemDrop>().m_itemData.Clone();
            data.m_dropPrefab = item;
            data.m_worldLevel = (byte)Game.m_worldLevel;
            ItemDrop drop = ItemDrop.DropItem(data, amount, Ground(offset) + Vector3.up, Quaternion.identity);
            if (drop != null && drop.m_nview != null && drop.m_nview.IsValid())
                drop.m_nview.GetZDO().Set(ZDOVars.s_spawnTime, ZNet.instance.GetTime().AddSeconds(-60).Ticks);
            if (drop != null) _t.Track(drop.gameObject);
            return drop;
        }

        private static int Count(Container container, string prefab)
        {
            if (container == null) return 0;
            return container.GetInventory().GetAllItems().Where(i => ItemBridge.PrefabName(i) == prefab).Sum(i => i.m_stack);
        }

        private static bool Live(ItemDrop drop) =>
            drop != null && drop.m_nview != null && drop.m_nview.IsValid() && drop.m_itemData != null && drop.m_itemData.m_stack > 0;
    }
}
#endif
