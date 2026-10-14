#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Brain;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Structures;
using UnityEngine;
using Reservations = Haugbui.Coordination.Reservations;

namespace Haugbui.SelfTest
{
    /// <summary>M6: ground items (HB-T-054), crops (HB-T-055), hives and nests (HB-T-056), harvest toggle and options (HB-T-057).</summary>
    internal sealed class HarvestSuite
    {
        private readonly SelfTestContext _t;
        private readonly FakeTime _time = new FakeTime();
        private readonly List<Container> _chests = new List<Container>();
        private Vector3 _arena;
        private TombController _tomb;
        private Container _wood, _carrot, _honey, _feathers;

        public HarvestSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Harvest";
            TimeSource.Override = _time;
            try
            {
                yield return Setup();
                if (_tomb == null) yield break;
                yield return GroundItems();
                yield return Crops();
                yield return Hives();
                yield return SetTime(FakeTime.Noon);
                yield return SelfTestContext.WaitFor(() => _tomb.Machine.State == HaugbuiState.Dormant, 30f);
                _t.Check("dawn: nothing carried", _tomb.Carried.IsEmpty, string.Join(", ", _tomb.Carried.Stacks));
            }
            finally
            {
                TimeSource.Override = null;
            }
        }

        private IEnumerator Setup()
        {
            Player player = _t.Player;
            _arena = SelfTestContext.FindDryArena(player.transform.position - player.transform.right * 14f + player.transform.forward * 6f, 9f);
            GameObject bed = _t.SpawnVanilla("bed", Ground(new Vector3(-8, 0, -4)), Quaternion.identity);
            if (bed == null)
            {
                _t.Fail("spawn test bed", "prefab 'bed' not found");
                yield break;
            }
            yield return null;
            bed.GetComponent<Bed>().SetOwner(player.GetPlayerID(), player.GetPlayerName());
            _time.Fraction = FakeTime.Noon;

            HaugbuiStructure tomb = _t.PlaceStructure(StructureKind.Tomb, _arena);
            if (!_t.Check("tomb placed", tomb != null)) yield break;
            _wood = Chest(new Vector3(6, 0, -3), "Wood");
            _carrot = Chest(new Vector3(-3, 0, -5), "Carrot");
            // Hives spawn their product without stamping the world level (Beehive.RPC_Extract).
            _honey = Chest(new Vector3(-6, 0, 2), "Honey", 0);
            _feathers = Chest(new Vector3(-6, 0, 5), "Feathers", 0);
            yield return SelfTestContext.WaitFrames(3);
            _tomb = tomb.GetComponent<TombController>();
            if (!_t.Check("chests placed", _wood != null && _carrot != null && _honey != null && _feathers != null)) _tomb = null;
            else _tomb.SetToggle(TombToggles.Harvesting, false, out _);
        }

        private IEnumerator GroundItems()
        {
            _tomb.SetToggle(TombToggles.GroundItems, false, out _);
            ItemDrop wood = Drop("Wood", 5, new Vector3(5, 1, 6), old: true);
            ItemDrop cheated = Drop("Wood", 3, new Vector3(7, 1, 7), old: true, cheated: true);
            ItemDrop axe = Drop("AxeStone", 1, new Vector3(4, 1, 8), old: true);
            // Within 3 m of the player but outside the player's own auto-pickup range.
            ItemDrop atPlayer = DropAt("Wood", 2, _t.Player.transform.position + _t.Player.transform.forward * 2.6f + Vector3.up, old: true);
            if (atPlayer != null) atPlayer.m_autoPickup = false;
            ItemDrop noDestination = Drop("Resin", 4, new Vector3(3, 1, 9), old: true);
            int woodBefore = Count(_wood, "Wood");

            yield return SetTime(FakeTime.Night);
            _t.Check("emerges at night", _tomb.Creature != null, _tomb.Machine.State.ToString());
            yield return new WaitForSeconds(5f);
            _t.Check("toggle OFF: no ground pickups", Live(wood) && Live(cheated), $"plan {_tomb.Worker.Plan}");

            ItemDrop fresh = Drop("Wood", 1, new Vector3(6, 1, 4), old: false);
            float freshAt = Time.time, freshTakenAfter = -1f;
            _tomb.SetToggle(TombToggles.GroundItems, true, out _);
            bool keysHeld = true, sawGather = false;
            float end = Time.time + 60f;
            while (Time.time < end && (Live(wood) || Live(cheated) || _tomb.Worker.Plan != null || !_tomb.Carried.IsEmpty))
            {
                TaskPlanSnapshot(ref sawGather, ref keysHeld);
                if (freshTakenAfter < 0 && !Live(fresh)) freshTakenAfter = Time.time - freshAt;
                yield return null;
            }
            _t.Check("old ground items collected", !Live(wood) && !Live(cheated) && sawGather,
                $"wood {Live(wood)}, cheated {Live(cheated)}, plan {_tomb.Worker.Plan}");
            _t.Check("a gather trip holds every drop it targets", keysHeld);
            float waitEnd = Time.time + 25f;
            while (Time.time < waitEnd && Live(fresh)) yield return null;
            if (freshTakenAfter < 0 && !Live(fresh)) freshTakenAfter = Time.time - freshAt;
            _t.Check("fresh drop only taken once older than 10 s", freshTakenAfter >= 9.5f, $"taken after {freshTakenAfter:0.0}s");
            _t.Check("equipment never taken", Live(axe));
            _t.Check("drop next to a player not taken", Live(atPlayer));
            _t.Check("item without destination stays on the ground", Live(noDestination));

            List<ItemDrop.ItemData> stacks = _wood.GetInventory().GetAllItems().Where(i => ItemBridge.PrefabName(i) == "Wood").ToList();
            _t.Check("ground wood sorted into the wood chest", Count(_wood, "Wood") >= woodBefore + 8, $"{Count(_wood, "Wood") - woodBefore} added");
            _t.Check("cheated drop stays cheated, clean stays clean",
                stacks.Where(i => i.m_cheated).Sum(i => i.m_stack) == 3 && stacks.Where(i => !i.m_cheated).Sum(i => i.m_stack) >= woodBefore + 5,
                string.Join(", ", stacks.Select(i => $"{i.m_stack}{(i.m_cheated ? " cheated" : "")}")));

            _tomb.SetToggle(TombToggles.GroundItems, false, out _);
            foreach (ItemDrop drop in new[] { axe, atPlayer, noDestination }.Where(Live)) _t.Track(drop.gameObject);
        }

        private void TaskPlanSnapshot(ref bool sawGather, ref bool keysHeld)
        {
            Core.Coordination.TaskPlan plan = _tomb.Worker.Plan;
            if (plan == null || plan.Kind != TaskKinds.Gather) return;
            sawGather = true;
            keysHeld &= plan.Keys.All(k => Reservations.Table.HolderOf(k) == _tomb.Guid);
        }

        private IEnumerator Crops()
        {
            var crops = new List<Pickable>();
            foreach (Vector3 offset in new[] { new Vector3(2, 0, -6), new Vector3(3, 0, -7), new Vector3(4, 0, -6) })
            {
                Pickable crop = Spawn<Pickable>("Pickable_Carrot", offset);
                if (crop != null) crops.Add(crop);
            }
            if (crops.Count == 0)
            {
                _t.Skip("crops", "prefab 'Pickable_Carrot' not found");
                yield break;
            }
            _t.Check("carrot is a cultivated crop (m_harvestable)", crops[0].m_harvestable);
            GameObject sapling = _t.SpawnVanilla("sapling_carrot", Ground(new Vector3(5, 0, -7)), Quaternion.identity);
            Pickable wild = Spawn<Pickable>("RaspberryBush", new Vector3(1, 0, -9));
            int carrotsBefore = Count(_carrot, "Carrot");
            yield return SelfTestContext.WaitFrames(3);

            yield return new WaitForSeconds(4f);
            _t.Check("harvest toggle OFF: crops untouched", crops.All(Live));

            _tomb.SetToggle(TombToggles.Harvesting, true, out _);
            bool sawHarvest = false;
            float end = Time.time + 60f;
            while (Time.time < end && (crops.Any(Live) || _tomb.Worker.Plan != null || !_tomb.Carried.IsEmpty || _tomb.Worker.OwnDrops.Count > 0))
            {
                sawHarvest |= _tomb.Worker.Plan?.Kind == TaskKinds.Harvest;
                yield return null;
            }
            int added = Count(_carrot, "Carrot") - carrotsBefore;
            _t.Check("mature carrots harvested", !crops.Any(Live) && sawHarvest, $"{crops.Count(Live)} left");
            _t.Check("harvest drops sorted into the carrot chest", added >= crops.Count, $"{added} carrots for {crops.Count} crops");
            _t.Check("no harvest drops left on the ground", !LooseNear(new Vector3(3, 0, -6), "Carrot", 6f), "loose carrots near the field");
            if (sapling != null) _t.Check("growing plant ignored and not replanted", sapling != null && sapling.GetComponent<ZNetView>().IsValid());
            else _t.Skip("growing plant ignored", "prefab 'sapling_carrot' not found");
            if (wild != null) _t.Check("wild berries untouched by default", !wild.GetPicked());
            else _t.Skip("wild berries untouched by default", "prefab 'RaspberryBush' not found");
        }

        private IEnumerator Hives()
        {
            Beehive full = PlaceHive("piece_beehive", new Vector3(8, 0, 2), 3);
            Beehive empty = PlaceHive("piece_beehive", new Vector3(9, 0, 6), 0);
            Beehive nest = PlaceHive("piece_birdnest", new Vector3(8, 0, -6), 2);
            if (full == null || nest == null)
            {
                _t.Skip("hives and nests", "prefab 'piece_beehive' or 'piece_birdnest' not placed");
                yield break;
            }
            int honey = Count(_honey, "Honey"), feathers = Count(_feathers, "Feathers");
            yield return SelfTestContext.WaitFor(() => Level(full) == 0 && Level(nest) == 0 && _tomb.Worker.Plan == null
                                                     && _tomb.Carried.IsEmpty && _tomb.Worker.OwnDrops.Count == 0, 60f);
            _t.Check("full hive extracted", Level(full) == 0 && Count(_honey, "Honey") - honey >= 3, $"level {Level(full)}, +{Count(_honey, "Honey") - honey} honey");
            _t.Check("bird nest yields feathers", Level(nest) == 0 && Count(_feathers, "Feathers") - feathers >= 2,
                $"level {Level(nest)}, +{Count(_feathers, "Feathers") - feathers} feathers");
            _t.Check("hive and nest never destroyed", Live(full) && Live(nest));
            if (empty != null) _t.Check("empty hive ignored", Live(empty) && Level(empty) == 0);

            _tomb.SetToggle(TombToggles.Harvesting, false, out _);
            SetLevel(full, 2);
            yield return new WaitForSeconds(4f);
            _t.Check("harvest toggle OFF stops hives too", Level(full) == 2);
        }

        private Beehive PlaceHive(string prefab, Vector3 offset, int level)
        {
            GameObject go = _t.PlaceVanilla(prefab, Ground(offset), Quaternion.identity);
            Beehive hive = go != null ? go.GetComponent<Beehive>() : null;
            if (hive != null) SetLevel(hive, level);
            return hive;
        }

        private static void SetLevel(Beehive hive, int level) => hive.m_nview.GetZDO().Set(ZDOVars.s_level, level);

        private static int Level(Beehive hive) => hive != null && hive.m_nview.IsValid() ? hive.GetHoneyLevel() : -1;

        private T Spawn<T>(string prefab, Vector3 offset) where T : Component
        {
            GameObject go = _t.SpawnVanilla(prefab, Ground(offset), Quaternion.identity);
            return go != null ? go.GetComponent<T>() : null;
        }

        private ItemDrop Drop(string prefab, int amount, Vector3 offset, bool old, bool cheated = false) =>
            DropAt(prefab, amount, Ground(offset) + Vector3.up, old, cheated);

        private ItemDrop DropAt(string prefab, int amount, Vector3 position, bool old, bool cheated = false)
        {
            GameObject item = ObjectDB.instance.GetItemPrefab(prefab);
            if (item == null) return null;
            ItemDrop.ItemData data = item.GetComponent<ItemDrop>().m_itemData.Clone();
            data.m_dropPrefab = item;
            data.m_cheated = cheated;
            data.m_worldLevel = (byte)Game.m_worldLevel;
            ItemDrop drop = ItemDrop.DropItem(data, amount, position, Quaternion.identity);
            if (old) drop.m_nview.GetZDO().Set(ZDOVars.s_spawnTime, ZNet.instance.GetTime().AddSeconds(-60).Ticks);
            return drop;
        }

        private static bool Live(Component c) => c != null && c.GetComponent<ZNetView>() != null && c.GetComponent<ZNetView>().IsValid();

        private bool LooseNear(Vector3 offset, string prefab, float radius)
        {
            Vector3 at = Ground(offset);
            return ItemDrop.s_instances.Any(d => d != null && ItemBridge.PrefabName(d.m_itemData) == prefab
                                                 && Vector3.Distance(d.transform.position, at) < radius);
        }

        private Container Chest(Vector3 offset, string prefab, int worldLevel = -1)
        {
            GameObject go = _t.PlaceVanilla("piece_chest_wood", Ground(offset), Quaternion.identity);
            Container container = go != null ? go.GetComponentInChildren<Container>() : null;
            if (container == null) return null;
            var stack = new ItemStack { Prefab = prefab, Amount = 1, WorldLevel = worldLevel >= 0 ? worldLevel : Game.m_worldLevel };
            ItemBridge.RefreshShared(stack);
            ItemBridge.AddTo(container.GetInventory(), stack);
            _chests.Add(container);
            return container;
        }

        private static int Count(Container container, string prefab) =>
            container == null ? 0 : ItemBridge.CountPrefab(container.GetInventory(), prefab);

        private IEnumerator SetTime(float fraction)
        {
            _time.Fraction = fraction;
            _tomb.TickNow();
            yield return SelfTestContext.WaitFrames(2);
        }

        private Vector3 Ground(Vector3 offset) => SelfTestContext.OnGround(_arena + offset);
    }
}
#endif
