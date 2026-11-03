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

        /// <summary>
        /// Replaces the lines. An item that was already requested keeps what was delivered, and an amount at or
        /// below that completes the line, same as <see cref="SetRequested"/>. A refused edit changes nothing.
        /// </summary>
        public RequestEditResult Replace(IEnumerable<(string Prefab, int Requested)> lines)
        {
            var desired = new List<(string Prefab, int Requested)>();
            var seen = new HashSet<string>();
            foreach ((string prefab, int requested) in lines ?? Enumerable.Empty<(string, int)>())
            {
                if (string.IsNullOrEmpty(prefab)) return RequestEditResult.InvalidItem;
                if (requested <= 0 || requested > MaxRequested) return RequestEditResult.InvalidAmount;
                if (!seen.Add(prefab)) return RequestEditResult.InvalidItem;
                desired.Add((prefab, requested));
            }
            if (desired.Count > MaxLines) return RequestEditResult.TooManyLines;

            var delivered = new Dictionary<string, int>();
            foreach (DeliveryRequest existing in _lines)
            {
                if (!delivered.TryGetValue(existing.ItemPrefab, out int count) || existing.Delivered > count)
                    delivered[existing.ItemPrefab] = existing.Delivered;
            }
            _lines.Clear();
            foreach ((string prefab, int requested) in desired)
            {
                int already = delivered.TryGetValue(prefab, out int count) ? count : 0;
                _lines.Add(new DeliveryRequest
                {
                    ItemPrefab = prefab,
                    Requested = Math.Max(requested, already),
                    Delivered = already,
                });
            }
            return RequestEditResult.Ok;
        }

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
    /// HB-083/084/088: one trip. Never more than remaining, than the chest can hold, or than the Haugbui can carry;
    /// everything taken is planned to go into the chest. Carried items of the item count first.
    /// <see cref="Combine"/> fills one trip with as many open lines as fit.
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
                int budget = source.Takeable(prefab);
                foreach (ItemStack stack in DepositPlanner.Merge(source.Items.Where(s => s.Prefab == prefab), Enumerable.Empty<ItemStack>()))
                {
                    if (left <= 0) return trip;
                    if (budget <= 0) break;
                    int n = Math.Min(Math.Min(left, stack.Amount), room.RoomFor(stack));
                    n = Math.Min(n, budget);
                    if (n <= 0) continue;
                    n = simulated.Add(stack.Copy(n));
                    if (n <= 0) continue;
                    room.Accept(stack, n);
                    trip.Takes.Add(new PlannedTake(source.Id, stack.Copy(n)));
                    AddPut(trip, stack, n);
                    budget -= n;
                    left -= n;
                }
            }
            return trip;
        }

        /// <summary>One walk: takes for every line that still fits, then the puts into the same chest.</summary>
        public static DeliveryTrip Combine(IEnumerable<(string Prefab, int Remaining)> lines, ContainerSnapshot chest,
            CarriedInventory carried, IEnumerable<ContainerSnapshot> sources, Vec3 from)
        {
            var combined = new DeliveryTrip();
            if (chest == null || lines == null) return combined;
            ContainerSnapshot room = chest.Clone();
            var stock = new List<ContainerSnapshot>();
            if (sources != null)
                foreach (ContainerSnapshot source in sources) stock.Add(source.Clone());
            var sim = new CarriedInventory(carried.Slots, carried.Capacity);
            sim.Restore(carried.Stacks.Select(s => s.Copy(s.Amount)));

            foreach ((string prefab, int remaining) in lines)
            {
                if (remaining <= 0 || string.IsNullOrEmpty(prefab)) continue;
                int inBag = sim.CountPrefab(prefab);
                DeliveryTrip trip = Plan(prefab, remaining, room, sim, stock, from);
                if (trip.Total <= 0) continue;
                int fromBag = Math.Min(trip.Puts.Where(p => p.Prefab == prefab).Sum(p => p.Amount), inBag);
                if (fromBag > 0) ReserveCommitted(sim, prefab, fromBag);
                foreach (PlannedTake take in trip.Takes)
                {
                    combined.Takes.Add(take);
                    Consume(stock, take);
                    sim.Add(take.Item);
                }
                foreach (ItemStack put in trip.Puts)
                {
                    AddPut(combined, put, put.Amount);
                    room.Accept(put, put.Amount);
                }
            }
            return combined;
        }

        /// <summary>
        /// Hides items this line will deliver so a later line does not deliver them again, and keeps their weight
        /// and slot reserved: they stay in the real bag until every take of the trip is done.
        /// </summary>
        private static void ReserveCommitted(CarriedInventory sim, string prefab, int amount)
        {
            int left = amount;
            float weight = 0f;
            int stacksBefore = sim.Stacks.Count;
            foreach (ItemStack stack in sim.Stacks.Where(s => s.Prefab == prefab && s.Amount > 0).ToList())
            {
                if (left <= 0) break;
                int n = Math.Min(left, stack.Amount);
                weight += n * stack.UnitWeight;
                sim.Remove(stack, n);
                left -= n;
            }
            int stacksFreed = Math.Max(0, stacksBefore - sim.Stacks.Count);
            // Removing the stack dropped its weight. Capacity loses that weight a second time so the free room shrinks.
            sim.Capacity = Math.Max(0f, sim.Capacity - 2f * weight);
            sim.Slots = Math.Max(sim.Stacks.Count, sim.Slots - stacksFreed);
        }

        private static void Consume(List<ContainerSnapshot> stock, PlannedTake take)
        {
            ContainerSnapshot box = stock.FirstOrDefault(s => s.Id == take.Source);
            if (box == null) return;
            int left = take.Item.Amount;
            foreach (ItemStack stack in box.Items.Where(i => i.StacksWith(take.Item)).ToList())
            {
                if (left <= 0) break;
                int n = Math.Min(left, stack.Amount);
                stack.Amount -= n;
                left -= n;
            }
            box.Items.RemoveAll(i => i.Amount <= 0);
        }

        private static void AddPut(DeliveryTrip trip, ItemStack stack, int amount)
        {
            ItemStack same = trip.Puts.FirstOrDefault(p => p.StacksWith(stack));
            if (same != null) same.Amount += amount;
            else trip.Puts.Add(stack.Copy(amount));
        }
    }
}
