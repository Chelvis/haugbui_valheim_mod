#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Brain;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Core.Storage;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Registry;
using Haugbui.Structures;
using UnityEngine;
using Reservations = Haugbui.Coordination.Reservations;

namespace Haugbui.SelfTest
{
    /// <summary>M4: registry, access policy, transfers, storage sorting with retry, reservations, loaded area.</summary>
    internal sealed class StorageSuite
    {
        private const long Stranger = 424242;
        private static readonly string[] Prefabs = { "Wood", "Stone", "Resin" };

        private readonly SelfTestContext _t;
        private readonly FakeTime _time = new FakeTime();
        private readonly List<Container> _chests = new List<Container>();
        private Vector3 _arena;
        private TombController _tomb;
        private long _owner;

        public StorageSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Storage";
            TimeSource.Override = _time;
            try
            {
                yield return Setup();
                if (_tomb == null) yield break;
                Registry();
                yield return Access();
                yield return Transfers();
                yield return VanillaStacking();
                yield return Sorting();
                yield return TwoTombs();
                LoadedAreaChecks();
            }
            finally
            {
                TimeSource.Override = null;
            }
        }

        private IEnumerator Setup()
        {
            Player player = _t.Player;
            _owner = player.GetPlayerID();
            _arena = SelfTestContext.FindDryArena(player.transform.position + player.transform.right * 12f + player.transform.forward * 4f, 9f);
            GameObject bed = _t.SpawnVanilla("bed", SelfTestContext.OnGround(_arena + new Vector3(-8, 0, -4)), Quaternion.identity);
            if (bed == null)
            {
                _t.Fail("spawn test bed", "prefab 'bed' not found");
                yield break;
            }
            yield return null;
            bed.GetComponent<Bed>().SetOwner(_owner, player.GetPlayerName());
            _time.Fraction = FakeTime.Noon;

            HaugbuiStructure tomb = _t.PlaceStructure(StructureKind.Tomb, _arena);
            if (!_t.Check("tomb placed", tomb != null)) yield break;
            yield return SelfTestContext.WaitFrames(3);
            _tomb = tomb.GetComponent<TombController>();
            _t.Check("tomb has a worker", _tomb != null && _tomb.Worker != null);
            if (_tomb == null) yield break;
            // World drops near the player are not part of the counts; only the harvest suite picks them up.
            _tomb.SetToggle(TombToggles.GroundItems, false, out _);
            _tomb.SetToggle(TombToggles.Harvesting, false, out _);
        }

        private void Registry()
        {
            string report = WorldRegistry.Verify(out bool ok);
            _t.Check("registry matches a scene scan", ok, report);

            var indexed = new List<Container>();
            WorldRegistry.QueryContainers(_arena, 40f, indexed);
            var expected = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                .Where(c => WorldRegistry.IsIndexable(c) && Vector3.Distance(c.transform.position, _arena) <= 40f).ToList();
            _t.Check("spatial query matches a brute-force scan", indexed.Count == expected.Count && expected.All(indexed.Contains),
                $"{indexed.Count} indexed, {expected.Count} in scene within 40 m");
            _t.Check("tomb storage is registered", WorldRegistry.Containers.Contains(_tomb.GetComponent<Container>()));
        }

        private IEnumerator Access()
        {
            IAccessPolicy policy = SinglePlayerAccessPolicy.Instance;
            var fixtures = new List<GameObject>();
            Container own = Chest(new Vector3(4, 0, 3), ("Wood", 1));
            Container ownPrivate = Container(_t.PlaceVanilla("piece_chest_private", Ground(new Vector3(6, 0, -2)), Quaternion.identity));
            Container strangerPrivate = Container(_t.SpawnVanilla("piece_chest_private", Ground(new Vector3(8, 0, -2)), Quaternion.identity));
            Container dungeon = Container(_t.SpawnVanilla("piece_chest_wood", Ground(new Vector3(10, 0, -2)), Quaternion.identity));
            if (strangerPrivate != null)
            {
                strangerPrivate.m_piece.m_creator = Stranger;
                strangerPrivate.m_nview.GetZDO().Set(ZDOVars.s_creator, Stranger);
            }
            fixtures.AddRange(new Component[] { own, ownPrivate, strangerPrivate, dungeon }.Where(c => c != null).Select(c => c.gameObject));
            yield return null;

            Verdict("own chest allowed", policy, own, ContainerVerdict.Allowed);
            Verdict("own private chest allowed", policy, ownPrivate, ContainerVerdict.Allowed);
            Verdict("another creator's private chest ignored", policy, strangerPrivate, ContainerVerdict.Private);
            Verdict("non-player-built chest ignored", policy, dungeon, ContainerVerdict.NotPlayerBuilt);
            Verdict("tomb storage never a destination", policy, _tomb.GetComponent<Container>(), ContainerVerdict.HaugbuiRole);

            foreach ((string prefab, ContainerVerdict expected) in new[]
                     {
                         ("Player_tombstone", ContainerVerdict.PlayerTombstone),
                         ("Cart", ContainerVerdict.Vehicle),
                         ("incinerator", ContainerVerdict.Obliterator),
                         ("CargoCrate", ContainerVerdict.AutoDestroyEmpty),
                     })
            {
                GameObject go = _t.SpawnVanilla(prefab, Ground(new Vector3(-6, 0, 6)) + Vector3.up * 0.2f, Quaternion.identity);
                Container container = go != null ? go.GetComponentInChildren<Container>() : null;
                if (container == null)
                {
                    _t.Skip($"excluded kind {prefab}", "prefab or container not found");
                    if (go != null) ZNetScene.instance.Destroy(go);
                    continue;
                }
                Verdict($"excluded kind {prefab}", policy, container, expected);
                ZNetScene.instance.Destroy(go);
                yield return null;
            }

            GameObject wardObject = _t.SpawnVanilla("guard_stone", Ground(new Vector3(4, 0, 5)), Quaternion.identity);
            PrivateArea ward = wardObject != null ? wardObject.GetComponent<PrivateArea>() : null;
            if (ward == null || own == null)
            {
                _t.Skip("ward checks", "prefab 'guard_stone' not found");
            }
            else
            {
                yield return null;
                ward.SetEnabled(true);
                Verdict("chest inside another creator's ward ignored", policy, own, ContainerVerdict.Private);
                ward.AddPermitted(_owner, _t.Player.GetPlayerName());
                Verdict("chest inside a ward that permits the owner allowed", policy, own, ContainerVerdict.Allowed);
                fixtures.Add(wardObject);
            }

            foreach (GameObject go in fixtures.Where(g => g != null)) ZNetScene.instance.Destroy(go);
            yield return SelfTestContext.WaitFrames(2);
        }

        private IEnumerator Transfers()
        {
            Container a = Chest(new Vector3(4, 0, -4), ("Wood", 1));
            Container b = Chest(new Vector3(-4, 0, -4), ("Stone", 1));
            Container tombContainer = _tomb.GetComponent<Container>();
            Inventory storage = tombContainer.GetInventory();
            if (!_t.Check("transfer chests placed", a != null && b != null)) yield break;
            yield return null;
            _tomb.TickNow();
            var carried = new CarriedEndpoint("carried:test", _tomb.Carried);
            var tomb = new ContainerEndpoint(tombContainer);

            ItemDrop.ItemData cheated = ObjectDB.instance.GetItemPrefab("Wood").GetComponent<ItemDrop>().m_itemData.Clone();
            cheated.m_dropPrefab = ObjectDB.instance.GetItemPrefab("Wood");
            cheated.m_stack = 5;
            cheated.m_cheated = true;
            cheated.m_worldLevel = (byte)Game.m_worldLevel;
            cheated.m_customData["haugbui_selftest"] = "kept";
            storage.AddItem(cheated);
            ItemStack cheatedStack = ItemBridge.FromItemData(storage.GetAllItems().First(i => i.m_cheated));
            Reservations.Transfers.Move(tomb, carried, cheatedStack, 5);
            _t.Check("cheated flag kept in the carried inventory", _tomb.Carried.Stacks.Any(s => s.Prefab == "Wood" && s.Cheated && s.Amount == 5),
                string.Join(", ", _tomb.Carried.Stacks));

            Add(tombContainer, "Wood", 10);
            TransferRecord back = Reservations.Transfers.Move(carried, tomb, cheatedStack, 5);
            List<ItemDrop.ItemData> wood = storage.GetAllItems().Where(i => ItemBridge.PrefabName(i) == "Wood").ToList();
            string woodText = string.Join(", ", wood.Select(i => $"{i.m_stack}{(i.m_cheated ? " cheated" : "")}"));
            _t.Check("cheated stack placed next to a clean one, never merged", back.Moved == 5 && wood.Count == 2
                && wood.Any(i => !i.m_cheated && i.m_stack == 10) && wood.Any(i => i.m_cheated && i.m_stack == 5), woodText);
            _t.Check("custom data kept through a transfer",
                wood.Any(i => i.m_cheated && i.m_customData.TryGetValue("haugbui_selftest", out string tag) && tag == "kept"));
            _t.Check("totals conserved next to a cheated stack", ItemBridge.CountPrefab(storage, "Wood") == 15 && _tomb.Carried.CountPrefab("Wood") == 0,
                $"storage {ItemBridge.CountPrefab(storage, "Wood")}, carried {_tomb.Carried.CountPrefab("Wood")}");
            Reservations.Transfers.Move(tomb, carried, Stack("Wood", 0), 15);
            _t.Check("taking clean wood never takes the cheated stack", _tomb.Carried.Stacks.All(s => !s.Cheated) && _tomb.Carried.CountPrefab("Wood") == 10
                && storage.GetAllItems().Count(i => i.m_cheated) == 1, string.Join(", ", _tomb.Carried.Stacks));
            _tomb.Carried.TakeAll();
            storage.RemoveAll();

            storage.AddItem(ObjectDB.instance.GetItemPrefab("Wood"), 10);
            a.m_inUse = true;
            TransferRecord inUse = Reservations.Transfers.Move(tomb, new ContainerEndpoint(a), Stack("Wood", 0), 10);
            a.m_inUse = false;
            _t.Check("container in use never modified", inUse.Status == TransferStatus.DestinationUnavailable && Count(a, "Wood") == 1,
                inUse.ToString());

            foreach ((string prefab, int amount) in new[] { ("Wood", 40), ("Stone", 30), ("Resin", 20) })
                Add(tombContainer, prefab, amount);
            var endpoints = new List<IItemEndpoint> { tomb, new ContainerEndpoint(a), new ContainerEndpoint(b), carried };
            Dictionary<string, int> before = WithCarried(Totals(new[] { tombContainer, a, b }));
            int duplicates = 0, moves = 0;
            Action<TransferRecord> watch = r => { if (!r.Conserved) duplicates++; };
            Reservations.Transfers.Recorded += watch;
            float end = Time.time + 3f, nextSave = Time.time + 1f;
            while (Time.time < end)
            {
                for (int i = 0; i < 5; i++)
                {
                    IItemEndpoint source = endpoints[UnityEngine.Random.Range(0, endpoints.Count)];
                    IItemEndpoint destination = endpoints[UnityEngine.Random.Range(0, endpoints.Count)];
                    if (source == destination) continue;
                    ItemStack item = Stack(Prefabs[UnityEngine.Random.Range(0, Prefabs.Length)], 0);
                    if (Reservations.Transfers.Move(source, destination, item, UnityEngine.Random.Range(1, 40)).Moved > 0) moves++;
                }
                if (Time.time >= nextSave)
                {
                    nextSave = Time.time + 1f;
                    ZNet.instance.Save(false);
                }
                yield return null;
            }
            Reservations.Transfers.Recorded -= watch;
            Dictionary<string, int> after = WithCarried(Totals(new[] { tombContainer, a, b }));
            _t.Check("random transfers conserve totals", Same(before, after) && duplicates == 0 && moves > 50,
                $"{moves} moves, {duplicates} duplicates, {Format(before)} → {Format(after)}");

            GameObject reloadedA = null, reloadedB = null;
            yield return _t.Reload(a.gameObject, go => reloadedA = go);
            yield return _t.Reload(b.gameObject, go => reloadedB = go);
            a = Container(reloadedA);
            b = Container(reloadedB);
            _t.Check("totals unchanged after reload", a != null && b != null && Same(before, WithCarried(Totals(new[] { tombContainer, a, b }))),
                a != null && b != null ? Format(WithCarried(Totals(new[] { tombContainer, a, b }))) : "chest missing");

            _tomb.Carried.TakeAll();
            storage.RemoveAll();
            if (a != null) ZNetScene.instance.Destroy(a.gameObject);
            if (b != null) ZNetScene.instance.Destroy(b.gameObject);
            yield return SelfTestContext.WaitFrames(2);
        }

        private IEnumerator Sorting()
        {
            Container tombContainer = _tomb.GetComponent<Container>();
            tombContainer.GetInventory().RemoveAll();
            _tomb.Carried.TakeAll();
            Container wood = Chest(new Vector3(5, 0, 4), ("Wood", 1));
            Container stone = Chest(new Vector3(-5, 0, 4), ("Stone", 1));
            Container empty = Chest(new Vector3(0, 0, 7));
            if (!_t.Check("sorting chests placed", wood != null && stone != null && empty != null)) yield break;
            Add(tombContainer, "Wood", 30);
            Add(tombContainer, "Stone", 20);
            Add(tombContainer, "Resin", 10);
            Dictionary<string, int> expected = WorldTotals();
            yield return null;

            yield return SetTime(FakeTime.Night);
            _t.Check("emerges to work", _tomb.Creature != null, _tomb.Machine.State.ToString());
            yield return SelfTestContext.WaitFor(() => Count(tombContainer, "Wood") == 0 && Count(tombContainer, "Stone") == 0
                                                     && _tomb.Carried.Stacks.All(s => s.Prefab == "Resin") && _tomb.Worker.Plan == null, 90f);

            _t.Check("tomb storage sorted first after emerging", Count(tombContainer, "Wood") == 0 && Count(tombContainer, "Stone") == 0,
                $"left in tomb storage: wood {Count(tombContainer, "Wood")}, stone {Count(tombContainer, "Stone")}; carried {string.Join(", ", _tomb.Carried.Stacks)}");
            _t.Check("wood went only to the chest that had wood", Count(wood, "Wood") == 31 && Count(stone, "Wood") == 0, $"{Count(wood, "Wood")}/31");
            _t.Check("stone went only to the chest that had stone", Count(stone, "Stone") == 21 && Count(wood, "Stone") == 0, $"{Count(stone, "Stone")}/21");
            _t.Check("empty chest never receives items", empty.GetInventory().NrOfItems() == 0, empty.GetInventory().NrOfItems().ToString());
            _t.Check("item without destination stays in tomb storage", Count(tombContainer, "Resin") == 10 && _tomb.Carried.Count(Stack("Resin", 0)) == 0,
                $"tomb {Count(tombContainer, "Resin")}, carried {_tomb.Carried.Count(Stack("Resin", 0))}");
            _t.Check("no destination: waits instead of retrying every frame", _tomb.Worker.NoDestination.Keys.Contains(Stack("Resin", 0).KindKey)
                                                                             && _tomb.Worker.Takes <= 6,
                $"{_tomb.Worker.Takes} takes, waiting: {string.Join(", ", _tomb.Worker.NoDestination.Keys)}");
            _t.Check("totals conserved after sorting", Same(expected, WorldTotals()), Format(WorldTotals()));

            Add(empty, "Resin", 1);
            expected = WorldTotals();
            _time.Offset += 61;
            yield return SelfTestContext.WaitFor(() => Count(empty, "Resin") == 11 && _tomb.Worker.Plan == null, 45f);
            _t.Check("retried once a matching chest appeared", Count(empty, "Resin") == 11 && Count(tombContainer, "Resin") == 0,
                $"chest {Count(empty, "Resin")}/11");

            Add(tombContainer, "Wood", 20);
            expected = WorldTotals();
            yield return SelfTestContext.WaitFor(() => _tomb.Worker.Plan?.Kind == TaskKinds.Deposit && _tomb.Machine.State == HaugbuiState.Traveling, 30f);
            if (_tomb.Worker.Plan?.Kind != TaskKinds.Deposit)
            {
                _t.Fail("target removed mid-task", "the Haugbui never started a deposit to remove the chest under");
            }
            else
            {
                int carriedWood = _tomb.Carried.Count(Stack("Wood", 0));
                int aborts = _tomb.Worker.Aborts;
                expected["Wood"] -= Count(wood, "Wood");
                ZNetScene.instance.Destroy(wood.gameObject);
                _chests.Remove(wood);
                yield return SelfTestContext.WaitFor(() => _tomb.Worker.Aborts > aborts, 3f);
                _t.Check("task stops when its target disappears", _tomb.Worker.Aborts > aborts, _tomb.Worker.LastAbort ?? "still running");
                _t.Check("carried items kept when the target disappears", _tomb.Carried.Count(Stack("Wood", 0)) == carriedWood,
                    $"{_tomb.Carried.Count(Stack("Wood", 0))}/{carriedWood}");
            }

            yield return SetTime(FakeTime.Noon);
            yield return SelfTestContext.WaitFor(() => _tomb.Machine.State == HaugbuiState.Dormant, 30f);
            _t.Check("dawn: kept items back in tomb storage", _tomb.Carried.IsEmpty, string.Join(", ", _tomb.Carried.Stacks));
            _t.Check("totals conserved at the end", Same(expected, WorldTotals()), $"{Format(expected)} → {Format(WorldTotals())}");
        }

        private IEnumerator TwoTombs()
        {
            HaugbuiStructure second = _t.PlaceStructure(StructureKind.Tomb, Ground(new Vector3(0, 0, -7)));
            if (!_t.Check("second tomb placed", second != null)) yield break;
            yield return SelfTestContext.WaitFrames(3);
            TombController other = second.GetComponent<TombController>();
            Container stone = _chests.FirstOrDefault(c => c != null && Count(c, "Stone") > 0);
            if (other == null || stone == null)
            {
                _t.Fail("two tombs setup", "missing controller or stone chest");
                yield break;
            }
            Container mine = _tomb.GetComponent<Container>(), theirs = other.GetComponent<Container>();
            Add(mine, "Stone", 10);
            Add(theirs, "Stone", 10);
            int stoneBefore = Count(stone, "Stone");
            string mineId = ContainerEndpoint.IdOf(mine), theirsId = ContainerEndpoint.IdOf(theirs);
            bool crossed = false, shared = false;

            _time.Fraction = FakeTime.Night;
            _tomb.TickNow();
            other.TickNow();
            float end = Time.time + 60f;
            while (Time.time < end && (Count(mine, "Stone") > 0 || Count(theirs, "Stone") > 0 || !_tomb.Carried.IsEmpty && _tomb.Carried.Stacks.Any(s => s.Prefab == "Stone")
                                       || other.Carried.Stacks.Any(s => s.Prefab == "Stone")))
            {
                crossed |= _tomb.Worker.Plan?.Steps.Any(s => s.Target == theirsId) == true || other.Worker.Plan?.Steps.Any(s => s.Target == mineId) == true;
                shared |= Reservations.Table.Leases.GroupBy(l => l.Key).Any(g => g.Select(l => l.Holder).Distinct().Count() > 1);
                yield return null;
            }
            _t.Check("two Haugbuis sort at the same time", Count(stone, "Stone") == stoneBefore + 20, $"{Count(stone, "Stone") - stoneBefore}/20 stored");
            _t.Check("each Haugbui takes only from its own tomb storage", !crossed);
            _t.Check("no key reserved by two Haugbuis", !shared);

            Add(theirs, "Stone", 10);
            yield return SelfTestContext.WaitFor(() => other.Worker.Plan != null, 10f);
            bool hadTask = other.Worker.Plan != null;
            if (other.Creature != null)
            {
                var hit = new HitData();
                hit.m_damage.m_damage = 100000f;
                other.Creature.Character.Damage(hit);
            }
            yield return SelfTestContext.WaitFor(() => other.Machine.State == HaugbuiState.Dead, 5f);
            _t.Check("leases and task released when a Haugbui dies mid-task",
                hadTask && other.Machine.State == HaugbuiState.Dead && other.Worker.Plan == null
                && Reservations.Table.Leases.All(l => l.Holder != other.Guid),
                $"had task {hadTask}, state {other.Machine.State}, leases {Reservations.Table.Leases.Count(l => l.Holder == other.Guid)}");

            yield return SetTime(FakeTime.Noon);
            yield return SelfTestContext.WaitFor(() => _tomb.Machine.State == HaugbuiState.Dormant, 30f);
        }

        /// <summary>How much of the 50 m disc is loaded depends on where the player stands and the simulation distance.</summary>
        private void LoadedAreaChecks()
        {
            float spot = LoadedArea.Coverage(_tomb.transform.position, 4f, 2f);
            _t.Check("tomb spot reported loaded", spot >= 0.999f, $"{spot:P0}");
            float far = LoadedArea.Coverage(_arena + new Vector3(5000, 0, 5000), 30f, 8f);
            _t.Check("far area reported unloaded", far == 0f, $"{far:P0}");
            float huge = LoadedArea.Coverage(_arena, 600f, 25f);
            _t.Check("huge radius reported partially loaded", huge > 0f && huge < 0.999f, $"{huge:P0}");
            _tomb.Worker.RefreshCoverage();
            float coverage = _tomb.Worker.Coverage;
            _t.Check("panel hint shown exactly when the radius is partly unloaded",
                _tomb.Worker.PartiallyLoaded == coverage < 0.999f, $"radius {_tomb.Attributes.Radius:0} m {coverage:P0} loaded, hint {_tomb.Worker.PartiallyLoaded}");
        }

        private void Verdict(string name, IAccessPolicy policy, Container container, ContainerVerdict expected)
        {
            if (container == null)
            {
                _t.Skip(name, "fixture not created");
                return;
            }
            ContainerVerdict actual = policy.CheckContainer(container, _owner, ContainerUse.StorageDestination);
            _t.Check(name, actual == expected, actual.ToString());
        }

        private IEnumerator SetTime(float fraction)
        {
            _time.Fraction = fraction;
            _tomb.TickNow();
            yield return SelfTestContext.WaitFrames(2);
        }

        private Vector3 Ground(Vector3 offset) => SelfTestContext.OnGround(_arena + offset);

        /// <summary>HB-T-109: StrictItemData OFF stacks like vanilla but never clears a cheated flag; totals conserved.</summary>
        private IEnumerator VanillaStacking()
        {
            _t.Check("StrictItemData is ON by default", (bool)HaugbuiPlugin.Instance.Settings.StrictItemData.DefaultValue);
            Container c = Chest(new Vector3(4, 0, -4), ("Wood", 10));
            Container tombContainer = _tomb.GetComponent<Container>();
            Inventory storage = tombContainer.GetInventory();
            if (!_t.Check("vanilla stacking chest placed", c != null)) yield break;
            yield return null;
            var tomb = new ContainerEndpoint(tombContainer);
            var chest = new ContainerEndpoint(c);
            var carried = new CarriedEndpoint("carried:test", _tomb.Carried);
            StackingRules.StrictItemData = false;
            try
            {
                AddCheated(storage, "Wood", 5);
                ItemStack cheatedWood = ItemBridge.FromItemData(storage.GetAllItems().First(i => i.m_cheated));
                TransferRecord joined = Reservations.Transfers.Move(tomb, chest, cheatedWood, 5);
                List<ItemDrop.ItemData> wood = c.GetInventory().GetAllItems().Where(i => ItemBridge.PrefabName(i) == "Wood").ToList();
                _t.Check("OFF: a cheated item joins the clean stack and makes it cheated, as vanilla",
                    joined.Moved == 5 && joined.Conserved && wood.Count == 1 && wood[0].m_stack == 15 && wood[0].m_cheated, Describe(wood));
                _t.Check("OFF: the stack keeps its own custom data", wood.Count == 1 && !wood[0].m_customData.ContainsKey("haugbui_selftest"));

                Add(tombContainer, "Wood", 5);
                TransferRecord clean = Reservations.Transfers.Move(tomb, chest, Stack("Wood", 0), 5);
                wood = c.GetInventory().GetAllItems().Where(i => ItemBridge.PrefabName(i) == "Wood").ToList();
                _t.Check("OFF: a clean item never clears a cheated stack", clean.Moved == 5 && wood.Count == 1 && wood[0].m_stack == 20 && wood[0].m_cheated,
                    Describe(wood));

                foreach ((string prefab, int amount) in new[] { ("Stone", 30), ("Resin", 20) }) Add(tombContainer, prefab, amount);
                AddCheated(storage, "Stone", 7);
                var endpoints = new List<IItemEndpoint> { tomb, chest, carried };
                Dictionary<string, int> before = WithCarried(Totals(new[] { tombContainer, c }));
                int cheatedLow = Cheated(tombContainer, c), duplicates = 0, cleared = 0, moves = 0;
                float end = Time.time + 2f;
                while (Time.time < end)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        IItemEndpoint source = endpoints[UnityEngine.Random.Range(0, endpoints.Count)];
                        IItemEndpoint destination = endpoints[UnityEngine.Random.Range(0, endpoints.Count)];
                        if (source == destination) continue;
                        ItemStack item = Stack(Prefabs[UnityEngine.Random.Range(0, Prefabs.Length)], 0);
                        item.Cheated = UnityEngine.Random.value < 0.3f;
                        TransferRecord r = Reservations.Transfers.Move(source, destination, item, UnityEngine.Random.Range(1, 40));
                        if (!r.Conserved) duplicates++;
                        if (r.Moved > 0) moves++;
                        int cheatedNow = Cheated(tombContainer, c);
                        if (cheatedNow < cheatedLow) cleared++;
                        cheatedLow = Mathf.Max(cheatedLow, cheatedNow);
                    }
                    yield return null;
                }
                Dictionary<string, int> after = WithCarried(Totals(new[] { tombContainer, c }));
                _t.Check("OFF: random transfers conserve totals", Same(before, after) && duplicates == 0 && moves > 20,
                    $"{moves} moves, {duplicates} duplicates, {Format(before)} → {Format(after)}");
                _t.Check("OFF: no cheated item ever became clean", cleared == 0, $"{cleared} moves lowered the cheated count");
            }
            finally
            {
                StackingRules.StrictItemData = HaugbuiPlugin.Instance.Settings.StrictItemData.Value;
            }
            _tomb.Carried.TakeAll();
            storage.RemoveAll();
            ZNetScene.instance.Destroy(c.gameObject);
            yield return SelfTestContext.WaitFrames(2);
        }

        private int Cheated(params Container[] containers) =>
            containers.SelectMany(x => x.GetInventory().GetAllItems()).Where(i => i.m_cheated).Sum(i => i.m_stack)
            + _tomb.Carried.Stacks.Where(st => st.Cheated).Sum(st => st.Amount);

        private static void AddCheated(Inventory inventory, string prefab, int amount)
        {
            GameObject go = ObjectDB.instance.GetItemPrefab(prefab);
            ItemDrop.ItemData item = go.GetComponent<ItemDrop>().m_itemData.Clone();
            item.m_dropPrefab = go;
            item.m_stack = amount;
            item.m_cheated = true;
            item.m_worldLevel = (byte)Game.m_worldLevel;
            item.m_customData["haugbui_selftest"] = "kept";
            // The test fixture goes into an empty slot so it never joins a clean stack.
            for (int y = 0; y < inventory.GetHeight(); y++)
                for (int x = 0; x < inventory.GetWidth(); x++)
                    if (inventory.GetItemAt(x, y) == null)
                    {
                        inventory.AddItem(item, amount, x, y);
                        return;
                    }
        }

        private static string Describe(IEnumerable<ItemDrop.ItemData> items) =>
            string.Join(", ", items.Select(i => $"{i.m_stack}{(i.m_cheated ? " cheated" : "")}"));

        private Container Chest(Vector3 offset, params (string Prefab, int Amount)[] items)
        {
            Container container = Container(_t.PlaceVanilla("piece_chest_wood", Ground(offset), Quaternion.identity));
            if (container == null) return null;
            foreach ((string prefab, int amount) in items) Add(container, prefab, amount);
            _chests.Add(container);
            return container;
        }

        private static Container Container(GameObject go) => go != null ? go.GetComponentInChildren<Container>() : null;

        private static void Add(Container container, string prefab, int amount)
        {
            GameObject item = ObjectDB.instance.GetItemPrefab(prefab);
            int max = item.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize;
            for (int left = amount; left > 0; left -= max) container.GetInventory().AddItem(item, Mathf.Min(left, max));
        }

        private static int Count(Container container, string prefab) =>
            container == null ? 0 : ItemBridge.Count(container.GetInventory(), Stack(prefab, 0));

        private static ItemStack Stack(string prefab, int amount)
        {
            var stack = new ItemStack { Prefab = prefab, Amount = amount, WorldLevel = Game.m_worldLevel };
            ItemBridge.RefreshShared(stack);
            return stack;
        }

        /// <summary>Every item this suite owns: tomb storage, its chests, carried and overflow.</summary>
        private Dictionary<string, int> WorldTotals()
        {
            Dictionary<string, int> totals = WithCarried(Totals(_chests.Where(c => c != null).Concat(new[] { _tomb.GetComponent<Container>() })));
            foreach (ItemStack s in _tomb.Overflow)
                totals[s.Prefab] = (totals.TryGetValue(s.Prefab, out int n) ? n : 0) + s.Amount;
            return totals;
        }

        private Dictionary<string, int> WithCarried(Dictionary<string, int> totals)
        {
            foreach (ItemStack s in _tomb.Carried.Stacks)
                totals[s.Prefab] = (totals.TryGetValue(s.Prefab, out int n) ? n : 0) + s.Amount;
            return totals;
        }

        private static Dictionary<string, int> Totals(IEnumerable<Container> containers)
        {
            var totals = new Dictionary<string, int>();
            foreach (ItemDrop.ItemData item in containers.Where(c => c != null).SelectMany(c => c.GetInventory().GetAllItems()))
            {
                string name = ItemBridge.PrefabName(item);
                totals[name] = (totals.TryGetValue(name, out int n) ? n : 0) + item.m_stack;
            }
            return totals;
        }

        private static bool Same(Dictionary<string, int> a, Dictionary<string, int> b) =>
            a.Where(p => p.Value != 0).OrderBy(p => p.Key).SequenceEqual(b.Where(p => p.Value != 0).OrderBy(p => p.Key));

        private static string Format(Dictionary<string, int> totals) =>
            string.Join(" ", totals.Where(p => p.Value != 0).OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));
    }
}
#endif
