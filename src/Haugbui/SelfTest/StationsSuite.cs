#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Brain;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Core.Production;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Integration.StationAdapters;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>M7 round 2: fermenter (HB-T-066), fuel-only stations (HB-T-067), dawn-aware oven (HB-T-068), Collection Chest fallback (HB-T-108).</summary>
    internal sealed class StationsSuite
    {
        private readonly SelfTestContext _t;
        private readonly FakeTime _time = new FakeTime();
        private readonly List<string> _taught = new List<string>();
        private Vector3 _arena;
        private TombController _tomb;

        public StationsSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Stations";
            TimeSource.Override = _time;
            try
            {
                yield return Setup();
                if (_tomb == null) yield break;
                yield return Fermenter();
                yield return FuelOnly("piece_bathtub", new Vector3(-7, 0, 6));
                yield return FuelOnly("piece_FrostKiln", new Vector3(-7, 0, 1));
                yield return FuelOnly("piece_shieldgenerator", new Vector3(-7, 0, -5));
                yield return Oven();
                yield return Fallback();
                yield return SetTime(FakeTime.Noon);
                yield return SelfTestContext.WaitFor(() => _tomb.Machine.State == HaugbuiState.Dormant, 30f);
            }
            finally
            {
                TimeSource.Override = null;
                FermenterAdapter.IgnoreCoverForTests = false;
                CollectionFallback.ForcedForTests = null;
                Player player = _t.Player;
                if (player != null) foreach (string name in _taught) player.m_knownMaterial.Remove(name);
            }
        }

        private IEnumerator Setup()
        {
            Player player = _t.Player;
            _arena = SelfTestContext.FindDryArena(player.transform.position - player.transform.right * 14f - player.transform.forward * 6f, 9f);
            GameObject bed = _t.SpawnVanilla("bed", Ground(new Vector3(8, 0, -6)), Quaternion.identity);
            if (bed == null)
            {
                _t.Fail("spawn test bed", "prefab 'bed' not found");
                yield break;
            }
            yield return null;
            bed.GetComponent<Bed>().SetOwner(player.GetPlayerID(), player.GetPlayerName());
            _time.Fraction = FakeTime.Noon;
            HaugbuiStructure tomb = _t.PlaceStructure(StructureKind.Tomb, _arena);
            HaugbuiStructure offering = _t.PlaceStructure(StructureKind.Offering, Ground(new Vector3(2.5f, 0, -2.5f)));
            yield return SelfTestContext.WaitFrames(3);
            if (!_t.Check("tomb level 2 placed", tomb != null && offering != null)) yield break;
            _tomb = tomb.GetComponent<TombController>();
            yield return SelfTestContext.WaitFor(() => _tomb.Structure.Progress.Level >= 2, 5f);
            _tomb.SetToggle(TombToggles.GroundItems, false, out _);
            _tomb.SetToggle(TombToggles.Harvesting, false, out _);
            _tomb.SetToggle(TombToggles.Production, true, out _);
            yield return SetTime(FakeTime.Night);
            _t.Check("emerges at night", _tomb.Creature != null);
        }

        private IEnumerator Fermenter()
        {
            Fermenter fermenter = Place<Fermenter>("fermenter", new Vector3(6, 0, 6));
            if (fermenter == null)
            {
                _t.Skip("fermenter", "prefab not placed");
                yield break;
            }
            var adapter = new FermenterAdapter();
            StationRecipe recipe = adapter.Recipes(fermenter).FirstOrDefault();
            if (!_t.Check("fermenter recipes read, yield from the prefab", recipe != null && recipe.Yield == 6, recipe != null ? $"{recipe.Key} ×{recipe.Yield}" : "none"))
                yield break;
            Teach(recipe.Input);
            Teach(recipe.Output);
            Container bases = Chest(new Vector3(3, 0, 6), (recipe.Input, 1));
            Container meads = Chest(new Vector3(9, 0, 4), (recipe.Output, 1));
            Job(fermenter, recipe, JobMode.Finite, 6);
            yield return new WaitForSeconds(2f);
            string blocked = adapter.Read(fermenter).Blocked;
            yield return new WaitForSeconds(4f);
            _t.Check("exposed fermenter reported and not loaded", blocked != null && Count(bases, recipe.Input) == 1, blocked ?? "not blocked");

            // Under the open sky the base never ferments; the test skips the cover rule and shortens the 2400 s.
            FermenterAdapter.IgnoreCoverForTests = true;
            fermenter.m_fermentationDuration = 1f;
            int before = Count(meads, recipe.Output);
            yield return SelfTestContext.WaitFor(() => JobOf(fermenter).Produced >= 6 && _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty, 90f);
            StationJobState job = JobOf(fermenter);
            _t.Check("fermenter: one base fed, tapped, 6 meads produced", job.Supplied == 1 && job.Produced == 6, $"supplied {job.Supplied}, produced {job.Produced}");
            _t.Check("meads stored", Count(meads, recipe.Output) - before == 6, $"+{Count(meads, recipe.Output) - before}");
            _t.Check("finite job of 6 complete with one base", ProductionJob.Phase(job, 6, true) == JobPhase.Complete && Count(bases, recipe.Input) == 0);
            FermenterAdapter.IgnoreCoverForTests = false;
        }

        private IEnumerator FuelOnly(string prefab, Vector3 offset)
        {
            GameObject go = _t.PlaceVanilla(prefab, Ground(offset), Quaternion.identity);
            // Some stations (ShieldGenerator) finish setting up in Start, on the next frame.
            yield return SelfTestContext.WaitFrames(2);
            MonoBehaviour station = go != null ? go.GetComponentsInChildren<MonoBehaviour>().FirstOrDefault(c => StationAdapterRegistry.For(c) != null) : null;
            if (station == null)
            {
                _t.Skip($"{prefab} kept fuelled", "prefab not placed or not supported");
                yield break;
            }
            IStationAdapter adapter = StationAdapterRegistry.For(station);
            StationState state = adapter.Read(station);
            if (!_t.Check($"{prefab} is fuel-only", adapter.Kind(station) == StationKind.FuelOnly && state.FuelPrefabs.Count > 0 && state.MaxFuel > 0,
                    $"{adapter.Kind(station)}, fuel {string.Join(",", state.FuelPrefabs)} max {state.MaxFuel}"))
                yield break;
            string fuel = state.FuelPrefabs[0];
            Teach(fuel);
            Container chest = Chest(offset + new Vector3(0, 0, 2.5f), (fuel, 30));
            StationRecipe recipe = adapter.Recipes(station).First();
            // The frost kiln makes FrozenFuel; a chest that holds it gives the product a destination.
            if (recipe.Output.Length > 0) Chest(offset + new Vector3(2.5f, 0, 2.5f), (recipe.Output, 1));
            Job(station, recipe, JobMode.Infinite, 0);
            float maxSeen = 0;
            float end = Time.time + 40f;
            while (Time.time < end && adapter.Read(station).Fuel <= state.MaxFuel / 2f)
            {
                maxSeen = Mathf.Max(maxSeen, adapter.Read(station).Fuel);
                yield return null;
            }
            yield return SelfTestContext.WaitFor(() => _tomb.Worker.Plan == null, 20f);
            float fuelNow = adapter.Read(station).Fuel;
            maxSeen = Mathf.Max(maxSeen, fuelNow);
            _t.Check($"{prefab} topped up, never above max", fuelNow > state.MaxFuel / 2f && maxSeen <= state.MaxFuel,
                $"fuel {fuelNow:0.0}/{state.MaxFuel}, max seen {maxSeen:0.0}, status {ProductionStatus.Get(StationAdapterRegistry.IdOf(station)) ?? "none"}");

            StationJobs.Edit(station, job =>
            {
                job.Enabled = false;
                return true;
            });
            station.GetComponent<ZNetView>()?.GetZDO()?.Set(ZDOVars.s_fuel, 0f);
            int left = Count(chest, fuel);
            yield return new WaitForSeconds(5f);
            _t.Check($"{prefab}: disabled job gets no fuel", Count(chest, fuel) == left && adapter.Read(station).Fuel < 1f);
        }

        private IEnumerator Oven()
        {
            CookingStation oven = Place<CookingStation>("piece_oven", new Vector3(5, 0, -6));
            if (oven == null)
            {
                _t.Skip("oven", "prefab not placed");
                yield break;
            }
            var adapter = new CookingStationAdapter();
            StationRecipe recipe = adapter.Recipes(oven).FirstOrDefault();
            if (!_t.Check("oven recipes and wood fuel read", recipe != null && adapter.Read(oven).FuelPrefabs.Contains("Wood"))) yield break;
            // Seconds instead of the prefab's cook time; the instance has its own conversion copies.
            foreach (CookingStation.ItemConversion conversion in oven.m_conversion) conversion.m_cookTime = 4f;
            recipe = adapter.Recipes(oven).First();
            Teach(recipe.Input);
            Teach(recipe.Output);
            Container raw = Chest(new Vector3(2, 0, -7), (recipe.Input, 3));
            Container cooked = Chest(new Vector3(8, 0, -8), (recipe.Output, 1));
            Chest(new Vector3(2, 0, -9), ("Wood", 20));

            // HB-121: shortly before dawn nothing is loaded that could not come off before the Haugbui heads home.
            foreach (CookingStation.ItemConversion conversion in oven.m_conversion) conversion.m_cookTime = 120f;
            Job(oven, adapter.Recipes(oven).First(), JobMode.Finite, 3);
            yield return SetTime(0.231f);
            yield return new WaitForSeconds(4f);
            _t.Check("near dawn: no slot loaded", adapter.Read(oven).Queued == 0,
                $"queued {adapter.Read(oven).Queued}, status {ProductionStatus.Get(StationAdapterRegistry.IdOf(oven))}");
            _t.Check("near dawn: status says so", ProductionStatus.Get(StationAdapterRegistry.IdOf(oven)) == "$haugbui_job_too_close_to_dawn",
                ProductionStatus.Get(StationAdapterRegistry.IdOf(oven)));

            foreach (CookingStation.ItemConversion conversion in oven.m_conversion) conversion.m_cookTime = 4f;
            yield return SetTime(FakeTime.Night);
            yield return SelfTestContext.WaitFor(() => _tomb.Machine.IsOut && !_tomb.Machine.ReturnPending, 20f);
            int before = Count(cooked, recipe.Output);
            bool burnt = false;
            float end = Time.time + 90f;
            while (Time.time < end && !(JobOf(oven).Produced >= 3 && _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty))
            {
                burnt |= Slots(oven).Any(s => s == oven.m_overCookedItem.name);
                yield return null;
            }
            StationJobState job = JobOf(oven);
            _t.Check("oven: 3 loaded, 3 taken off and stored", job.Supplied == 3 && job.Produced == 3 && Count(cooked, recipe.Output) - before == 3,
                $"supplied {job.Supplied}, produced {job.Produced}, stored +{Count(cooked, recipe.Output) - before}");
            _t.Check("nothing burnt", !burnt && !Loose(oven.m_overCookedItem.name, oven.transform.position));
            _t.Check("oven fuelled within capacity", adapter.Read(oven).Fuel <= oven.m_maxFuel);
            _t.Check("raw items taken only for the job", Count(raw, recipe.Input) == 0);
        }

        private IEnumerator Fallback()
        {
            HaugbuiStructure collectionPiece = _t.PlaceStructure(StructureKind.CollectionChest, Ground(new Vector3(-3, 0, -8)));
            Container collection = collectionPiece != null ? collectionPiece.GetComponent<Container>() : null;
            if (!_t.Check("Collection Chest placed", collection != null)) yield break;
            Container tombStorage = _tomb.GetComponent<Container>();
            int storageBefore = Count(tombStorage, "Resin");
            ItemDrop resin = Drop("Resin", 4, new Vector3(-5, 1, -3));
            _tomb.SetToggle(TombToggles.GroundItems, true, out _);

            CollectionFallback.ForcedForTests = false;
            yield return new WaitForSeconds(5f);
            _t.Check("fallback OFF: item without destination stays on the ground", Live(resin));

            CollectionFallback.ForcedForTests = true;
            yield return SelfTestContext.WaitFor(() => !Live(resin) && _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty, 40f);
            _t.Check("fallback ON: item without destination goes to the Collection Chest", !Live(resin) && Count(collection, "Resin") == 4,
                $"on ground {Live(resin)}, in chest {Count(collection, "Resin")}, carried {_tomb.Carried.CountPrefab("Resin")}");
            _t.Check("never into tomb storage", Count(tombStorage, "Resin") == storageBefore);
            yield return new WaitForSeconds(5f);
            _t.Check("no loop: it stays in the Collection Chest", Count(collection, "Resin") == 4 && _tomb.Carried.CountPrefab("Resin") == 0);
            _tomb.SetToggle(TombToggles.GroundItems, false, out _);
            CollectionFallback.ForcedForTests = null;
        }

        private T Place<T>(string prefab, Vector3 offset) where T : Component
        {
            GameObject go = _t.PlaceVanilla(prefab, Ground(offset), Quaternion.identity);
            return go != null ? go.GetComponentInChildren<T>() : null;
        }

        private static void Job(MonoBehaviour station, StationRecipe recipe, JobMode mode, int target) =>
            StationJobs.Edit(station, job =>
            {
                ProductionJob.Edit(job, true, recipe.Input, recipe.Output, mode, target);
                return true;
            });

        private static StationJobState JobOf(MonoBehaviour station) => StationJobs.Read(station) ?? new StationJobState();

        private static IEnumerable<string> Slots(CookingStation oven)
        {
            ZDO zdo = oven.m_nview.GetZDO();
            for (int i = 0; i < oven.m_slots.Length; i++) yield return zdo.GetString("slot" + i);
        }

        private static bool Loose(string prefab, Vector3 near) =>
            ItemDrop.s_instances.Any(d => d != null && ItemBridge.PrefabName(d.m_itemData) == prefab && Vector3.Distance(d.transform.position, near) < 8f);

        private ItemDrop Drop(string prefab, int amount, Vector3 offset)
        {
            GameObject item = ObjectDB.instance.GetItemPrefab(prefab);
            ItemDrop.ItemData data = item.GetComponent<ItemDrop>().m_itemData.Clone();
            data.m_dropPrefab = item;
            data.m_worldLevel = (byte)Game.m_worldLevel;
            ItemDrop drop = ItemDrop.DropItem(data, amount, Ground(offset) + Vector3.up, Quaternion.identity);
            drop.m_nview.GetZDO().Set(ZDOVars.s_spawnTime, ZNet.instance.GetTime().AddSeconds(-60).Ticks);
            return drop;
        }

        private static bool Live(Component c) => c != null && c.GetComponent<ZNetView>() != null && c.GetComponent<ZNetView>().IsValid();

        private void Teach(string prefab)
        {
            Player player = _t.Player;
            string name = ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
            if (player == null || name == null || player.IsMaterialKnown(name)) return;
            player.m_knownMaterial.Add(name);
            _taught.Add(name);
        }

        private Container Chest(Vector3 offset, params (string Prefab, int Amount)[] items)
        {
            GameObject go = _t.PlaceVanilla("piece_chest_wood", Ground(offset), Quaternion.identity);
            Container container = go != null ? go.GetComponentInChildren<Container>() : null;
            if (container == null) return null;
            foreach ((string prefab, int amount) in items)
            {
                var stack = new ItemStack { Prefab = prefab, Amount = amount, WorldLevel = Game.m_worldLevel };
                ItemBridge.RefreshShared(stack);
                ItemBridge.AddTo(container.GetInventory(), stack);
            }
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
