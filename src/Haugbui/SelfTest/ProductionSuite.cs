#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Brain;
using Haugbui.Core.Coordination;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Core.Production;
using Haugbui.Core.Storage;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Integration.StationAdapters;
using Haugbui.Structures;
using Haugbui.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Haugbui.SelfTest
{
    /// <summary>M7: station adapters, production panel, level gate, finite/infinite jobs, outputs, reservations, manual use, ownership.</summary>
    internal sealed class ProductionSuite
    {
        private const string Ore = "CopperOre";
        private const string Bar = "Copper";

        private readonly SelfTestContext _t;
        private readonly FakeTime _time = new FakeTime();
        private readonly List<string> _taught = new List<string>();
        private Vector3 _arena;
        private TombController _tomb;
        private Smelter _smelter;
        private Container _oreChest, _coalChest, _barChest;
        private StationRecipe _recipe;
        private int _maxQueueSeen;
        private float _maxFuelSeen;

        public ProductionSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Production";
            TimeSource.Override = _time;
            try
            {
                yield return Setup();
                if (_tomb == null || _smelter == null) yield break;
                Adapters();
                yield return Panel();
                yield return LevelGate();
                if (_tomb.Structure.Progress.Level < 2) yield break;
                yield return FiniteJob();
                yield return Reload();
                yield return ManualUse();
                yield return DeliveryReservation();
                yield return Ownership();
                yield return ToggleOff();
                yield return SpinningWheelAndWindmill();
                yield return GroundSupply();
                yield return SetTime(FakeTime.Noon);
                yield return SelfTestContext.WaitFor(() => _tomb.Machine.State == HaugbuiState.Dormant, 30f);
            }
            finally
            {
                TimeSource.Override = null;
                if (ProductionPanel.IsOpen) ProductionPanel.Close();
                RemoveLooseProducts();
                Player player = _t.Player;
                if (player != null) foreach (string name in _taught) player.m_knownMaterial.Remove(name);
            }
        }

        private IEnumerator Setup()
        {
            Player player = _t.Player;
            _arena = SelfTestContext.FindDryArena(player.transform.position + player.transform.right * 14f + player.transform.forward * 6f, 9f);
            RemoveLooseProducts();
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
            _oreChest = Chest(new Vector3(-4, 0, 4));
            _coalChest = Chest(new Vector3(-4, 0, 6));
            _barChest = Chest(new Vector3(4, 0, 5));
            GameObject smelter = _t.PlaceVanilla("smelter", Ground(new Vector3(0, 0, 7)), Quaternion.identity);
            _smelter = smelter != null ? smelter.GetComponentInChildren<Smelter>() : null;
            yield return SelfTestContext.WaitFrames(3);
            _tomb = tomb.GetComponent<TombController>();
            if (!_t.Check("chests and smelter placed", _oreChest != null && _coalChest != null && _barChest != null && _smelter != null))
            {
                _tomb = null;
                yield break;
            }
            _tomb.SetToggle(TombToggles.GroundItems, false, out _);
            _tomb.SetToggle(TombToggles.Harvesting, false, out _);
            // Seconds instead of the prefab's 30 s per bar; only this instance changes.
            _smelter.m_secPerProduct = 1f;
            Add(_oreChest, Ore, 6);
            Add(_oreChest, Ore, 2, cheated: true);
            Add(_coalChest, "Coal", 30);
            Add(_barChest, Bar, 1);
            Teach(Ore);
            Teach(Bar);
            _recipe = new SmelterAdapter().Recipes(_smelter).FirstOrDefault(r => r.Input == Ore && r.Output == Bar);
        }

        private void Adapters()
        {
            _t.Check("smelter is supported", StationAdapterRegistry.For(_smelter) is SmelterAdapter);
            _t.Check("smelter recipe CopperOre → Copper read from the station", _recipe != null);
            StationState state = new SmelterAdapter().Read(_smelter);
            _t.Check("smelter capacities read from the prefab", state.MaxQueue == 10 && state.MaxFuel == 20 && state.FuelPrefabs.SequenceEqual(new[] { "Coal" }),
                $"queue {state.MaxQueue}, fuel {state.MaxFuel} {string.Join(",", state.FuelPrefabs)}");

            foreach (string prefab in new[] { "piece_workbench", "forge", "piece_cauldron", "piece_MeadCauldron", "piece_preptable", "incinerator" })
            {
                GameObject go = _t.SpawnVanilla(prefab, Ground(new Vector3(9, 0, -8)), Quaternion.identity);
                if (go == null)
                {
                    _t.Skip($"{prefab} has no automation", "prefab not found");
                    continue;
                }
                bool none = go.GetComponentsInChildren<MonoBehaviour>().All(c => StationAdapterRegistry.For(c) == null)
                            && go.GetComponentsInChildren<Switch>().All(s => ProductionPanelPatches.StationOf(s) == null);
                _t.Check($"{prefab} has no automation panel", none);
                ZNetScene.instance.Destroy(go);
            }

            foreach ((string prefab, string input, string output, string fuel) in new[]
                     {
                         ("eitrrefinery", "Softtissue", "Eitr", "Sap"), ("charcoal_kiln", "Wood", "Coal", null),
                         ("blastfurnace", "GoldOre", "Gold", "Coal"), ("windmill", "OatSeeds", "Oat", null),
                     })
            {
                GameObject go = _t.SpawnVanilla(prefab, Ground(new Vector3(9, 0, -8)), Quaternion.identity);
                Smelter station = go != null ? go.GetComponentInChildren<Smelter>() : null;
                if (station == null)
                {
                    _t.Skip($"{prefab} adapter", "prefab not found");
                    continue;
                }
                IStationAdapter adapter = StationAdapterRegistry.For(station);
                StationState read = adapter?.Read(station);
                bool recipe = adapter != null && adapter.Recipes(station).Any(r => r.Input == input && r.Output == output);
                bool fuelOk = fuel == null ? read?.MaxFuel == 0 : read?.FuelPrefabs.Contains(fuel) == true;
                _t.Check($"{prefab}: {input} → {output}{(fuel != null ? ", fuel " + fuel : "")}", recipe && fuelOk);
                ZNetScene.instance.Destroy(go);
            }
        }

        private IEnumerator Panel()
        {
            Switch addOre = _smelter.m_addOreSwitch;
            addOre.Interact(_t.Player, false, true);
            yield return null;
            _t.Check("Shift+E on the smelter opens the production panel", ProductionPanel.IsOpen && ProductionPanel.Current == _smelter);
            Player player = _t.Player;
            _t.Check("panel lists only discovered recipes", ProductionPanel.Recipes.Count > 0 && ProductionPanel.Recipes.All(r => ProductionGate.Known(player, r)),
                string.Join(", ", ProductionPanel.Recipes.Select(r => r.Key)));
            _t.Check("finite job needs an amount", !ProductionPanel.Apply(true, _recipe, JobMode.Finite, 0));
            _t.Check("hover shows the Shift+E hint", addOre.GetHoverText().Contains(Localization.instance.Localize("$haugbui_job_open")));
            _t.Check("station hover shows the automation status",
                addOre.GetHoverText().Contains(Localization.instance.Localize("$haugbui_job_hover")), addOre.GetHoverText());
            _t.Check("station hover offset", !float.IsNaN(addOre.GetHoverOffset()));
        }

        private IEnumerator LevelGate()
        {
            _t.Check("job saved from the panel", ProductionPanel.Apply(true, _recipe, JobMode.Finite, 3));
            StationJobState job = StationJobs.Read(_smelter);
            _t.Check("job persisted on the station", job != null && job.Enabled && job.InputPrefab == Ore && job.Target == 3);
            _t.Check("level 1: panel explains the unlock level",
                ProductionPanel.StatusText(_smelter, job) == Localization.instance.Localize("$haugbui_job_needs_level", "2"),
                ProductionPanel.StatusText(_smelter, job));
            _t.Check("production panel blocks movement", ProductionPanel.IsOpen && !Player.m_localPlayer.TakeInput());
            ProductionPanel.Dismiss.onClick.Invoke();
            yield return null;
            _t.Check("production corner X closes and movement works", !ProductionPanel.IsOpen && Player.m_localPlayer.TakeInput(),
                $"open {ProductionPanel.IsOpen}, input {Player.m_localPlayer.TakeInput()}, inventory {InventoryGui.IsVisible()}");

            yield return SetTime(FakeTime.Night);
            _t.Check("emerges at night", _tomb.Creature != null, _tomb.Machine.State.ToString());
            yield return new WaitForSeconds(4f);
            _t.Check("level 1: no production", Supplied() == 0 && Queued() == 0, $"supplied {Supplied()}");

            HaugbuiStructure offering = _t.PlaceStructure(StructureKind.Offering, Ground(new Vector3(2.5f, 0, -2.5f)));
            yield return SelfTestContext.WaitFor(() => _tomb.Structure.Progress.Level >= 2, 5f);
            _t.Check("level 2 after the Offering", offering != null && _tomb.Structure.Progress.Level == 2, $"level {_tomb.Structure.Progress.Level}");
            _tomb.SetToggle(TombToggles.Production, true, out _);
        }

        private IEnumerator FiniteJob()
        {
            int oreBefore = Count(_oreChest, Ore), barsBefore = Count(_barChest, Bar);
            bool sawFeed = false, sawOutputs = false, keysHeld = true;
            float end = Time.time + 90f;
            while (Time.time < end && !(Job().Produced >= 3 && _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty))
            {
                Sample();
                Core.Coordination.TaskPlan plan = _tomb.Worker.Plan;
                sawFeed |= plan?.Kind == TaskKinds.ProductionFeed;
                if (plan?.Kind == TaskKinds.ProductionOutputs)
                {
                    sawOutputs = true;
                    keysHeld &= plan.Keys.All(k => Coordination.Reservations.Table.HolderOf(k) == _tomb.Guid);
                }
                yield return null;
            }
            StationJobState job = Job();
            _t.Check("finite job: 3 supplied, 3 produced", job.Supplied == 3 && job.Produced == 3, $"supplied {job.Supplied}, produced {job.Produced}");
            _t.Check("feeding and output trips seen", sawFeed && sawOutputs);
            _t.Check("output trip holds every drop it targets", keysHeld);
            _t.Check("queue never above m_maxOre, fuel never above m_maxFuel", _maxQueueSeen <= _smelter.m_maxOre && _maxFuelSeen <= _smelter.m_maxFuel,
                $"queue {_maxQueueSeen}, fuel {_maxFuelSeen:0.0}");
            _t.Check("job owned by this tomb", job.TombGuid == _tomb.Guid);
            _t.Check("bars stored in the copper chest", Count(_barChest, Bar) - barsBefore == 3, $"+{Count(_barChest, Bar) - barsBefore}");
            _t.Check("clean ore used first, cheated ore untouched", Count(_oreChest, Ore, cheated: true) == 2 && oreBefore - Count(_oreChest, Ore) == 3,
                $"clean {Count(_oreChest, Ore, false)}, cheated {Count(_oreChest, Ore, true)}");
            _t.Check("bars from clean ore are clean", Count(_barChest, Bar, cheated: true) == 0);
            _t.Check("no bars left at the output point", !Loose(Bar));
            _t.Check("hover shows the job progress", _smelter.m_addOreSwitch.GetHoverText().Contains("3/3"), _smelter.m_addOreSwitch.GetHoverText());

            int ore = Count(_oreChest, Ore);
            yield return new WaitForSeconds(5f);
            _t.Check("complete job takes no more ore", Count(_oreChest, Ore) == ore && Job().Supplied == 3);
            _t.Check("panel status: complete", ProductionPanel.StatusText(_smelter, Job()) == "$haugbui_job_complete", ProductionPanel.StatusText(_smelter, Job()));
        }

        private IEnumerator Reload()
        {
            StationJobs.Edit(_smelter, job =>
            {
                ProductionJob.Edit(job, true, Ore, Bar, JobMode.Finite, 4);
                return true;
            });
            GameObject reloaded = null;
            yield return _t.Reload(_smelter.gameObject, go => reloaded = go);
            _smelter = reloaded != null ? reloaded.GetComponentInChildren<Smelter>() : null;
            StationJobState job = _smelter != null ? StationJobs.Read(_smelter) : null;
            _t.Check("job and progress survive a reload", job != null && job.Enabled && job.Target == 4 && job.Supplied == 3 && job.Produced == 3,
                job == null ? "no job" : $"target {job.Target}, supplied {job.Supplied}, produced {job.Produced}");
            if (_smelter == null) yield break;
            _smelter.m_secPerProduct = 1f;
            yield return SelfTestContext.WaitFor(() => Job().Produced >= 4 && _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty, 60f);
            _t.Check("raised target resumes the job after reload", Job().Supplied == 4 && Job().Produced == 4, $"supplied {Job().Supplied}, produced {Job().Produced}");
        }

        private IEnumerator ManualUse()
        {
            if (_smelter == null) yield break;
            SetJob(false, JobMode.Infinite, 0);
            _smelter.m_secPerProduct = 1000f;
            _smelter.m_nview.InvokeRPC("RPC_AddOre", "TinOre", false);
            int supplied = Job().Supplied;
            SetJob(true, JobMode.Infinite, 0);
            yield return new WaitForSeconds(5f);
            _t.Check("manual input pauses the job", Job().Supplied == supplied && Queued() == 1,
                $"supplied {Job().Supplied - supplied}, queue {Queued()}");
            _t.Check("status says manual use", ProductionStatus.Get(StationAdapterRegistry.IdOf(_smelter)) == "$haugbui_job_manual_use",
                ProductionStatus.Get(StationAdapterRegistry.IdOf(_smelter)));
            _t.Check("the Haugbui never removes queued items", _smelter.m_nview.GetZDO().GetString("item0") == "TinOre");
            // The queue burns a little fuel even at 1000 s per bar, so "topped up" is "well above empty".
            _t.Check("manual input still gets fuel", _smelter.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel) > 1f,
                $"fuel {_smelter.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel):0.0}");

            _smelter.m_secPerProduct = 1f;
            yield return SelfTestContext.WaitFor(() => Job().Supplied > supplied, 40f);
            _t.Check("job resumes once the manual input is done", Job().Supplied > supplied);
            SetJob(false, JobMode.Infinite, 0);
            yield return SelfTestContext.WaitFor(() => Queued() == 0 && _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty && !Loose(Bar), 40f);
            _t.Check("station empty after manual use", Queued() == 0, $"queue {Queued()}");
            // The tin bar is the player's, not the job's: it stays at the output point.
            foreach (ItemDrop tin in ItemDrop.s_instances.Where(d => d != null && ItemBridge.PrefabName(d.m_itemData) == "Tin").ToList())
                if (Vector3.Distance(tin.transform.position, _smelter.transform.position) < 5f) _t.Track(tin.gameObject);
        }

        private IEnumerator DeliveryReservation()
        {
            HaugbuiStructure deliveryPiece = _t.PlaceStructure(StructureKind.DeliveryChest, Ground(new Vector3(6, 0, -4)));
            DeliveryChest delivery = deliveryPiece != null ? deliveryPiece.GetComponent<DeliveryChest>() : null;
            if (delivery == null)
            {
                _t.Fail("delivery chest placed", "not placed");
                yield break;
            }
            yield return SelfTestContext.WaitFrames(2);
            int all = Count(_oreChest, Ore);
            delivery.Edit(l => l.Request(Ore, all) == RequestEditResult.Ok);
            int supplied = Job().Supplied;
            SetJob(true, JobMode.Infinite, 0);
            // The ordinary chest keeps one ore, so the line stays open on that last item and the smelter never sees it.
            yield return SelfTestContext.WaitFor(() => delivery.Ledger.Remaining(Ore) <= 1 && _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty, 45f);
            yield return new WaitForSeconds(3f);
            _t.Check("ore requested by a Delivery Chest is delivered, not smelted",
                delivery.Ledger.Remaining(Ore) == 1 && Job().Supplied == supplied,
                $"remaining {delivery.Ledger.Remaining(Ore)}, smelted {Job().Supplied - supplied}");
            SetJob(false, JobMode.Infinite, 0);
        }

        private IEnumerator Ownership()
        {
            Add(_oreChest, Ore, 4);
            yield return SelfTestContext.WaitFor(() => Queued() == 0 && _tomb.Worker.Plan == null, 30f);
            double now = TimeSource.Current.Now;
            StationJobs.Edit(_smelter, job =>
            {
                job.Enabled = true;
                job.Mode = JobMode.Infinite;
                job.TombGuid = "another-tomb";
                job.OwnerSeen = now;
                return true;
            });
            int supplied = Job().Supplied;
            yield return new WaitForSeconds(4f);
            _t.Check("a job owned by another tomb is ignored", Job().Supplied == supplied && Job().TombGuid == "another-tomb", $"owner {Job().TombGuid}");

            double takeover = HaugbuiPlugin.Instance.Settings.JobTakeoverMinutes.Value * 60.0;
            StationJobs.Edit(_smelter, job =>
            {
                job.OwnerSeen = now - takeover - 1;
                return true;
            });
            yield return SelfTestContext.WaitFor(() => Job().TombGuid == _tomb.Guid && Job().Supplied > supplied, 30f);
            _t.Check("takeover once the owner has not seen it for JobTakeoverMinutes", Job().TombGuid == _tomb.Guid && Job().Supplied > supplied,
                $"owner {Job().TombGuid}, supplied +{Job().Supplied - supplied}");
        }

        private IEnumerator ToggleOff()
        {
            _tomb.SetToggle(TombToggles.Production, false, out _);
            yield return SelfTestContext.WaitFor(() => _tomb.Worker.Plan == null, 20f);
            Add(_oreChest, Ore, 3);
            int supplied = Job().Supplied;
            yield return new WaitForSeconds(5f);
            _t.Check("Production toggle OFF stops all production tasks", Job().Supplied == supplied);
            _t.Check("panel says production is off", ProductionPanel.StatusText(_smelter, Job()) == "$haugbui_job_toggle_off");
            SetJob(false, JobMode.Infinite, 0);
            _tomb.SetToggle(TombToggles.Production, true, out _);
            yield return SelfTestContext.WaitFor(() => Queued() == 0 && _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty && !Loose(Bar), 40f);
            _t.Check("products of a disabled job are still collected", Queued() == 0 && !Loose(Bar) && Job().Produced == Job().Supplied,
                $"queue {Queued()}, loose bars {Loose(Bar)}, supplied {Job().Supplied}, produced {Job().Produced}");
        }

        private IEnumerator GroundSupply()
        {
            SetJob(false, JobMode.Finite, 0);
            GameObject kilnGo = _t.PlaceVanilla("charcoal_kiln", Ground(new Vector3(6, 0, -6)), Quaternion.identity);
            Smelter kiln = kilnGo != null ? kilnGo.GetComponentInChildren<Smelter>() : null;
            StationRecipe wood = kiln != null ? new SmelterAdapter().Recipes(kiln).FirstOrDefault(r => r.Input == "Wood") : null;
            if (kiln == null || wood == null)
            {
                _t.Skip("ground supply", kiln == null ? "charcoal kiln was not placed" : "the kiln has no wood recipe");
                yield break;
            }
            StationJobs.Edit(kiln, job =>
            {
                ProductionJob.Edit(job, true, wood.Input, wood.Output, JobMode.Finite, 2);
                return true;
            });
            ItemDrop pile = Drop("Wood", 4, new Vector3(5, 0, -5));
            if (!_t.Check("wood for the kiln is on the ground", pile != null)) yield break;
            _tomb.SetToggle(TombToggles.GroundItems, false, out _);
            yield return new WaitForSeconds(8f);
            int queuedOff = new SmelterAdapter().Read(kiln).Queued;
            _t.Check("toggle off: wood only on the ground is not loaded", Live(pile) && queuedOff == 0, $"queue {queuedOff}");

            Container woodChest = Chest(new Vector3(7, 0, -4));
            Add(woodChest, "Wood", 4);
            int chestBefore = Count(woodChest, "Wood");
            _tomb.SetToggle(TombToggles.GroundItems, true, out _);
            bool dropFirst = false;
            float end = Time.time + 90f;
            while (Time.time < end && Live(pile))
            {
                TaskPlan plan = _tomb.Worker.Plan;
                if (plan != null && plan.Kind == TaskKinds.ProductionFeed)
                {
                    string id = DropEndpoint.IdOf(pile);
                    int pick = -1, take = -1;
                    for (int i = 0; i < plan.Steps.Count; i++)
                    {
                        if (pick < 0 && plan.Steps[i].Kind == StepKind.PickUp && plan.Steps[i].Target == id) pick = i;
                        if (take < 0 && plan.Steps[i].Kind == StepKind.Take) take = i;
                    }
                    if (pick >= 0 && (take < 0 || pick < take)) dropFirst = true;
                }
                yield return null;
            }
            _t.Check("toggle on: the ground wood is taken", !Live(pile), "the drop is still there");
            _t.Check("the drop is used before the chest", !Live(pile) && Count(woodChest, "Wood") == chestBefore,
                $"drop first {dropFirst}, chest {Count(woodChest, "Wood")}/{chestBefore}");

            yield return SelfTestContext.WaitFor(() => _tomb.Worker.Plan == null, 20f);
            StationJobs.Edit(kiln, job =>
            {
                ProductionJob.Edit(job, true, wood.Input, wood.Output, JobMode.Infinite, 0);
                return true;
            });
            var stone = new ItemStack { Prefab = "Stone", Amount = 1, WorldLevel = Game.m_worldLevel };
            ItemBridge.RefreshShared(stone);
            float gap = _tomb.Carried.Capacity * CarryCycle.UnloadFraction - _tomb.Carried.Weight;
            if (stone.UnitWeight > 0f && gap > 0f)
                stone.Amount = Mathf.CeilToInt((gap + 0.05f) / stone.UnitWeight);
            _tomb.Carried.Add(stone);
            if (_t.Check("bag is full enough to unload", CarryCycle.ShouldUnload(_tomb.Carried.Weight, _tomb.Carried.Capacity),
                    $"{_tomb.Carried.Weight:0.00}/{_tomb.Carried.Capacity:0.00}"))
            {
                int woodLeft = Count(woodChest, "Wood");
                bool fetched = false;
                float watch = Time.time + 8f;
                while (Time.time < watch)
                {
                    TaskPlan plan = _tomb.Worker.Plan;
                    if (plan != null && plan.Kind == TaskKinds.ProductionFeed)
                        fetched |= plan.Steps.Any(s => s.Kind == StepKind.Take || s.Kind == StepKind.PickUp);
                    yield return null;
                }
                _t.Check("a full bag does not fetch more for the kiln", !fetched && Count(woodChest, "Wood") == woodLeft,
                    $"fetched {fetched}, chest {Count(woodChest, "Wood")}/{woodLeft}, weight {_tomb.Carried.Weight:0}");
            }
            _tomb.Carried.Remove(stone, _tomb.Carried.CountPrefab("Stone"));
            RemovePrefab(woodChest, "Stone");
            RemovePrefab(_tomb.GetComponent<Container>(), "Stone");

            StationJobs.Edit(kiln, job =>
            {
                job.Enabled = false;
                return true;
            });
            _tomb.SetToggle(TombToggles.GroundItems, false, out _);
        }

        private ItemDrop Drop(string prefab, int amount, Vector3 offset)
        {
            GameObject item = ObjectDB.instance.GetItemPrefab(prefab);
            if (item == null) return null;
            ItemDrop.ItemData data = item.GetComponent<ItemDrop>().m_itemData.Clone();
            data.m_dropPrefab = item;
            data.m_worldLevel = (byte)Game.m_worldLevel;
            ItemDrop drop = ItemDrop.DropItem(data, amount, Ground(offset) + Vector3.up, Quaternion.identity);
            drop.m_nview.GetZDO().Set(ZDOVars.s_spawnTime, ZNet.instance.GetTime().AddSeconds(-60).Ticks);
            _t.Track(drop.gameObject);
            return drop;
        }

        private static bool Live(Component component) =>
            component != null && component.GetComponent<ZNetView>() != null && component.GetComponent<ZNetView>().IsValid();

        private IEnumerator SpinningWheelAndWindmill()
        {
            GameObject wheelGo = _t.PlaceVanilla("piece_spinningwheel", Ground(new Vector3(-6, 0, -6)), Quaternion.identity);
            Smelter wheel = wheelGo != null ? wheelGo.GetComponentInChildren<Smelter>() : null;
            if (wheel == null)
            {
                _t.Skip("unroofed spinning wheel reports 'needs roof'", "prefab not placed");
                yield break;
            }
            yield return new WaitForSeconds(2f);
            string blocked = StationAdapterRegistry.For(wheel).Read(wheel).Blocked;
            _t.Check("unroofed spinning wheel reports 'needs roof'", blocked == "$haugbui_station_needs_roof", blocked ?? "none");
        }

        /// <summary>Products left by an earlier, interrupted run would otherwise be collected as this run's outputs.</summary>
        private void RemoveLooseProducts()
        {
            foreach (ItemDrop drop in ItemDrop.s_instances.ToList())
            {
                if (drop == null || drop.m_nview == null || !drop.m_nview.IsValid() || !drop.m_nview.IsOwner()) continue;
                string prefab = ItemBridge.PrefabName(drop.m_itemData);
                if ((prefab == Bar || prefab == "Tin") && Vector3.Distance(drop.transform.position, _arena) < 20f) drop.m_nview.Destroy();
            }
        }

        private void Sample()
        {
            if (_smelter == null) return;
            ZDO zdo = _smelter.m_nview.GetZDO();
            _maxQueueSeen = Mathf.Max(_maxQueueSeen, zdo.GetInt(ZDOVars.s_queued));
            _maxFuelSeen = Mathf.Max(_maxFuelSeen, zdo.GetFloat(ZDOVars.s_fuel));
        }

        private void SetJob(bool enabled, JobMode mode, int target) =>
            StationJobs.Edit(_smelter, job =>
            {
                ProductionJob.Edit(job, enabled, Ore, Bar, mode, target);
                return true;
            });

        private StationJobState Job() => (_smelter != null ? StationJobs.Read(_smelter) : null) ?? new StationJobState();

        private int Supplied() => Job().Supplied;

        private int Queued() => _smelter != null && _smelter.m_nview.IsValid() ? _smelter.m_nview.GetZDO().GetInt(ZDOVars.s_queued) : 0;

        private bool Loose(string prefab) =>
            ItemDrop.s_instances.Any(d => d != null && ItemBridge.PrefabName(d.m_itemData) == prefab
                                          && Vector3.Distance(d.transform.position, _smelter.transform.position) < 5f);

        private void Teach(string prefab)
        {
            Player player = _t.Player;
            string name = ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
            if (player == null || name == null || player.IsMaterialKnown(name)) return;
            player.m_knownMaterial.Add(name);
            _taught.Add(name);
        }

        private Container Chest(Vector3 offset)
        {
            GameObject go = _t.PlaceVanilla("piece_chest_wood", Ground(offset), Quaternion.identity);
            return go != null ? go.GetComponentInChildren<Container>() : null;
        }

        private static void Add(Container container, string prefab, int amount, bool cheated = false)
        {
            var stack = new ItemStack { Prefab = prefab, Amount = amount, WorldLevel = Game.m_worldLevel, Cheated = cheated };
            ItemBridge.RefreshShared(stack);
            ItemBridge.AddTo(container.GetInventory(), stack);
        }

        private static int Count(Container container, string prefab) =>
            container == null ? 0 : ItemBridge.CountPrefab(container.GetInventory(), prefab);

        private static void RemovePrefab(Container container, string prefab)
        {
            if (container == null) return;
            Inventory inventory = container.GetInventory();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems().Where(i => ItemBridge.PrefabName(i) == prefab).ToList())
                inventory.RemoveItem(item);
        }

        private static int Count(Container container, string prefab, bool cheated) =>
            container == null ? 0 : container.GetInventory().GetAllItems().Where(i => ItemBridge.PrefabName(i) == prefab && i.m_cheated == cheated).Sum(i => i.m_stack);

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
