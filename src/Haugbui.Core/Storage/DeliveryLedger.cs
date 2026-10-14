using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;
using Haugbui.Core.Persistence;

namespace Haugbui.Core.Storage
{
    public enum RequestEditResult
    {
        Ok,
        InvalidItem,
        InvalidAmount,
        TooManyLines,
        NoSuchLine
    }

    /// <summary>
    /// HB-082..088: request lines of one Delivery Chest. Remaining = requested − delivered; at most one open line per item.
    /// A line with nothing remaining is complete and stays listed until cleared.
    /// </summary>
    public sealed class DeliveryLedger
    {
        public const int MaxLines = 10;
        public const int MaxRequested = 99999;

        private readonly List<DeliveryRequest> _lines;

        public DeliveryLedger(IEnumerable<DeliveryRequest> lines = null)
        {
            _lines = (lines ?? Enumerable.Empty<DeliveryRequest>())
                .Where(l => !string.IsNullOrEmpty(l.ItemPrefab))
                .Select(l => new DeliveryRequest { ItemPrefab = l.ItemPrefab, Requested = Math.Max(0, l.Requested), Delivered = Math.Max(0, l.Delivered) })
                .ToList();
        }

        public IReadOnlyList<DeliveryRequest> Lines => _lines;

        public static bool IsComplete(DeliveryRequest line) => line.Remaining <= 0;

        public IEnumerable<DeliveryRequest> OpenLines => _lines.Where(l => !IsComplete(l));

        public DeliveryRequest OpenLine(string prefab) => _lines.FirstOrDefault(l => l.ItemPrefab == prefab && !IsComplete(l));

        public int Remaining(string prefab) => OpenLines.Where(l => l.ItemPrefab == prefab).Sum(l => l.Remaining);

        /// <summary>Adds a line, or sets the requested amount of the open line for the same item.</summary>
        public RequestEditResult Request(string prefab, int requested)
        {
            if (string.IsNullOrEmpty(prefab)) return RequestEditResult.InvalidItem;
            if (requested <= 0 || requested > MaxRequested) return RequestEditResult.InvalidAmount;
            DeliveryRequest open = OpenLine(prefab);
            if (open != null) return SetRequested(_lines.IndexOf(open), requested);
            if (_lines.Count >= MaxLines) return RequestEditResult.TooManyLines;
            _lines.Add(new DeliveryRequest { ItemPrefab = prefab, Requested = requested });
            return RequestEditResult.Ok;
        }

        /// <summary>HB-085: a new amount at or below what was delivered completes the line.</summary>
        public RequestEditResult SetRequested(int index, int requested)
        {
            if (index < 0 || index >= _lines.Count) return RequestEditResult.NoSuchLine;
            if (requested <= 0 || requested > MaxRequested) return RequestEditResult.InvalidAmount;
            DeliveryRequest line = _lines[index];
            line.Requested = Math.Max(requested, line.Delivered);
            return RequestEditResult.Ok;
        }

        public RequestEditResult Cancel(int index)
        {
            if (index < 0 || index >= _lines.Count) return RequestEditResult.NoSuchLine;
            _lines.RemoveAt(index);
            return RequestEditResult.Ok;
        }

        public int ClearCompleted() => _lines.RemoveAll(IsComplete);

        /// <summary>Counts a delivery against the open line; never above what remains. Returns the amount counted.</summary>
        public int RecordDelivered(string prefab, int amount)
        {
            DeliveryRequest line = OpenLine(prefab);
            if (line == null || amount <= 0) return 0;
            int counted = Math.Min(amount, line.Remaining);
            line.Delivered += counted;
            return counted;
        }

        public List<DeliveryRequest> ToList() =>
            _lines.Select(l => new DeliveryRequest { ItemPrefab = l.ItemPrefab, Requested = l.Requested, Delivered = l.Delivered }).ToList();

        /// <summary>HB-087: remaining quantities per item over many chests; production jobs must leave these in storage.</summary>
        public static Dictionary<string, int> Reserved(IEnumerable<DeliveryLedger> ledgers)
        {
            var totals = new Dictionary<string, int>();
            foreach (DeliveryRequest line in ledgers.SelectMany(l => l.OpenLines))
                totals[line.ItemPrefab] = (totals.TryGetValue(line.ItemPrefab, out int n) ? n : 0) + line.Remaining;
            return totals;
        }
    }

    public readonly struct PlannedTake
    {
        public PlannedTake(string source, ItemStack item)
        {
            Source = source;
            Item = item;
        }

        public string Source { get; }

        /// <summary>Item data and amount (Item.Amount).</summary>
        public ItemStack Item { get; }
    }

    public sealed class DeliveryTrip
    {
        public List<PlannedTake> Takes { get; } = new List<PlannedTake>();

        /// <summary>Identical item data merged; each amount is what goes into the chest.</summary>
        public List<ItemStack> Puts { get; } = new List<ItemStack>();

        public int Total => Puts.Sum(p => p.Amount);
    }

    /// <summary>
    /// HB-083/084/088: one trip for one request line. Never more than remaining, than the chest can hold, or than the
    /// Haugbui can carry; everything taken is planned to go into the chest. Carried items of the item count first.
    /// </summary>
    public static class DeliveryPlanner
    {
        public static DeliveryTrip Plan(string prefab, int remaining, ContainerSnapshot chest, CarriedInventory carried,
            IEnumerable<ContainerSnapshot> sources, Vec3 from)
        {
            var trip = new DeliveryTrip();
            if (remaining <= 0 || chest == null) return trip;
            ContainerSnapshot room = chest.Clone();
            int left = remaining;

            foreach (ItemStack stack in DepositPlanner.Merge(carried.Stacks.Where(s => s.Prefab == prefab), Enumerable.Empty<ItemStack>()))
            {
                if (left <= 0) break;
                int n = room.Accept(stack, Math.Min(left, stack.Amount));
                if (n <= 0) continue;
                AddPut(trip, stack, n);
                left -= n;
            }

            var simulated = new CarriedInventory(carried.Slots, carried.Capacity);
            simulated.Restore(carried.Stacks.Select(s => s.Copy(s.Amount)));
            foreach (ContainerSnapshot source in sources.OrderBy(s => Vec3.Distance(from, s.Position)).ThenBy(s => s.Id, StringComparer.Ordinal))
            {
                foreach (ItemStack stack in DepositPlanner.Merge(source.Items.Where(s => s.Prefab == prefab), Enumerable.Empty<ItemStack>()))
                {
                    if (left <= 0) return trip;
                    int n = Math.Min(Math.Min(left, stack.Amount), room.RoomFor(stack));
                    if (n <= 0) continue;
                    n = simulated.Add(stack.Copy(n));
                    if (n <= 0) continue;
                    room.Accept(stack, n);
                    trip.Takes.Add(new PlannedTake(source.Id, stack.Copy(n)));
                    AddPut(trip, stack, n);
                    left -= n;
                }
            }
            return trip;
        }

        private static void AddPut(DeliveryTrip trip, ItemStack stack, int amount)
        {
            ItemStack same = trip.Puts.FirstOrDefault(p => p.StacksWith(stack));
            if (same != null) same.Amount += amount;
            else trip.Puts.Add(stack.Copy(amount));
        }
    }
}
