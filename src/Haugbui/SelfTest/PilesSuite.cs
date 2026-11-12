#if DEBUG
using System.Collections;
using System.Linq;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Core.Production;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Integration.StationAdapters;
using Haugbui.Registry;
using Haugbui.UI;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>Chests before piles, keep-lit fires, and a frost kiln that stops adding fuel.</summary>
    internal sealed class PilesSuite
    {
        private readonly SelfTestContext _t;
        private readonly FakeTime _time = new FakeTime();
        private Vector3 _arena;
        private TombController _tomb;

        public PilesSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "piles";
            TimeSource.Override = _time;
            // Level 2 reaches 60 m, which still includes the chests of a base inside the active area.
            TombController.RadiusOverride = 20f;
            bool freeBuild = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildCost);
            if (freeBuild) ZoneSystem.instance.RemoveGlobalKey(GlobalKeys.NoBuildCost);
            try
            {
                _arena = SelfTestContext.IsolatedArena(_t.Player.transform.position + _t.Player.transform.forward * 200f, 160f);
                yield return Setup();
                if (_tomb == null) yield break;
                yield return Piles();
                yield return Fires();
                yield return Kiln();
                yield return Title();
            }
            finally
            {
                TimeSource.Override = null;
                TombController.RadiusOverride = null;
                if (freeBuild && ZoneSystem.instance != null) ZoneSystem.instance.SetGlobalKey(GlobalKeys.NoBuildCost);
            }
        }

        private IEnumerator Setup()
        {
            _time.Fraction = FakeTime.Noon;
            GameObject bed = _t.SpawnVanilla("bed", Ground(new Vector3(8f, 0f, -6f)), Quaternion.identity);
            if (bed == null)
            {
                _t.Fail("spawn test bed", "prefab 'bed' not found");
                yield break;
            }
            yield return null;
            Player player = _t.Player;
            Bed owned = bed.GetComponent<Bed>();
            ZNetView view = bed.GetComponent<ZNetView>();
            if (owned == null || view == null || !view.IsValid())
            {
                _t.Fail("spawn test bed", "the bed has no network view");
                yield break;
            }
            owned.SetOwner(player.GetPlayerID(), player.GetPlayerName());
            Haugbui.Structures.HaugbuiStructure tomb = _t.PlaceStructure(Haugbui.Core.Structures.StructureKind.Tomb, _arena);
            Haugbui.Structures.HaugbuiStructure offering = _t.PlaceStructure(Haugbui.Core.Structures.StructureKind.Offering, Ground(new Vector3(2.5f, 0f, -2.5f)));
            yield return SelfTestContext.WaitFrames(3);
            if (!_t.Check("tomb level 2 placed", tomb != null && offering != null)) yield break;
            _tomb = tomb.GetComponent<TombController>();
            yield return SelfTestContext.WaitFor(() => _tomb.Structure.Progress.Level >= 2, 5f);
            _tomb.SetToggle(TombToggles.GroundItems, true, out _);
            _tomb.SetToggle(TombToggles.Harvesting, false, out _);
            _tomb.SetToggle(TombToggles.Production, true, out _);
            _tomb.SetToggle(TombToggles.MaterialPiles, false, out _);
            yield return SetTime(FakeTime.Night);
            _t.Check("emerges at night with piles off", _tomb.Creature != null && !_tomb.State.Has(TombToggles.MaterialPiles));
        }

        private IEnumerator Piles()
        {
            string pilePrefab = FindPile("Wood");
            string consumerPrefab = FindWoodConsumer();
            if (pilePrefab == null || consumerPrefab == null)
            {
                _t.Skip("wood pile and a station that burns wood", $"pile {pilePrefab ?? "none"}, station {consumerPrefab ?? "none"}");
                yield break;
            }
            GameObject pile = _t.PlaceVanilla(pilePrefab, Ground(new Vector3(4f, 0f, 5f)), Quaternion.identity);
            GameObject forge = _t.PlaceVanilla("forge", Ground(new Vector3(7f, 0f, 5f)), Quaternion.identity);
            Smelter consumer = PlaceSmelter(consumerPrefab, new Vector3(4f, 0f, 8f));
            if (pile == null || consumer == null)
            {
                _t.Skip("pile and wood station placed", "placement failed");
                yield break;
            }
            Teach("Wood");
            IStationAdapter adapter = StationAdapterRegistry.For(consumer);
            StationRecipe recipe = adapter.Recipes(consumer).First(r => r.Input == "Wood");
            Teach(recipe.Output);
            consumer.m_maxOre = 100;
            consumer.m_maxFuel = 0;
            consumer.m_requiresRoof = false;
            consumer.m_smokeSpawner = null;
            consumer.m_secPerProduct = 9999f;
            Container chest = Chest(new Vector3(1f, 0f, 5f), ("Wood", 20));
            WorldRegistry.EnsureStation(consumer, adapter.View(consumer));
            Job(consumer, recipe, JobMode.Finite, 5);
            yield return new WaitForSeconds(8f);
            _t.Check("piles off: the wood pile stays while the chest supplies the job", Live(pile), pile == null ? "gone" : "standing");
            _t.Check("a forge is not a pile", forge != null && Live(forge));

            _tomb.SetToggle(TombToggles.MaterialPiles, true, out _);
            int before = HeldWood(chest) + adapter.Read(consumer).Queued;
            yield return new WaitForSeconds(8f);
            _t.Check("chests first: a stocked chest leaves the pile standing", Live(pile),
                $"queued {adapter.Read(consumer).Queued}, chest {Count(chest, "Wood")}");

            while (chest.GetInventory().NrOfItems() > 0) chest.GetInventory().RemoveItem(chest.GetInventory().GetItem(0));
            var seedStack = new Haugbui.Core.Creature.ItemStack { Prefab = "Wood", Amount = 1, WorldLevel = Game.m_worldLevel };
            ItemBridge.RefreshShared(seedStack);
            ItemBridge.AddTo(chest.GetInventory(), seedStack);
            int seed = HeldWood(chest) + adapter.Read(consumer).Queued;
            int stack = pile.GetComponent<Piece>().m_resources[0].m_amount;
            int accounted = PileGateway.RefundsAccountedBeforeRemoval;
            bool saved = StationJobs.Edit(consumer, job =>
            {
                ProductionJob.Edit(job, true, recipe.Input, recipe.Output, JobMode.Finite, 80);
                return true;
            });
            _t.Check("the wood job now wants more than the chest can give", saved && (StationJobs.Read(consumer)?.Target ?? 0) == 80);
            _tomb.Worker.Abort("test");
            if (_tomb.Machine.State == HaugbuiState.Traveling) _tomb.Machine.TryEnter(HaugbuiState.Idle);
            _tomb.Worker.PlanNow();
            float end = Time.time + 25f;
            while (Time.time < end && Live(pile)) yield return null;
            int after = HeldWood(chest) + adapter.Read(consumer).Queued;
            bool standing = Live(pile);
            string detail = "gone";
            if (standing)
            {
                bool seen = PileGateway.TrySnapshot(pile.GetComponent<Piece>(), out _);
                StationState state = adapter.Read(consumer);
                detail = $"still standing, pile {(seen ? "seen" : "not a pile")}, blocked {state.Blocked ?? "-"}, plan {_tomb.Worker.Plan?.Kind ?? "none"}";
            }
            _t.Check("a short chest opens the pile", !standing, detail);
            _t.Check("the refund is counted before the piece is gone", PileGateway.RefundsAccountedBeforeRemoval > accounted);
            _t.Check("opening the pile does not lose its wood", after == seed + stack, $"before {seed}+{stack}, after {after}");
            StationJobs.Edit(consumer, job =>
            {
                job.Enabled = false;
                return true;
            });
        }

        private IEnumerator Fires()
        {
            string hearthName = FindFire();
            if (hearthName == null)
            {
                _t.Skip("a refillable fireplace exists", "none");
                yield break;
            }
            Fireplace low = PlaceFire(hearthName, new Vector3(-4f, 0f, 4f), 1f, false);
            Fireplace empty = PlaceFire(hearthName, new Vector3(-4f, 0f, 7f), 0f, true);
            Fireplace warm = PlaceFire(hearthName, new Vector3(-4f, 0f, 10f), 4f, true);
            Fireplace off = PlaceFire(hearthName, new Vector3(-7f, 0f, 4f), 0f, false);
            if (low == null)
            {
                _t.Skip("fireplaces placed", "placement failed");
                yield break;
            }
            string fuel = KeepLit.FuelPrefab(low);
            Teach(fuel);
            Chest(new Vector3(-4f, 0f, 1f), (fuel, 40));
            string before = low.GetHoverText();
            low.Interact(_t.Player, false, true);
            _t.Check("shift+E switches keep lit", KeepLit.IsOn(low) && low.GetHoverText() != before, low.GetHoverText());
            float end = Time.time + 30f;
            while (Time.time < end && (Mathf.Ceil(KeepLit.Fuel(low)) <= 1f || Mathf.Ceil(KeepLit.Fuel(empty)) <= 1f)) yield return null;
            _t.Check("a hearth showing 1 is filled", Mathf.Ceil(KeepLit.Fuel(low)) > 1f, KeepLit.Fuel(low).ToString("0.0"));
            _t.Check("a hearth showing 0 is filled", Mathf.Ceil(KeepLit.Fuel(empty)) > 1f, KeepLit.Fuel(empty).ToString("0.0"));
            _t.Check("a hearth above 1 is left alone", Mathf.Ceil(KeepLit.Fuel(warm)) <= 4f, KeepLit.Fuel(warm).ToString("0.0"));
            _t.Check("an off hearth is left alone", KeepLit.Fuel(off) <= 0.01f, KeepLit.Fuel(off).ToString("0.0"));
        }

        private IEnumerator Kiln()
        {
            GameObject kilnObject = _t.PlaceVanilla("piece_FrostKiln", Ground(new Vector3(0f, 0f, 12f)), Quaternion.identity);
            GameObject tubObject = _t.PlaceVanilla("piece_bathtub", Ground(new Vector3(4f, 0f, 12f)), Quaternion.identity);
            yield return SelfTestContext.WaitFrames(2);
            Smelter kiln = kilnObject != null ? kilnObject.GetComponentInChildren<Smelter>() : null;
            Smelter tub = tubObject != null ? tubObject.GetComponentInChildren<Smelter>() : null;
            if (kiln == null || tub == null)
            {
                _t.Skip("frost kiln and hot tub", "missing");
                yield break;
            }
            kiln.m_requiresRoof = false;
            kiln.m_secPerProduct = 1f;
            IStationAdapter adapter = StationAdapterRegistry.For(kiln);
            WorldRegistry.EnsureStation(kiln, adapter.View(kiln));
            StationState state = adapter.Read(kiln);
            _t.Check("the frost kiln reports fuel per product", state.FuelPerProduct > 0f, state.FuelPerProduct.ToString());
            StationRecipe recipe = adapter.Recipes(kiln).FirstOrDefault(r => r.Output.Length > 0);
            if (recipe == null)
            {
                _t.Skip("the frost kiln has a product", "none");
                yield break;
            }
            string fuel = state.FuelPrefabs[0];
            Teach(fuel);
            Teach(recipe.Output);
            Container fuelChest = Chest(new Vector3(-2f, 0f, 12f), (fuel, 40));
            Container products = Chest(new Vector3(2f, 0f, 14f), (recipe.Output, 1));
            Job(kiln, recipe, JobMode.Finite, 3);
            kiln.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_fuel, 0f);
            float end = Time.time + 50f;
            while (Time.time < end && (StationJobs.Read(kiln)?.Produced ?? 0) < 3) yield return null;
            int produced = StationJobs.Read(kiln)?.Produced ?? 0;
            int left = Count(fuelChest, fuel);
            yield return new WaitForSeconds(6f);
            _t.Check("a finite frost kiln stops at the requested products", produced >= 3 && produced <= 6, produced.ToString());
            _t.Check("fuel left in the chest stays after the kiln finishes", Count(fuelChest, fuel) == left,
                $"at finish {left}, later {Count(fuelChest, fuel)}, stored {Count(products, recipe.Output)}");
            ProductionPanel.Open(tub);
            yield return null;
            _t.Check("the hot tub stays infinite only", ProductionPanel.IsOpen && !ProductionPanel.ModeFieldEnabled);
            ProductionPanel.Close();
            ProductionPanel.Open(kiln);
            yield return null;
            _t.Check("the frost kiln offers finite and infinite", ProductionPanel.IsOpen && ProductionPanel.ModeFieldEnabled);
            ProductionPanel.Close();
            StationJobs.Edit(kiln, job =>
            {
                job.Enabled = false;
                return true;
            });
        }

        private IEnumerator Title()
        {
            GameObject station = _t.PlaceVanilla("blastfurnace", Ground(new Vector3(8f, 0f, 12f)), Quaternion.identity);
            yield return SelfTestContext.WaitFrames(2);
            Smelter smelter = station != null ? station.GetComponentInChildren<Smelter>() : null;
            if (smelter == null)
            {
                _t.Skip("production title clears the corner", "blast furnace missing");
                yield break;
            }
            ProductionPanel.Open(smelter);
            yield return null;
            _t.Check("the production title clears the wood border", Mathf.Abs(ProductionPanel.TitleCenterY - (-64f)) < 0.1f,
                ProductionPanel.TitleCenterY.ToString("0"));
            _t.Check("recipe rows stay where they were", Mathf.Abs(ProductionPanel.RecipeRowY - (-115f)) < 0.1f,
                ProductionPanel.RecipeRowY.ToString("0"));
            ProductionPanel.Close();
        }

        private Smelter PlaceSmelter(string prefab, Vector3 offset)
        {
            GameObject placed = _t.PlaceVanilla(prefab, Ground(offset), Quaternion.identity);
            return placed != null ? placed.GetComponentInChildren<Smelter>() : null;
        }

        private Fireplace PlaceFire(string prefab, Vector3 offset, float fuel, bool keep)
        {
            GameObject placed = _t.PlaceVanilla(prefab, Ground(offset), Quaternion.identity);
            Fireplace fire = placed != null ? placed.GetComponentInChildren<Fireplace>() : null;
            if (fire == null) return null;
            ZNetView view = fire.GetComponent<ZNetView>() ?? placed.GetComponent<ZNetView>();
            view.GetZDO().Set(ZDOVars.s_fuel, fuel);
            KeepLit.Set(fire, keep);
            return fire;
        }

        private static string FindPile(string itemPrefab)
        {
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                Piece piece = prefab.GetComponent<Piece>();
                if (piece?.m_resources == null || piece.m_resources.Length != 1) continue;
                Piece.Requirement requirement = piece.m_resources[0];
                ItemDrop item = requirement?.m_resItem;
                if (item == null || !requirement.m_recover || item.gameObject.name != itemPrefab) continue;
                if (requirement.m_amount != item.m_itemData.m_shared.m_maxStackSize || requirement.m_amount <= 1) continue;
                return prefab.name;
            }
            return null;
        }

        private static string FindWoodConsumer()
        {
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                Smelter smelter = prefab.GetComponent<Smelter>();
                if (smelter?.m_conversion == null || smelter.m_maxOre <= 0) continue;
                if (smelter.m_conversion.Any(c => c?.m_from != null && c.m_from.gameObject.name == "Wood")) return prefab.name;
            }
            return null;
        }

        private static string FindFire()
        {
            string any = null;
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                Fireplace fire = prefab.GetComponent<Fireplace>();
                if (fire == null || !fire.m_canRefill || fire.m_infiniteFuel || fire.m_fuelItem == null) continue;
                if (prefab.name.IndexOf("hearth", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || prefab.name.IndexOf("fire_pit", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return prefab.name;
                if (any == null) any = prefab.name;
            }
            return any;
        }

        private void Teach(string prefab)
        {
            Player player = _t.Player;
            string name = ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
            if (player == null || name == null || player.IsMaterialKnown(name)) return;
            player.m_knownMaterial.Add(name);
        }

        private Container Chest(Vector3 offset, params (string Prefab, int Amount)[] items)
        {
            GameObject go = _t.PlaceVanilla("piece_chest_wood", Ground(offset), Quaternion.identity);
            Container container = go != null ? go.GetComponentInChildren<Container>() : null;
            if (container == null) return null;
            foreach ((string prefab, int amount) in items)
            {
                var stack = new Haugbui.Core.Creature.ItemStack { Prefab = prefab, Amount = amount, WorldLevel = Game.m_worldLevel };
                ItemBridge.RefreshShared(stack);
                ItemBridge.AddTo(container.GetInventory(), stack);
            }
            return container;
        }

        private int HeldWood(Container chest)
        {
            int total = Count(chest, "Wood") + _tomb.Carried.CountPrefab("Wood");
            foreach (ItemDrop drop in ItemDrop.s_instances)
            {
                if (drop == null || ItemBridge.PrefabName(drop.m_itemData) != "Wood") continue;
                if (Vector3.Distance(drop.transform.position, _arena) > 20f) continue;
                total += drop.m_itemData.m_stack;
            }
            return total;
        }

        private static int Count(Container container, string prefab) =>
            container == null ? 0 : ItemBridge.CountPrefab(container.GetInventory(), prefab);

        private static void Job(MonoBehaviour station, StationRecipe recipe, JobMode mode, int target) =>
            StationJobs.Edit(station, job =>
            {
                ProductionJob.Edit(job, true, recipe.Input, recipe.Output, mode, target);
                return true;
            });

        private static bool Live(GameObject go) => go != null && go.GetComponent<ZNetView>() != null && go.GetComponent<ZNetView>().IsValid();

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
