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
using UnityEngine.UI;

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
            yield return Structures();
            yield return EditButtons();
            yield return Editor();
                yield return Collection();
                yield return Delivery();
                yield return DeliveryModes();
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

        private IEnumerator Structures()
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
                Transform mark = chest.transform.Find(StructureVisuals.MarkName);
                var label = mark != null ? mark.GetComponentInChildren<TMPro.TextMeshProUGUI>(true) : null;
                _t.Check($"{name} chest mark is not a sign", mark != null && mark.gameObject.activeInHierarchy && mark.GetComponent<Sign>() == null && label != null && !label.raycastTarget, label != null ? label.text : "missing");
                _t.Check($"{name} chest mark bobs five centimetres",
                    mark != null && mark.GetComponent<MarkFloat>() != null
                    && Mathf.Approximately(MarkFloat.Lift(0f), 0f) && Mathf.Approximately(MarkFloat.Lift(1f), MarkFloat.Travel)
                    && Mathf.Approximately(MarkFloat.Lift(2f), 0f));
                if (chest.GetComponent<HaugbuiStructure>().Kind == StructureKind.DeliveryChest)
                {
                    _t.Check("delivery chest has no extra props", chest.transform.Find(StructureVisuals.RootName) == null);
                    _t.Check("delivery chest uses the painted wood texture",
                        chest.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m != null && m.mainTexture != null && m.mainTexture.name == "delivery.png")));
                    _t.Check("delivery chest keeps the wood relief",
                        chest.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m != null && m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") != null && m.GetTexture("_BumpMap").name.Contains("woodchest"))));
                    _t.Check("delivery chest mark points down", label != null && label.text.Contains("#6ED4FF"));
                }
                else
                {
                    _t.Check("collection chest uses the painted wood texture",
                        chest.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m != null && m.mainTexture != null && m.mainTexture.name == "collection.png")));
                    _t.Check("collection chest mark points up", label != null && label.text.Contains("#5EFFC0"));
                }
            }
            _t.Check("delivery chest has a request ledger", _deliveryA.GetComponent<DeliveryChest>() != null);
            _t.Check("collection chest has no request ledger", _collection.GetComponent<DeliveryChest>() == null);
            string hover = _deliveryA.GetHoverText();
            _t.Check("delivery hover shows the alt-interact hint", hover.Contains(Localization.instance.Localize("$haugbui_delivery_edit")), hover);
            _t.Check("delivery hover shows copy and paste",
                hover.Contains("Shift+C") && hover.Contains(Localization.instance.Localize("$haugbui_delivery_copy"))
                && hover.Contains("Shift+V") && hover.Contains(Localization.instance.Localize("$haugbui_delivery_paste")), hover);
            _t.Check("delivery hover states its role", hover.Contains(Localization.instance.Localize("$haugbui_hover_delivery")), hover);
            _t.Check("collection hover states its role",
                _collection.GetHoverText().Contains(Localization.instance.Localize("$haugbui_hover_collection")), _collection.GetHoverText());
            _t.Check("chest hover offset", !float.IsNaN(_deliveryA.GetHoverOffset()) && !float.IsNaN(_collection.GetHoverOffset()));
            CraftMenuHidden();
            // Hide() leaves the inventory "visible" for two frames, which keeps movement blocked on its own.
            yield return UntilInventoryClosed();
            DeliveryPanel.Open(_deliveryA.GetComponent<DeliveryChest>());
            _t.Check("delivery panel blocks movement", DeliveryPanel.IsOpen && !_t.Player.TakeInput());
            DeliveryPanel.Dismiss.onClick.Invoke();
            _t.Check("delivery corner X closes and movement works", !DeliveryPanel.IsOpen && _t.Player.TakeInput(),
                $"open {DeliveryPanel.IsOpen}, input {_t.Player.TakeInput()}, inventory {InventoryGui.IsVisible()}");
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

        /// <summary>The chest window closes itself past 4 m, so the check has to stand next to it.</summary>
        private void StandBeside(Component thing)
        {
            Player player = _t.Player;
            Vector3 away = player.transform.position - thing.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.back;
            away.Normalize();
            Vector3 stand = thing.transform.position + away * 1.5f;
            stand.y = ZoneSystem.instance.GetGroundHeight(stand);
            player.transform.position = stand;
            Rigidbody body = player.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = stand;
                body.linearVelocity = Vector3.zero;
            }
        }

        private static IEnumerator UntilInventoryClosed()
        {
            for (int i = 0; i < 5 && InventoryGui.IsVisible(); i++) yield return null;
        }

        private static void Release(Container container)
        {
            if (container != null && container.IsInUse()) container.SetInUse(false);
        }

        private void CraftMenuHidden()
        {
            InventoryGui gui = InventoryGui.instance;
            if (!_t.Check("inventory is available", gui != null && gui.m_crafting != null)) return;
            try
            {
                gui.Show(_tomb.GetComponent<Container>());
                _t.Check("tomb storage hides the crafting menu", !gui.m_crafting.gameObject.activeSelf);
                gui.Show(_collection);
                _t.Check("collection chest hides the crafting menu", !gui.m_crafting.gameObject.activeSelf);
                gui.Show(_deliveryA);
                _t.Check("delivery chest hides the crafting menu", !gui.m_crafting.gameObject.activeSelf);
                gui.Show(_wood);
                _t.Check("a wood chest still shows the crafting menu", gui.m_crafting.gameObject.activeSelf);
            }
            finally
            {
                gui.Hide();
                _t.Check("closing storage brings the crafting menu back", gui.m_crafting.gameObject.activeSelf);
            }
        }

        private IEnumerator EditButtons()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null)
            {
                _t.Skip("chest edit buttons", "no inventory");
                yield break;
            }
            int wood = CountPrefab(_wood, "Wood");
            StandBeside(_collection);
            gui.Show(_collection);
            yield return null;
            _t.Check("a collection chest has no edit button", !ChestEditButtons.Visible);
            // Show() replaces the open container and leaves the previous one marked in use, so the Haugbui skips it.
            Release(_collection);
            StandBeside(_wood);
            gui.Show(_wood);
            yield return null;
            _t.Check("a vanilla chest shows Edit filters",
                ChestEditButtons.Visible && ChestEditButtons.Label == Localization.instance.Localize("$haugbui_filter_edit"),
                $"visible {ChestEditButtons.Visible}, label {ChestEditButtons.Label}");
            ChestEditButtons.Press();
            _t.Check("Edit filters opens the filter panel", ChestFilterPanel.IsOpen && ChestFilterPanel.Current == _wood,
                ChestFilterPanel.IsOpen ? "wrong chest" : "closed");
            _t.Check("Edit filters closes the chest window", !gui.IsContainerOpen());
            ChestFilterPanel.Dismiss.onClick.Invoke();
            yield return UntilInventoryClosed();
            _t.Check("closing Edit filters leaves movement free", !ChestFilterPanel.IsOpen && _t.Player.TakeInput(),
                $"open {ChestFilterPanel.IsOpen}, input {_t.Player.TakeInput()}, inventory {InventoryGui.IsVisible()}");

            StandBeside(_deliveryA);
            gui.Show(_deliveryA);
            yield return null;
            _t.Check("a delivery chest shows Edit requests",
                ChestEditButtons.Visible && ChestEditButtons.Label == Localization.instance.Localize("$haugbui_delivery_edit"),
                $"visible {ChestEditButtons.Visible}, label {ChestEditButtons.Label}");
            ChestEditButtons.Press();
            _t.Check("Edit requests opens the request editor",
                DeliveryPanel.IsOpen && DeliveryPanel.Current == _deliveryA.GetComponent<DeliveryChest>(),
                DeliveryPanel.IsOpen ? "wrong chest" : "closed");
            DeliveryPanel.Dismiss.onClick.Invoke();
            yield return UntilInventoryClosed();
            _t.Check("closing Edit requests leaves movement free", !DeliveryPanel.IsOpen && _t.Player.TakeInput(),
                $"open {DeliveryPanel.IsOpen}, input {_t.Player.TakeInput()}, inventory {InventoryGui.IsVisible()}");
            gui.Hide();
            _t.Check("the edit buttons do not move items", CountPrefab(_wood, "Wood") == wood, CountPrefab(_wood, "Wood").ToString());
        }

        private IEnumerator DeliveryDraft(DeliveryChest chest)
        {
            DeliveryPanel.TypeSearch("Wo");
            yield return null;
            Button wood = DeliveryPanel.Suggestion("Wood");
            if (wood == null)
            {
                _t.Skip("delivery suggestion click", "Wood is not in the suggestion list");
                yield break;
            }
            wood.onClick.Invoke();
            yield return null;
            _t.Check("clicking a suggestion adds a delivery row and hides the list",
                DeliveryPanel.RowCount == 1 && !DeliveryPanel.SuggestionsVisible);
            string keepLabel = Localization.instance.Localize("$haugbui_delivery_keep_amount");
            _t.Check("keep amount starts off", DeliveryPanel.KeepToggle("Wood") != null && !DeliveryPanel.KeepChecked("Wood"));
            _t.Check("keep amount is labelled", DeliveryPanel.KeepLabel("Wood") == keepLabel, DeliveryPanel.KeepLabel("Wood"));
            _t.Check("keep amount has no column header",
                DeliveryPanel.HeaderText.Contains(Localization.instance.Localize("$haugbui_delivery_col_requested"))
                && !DeliveryPanel.HeaderText.Contains(keepLabel), DeliveryPanel.HeaderText);
            _t.Check("the search text stays", DeliveryPanel.SearchText == "Wo", DeliveryPanel.SearchText);
            _t.Check("a new row shows the amount already in the chest", DeliveryPanel.ShownAmount("Wood") == CountPrefab(_deliveryA, "Wood"),
                DeliveryPanel.ShownAmount("Wood").ToString());
            _t.Check("Save stays off until the amount is filled", !DeliveryPanel.CanSave && !DeliveryPanel.SaveButton.interactable);
            _t.Check("clear all is available once there is a row", DeliveryPanel.ClearButton.interactable);
            DeliveryPanel.SetAmount("Wood", "4");
            _t.Check("Save turns on when every amount is filled", DeliveryPanel.CanSave && DeliveryPanel.SaveButton.interactable);
            DeliveryPanel.SaveButton.onClick.Invoke();
            DeliveryRequest saved = chest.Ledger.Lines.SingleOrDefault(l => l.ItemPrefab == "Wood");
            _t.Check("Save writes the request", chest.Ledger.Remaining("Wood") == 4 && saved != null && !saved.KeepAmount, Lines(chest));
            _t.Check("Save closes the request editor", !DeliveryPanel.IsOpen);
            DeliveryPanel.Open(chest);
            DeliveryPanel.TypeSearch("");
            Button stone = DeliveryPanel.Suggestion("Stone");
            if (stone != null)
            {
                stone.onClick.Invoke();
                _t.Check("Save stays off while any amount is empty", !DeliveryPanel.CanSave);
                DeliveryPanel.RemoveButton("Stone").onClick.Invoke();
                _t.Check("the row X removes that delivery line", DeliveryPanel.RowCount == 1);
            }
            else _t.Skip("second delivery row", "Stone is not in the suggestion list");
            DeliveryPanel.ClearButton.onClick.Invoke();
            _t.Check("clear all empties the list", DeliveryPanel.RowCount == 0 && DeliveryPanel.ShowsEmpty);
            _t.Check("clear all waits for Save", chest.Ledger.Remaining("Wood") == 4, Lines(chest));
            DeliveryPanel.Dismiss.onClick.Invoke();
            DeliveryPanel.Open(chest);
            _t.Check("closing without Save keeps the requests", DeliveryPanel.RowCount == 1 && chest.Ledger.Remaining("Wood") == 4, Lines(chest));
            DeliveryPanel.ClearButton.onClick.Invoke();
            DeliveryPanel.SaveButton.onClick.Invoke();
            _t.Check("saving an empty list clears the requests", chest.Ledger.Lines.Count == 0, Lines(chest));
            _t.Check("Save closes the request editor", !DeliveryPanel.IsOpen);
        }

        private IEnumerator RequestClipboard(DeliveryChest chest)
        {
            PanelClipboard.Reset();
            Text copy = DeliveryPanel.CopyButton != null ? DeliveryPanel.CopyButton.GetComponentInChildren<Text>() : null;
            Text paste = DeliveryPanel.PasteButton != null ? DeliveryPanel.PasteButton.GetComponentInChildren<Text>() : null;
            _t.Check("paste request waits for a copy", DeliveryPanel.PasteButton != null && !DeliveryPanel.PasteButton.interactable);
            _t.Check("copy request is available", DeliveryPanel.CopyButton != null && DeliveryPanel.CopyButton.interactable
                && copy != null && copy.text == Localization.instance.Localize("$haugbui_delivery_copy")
                && paste != null && paste.text == Localization.instance.Localize("$haugbui_delivery_paste"));
            if (DeliveryPanel.CopyButton == null) yield break;
            string copied = Lines(chest);
            DeliveryPanel.CopyButton.onClick.Invoke();
            DeliveryChest other = _deliveryB.GetComponent<DeliveryChest>();
            other.Edit(ledger =>
            {
                while (ledger.Lines.Count > 0) ledger.Cancel(0);
                return ledger.Request("Resin", 9) == RequestEditResult.Ok;
            });
            DeliveryPanel.Open(other);
            _t.Check("paste request is available on another chest", DeliveryPanel.PasteButton.interactable);
            DeliveryPanel.PasteButton.onClick.Invoke();
            _t.Check("paste request replaces the whole list and stays open", DeliveryPanel.IsOpen && Lines(other) == copied, Lines(other));
            _t.Check("the copied chest keeps its requests", Lines(chest) == copied, Lines(chest));

            other.Edit(ledger =>
            {
                while (ledger.Lines.Count > 0) ledger.Cancel(0);
                return ledger.Request("Wood", 10) == RequestEditResult.Ok;
            });
            other.RecordDelivered("Wood", 6);
            DeliveryPanel.Open(chest);
            DeliveryPanel.ClearButton.onClick.Invoke();
            DeliveryPanel.TypeSearch("Wo");
            yield return null;
            Button onlyWood = DeliveryPanel.Suggestion("Wood");
            if (onlyWood != null)
            {
                onlyWood.onClick.Invoke();
                _t.Check("copy request stays off while an amount is empty", !DeliveryPanel.CopyButton.interactable);
                DeliveryPanel.SetAmount("Wood", "4");
                DeliveryPanel.SetKeep("Wood", true);
                DeliveryPanel.CopyButton.onClick.Invoke();
                DeliveryPanel.Open(other);
                DeliveryPanel.PasteButton.onClick.Invoke();
                DeliveryRequest wood = other.Ledger.Lines.FirstOrDefault(line => line.ItemPrefab == "Wood");
                _t.Check("paste request starts the quota over and keeps the checkbox",
                    other.Ledger.Lines.Count == 1 && wood != null && wood.Delivered == 0 && wood.Requested == 4 && wood.KeepAmount, Lines(other));
            }
            else _t.Skip("paste request keeps delivered", "Wood is not in the suggestion list");

            DeliveryPanel.Open(chest);
            DeliveryPanel.ClearButton.onClick.Invoke();
            DeliveryPanel.CopyButton.onClick.Invoke();
            DeliveryPanel.Open(other);
            DeliveryPanel.PasteButton.onClick.Invoke();
            _t.Check("pasting an empty request list clears the chest", other.Ledger.Lines.Count == 0, Lines(other));
            _t.Check("an empty copy does not change the source until paste", chest.Ledger.Remaining("Wood") == 120, Lines(chest));
            other.Edit(ledger =>
            {
                while (ledger.Lines.Count > 0) ledger.Cancel(0);
                return true;
            });
            PanelClipboard.Reset();
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
            _t.Check("an empty delivery list says there is nothing to deliver",
                DeliveryPanel.ShowsEmpty && DeliveryPanel.EmptyText.Contains(Localization.instance.Localize("$haugbui_delivery_none")), DeliveryPanel.EmptyText);
            _t.Check("extra items stay in the chest", DeliveryPanel.Footnote.Contains(Localization.instance.Localize("$haugbui_delivery_keep_extra")), DeliveryPanel.Footnote);
            _t.Check("clear all waits until there is a row", !DeliveryPanel.ClearButton.interactable);
            yield return DeliveryDraft(chest);
            DeliveryPanel.Open(chest);
            _t.Check("delivery suggestions are items the character has picked up",
                DeliveryPanel.KnownItems.All(k => MaterialKnown(k.Prefab)));
            _t.Check("equipment is not selectable", DeliveryPanel.KnownItems.All(k => IsTransportable(k.Prefab)),
                string.Join(", ", DeliveryPanel.KnownItems.Where(k => !IsTransportable(k.Prefab)).Select(k => k.Prefab)));
            bool entered = new[] { ("Wood", 100), ("Stone", 200), ("Resin", 50), ("Iron", 30) }
                .All(r => DeliveryPanel.Submit(r.Item1, r.Item2) == RequestEditResult.Ok);
            _t.Check("spec example entered (100 Wood, 200 Stone, 50 Resin, 30 Iron)",
                entered && chest.Ledger.Lines.Select(l => $"{l.ItemPrefab}:{l.Remaining}").SequenceEqual(new[] { "Wood:100", "Stone:200", "Resin:50", "Iron:30" }),
                Lines(chest));
            _t.Check("editing a line sets its amount", DeliveryPanel.Submit("Wood", 120) == RequestEditResult.Ok && chest.Ledger.Remaining("Wood") == 120, Lines(chest));
            _t.Check("invalid amount refused", DeliveryPanel.Submit("Wood", 0) == RequestEditResult.InvalidAmount);
            yield return RequestClipboard(chest);
            DeliveryPanel.Close();
            yield return null;
            _t.Check("editor closes", !DeliveryPanel.IsOpen);

            chest.Edit(l =>
            {
                int index = -1;
                for (int i = 0; i < l.Lines.Count; i++)
                    if (l.Lines[i].ItemPrefab == "Wood") index = i;
                return index >= 0 && l.SetRequested(index, 120, true) == RequestEditResult.Ok;
            });
            GameObject reloaded = null;
            yield return _t.Reload(_deliveryA.gameObject, go => reloaded = go);
            _deliveryA = reloaded != null ? reloaded.GetComponentInChildren<Container>() : null;
            DeliveryChest back = _deliveryA != null ? _deliveryA.GetComponent<DeliveryChest>() : null;
            _t.Check("requests survive reload", back != null && back.Ledger.Lines.Count == 4 && back.Ledger.Remaining("Wood") == 120
                && back.Ledger.Lines.Any(l => l.ItemPrefab == "Wood" && l.KeepAmount && l.Delivered == 0),
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
            while (Time.time < end && !(CountPrefab(_deliveryA, "Wood") == 15 && !HasLine(a, "Wood")
                                        && Delivered(a, "Stone") == 10 && CountPrefab(_deliveryB, "Resin") == 3 && !HasLine(b, "Resin")
                                        && _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty))
            {
                maxWood = Mathf.Max(maxWood, CountPrefab(_deliveryA, "Wood"));
                sawDeliver |= _tomb.Worker.Plan?.Kind == TaskKinds.Deliver;
                yield return null;
            }
            // A one-trip delivery completes between two samples; keep watching while the worker plans again.
            float settle = Time.time + 3f * HaugbuiPlugin.Instance.Settings.PlanningIntervalSeconds.Value + 1f;
            while (Time.time < settle)
            {
                maxWood = Mathf.Max(maxWood, CountPrefab(_deliveryA, "Wood"));
                yield return null;
            }
            _t.Check("delivers the requested wood", sawDeliver && CountPrefab(_deliveryA, "Wood") == 15 && !HasLine(a, "Wood"),
                $"chest {CountPrefab(_deliveryA, "Wood")}, {Lines(a)}");
            _t.Check("never delivers more than requested", maxWood == 15 && !HasLine(a, "Wood"), $"max {maxWood} in the chest");
            // Eleven stone in an ordinary chest: one stays, ten are delivered, the line stays open.
            _t.Check("short stock: delivers what exists, line stays open",
                Delivered(a, "Stone") == 10 && a.Ledger.Remaining("Stone") == 20 && CountPrefab(_stone, "Stone") == 1, Lines(a));
            _t.Check("two Delivery Chests served independently", CountPrefab(_deliveryB, "Resin") == 3 && !HasLine(b, "Resin")
                                                                   && CountPrefab(_deliveryA, "Resin") == 0, Lines(b));
            _t.Check("Collection Chest is a delivery source", CountPrefab(_collection, "Resin") == 2, CountPrefab(_collection, "Resin").ToString());
            _t.Check("totals conserved after delivery", Same(expected, WorldTotals()), $"{Format(expected)} → {Format(WorldTotals())}");

            GameObject reloaded = null;
            yield return _t.Reload(_deliveryA.gameObject, go => reloaded = go);
            _deliveryA = reloaded != null ? reloaded.GetComponentInChildren<Container>() : null;
            a = _deliveryA != null ? _deliveryA.GetComponent<DeliveryChest>() : null;
            if (!_t.Check("an unfinished quota survives reload and a finished one stays gone",
                a != null && !HasLine(a, "Wood") && CountPrefab(_deliveryA, "Wood") == 15 && Delivered(a, "Stone") == 10, a != null ? Lines(a) : "chest missing"))
                yield break;
            ReplaceChest(_deliveryA);

            ItemBridge.RemoveFrom(_deliveryA.GetInventory(), Stack("Wood", 0), 15);
            ItemBridge.RemoveFrom(_deliveryA.GetInventory(), CheatedTag(), 15);
            yield return SelfTestContext.WaitFor(() => CountPrefab(_deliveryA, "Wood") > 0, 8f);
            _t.Check("taking items out of a finished quota does not bring more", !HasLine(a, "Wood") && CountPrefab(_deliveryA, "Wood") == 0, Lines(a));

            Add(_deliveryB, "Stone", 500);
            Add(_wood, "Wood", 10);
            b.Edit(l => l.Request("Wood", 5) == RequestEditResult.Ok);
            yield return SelfTestContext.WaitFor(() => CountPrefab(_deliveryB, "Wood") > 0, 10f);
            _t.Check("full Delivery Chest receives nothing, line stays open", CountPrefab(_deliveryB, "Wood") == 0 && b.Ledger.Remaining("Wood") == 5, Lines(b));
            ItemBridge.RemoveFrom(_deliveryB.GetInventory(), Stack("Stone", 0), 500);
            b.Edit(l => l.Cancel(l.Lines.ToList().FindIndex(x => x.ItemPrefab == "Wood")) == RequestEditResult.Ok);
        }

        /// <summary>Quota edits forget delivered. Keep amount follows the chest and leaves a surplus where it is.</summary>
        private IEnumerator DeliveryModes()
        {
            DeliveryChest a = _deliveryA.GetComponent<DeliveryChest>();
            DeliveryChest b = _deliveryB.GetComponent<DeliveryChest>();
            yield return SelfTestContext.WaitFor(() => _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty, 20f);
            a.Edit(l =>
            {
                while (l.Lines.Count > 0) l.Cancel(0);
                return true;
            });
            Add(_wood, "Wood", 60);
            Dictionary<string, int> expected = WorldTotals();

            a.Edit(l => l.Request("Wood", 10) == RequestEditResult.Ok);
            _tomb.Worker.PlanNow();
            yield return SelfTestContext.WaitFor(() => CountPrefab(_deliveryA, "Wood") == 10 && !HasLine(a, "Wood") && _tomb.Carried.IsEmpty, 90f);
            _t.Check("a finished quota of 10 is removed", CountPrefab(_deliveryA, "Wood") == 10 && !HasLine(a, "Wood"), Lines(a));

            if (GUIManager.IsHeadless() || GUIManager.CustomGUIFront == null)
            {
                a.Edit(l => l.Request("Wood", 20) == RequestEditResult.Ok);
            }
            else
            {
                DeliveryPanel.Open(a);
                RequestEditResult? edited = DeliveryPanel.Submit("Wood", 20);
                DeliveryPanel.Close();
                DeliveryRequest fresh = a.Ledger.Lines.FirstOrDefault(l => l.ItemPrefab == "Wood");
                _t.Check("changing 10 into 20 saves a fresh quota",
                    edited == RequestEditResult.Ok && fresh != null && fresh.Requested == 20 && fresh.Delivered == 0 && !fresh.KeepAmount,
                    Lines(a));
            }
            _tomb.Worker.PlanNow();
            yield return SelfTestContext.WaitFor(() => CountPrefab(_deliveryA, "Wood") == 30 && !HasLine(a, "Wood") && _tomb.Carried.IsEmpty, 90f);
            _t.Check("the new 20 arrives on top of the first 10", CountPrefab(_deliveryA, "Wood") == 30 && !HasLine(a, "Wood"),
                $"chest {CountPrefab(_deliveryA, "Wood")}, {Lines(a)}");

            yield return SelfTestContext.WaitFor(() => _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty, 20f);
            _tomb.Worker.Paused = true;
            a.Edit(l => l.Request("Wood", 10) == RequestEditResult.Ok && l.RecordDelivered("Wood", 6) == 6);
            _t.Check("a partial quota is still listed", Delivered(a, "Wood") == 6 && a.Ledger.Remaining("Wood") == 4, Lines(a));
            if (GUIManager.IsHeadless() || GUIManager.CustomGUIFront == null)
            {
                a.Edit(l => l.Replace(new RequestDraft[0]) == RequestEditResult.Ok && l.Request("Wood", 10) == RequestEditResult.Ok);
            }
            else
            {
                DeliveryPanel.Open(a);
                DeliveryPanel.ClearButton.onClick.Invoke();
                DeliveryPanel.SaveButton.onClick.Invoke();
                _t.Check("clear all drops the partial quota", !HasLine(a, "Wood"), Lines(a));
                DeliveryPanel.Open(a);
                RequestEditResult? again = DeliveryPanel.Submit("Wood", 10);
                DeliveryPanel.Close();
                _t.Check("a new line after clear all owes 10",
                    again == RequestEditResult.Ok && Delivered(a, "Wood") == 0 && a.Ledger.Remaining("Wood") == 10, Lines(a));
            }
            _tomb.Worker.Paused = false;
            _tomb.Worker.PlanNow();
            yield return SelfTestContext.WaitFor(() => CountPrefab(_deliveryA, "Wood") == 40 && !HasLine(a, "Wood") && _tomb.Carried.IsEmpty, 90f);
            _t.Check("the line after clear all brings 10 more", CountPrefab(_deliveryA, "Wood") == 40 && !HasLine(a, "Wood"),
                $"chest {CountPrefab(_deliveryA, "Wood")}");

            yield return SelfTestContext.WaitFor(() => _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty, 20f);
            _tomb.Worker.Paused = true;
            if (GUIManager.IsHeadless() || GUIManager.CustomGUIFront == null)
            {
                a.Edit(l => l.Request("Wood", 8, keepAmount: true) == RequestEditResult.Ok);
            }
            else
            {
                DeliveryPanel.Open(a);
                DeliveryPanel.Submit("Wood", 8);
                DeliveryPanel.SetKeep("Wood", true);
                DeliveryPanel.SaveButton.onClick.Invoke();
            }
            DeliveryRequest kept = a.Ledger.Lines.SingleOrDefault(l => l.ItemPrefab == "Wood");
            _t.Check("keep amount is saved on the line", kept != null && kept.KeepAmount && kept.Requested == 8 && kept.Delivered == 0, Lines(a));
            _tomb.Worker.Paused = false;
            float settle = Time.time + 3f * HaugbuiPlugin.Instance.Settings.PlanningIntervalSeconds.Value + 1f;
            while (Time.time < settle) yield return null;
            DeliveryRequest standing = a.Ledger.Lines.FirstOrDefault(l => l.ItemPrefab == "Wood");
            _t.Check("keep amount leaves a surplus in the chest", CountPrefab(_deliveryA, "Wood") == 40 && standing != null && standing.KeepAmount,
                $"chest {CountPrefab(_deliveryA, "Wood")}, {Lines(a)}");

            MovePrefab(_deliveryA, _wood, "Wood", 3);
            _tomb.Worker.PlanNow();
            yield return SelfTestContext.WaitFor(() => CountPrefab(_deliveryA, "Wood") == 8 && _tomb.Carried.IsEmpty, 90f);
            _t.Check("keep amount refills what was taken", CountPrefab(_deliveryA, "Wood") == 8 && HasLine(a, "Wood"),
                $"chest {CountPrefab(_deliveryA, "Wood")}, {Lines(a)}");

            int surplus = ItemBridge.RemoveFrom(_wood.GetInventory(), Stack("Wood", 0), 4);
            if (surplus > 0) Add(_deliveryA, "Wood", surplus);
            int held = CountPrefab(_deliveryA, "Wood");
            settle = Time.time + 3f * HaugbuiPlugin.Instance.Settings.PlanningIntervalSeconds.Value + 1f;
            while (Time.time < settle) yield return null;
            _t.Check("keep amount does not remove a surplus", CountPrefab(_deliveryA, "Wood") == held && HasLine(a, "Wood"),
                $"chest {CountPrefab(_deliveryA, "Wood")}, held {held}");

            Add(_deliveryB, "Stone", 500);
            b.Edit(l => l.Request("Wood", 5, keepAmount: true) == RequestEditResult.Ok);
            yield return SelfTestContext.WaitFor(() => CountPrefab(_deliveryB, "Wood") > 0, 10f);
            DeliveryRequest blocked = b.Ledger.Lines.FirstOrDefault(l => l.ItemPrefab == "Wood");
            _t.Check("a full chest stops a keep-amount line and the line stays",
                CountPrefab(_deliveryB, "Wood") == 0 && blocked != null && blocked.KeepAmount, Lines(b));
            ItemBridge.RemoveFrom(_deliveryB.GetInventory(), Stack("Stone", 0), 500);
            b.Edit(l =>
            {
                while (l.Lines.Count > 0) l.Cancel(0);
                return true;
            });
            a.Edit(l =>
            {
                while (l.Lines.Count > 0) l.Cancel(0);
                return true;
            });
            yield return SelfTestContext.WaitFor(() => _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty, 20f);
            _t.Check("totals conserved across quota and keep amount", Same(expected, WorldTotals()), $"{Format(expected)} → {Format(WorldTotals())}");
        }

        private static void MovePrefab(Container from, Container to, string prefab, int leave)
        {
            int extra = CountPrefab(from, prefab) - leave;
            if (extra <= 0) return;
            int clean = ItemBridge.RemoveFrom(from.GetInventory(), Stack(prefab, 0), extra);
            if (clean > 0) Add(to, prefab, clean);
            extra -= clean;
            if (extra <= 0) return;
            int cheated = ItemBridge.RemoveFrom(from.GetInventory(), CheatedTag(), extra);
            if (cheated <= 0) return;
            ItemStack back = CheatedTag();
            back.Amount = cheated;
            ItemBridge.AddTo(to.GetInventory(), back);
        }

        private IEnumerator Finish()
        {
            _tomb.Worker.Paused = false;
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

        private static bool HasLine(DeliveryChest chest, string prefab) =>
            chest != null && chest.Ledger.Lines.Any(l => l.ItemPrefab == prefab);

        private static string Lines(DeliveryChest chest) =>
            chest == null ? "missing" : string.Join(", ", chest.Ledger.Lines.Select(l => $"{l.ItemPrefab} {l.Delivered}/{l.Requested}{(l.KeepAmount ? " keep" : "")}"));

        private static bool IsTransportable(string prefab) => ItemBridge.IsTransportable(new ItemStack { Prefab = prefab });

        private static bool MaterialKnown(string prefab)
        {
            GameObject item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
            string shared = drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                ? drop.m_itemData.m_shared.m_name : null;
            return !string.IsNullOrEmpty(shared) && Player.m_localPlayer != null && Player.m_localPlayer.IsMaterialKnown(shared);
        }

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
