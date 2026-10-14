#if DEBUG
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
using Haugbui.Structures;
using Haugbui.UI;
using Jotunn.Managers;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>M5: Collection and Delivery Chests, request ledger and editor, collection and delivery tasks.</summary>
    internal sealed class ChestSuite
    {
        private const string Tag = "haugbui_selftest";

        private readonly SelfTestContext _t;
        private readonly FakeTime _time = new FakeTime();
        private readonly List<Container> _chests = new List<Container>();
        private Vector3 _arena;
        private TombController _tomb;
        private Container _collection, _deliveryA, _deliveryB, _wood, _stone;

        public ChestSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Chests";
            TimeSource.Override = _time;
            try
            {
                yield return Setup();
                if (_tomb == null) yield break;
                Structures();
                yield return Editor();
                yield return Collection();
                yield return Delivery();
                yield return Finish();
            }
            finally
            {
                TimeSource.Override = null;
                if (DeliveryPanel.IsOpen) DeliveryPanel.Close();
            }
        }

        private IEnumerator Setup()
        {
            Player player = _t.Player;
            _arena = SelfTestContext.FindDryArena(player.transform.position + player.transform.right * 12f + player.transform.forward * 4f, 9f);
            GameObject bed = _t.SpawnVanilla("bed", SelfTestContext.OnGround(_arena + new Vector3(-8, 0, -4)), Quaternion.identity);
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
            _collection = Place(StructureKind.CollectionChest, new Vector3(4, 0, 4));
            _deliveryA = Place(StructureKind.DeliveryChest, new Vector3(-4, 0, 4));
            _deliveryB = Place(StructureKind.DeliveryChest, new Vector3(-6, 0, 0));
            _wood = Chest(new Vector3(6, 0, -3), ("Wood", 1));
            _stone = Chest(new Vector3(-3, 0, -5), ("Stone", 1));
            yield return SelfTestContext.WaitFrames(3);
            _tomb = tomb.GetComponent<TombController>();
            if (!_t.Check("chests placed", _collection != null && _deliveryA != null && _deliveryB != null && _wood != null && _stone != null))
                _tomb = null;
        }

        private void Structures()
        {
            foreach ((string name, Container chest) in new[] { ("collection", _collection), ("delivery", _deliveryA) })
            {
                Inventory inventory = chest.GetInventory();
                _t.Check($"{name} chest has exactly 10 slots", inventory.GetWidth() * inventory.GetHeight() == 10,
                    $"{inventory.GetWidth()}x{inventory.GetHeight()}");
                _t.Check($"{name} chest saved as the wood chest host", chest.m_nview.GetZDO().GetPrefab() == "piece_chest_wood".GetStableHashCode());
                _t.Check($"{name} chest has its own name", chest.m_name == StructureDefinitions.ContainerName(chest.GetComponent<HaugbuiStructure>().Kind),
                    chest.m_name);
                _t.Check($"{name} chest costs more than a wood chest", Cost(chest.GetComponent<Piece>()) > Cost(ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Piece>()),
                    $"{Cost(chest.GetComponent<Piece>())} items");
                _t.Check($"{name} chest looks different", chest.transform.Find(StructureVisuals.RootName) != null);
            }
            _t.Check("delivery chest has a request ledger", _deliveryA.GetComponent<DeliveryChest>() != null);
            _t.Check("collection chest has no request ledger", _collection.GetComponent<DeliveryChest>() == null);
            string hover = _deliveryA.GetHoverText();
            _t.Check("delivery hover shows the alt-interact hint", hover.Contains(Localization.instance.Localize("$haugbui_delivery_edit")), hover);
            IAccessPolicy policy = SinglePlayerAccessPolicy.Instance;
            long owner = _t.Player.GetPlayerID();
            _t.Check("collection chest is a source, never a destination",
                policy.CheckContainer(_collection, owner, ContainerUse.StorageSource) == ContainerVerdict.Allowed
                && policy.CheckContainer(_collection, owner, ContainerUse.StorageDestination) == ContainerVerdict.HaugbuiRole);
            _t.Check("delivery chest only receives its own requests",
                policy.CheckContainer(_deliveryA, owner, ContainerUse.DeliveryTarget) == ContainerVerdict.Allowed
                && policy.CheckContainer(_deliveryA, owner, ContainerUse.StorageSource) == ContainerVerdict.HaugbuiRole
                && policy.CheckContainer(_deliveryA, owner, ContainerUse.StorageDestination) == ContainerVerdict.HaugbuiRole);
        }

        private IEnumerator Editor()
        {
            DeliveryChest chest = _deliveryA.GetComponent<DeliveryChest>();
            if (GUIManager.IsHeadless() || GUIManager.CustomGUIFront == null)
            {
                _t.Skip("request editor", "no GUI");
                yield break;
            }
            _deliveryA.Interact(_t.Player, false, true);
            yield return null;
            _t.Check("Shift+E opens the request editor", DeliveryPanel.IsOpen && DeliveryPanel.Current == chest);
            _t.Check("equipment is not selectable", DeliveryPanel.KnownItems.All(k => IsTransportable(k.Prefab)),
                string.Join(", ", DeliveryPanel.KnownItems.Where(k => !IsTransportable(k.Prefab)).Select(k => k.Prefab)));
            bool entered = new[] { ("Wood", 100), ("Stone", 200), ("Resin", 50), ("Iron", 30) }
                .All(r => DeliveryPanel.Submit(r.Item1, r.Item2) == RequestEditResult.Ok);
            _t.Check("spec example entered (100 Wood, 200 Stone, 50 Resin, 30 Iron)",
                entered && chest.Ledger.Lines.Select(l => $"{l.ItemPrefab}:{l.Remaining}").SequenceEqual(new[] { "Wood:100", "Stone:200", "Resin:50", "Iron:30" }),
                Lines(chest));
            _t.Check("editing a line sets its amount", DeliveryPanel.Submit("Wood", 120) == RequestEditResult.Ok && chest.Ledger.Remaining("Wood") == 120, Lines(chest));
            _t.Check("invalid amount refused", DeliveryPanel.Submit("Wood", 0) == RequestEditResult.InvalidAmount);
            DeliveryPanel.Close();
            yield return null;
            _t.Check("editor closes", !DeliveryPanel.IsOpen);

            GameObject reloaded = null;
            yield return _t.Reload(_deliveryA.gameObject, go => reloaded = go);
            _deliveryA = reloaded != null ? reloaded.GetComponentInChildren<Container>() : null;
            DeliveryChest back = _deliveryA != null ? _deliveryA.GetComponent<DeliveryChest>() : null;
            _t.Check("requests survive reload", back != null && back.Ledger.Lines.Count == 4 && back.Ledger.Remaining("Wood") == 120,
                back != null ? Lines(back) : "chest missing");
            if (back != null) back.Edit(l =>
            {
                while (l.Lines.Count > 0) l.Cancel(0);
                return true;
            });
            _t.Check("cancelled lines are gone", back != null && back.Ledger.Lines.Count == 0);
            ReplaceChest(_deliveryA);
        }

        private IEnumerator Collection()
        {
            Add(_collection, "Wood", 20);
            Add(_collection, "Stone", 10);
            Add(_collection, "Resin", 5);
            ItemStack cheated = Stack("Wood", 3);
            cheated.Cheated = true;
            cheated.CustomData[Tag] = "kept";
            ItemBridge.AddTo(_collection.GetInventory(), cheated);
            bool axe = AddEquipment(_collection, "AxeStone");
            Dictionary<string, int> expected = WorldTotals();

            yield return SetTime(FakeTime.Night);
            _t.Check("emerges to work", _tomb.Creature != null, _tomb.Machine.State.ToString());
            bool sawCollect = false;
            float end = Time.time + 180f;
            while (Time.time < end && !(CountPrefab(_collection, "Wood") == 0 && CountPrefab(_collection, "Stone") == 0 && _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty))
            {
                sawCollect |= _tomb.Worker.Plan?.Kind == TaskKinds.Collect;
                yield return null;
            }
            _t.Check("Collection Chest emptied by a collect task", sawCollect && CountPrefab(_collection, "Wood") == 0 && CountPrefab(_collection, "Stone") == 0,
                $"left: wood {CountPrefab(_collection, "Wood")}, stone {CountPrefab(_collection, "Stone")}; carried {string.Join(", ", _tomb.Carried.Stacks)}");
            _t.Check("wood sorted to the wood chest", CountPrefab(_wood, "Wood") == 24, $"{CountPrefab(_wood, "Wood")}/24");
            _t.Check("stone sorted to the stone chest", CountPrefab(_stone, "Stone") == 11, $"{CountPrefab(_stone, "Stone")}/11");
            _t.Check("item without destination stays in the Collection Chest", CountPrefab(_collection, "Resin") == 5, CountPrefab(_collection, "Resin").ToString());
            if (axe) _t.Check("equipment stays in the Collection Chest", CountPrefab(_collection, "AxeStone") == 1);
            else _t.Skip("equipment stays in the Collection Chest", "AxeStone not found");

            List<ItemDrop.ItemData> wood = _wood.GetInventory().GetAllItems().Where(i => ItemBridge.PrefabName(i) == "Wood").ToList();
            _t.Check("collected cheated wood kept apart from the clean stack",
                wood.Where(i => !i.m_cheated).Sum(i => i.m_stack) == 21 && wood.Where(i => i.m_cheated).Sum(i => i.m_stack) == 3,
                string.Join(", ", wood.Select(i => $"{i.m_stack}{(i.m_cheated ? " cheated" : "")}")));
            _t.Check("custom data kept through collection", wood.Any(i => i.m_cheated && i.m_customData.TryGetValue(Tag, out string v) && v == "kept"));
            _t.Check("totals conserved after collection", Same(expected, WorldTotals()), $"{Format(expected)} → {Format(WorldTotals())}");
        }

        private IEnumerator Delivery()
        {
            DeliveryChest a = _deliveryA.GetComponent<DeliveryChest>();
            DeliveryChest b = _deliveryB.GetComponent<DeliveryChest>();
            Dictionary<string, int> expected = WorldTotals();
            a.Edit(l => l.Request("Wood", 15) == RequestEditResult.Ok && l.Request("Stone", 30) == RequestEditResult.Ok);
            b.Edit(l => l.Request("Resin", 3) == RequestEditResult.Ok);
            int maxWood = 0;
            bool sawDeliver = false;
            float end = Time.time + 180f;
            while (Time.time < end && !(Delivered(a, "Wood") == 15 && Delivered(a, "Stone") == 11 && Delivered(b, "Resin") == 3
                                        && _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty))
            {
                maxWood = Mathf.Max(maxWood, CountPrefab(_deliveryA, "Wood"));
                sawDeliver |= _tomb.Worker.Plan?.Kind == TaskKinds.Deliver;
                yield return null;
            }
            _t.Check("delivers the requested wood", sawDeliver && Delivered(a, "Wood") == 15 && CountPrefab(_deliveryA, "Wood") == 15,
                $"ledger {Delivered(a, "Wood")}, chest {CountPrefab(_deliveryA, "Wood")}");
            _t.Check("never delivers more than requested", maxWood <= 15, maxWood.ToString());
            _t.Check("short stock: delivers what exists, line stays open",
                Delivered(a, "Stone") == 11 && a.Ledger.Remaining("Stone") == 19 && CountPrefab(_stone, "Stone") == 0, Lines(a));
            _t.Check("two Delivery Chests served independently", Delivered(b, "Resin") == 3 && CountPrefab(_deliveryB, "Resin") == 3
                                                                   && CountPrefab(_deliveryA, "Resin") == 0, Lines(b));
            _t.Check("Collection Chest is a delivery source", CountPrefab(_collection, "Resin") == 2, CountPrefab(_collection, "Resin").ToString());
            _t.Check("totals conserved after delivery", Same(expected, WorldTotals()), $"{Format(expected)} → {Format(WorldTotals())}");

            GameObject reloaded = null;
            yield return _t.Reload(_deliveryA.gameObject, go => reloaded = go);
            _deliveryA = reloaded != null ? reloaded.GetComponentInChildren<Container>() : null;
            a = _deliveryA != null ? _deliveryA.GetComponent<DeliveryChest>() : null;
            if (!_t.Check("delivered counts survive reload", a != null && Delivered(a, "Wood") == 15 && Delivered(a, "Stone") == 11, a != null ? Lines(a) : "chest missing"))
                yield break;
            ReplaceChest(_deliveryA);

            ItemBridge.RemoveFrom(_deliveryA.GetInventory(), Stack("Wood", 0), 15);
            ItemBridge.RemoveFrom(_deliveryA.GetInventory(), CheatedTag(), 15);
            yield return SelfTestContext.WaitFor(() => CountPrefab(_deliveryA, "Wood") > 0, 8f);
            _t.Check("taking items out does not reduce delivered", Delivered(a, "Wood") == 15 && CountPrefab(_deliveryA, "Wood") == 0, Lines(a));

            Add(_deliveryB, "Stone", 500);
            Add(_wood, "Wood", 10);
            b.Edit(l => l.Request("Wood", 5) == RequestEditResult.Ok);
            yield return SelfTestContext.WaitFor(() => CountPrefab(_deliveryB, "Wood") > 0, 10f);
            _t.Check("full Delivery Chest receives nothing, line stays open", CountPrefab(_deliveryB, "Wood") == 0 && b.Ledger.Remaining("Wood") == 5, Lines(b));
            ItemBridge.RemoveFrom(_deliveryB.GetInventory(), Stack("Stone", 0), 500);
            b.Edit(l => l.Cancel(l.Lines.ToList().FindIndex(x => x.ItemPrefab == "Wood")) == RequestEditResult.Ok);
        }

        private IEnumerator Finish()
        {
            Dictionary<string, int> expected = WorldTotals();
            yield return SetTime(FakeTime.Noon);
            yield return SelfTestContext.WaitFor(() => _tomb.Machine.State == HaugbuiState.Dormant, 30f);
            _t.Check("dawn: nothing carried", _tomb.Carried.IsEmpty, string.Join(", ", _tomb.Carried.Stacks));
            _t.Check("totals conserved at the end", Same(expected, WorldTotals()), $"{Format(expected)} → {Format(WorldTotals())}");
        }

        private static ItemStack CheatedTag()
        {
            ItemStack stack = Stack("Wood", 0);
            stack.Cheated = true;
            stack.CustomData[Tag] = "kept";
            return stack;
        }

        private static int Delivered(DeliveryChest chest, string prefab) =>
            chest.Ledger.Lines.Where(l => l.ItemPrefab == prefab).Sum(l => l.Delivered);

        private static string Lines(DeliveryChest chest) =>
            string.Join(", ", chest.Ledger.Lines.Select(l => $"{l.ItemPrefab} {l.Delivered}/{l.Requested}"));

        private static bool IsTransportable(string prefab) => ItemBridge.IsTransportable(new ItemStack { Prefab = prefab });

        private static int Cost(Piece piece) => piece.m_resources.Sum(r => r.m_amount);

        private IEnumerator SetTime(float fraction)
        {
            _time.Fraction = fraction;
            _tomb.TickNow();
            yield return SelfTestContext.WaitFrames(2);
        }

        private Vector3 Ground(Vector3 offset) => SelfTestContext.OnGround(_arena + offset);

        private Container Place(StructureKind kind, Vector3 offset)
        {
            HaugbuiStructure structure = _t.PlaceStructure(kind, Ground(offset));
            Container container = structure != null ? structure.GetComponent<Container>() : null;
            if (container != null) _chests.Add(container);
            return container;
        }

        private Container Chest(Vector3 offset, params (string Prefab, int Amount)[] items)
        {
            GameObject go = _t.PlaceVanilla("piece_chest_wood", Ground(offset), Quaternion.identity);
            Container container = go != null ? go.GetComponentInChildren<Container>() : null;
            if (container == null) return null;
            foreach ((string prefab, int amount) in items) Add(container, prefab, amount);
            _chests.Add(container);
            return container;
        }

        private void ReplaceChest(Container reloaded)
        {
            _chests.RemoveAll(c => c == null);
            if (reloaded != null && !_chests.Contains(reloaded)) _chests.Add(reloaded);
        }

        private static void Add(Container container, string prefab, int amount) => ItemBridge.AddTo(container.GetInventory(), Stack(prefab, amount));

        private static bool AddEquipment(Container container, string prefab)
        {
            GameObject item = ObjectDB.instance.GetItemPrefab(prefab);
            if (item == null) return false;
            ItemBridge.AddTo(container.GetInventory(), Stack(prefab, 1));
            return CountPrefab(container, prefab) == 1;
        }

        private static int CountPrefab(Container container, string prefab) =>
            container == null ? 0 : ItemBridge.CountPrefab(container.GetInventory(), prefab);

        private static ItemStack Stack(string prefab, int amount)
        {
            var stack = new ItemStack { Prefab = prefab, Amount = amount, WorldLevel = Game.m_worldLevel };
            ItemBridge.RefreshShared(stack);
            return stack;
        }

        /// <summary>Every item this suite owns: its chests, tomb storage, carried and overflow.</summary>
        private Dictionary<string, int> WorldTotals()
        {
            var totals = new Dictionary<string, int>();
            foreach (ItemDrop.ItemData item in _chests.Where(c => c != null).Concat(new[] { _tomb.GetComponent<Container>() })
                         .SelectMany(c => c.GetInventory().GetAllItems()))
                Add(totals, ItemBridge.PrefabName(item), item.m_stack);
            foreach (ItemStack s in _tomb.Carried.Stacks.Concat(_tomb.Overflow)) Add(totals, s.Prefab, s.Amount);
            return totals;
        }

        private static void Add(Dictionary<string, int> totals, string prefab, int amount) =>
            totals[prefab] = (totals.TryGetValue(prefab, out int n) ? n : 0) + amount;

        private static bool Same(Dictionary<string, int> a, Dictionary<string, int> b) =>
            a.Where(p => p.Value != 0).OrderBy(p => p.Key).SequenceEqual(b.Where(p => p.Value != 0).OrderBy(p => p.Key));

        private static string Format(Dictionary<string, int> totals) =>
            string.Join(" ", totals.Where(p => p.Value != 0).OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));
    }
}
#endif
