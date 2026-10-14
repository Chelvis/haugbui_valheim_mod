using System.Collections.Generic;
using Haugbui.Core.Coordination;
using Haugbui.Core.Storage;
using Haugbui.Diagnostics;
using UnityEngine;

namespace Haugbui.Coordination
{
    /// <summary>Session-wide reservations and transfer ledger; cleared on every world load (never persisted).</summary>
    internal static class Reservations
    {
        private const int LedgerSize = 64;
        private static readonly Queue<TransferRecord> RecentRecords = new Queue<TransferRecord>();

        static Reservations()
        {
            Transfers.Recorded += Record;
        }

        public static ReservationTable Table { get; } = new ReservationTable(() => Time.timeAsDouble);
        public static TransferEngine Transfers { get; } = new TransferEngine();
        public static IEnumerable<TransferRecord> Ledger => RecentRecords;
        public static int DuplicatesLogged { get; private set; }

        public static void Clear()
        {
            Table.Clear();
            RecentRecords.Clear();
        }

        private static void Record(TransferRecord record)
        {
            if (record.Status == TransferStatus.NothingToMove) return;
            RecentRecords.Enqueue(record);
            while (RecentRecords.Count > LedgerSize) RecentRecords.Dequeue();
            if (!record.Conserved)
            {
                DuplicatesLogged++;
                Log.Error("Transfer left a duplicate (never a loss): " + record);
            }
            else if (record.Status == TransferStatus.RolledBack || record.Status == TransferStatus.Failed)
            {
                Log.Warning("Transfer " + record);
            }
            else
            {
                Log.Debug("Transfer " + record);
            }
        }
    }
}
