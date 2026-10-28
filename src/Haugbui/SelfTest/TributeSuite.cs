#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Core.Structures;
using Haugbui.Core.Tribute;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Structures;
using Haugbui.UI;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>
    /// M6 tribute and servant identity: shift charging only with work (HB-T-101), offers and panel (HB-T-102), refund
    /// (HB-T-103), servant slots (HB-T-104) and death/new-tomb timing (HB-T-105), all on the fake clock.
    /// </summary>
    internal sealed class TributeSuite
    {
        private readonly SelfTestContext _t;
        private readonly FakeTime _time = new FakeTime();
        private readonly List<ItemDrop.ItemData> _held = new List<ItemDrop.ItemData>();
        private Vector3 _arena;
        private long _owner;
        private TombController _tomb;
        private Container _collection, _wood;

        public TributeSuite(SelfTestContext context) => _t = context;

        private TributeTable Table => HaugbuiPlugin.Instance.Settings.TributeTable;
        private Inventory PlayerInventory => _t.Player.GetInventory();

        public IEnumerator Run()
        {
            _t.Suite = "Tribute";
            TimeSource.Override = _time;
            Config.HaugbuiConfig settings = HaugbuiPlugin.Instance.Settings;
            bool tributeWas = settings.TributeEnabled.Value;
            string refundWas = settings.TributeOnTombDestroyed.Value;
            settings.TributeEnabled.Value = true;
            settings.TributeOnTombDestroyed.Value = "DropCoins";
            HoldValuables();
            try
            {
                yield return Setup();
                if (_tomb == null) yield break;
                yield return NewTombWaits();
                yield return NoWorkNoCharge();
                yield return Offers();
                yield return SameShiftNotChargedTwice();
                yield return NewShifts();
                yield return Death();
                yield return Altar();
                yield return TributeOff();
                yield return Slots();
            }
            finally
            {
                TimeSource.Override = null;
                settings.TributeEnabled.Value = tributeWas;
                settings.TributeOnTombDestroyed.Value = refundWas;
                if (TombPanel.IsOpen) TombPanel.Close();
                RestoreValuables();
            }
        }

        private IEnumerator Setup()
        {
            Player player = _t.Player;
            _owner = player.GetPlayerID();
            _arena = SelfTestContext.FindDryArena(player.transform.position - player.transform.forward * 14f, 9f);
            GameObject bed = _t.SpawnVanilla("bed", Ground(new Vector3(-8, 0, -4)), Quaternion.identity);
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
            HaugbuiStructure collection = _t.PlaceStructure(StructureKind.CollectionChest, Ground(new Vector3(4, 0, 4)));
            GameObject wood = _t.PlaceVanilla("piece_chest_wood", Ground(new Vector3(6, 0, -3)), Quaternion.identity);
            yield return SelfTestContext.WaitFrames(3);
            _collection = collection != null ? collection.GetComponent<Container>() : null;
            _wood = wood != null ? wood.GetComponentInChildren<Container>() : null;
            _tomb = tomb.GetComponent<TombController>();
            if (!_t.Check("chests placed", _collection != null && _wood != null && _tomb != null))
            {
                _tomb = null;
                yield break;
            }
            Add(_wood.GetInventory(), "Wood", 1, cheated: false);
            // Only Collection Chest work counts here; ground items and crops near the test spot must not.
            _tomb.SetToggle(TombToggles.GroundItems, false, out _);
            _tomb.SetToggle(TombToggles.Harvesting, false, out _);
        }

        private IEnumerator NewTombWaits()
        {
            double dusk = WorldClock.NextDusk(_time.Now - 5, FakeTime.DayLength);
            _t.Check("new tomb starts at the next dusk", Math.Abs(_tomb.State.StartTime - dusk) < 10,
                $"start {_tomb.State.StartTime:0}, dusk {dusk:0}");
            _t.Check("bound to a servant slot named Haugbui", _tomb.State.SlotIndex == 0 && _tomb.ServantName == ServantSlots.DefaultName(0),
                $"slot {_tomb.State.SlotIndex}, {_tomb.ServantName}");
            string key = ServantSlots.KeyName(_owner, 0);
            string saved = ZoneSystem.instance.m_globalKeys.FirstOrDefault(k => k.StartsWith(key + " ", StringComparison.Ordinal));
            ZoneSystem.GetKeyValue(saved?.ToLower() ?? "", out string value, out GlobalKeys kind);
            _t.Check("slot is in the world's saved global keys and survives the load's lower-casing",
                saved != null && kind == GlobalKeys.NonServerOption && ServantSlots.Decode(value, out ServantSlot loaded) == BlobReadStatus.Ok
                && loaded.TombGuid == _tomb.Guid, saved ?? "missing");
            _tomb.TickNow();
            string status = Localization.instance.Localize(_tomb.StatusLine());
            _t.Check("before its first dusk: inside, countdown shown", _tomb.Creature == null && _tomb.WaitRemaining > 0
                                                                      && status.Contains(Localization.instance.Localize("$haugbui_status_starts_in", "").Trim()),
                status);
            yield return null;
        }

        private IEnumerator NoWorkNoCharge()
        {
            yield return SetTime(FakeTime.Night);
            _t.Check("night without work: dormant, no charge", _tomb.Creature == null && _tomb.Waiting == ShiftDecision.NoWork
                                                               && _tomb.State.LastPaidShift == WorldClock.NoShift,
                $"waiting {_tomb.Waiting}, creature {_tomb.Creature != null}");
            _t.Check("status says it sleeps for lack of work", _tomb.StatusLine() == "$haugbui_status_no_work", _tomb.StatusLine());

            Add(_collection.GetInventory(), "Wood", 5, cheated: false);
            _tomb.TickNow();
            yield return null;
            _t.Check("work but no tribute: stays dormant", _tomb.Creature == null && _tomb.Waiting == ShiftDecision.NoTribute,
                $"waiting {_tomb.Waiting}");
            string hover = _tomb.GetComponent<Container>().GetHoverText();
            _t.Check("hover shows the balance and 'no tribute'",
                hover.Contains(Localization.instance.Localize("$haugbui_status_no_tribute"))
                && hover.Contains(Localization.instance.Localize("$haugbui_hover_tribute", "0", _tomb.ShiftCost.ToString())), hover);
        }

        private IEnumerator Offers()
        {
            Give("Coins", 3, false);
            Give("Amber", 1, false);
            Give("SilverNecklace", 1, true);
            _t.Check("offer fails without the item", Count("Ruby") == 0 && !_tomb.Offer("Ruby", _t.Player));

            int coins = Count("Coins");
            bool exact = true;
            for (int i = 0; i < 3; i++)
            {
                int before = _tomb.State.Balance;
                _tomb.Offer("Coins", _t.Player);
                exact &= _tomb.State.Balance == before + Table.ValueOf("Coins") && Count("Coins") == coins - i - 1;
            }
            _t.Check("each offer takes exactly one item and adds its value", exact && _tomb.State.Balance == 3,
                $"balance {_tomb.State.Balance}, coins {Count("Coins")}");
            long shift = WorldClock.ShiftId(_time.Now, FakeTime.DayLength);
            _tomb.Offer("Amber", _t.Player);
            yield return SelfTestContext.WaitFrames(2);
            _t.Check("enough tribute and work: charged once and emerges",
                _tomb.Creature != null && _tomb.State.Balance == 3 + Table.ValueOf("Amber") - _tomb.ShiftCost && _tomb.State.LastPaidShift == shift,
                $"balance {_tomb.State.Balance}, paid {_tomb.State.LastPaidShift} (shift {shift}), out {_tomb.Creature != null}");

            int cheatedBefore = _tomb.State.CheatedBalance;
            _tomb.Offer("SilverNecklace", _t.Player);
            _t.Check("cheated valuable counted in the cheated part",
                _tomb.State.CheatedBalance == cheatedBefore + Table.ValueOf("SilverNecklace") && Count("SilverNecklace") == 0,
                $"cheated {_tomb.State.CheatedBalance}");

            ShiftTimer timer = _tomb.Timer;
            double expected = WorldClock.ShiftEnd(shift, FakeTime.DayLength) - HaugbuiPlugin.Instance.Settings.DawnReturnLeadSeconds.Value - _time.Now;
            _t.Check("shift timer matches the presence policy", timer.InShift && Math.Abs(timer.Seconds - expected) < 2,
                $"{timer.Seconds:0} s, expected {expected:0} s");
            Panel();
        }

        private void Panel()
        {
            TombPanel.Open(_tomb);
            if (!TombPanel.IsOpen)
            {
                _t.Skip("tribute panel", "no GUI");
                return;
            }
            _t.Check("panel has one offer button per valuable", TombPanel.OfferButtons.Select(b => b.Prefab).SequenceEqual(Table.Values.Select(v => v.Key)),
                string.Join(", ", TombPanel.OfferButtons.Select(b => b.Prefab)));
            int offerRows = TombPanel.OfferButtons
                .Select(b => Mathf.RoundToInt(((RectTransform)b.Button.transform).anchoredPosition.y)).Distinct().Count();
            _t.Check("offer buttons sit on three rows", offerRows == Math.Min(3, TombPanel.OfferButtons.Count), offerRows.ToString());
            _t.Check("offer buttons disabled without the item",
                TombPanel.OfferButtons.Where(b => Count(b.Prefab) == 0).All(b => !b.Button.interactable));
            _t.Check("panel shows balance and shift timer",
                TombPanel.TributeText.Contains(_tomb.State.Balance.ToString())
                && TombPanel.TributeText.Contains(Localization.instance.Localize("$haugbui_panel_shift_left", "").Trim()), TombPanel.TributeText);
            TombPanel.Close();
        }

        private IEnumerator SameShiftNotChargedTwice()
        {
            int balance = _tomb.State.Balance;
            _tomb.SetToggle(TombToggles.Resting, true, out _);
            yield return SelfTestContext.WaitFor(() => _tomb.Machine.State == HaugbuiState.Dormant, 20f);
            _tomb.SetToggle(TombToggles.Resting, false, out _);
            yield return SelfTestContext.WaitFrames(2);
            _t.Check("Rest off in the same shift: emerges without paying again", _tomb.Creature != null && _tomb.State.Balance == balance,
                $"balance {_tomb.State.Balance}/{balance}");

            GameObject reloaded = null;
            yield return _t.Reload(_tomb.gameObject, go => reloaded = go);
            _tomb = reloaded != null ? reloaded.GetComponent<TombController>() : null;
            if (!_t.Check("tomb reloaded", _tomb != null)) yield break;
            yield return new WaitForSeconds(1f);
            _t.Check("balance, paid shift and slot survive reload; resumes unpaid",
                _tomb.State.Balance == balance && _tomb.State.LastPaidShift == WorldClock.ShiftId(_time.Now, FakeTime.DayLength)
                                               && _tomb.State.SlotIndex == 0 && _tomb.Creature != null,
                $"balance {_tomb.State.Balance}, slot {_tomb.State.SlotIndex}, out {_tomb.Creature != null}");
            yield return SelfTestContext.WaitFor(() => _collection.GetInventory().NrOfItems() == 0 && _tomb.Worker.Plan == null && _tomb.Carried.IsEmpty, 60f);
        }

        private IEnumerator NewShifts()
        {
            int balance = _tomb.State.Balance;
            _time.ToNextDusk(30);
            _tomb.TickNow();
            yield return SelfTestContext.WaitFor(() => _tomb.Machine.State == HaugbuiState.Dormant, 20f);
            _t.Check("new night without work: goes home, charges nothing", _tomb.Machine.State == HaugbuiState.Dormant && _tomb.State.Balance == balance,
                $"state {_tomb.Machine.State}, balance {_tomb.State.Balance}/{balance}");

            Add(_collection.GetInventory(), "Wood", 4, cheated: false);
            _tomb.TickNow();
            yield return SelfTestContext.WaitFrames(2);
            _t.Check("work appears mid-shift: charged once and emerges", _tomb.Creature != null && _tomb.State.Balance == balance - _tomb.ShiftCost,
                $"balance {_tomb.State.Balance}");
            _t.Check("cheated part is spent first", _tomb.State.CheatedBalance == Math.Max(0, Table.ValueOf("SilverNecklace") - _tomb.ShiftCost),
                $"cheated {_tomb.State.CheatedBalance}");
        }

        private IEnumerator Death()
        {
            int balance = _tomb.State.Balance;
            Kill();
            yield return SelfTestContext.WaitFor(() => _tomb.Machine.State == HaugbuiState.Dead, 5f);
            _t.Check("death: no refund, next return must be paid", _tomb.State.Balance == balance && _tomb.State.LastPaidShift == WorldClock.NoShift);
            ServantSlot slot = ServantRegistry.Read(_owner, _tomb.State.SlotIndex);
            _t.Check("death time kept in the servant slot", slot != null && !double.IsNaN(slot.DeathTime));

            _time.Offset += TombController.CooldownSeconds + 5;
            Add(_collection.GetInventory(), "Wood", 3, cheated: false);
            _tomb.TickNow();
            _t.Check("cooldown over at night without the Altar: waits for the next dusk", _tomb.Creature == null && _tomb.WaitRemaining > 0 && _time.IsNight,
                $"waits {_tomb.WaitRemaining:0}s");
            _time.ToNextDusk();
            _tomb.TickNow();
            yield return SelfTestContext.WaitFrames(2);
            _t.Check("returns at the next dusk as a new, charged shift", _tomb.Creature != null && _tomb.State.Balance == balance - _tomb.ShiftCost,
                $"balance {_tomb.State.Balance}, out {_tomb.Creature != null}");
            slot = ServantRegistry.Read(_owner, _tomb.State.SlotIndex);
            _t.Check("slot death time cleared on return", slot != null && double.IsNaN(slot.DeathTime));
        }

        private IEnumerator Altar()
        {
            HaugbuiStructure altar = _t.PlaceStructure(StructureKind.Altar, Ground(new Vector3(3, 0, 0)));
            if (!_t.Check("Altar placed", altar != null)) yield break;
            yield return SelfTestContext.WaitFrames(3);
            Give("Coins", _tomb.ShiftCost, false);
            for (int i = 0; i < _tomb.ShiftCost; i++) _tomb.Offer("Coins", _t.Player);
            int balance = _tomb.State.Balance;
            if (_tomb.Creature == null) yield return SetTime(FakeTime.Night);
            Kill();
            yield return SelfTestContext.WaitFor(() => _tomb.Machine.State == HaugbuiState.Dead, 5f);
            _time.Offset += TombController.CooldownSeconds - 30;
            Add(_collection.GetInventory(), "Wood", 2, cheated: false);
            _tomb.TickNow();
            _t.Check("Altar: still dead just before the cooldown ends", _tomb.Creature == null);
            _time.Offset += 35;
            _tomb.TickNow();
            yield return SelfTestContext.WaitFrames(2);
            _t.Check("Altar: returns at cooldown end, charged", _tomb.Creature != null && _tomb.State.Balance == balance - _tomb.ShiftCost,
                $"balance {_tomb.State.Balance}/{balance}, out {_tomb.Creature != null}");
            ZNetScene.instance.Destroy(altar.gameObject);
            yield return SelfTestContext.WaitFrames(3);
        }

        private IEnumerator TributeOff()
        {
            Config.HaugbuiConfig settings = HaugbuiPlugin.Instance.Settings;
            settings.TributeEnabled.Value = false;
            int balance = _tomb.State.Balance;
            _time.ToNextDusk();
            _tomb.TickNow();
            yield return SelfTestContext.WaitFrames(2);
            if (_tomb.Creature == null) _tomb.TickNow();
            _t.Check("TributeEnabled=false never charges", _tomb.State.Balance == balance && _tomb.Creature != null,
                $"balance {_tomb.State.Balance}/{balance}, out {_tomb.Creature != null}");
            settings.TributeEnabled.Value = true;
        }

        private IEnumerator Slots()
        {
            HaugbuiStructure second = _t.PlaceStructure(StructureKind.Tomb, Ground(new Vector3(0, 0, -8)));
            HaugbuiStructure third = _t.PlaceStructure(StructureKind.Tomb, Ground(new Vector3(-8, 0, 6)));
            yield return SelfTestContext.WaitFrames(3);
            TombController b = second != null ? second.GetComponent<TombController>() : null;
            TombController c = third != null ? third.GetComponent<TombController>() : null;
            if (!_t.Check("second and third tombs placed", b != null && c != null)) yield break;
            _t.Check("servants named in slot order", b.ServantName == ServantSlots.DefaultName(1) && c.ServantName == ServantSlots.DefaultName(2),
                $"{b.ServantName}, {c.ServantName}");
            _t.Check("a tomb built at night waits for the next dusk", b.WaitRemaining > 0 && b.Creature == null && _time.IsNight,
                $"waits {b.WaitRemaining:0}s");
            if (HaugbuiPlugin.Instance.Settings.MaxHaugbuisPerPlayer.Value == 3)
                _t.Check("fourth tomb refused at the default limit", PlacementCheck.Check(StructureKind.Tomb, Ground(new Vector3(8, 0, 8)), _t.Player) == PlacementResult.MaxTombs);
            else _t.Skip("fourth tomb refused at the default limit", "MaxHaugbuisPerPlayer is not 3");

            // Destroy a dead servant's tomb with a balance; rebuild on the same spot.
            Give("Coins", 4, false);
            Give("Ruby", 1, true);
            for (int i = 0; i < 4; i++) b.Offer("Coins", _t.Player);
            b.Offer("Ruby", _t.Player);
            // As if it had died a moment ago.
            double death = _time.Now - 10;
            b.State.DeathTime = death;
            b.Machine.Restore(HaugbuiState.Dead);
            b.TickNow();
            ServantRegistry.Update(_owner, b.State.SlotIndex, s => s.DeathTime = death);
            int slotIndex = b.State.SlotIndex;
            double waitBefore = b.WaitRemaining;
            Vector3 at = b.transform.position;
            int clean = b.State.Balance - b.State.CheatedBalance, cheated = b.State.CheatedBalance;
            b.GetComponent<WearNTear>().Remove();
            yield return new WaitForSeconds(1f);
            (int cleanCoins, int cheatedCoins) = CoinsNear(at);
            TrackDropsNear(at);
            _t.Check("destroyed tomb: balance dropped as Coins", cleanCoins == clean && cheatedCoins == cheated,
                $"clean {cleanCoins}/{clean}, cheated {cheatedCoins}/{cheated}");
            ServantSlot vacant = ServantRegistry.Read(_owner, slotIndex);
            _t.Check("destroyed tomb: slot vacant, name and death kept",
                vacant != null && vacant.IsVacant && vacant.Name == ServantSlots.DefaultName(slotIndex) && !double.IsNaN(vacant.DeathTime));

            HaugbuiStructure rebuilt = _t.PlaceStructure(StructureKind.Tomb, at);
            yield return SelfTestContext.WaitFrames(3);
            TombController d = rebuilt != null ? rebuilt.GetComponent<TombController>() : null;
            _t.Check("rebuilt tomb takes the vacant slot: same name, cooldown inherited",
                d != null && d.State.SlotIndex == slotIndex && d.ServantName == ServantSlots.DefaultName(slotIndex)
                && d.Machine.State == HaugbuiState.Dead && d.State.Balance == 0,
                d != null ? $"slot {d.State.SlotIndex}, {d.ServantName}, {d.Machine.State}, balance {d.State.Balance}" : "missing");
            if (d != null)
                _t.Check("rebuilding during cooldown is never faster than waiting", d.WaitRemaining >= waitBefore - 5,
                    $"{d.WaitRemaining:0}s vs {waitBefore:0}s");

            HaugbuiPlugin.Instance.Settings.TributeOnTombDestroyed.Value = "Forfeit";
            Give("Coins", 2, false);
            if (c != null)
            {
                c.Offer("Coins", _t.Player);
                c.Offer("Coins", _t.Player);
                Vector3 cAt = c.transform.position;
                c.GetComponent<WearNTear>().Remove();
                yield return new WaitForSeconds(1f);
                (int forfeitClean, int forfeitCheated) = CoinsNear(cAt);
                TrackDropsNear(cAt);
                _t.Check("Forfeit drops nothing", forfeitClean == 0 && forfeitCheated == 0, $"{forfeitClean + forfeitCheated} coins");
            }
            HaugbuiPlugin.Instance.Settings.TributeOnTombDestroyed.Value = "DropCoins";
        }

        private (int Clean, int Cheated) CoinsNear(Vector3 at)
        {
            int clean = 0, cheated = 0;
            foreach (ItemDrop drop in UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
            {
                if (drop == null || ItemBridge.PrefabName(drop.m_itemData) != "Coins" || Vector3.Distance(drop.transform.position, at) > 4f) continue;
                if (drop.m_itemData.m_cheated) cheated += drop.m_itemData.m_stack;
                else clean += drop.m_itemData.m_stack;
                _t.Track(drop.gameObject);
            }
            return (clean, cheated);
        }

        private void Kill()
        {
            if (_tomb.Creature == null) return;
            var hit = new HitData();
            hit.m_damage.m_damage = 100000f;
            _tomb.Creature.Character.Damage(hit);
        }

        private void Give(string prefab, int amount, bool cheated) => Add(PlayerInventory, prefab, amount, cheated);

        private bool IsValuable(ItemDrop.ItemData item) => Table.ValueOf(ItemBridge.PrefabName(item)) > 0;

        /// <summary>The player's own valuables leave the inventory for the run, so offers only ever take test items.</summary>
        private void HoldValuables()
        {
            foreach (ItemDrop.ItemData item in PlayerInventory.GetAllItems().Where(IsValuable).ToList())
            {
                _held.Add(item.Clone());
                PlayerInventory.RemoveItem(item);
            }
        }

        private void RestoreValuables()
        {
            foreach (ItemDrop.ItemData item in PlayerInventory.GetAllItems().Where(IsValuable).ToList()) PlayerInventory.RemoveItem(item);
            foreach (ItemDrop.ItemData item in _held) ItemBridge.AddTo(PlayerInventory, ItemBridge.FromItemData(item));
            _held.Clear();
        }

        private void TrackDropsNear(Vector3 at)
        {
            foreach (ItemDrop drop in UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
                if (drop != null && Vector3.Distance(drop.transform.position, at) < 5f) _t.Track(drop.gameObject);
        }

        private static void Add(Inventory inventory, string prefab, int amount, bool cheated)
        {
            var stack = new ItemStack { Prefab = prefab, Amount = amount, WorldLevel = Game.m_worldLevel, Cheated = cheated };
            ItemBridge.RefreshShared(stack);
            ItemBridge.AddTo(inventory, stack);
        }

        private int Count(string prefab) => ItemBridge.CountPrefab(PlayerInventory, prefab);

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
