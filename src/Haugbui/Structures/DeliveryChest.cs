using System.Collections.Generic;
using Haugbui.Core.Persistence;
using Haugbui.Core.Storage;
using Haugbui.Diagnostics;
using Haugbui.Integration;
using UnityEngine;

namespace Haugbui.Structures
{
    /// <summary>HB-T-050/051: the request ledger of one Delivery Chest, stored in its ZDO (HB-086).</summary>
    internal sealed class DeliveryChest : MonoBehaviour
    {
        private PersistentSlot<List<DeliveryRequest>> _slot;
        private uint _readRevision = uint.MaxValue;
        private DeliveryLedger _ledger = new DeliveryLedger();

        internal Container Container { get; private set; }
        internal ZNetView View { get; private set; }
        internal bool ReadOnly => _slot != null && _slot.State == SlotState.ReadOnly;

        /// <summary>The current lines; re-read when the ZDO changed (another peer, a reload).</summary>
        internal DeliveryLedger Ledger
        {
            get
            {
                Refresh();
                return _ledger;
            }
        }

        private void Awake()
        {
            View = GetComponent<ZNetView>();
            Container = GetComponent<Container>();
            Refresh();
        }

        private void Refresh()
        {
            ZDO zdo = View != null && View.IsValid() ? View.GetZDO() : null;
            if (zdo == null || zdo.DataRevision == _readRevision) return;
            _slot = new PersistentSlot<List<DeliveryRequest>>(new ZdoBlobStore(zdo), ZdoKeys.Requests, DeliveryRequestsSerializer.Instance);
            List<DeliveryRequest> lines = _slot.Load();
            if (_slot.State == SlotState.ReadOnly)
                Log.ErrorRateLimited("requests:" + zdo.m_uid, $"Delivery Chest {zdo.m_uid}: requests unreadable ({_slot.LastReadStatus}); kept untouched.");
            _ledger = new DeliveryLedger(lines);
            _readRevision = zdo.DataRevision;
        }

        /// <summary>Applies an edit and saves it; only the ZDO owner writes (HB-171).</summary>
        internal bool Edit(System.Func<DeliveryLedger, bool> change)
        {
            DeliveryLedger ledger = Ledger;
            if (ReadOnly || View == null || !View.IsValid() || !View.IsOwner()) return false;
            if (!change(ledger)) return false;
            if (!_slot.TrySave(ledger.ToList())) return false;
            _readRevision = View.GetZDO().DataRevision;
            return true;
        }

        internal int RecordDelivered(string prefab, int amount)
        {
            int counted = 0;
            Edit(ledger => (counted = ledger.RecordDelivered(prefab, amount)) > 0);
            return counted;
        }
    }
}
