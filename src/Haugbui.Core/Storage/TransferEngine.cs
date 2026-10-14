using System;
using System.Linq;
using Haugbui.Core.Creature;

namespace Haugbui.Core.Storage
{
    /// <summary>One end of a transfer: a container, the carried inventory, later stations and ground drops.</summary>
    public interface IItemEndpoint
    {
        string Id { get; }

        /// <summary>Null when usable now; otherwise a short reason (in use, not owner, not loaded...).</summary>
        string Unavailable();

        /// <summary>Amount identical to <paramref name="item"/> (<see cref="ItemStack.StacksWith"/>).</summary>
        int Count(ItemStack item);

        /// <summary>How many of <paramref name="item"/> can be added without exceeding any limit.</summary>
        int RoomFor(ItemStack item);

        /// <summary>
        /// Adds up to item.Amount, never more than <see cref="RoomFor"/>: tops up the stacks <see cref="StackingRules"/>
        /// allows (identical data by default) and never clears a cheated flag. Returns the amount added.
        /// </summary>
        int Add(ItemStack item);

        /// <summary>Removes only identical items, so a cheated item never comes out clean; returns the amount removed.</summary>
        int Remove(ItemStack item, int amount);

        /// <summary>Amount of the same item, quality and world level, whatever its other data.</summary>
        int CountKind(ItemStack item);

        /// <summary>Rollback only: removes identical items first, then any of the same kind; returns the amount removed.</summary>
        int RemoveKind(ItemStack item, int amount);
    }

    public enum TransferStatus
    {
        Moved,
        SourceUnavailable,
        DestinationUnavailable,
        NothingToMove,
        NoRoom,
        RolledBack,
        Failed
    }

    public sealed class TransferRecord
    {
        public string Source;
        public string Destination;
        public string Item;
        public int Requested;
        public int Moved;
        public TransferStatus Status;
        public string Reason;
        public int SourceBefore, SourceAfter, DestinationBefore, DestinationAfter;

        /// <summary>False only for a duplicate left by a failed rollback; an item is never lost.</summary>
        public bool Conserved => SourceBefore + DestinationBefore == SourceAfter + DestinationAfter;

        public override string ToString() =>
            $"{Status} {Item} {Moved}/{Requested} {Source} -> {Destination} " +
            $"(src {SourceBefore}->{SourceAfter}, dst {DestinationBefore}->{DestinationAfter}){(Reason != null ? " " + Reason : "")}";
    }

    /// <summary>
    /// HB-045/061/175, ARCHITECTURE §7: destination first, then the source, in one call; if the source gives less than
    /// was added, the surplus is taken back from the destination. Worst case is a duplicate, never a loss.
    /// </summary>
    public sealed class TransferEngine
    {
        public event Action<TransferRecord> Recorded;

        public TransferRecord Move(IItemEndpoint source, IItemEndpoint destination, ItemStack item, int amount)
        {
            var record = new TransferRecord
            {
                Source = source?.Id, Destination = destination?.Id, Item = item?.Prefab, Requested = amount
            };
            if (source == null || item == null || amount <= 0) return Finish(record, TransferStatus.NothingToMove, null);
            string reason = source.Unavailable();
            if (reason != null) return Finish(record, TransferStatus.SourceUnavailable, reason);
            if (destination == null) return Finish(record, TransferStatus.DestinationUnavailable, "missing");
            reason = destination.Unavailable();
            if (reason != null) return Finish(record, TransferStatus.DestinationUnavailable, reason);

            record.SourceBefore = record.SourceAfter = Measured(source, item);
            record.DestinationBefore = record.DestinationAfter = Measured(destination, item);
            int n = Math.Min(amount, source.Count(item));
            if (n <= 0) return Finish(record, TransferStatus.NothingToMove, null);
            n = Math.Min(n, destination.RoomFor(item));
            if (n <= 0) return Finish(record, TransferStatus.NoRoom, null);

            int added;
            try
            {
                added = Math.Max(0, destination.Add(item.Copy(n)));
            }
            catch (Exception e)
            {
                // Nothing is removed from the source, so a partial add can only duplicate.
                Measure(record, source, destination, item);
                return Finish(record, TransferStatus.Failed, "add: " + e.Message);
            }
            if (added == 0)
            {
                Measure(record, source, destination, item);
                return Finish(record, TransferStatus.NoRoom, null);
            }

            int removed = SafeRemove(source, item, added);
            record.Moved = removed;
            if (removed < added)
            {
                // With vanilla stacking the added items may have joined a stack with other data (StackingRules).
                int undone = StackingRules.StrictItemData
                    ? SafeRemove(destination, item, added - removed)
                    : SafeRemoveKind(destination, item, added - removed);
                Measure(record, source, destination, item);
                return Finish(record, TransferStatus.RolledBack,
                    $"source gave {removed}/{added}, undone {undone}" + (undone < added - removed ? " (duplicate left)" : ""));
            }
            Measure(record, source, destination, item);
            return Finish(record, TransferStatus.Moved, null);
        }

        private static int SafeRemove(IItemEndpoint endpoint, ItemStack item, int amount)
        {
            try
            {
                return Math.Max(0, Math.Min(amount, endpoint.Remove(item, amount)));
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static int SafeRemoveKind(IItemEndpoint endpoint, ItemStack item, int amount)
        {
            try
            {
                return Math.Max(0, Math.Min(amount, endpoint.RemoveKind(item, amount)));
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>Identical items in strict mode; the whole kind with vanilla stacking, where a moved item may join other data.</summary>
        private static int Measured(IItemEndpoint endpoint, ItemStack item) =>
            StackingRules.StrictItemData ? endpoint.Count(item) : endpoint.CountKind(item);

        private static void Measure(TransferRecord record, IItemEndpoint source, IItemEndpoint destination, ItemStack item)
        {
            record.SourceAfter = Measured(source, item);
            record.DestinationAfter = Measured(destination, item);
        }

        private TransferRecord Finish(TransferRecord record, TransferStatus status, string reason)
        {
            record.Status = status;
            record.Reason = reason;
            Recorded?.Invoke(record);
            return record;
        }
    }

    /// <summary>The Haugbui's carried inventory as a transfer endpoint.</summary>
    public sealed class CarriedEndpoint : IItemEndpoint
    {
        private readonly CarriedInventory _inventory;

        public CarriedEndpoint(string id, CarriedInventory inventory)
        {
            Id = id;
            _inventory = inventory;
        }

        public string Id { get; }
        public string Unavailable() => null;
        public int Count(ItemStack item) => _inventory.Count(item);
        public int RoomFor(ItemStack item) => _inventory.CanAccept(item.Copy(int.MaxValue / 2));
        public int Add(ItemStack item) => _inventory.Add(item);
        public int Remove(ItemStack item, int amount) => _inventory.Remove(item, amount);
        public int CountKind(ItemStack item) => _inventory.Stacks.Where(s => s.SameKind(item)).Sum(s => s.Amount);

        public int RemoveKind(ItemStack item, int amount)
        {
            int removed = _inventory.Remove(item, amount);
            foreach (ItemStack stack in _inventory.Stacks.Where(s => s.SameKind(item)).ToList())
            {
                if (removed >= amount) break;
                removed += _inventory.Remove(stack, amount - removed);
            }
            return removed;
        }
    }
}
