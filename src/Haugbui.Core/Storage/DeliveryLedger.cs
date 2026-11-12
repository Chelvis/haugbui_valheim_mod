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

    /// <summary>One row of a delivery list, as the player saved it. Delivered is not part of the edit.</summary>
    public readonly struct RequestDraft
    {
        public RequestDraft(string prefab, int requested, bool keepAmount = false)
        {
            Prefab = prefab;
            Requested = requested;
            KeepAmount = keepAmount;
        }

        public string Prefab { get; }
        public int Requested { get; }
        public bool KeepAmount { get; }
    }

    /// <summary>
    /// Request lines of one Delivery Chest. At most one line per item.
    /// A quota counts what this saved line has received, and leaves the list when that quota is met.
    /// A keep-amount line stays: what it still needs is the requested amount minus what the chest holds now.
    /// Saving a line starts its delivered count over.
    /// </summary>
    public sealed class DeliveryLedger
    {
        public const int MaxLines = 10;
        public const int MaxRequested = 99999;

        private readonly List<DeliveryRequest> _lines;

        public DeliveryLedger(IEnumerable<DeliveryRequest> lines = null)
        {
            _lines = (lines ?? Enumerable.Empty<DeliveryRequest>())
                .Where(l => l != null && !string.IsNullOrEmpty(l.ItemPrefab))
                .Select(l => new DeliveryRequest
                {
                    ItemPrefab = l.ItemPrefab,
                    Requested = Math.Max(0, l.Requested),
                    Delivered = Math.Max(0, l.Delivered),
                    KeepAmount = l.KeepAmount,
                })
                .Where(l => l.Requested > 0 && (l.KeepAmount || l.Delivered < l.Requested))
                .ToList();
        }

        public IReadOnlyList<DeliveryRequest> Lines => _lines;

        /// <summary>A quota that has arrived. A keep-amount line is never finished this way.</summary>
        public static bool IsComplete(DeliveryRequest line) => line != null && !line.KeepAmount && line.Delivered >= line.Requested;

        public IEnumerable<DeliveryRequest> OpenLines => _lines.Where(l => !IsComplete(l));

        public DeliveryRequest OpenLine(string prefab) => _lines.FirstOrDefault(l => l.ItemPrefab == prefab && !IsComplete(l));

        /// <summary>What a quota line still owes. A keep-amount line reports its requested amount; the chest count is applied by <see cref="Owed"/>.</summary>
        public int Remaining(string prefab) => OpenLines.Where(l => l.ItemPrefab == prefab).Sum(l => l.KeepAmount ? l.Requested : l.Remaining);

        /// <summary>Keep-amount compares the chest. A quota counts only what this saved line has received.</summary>
        public static int Owed(DeliveryRequest line, int inChest)
        {
            if (line == null || IsComplete(line)) return 0;
            if (line.KeepAmount) return Math.Max(0, line.Requested - Math.Max(0, inChest));
            return line.Remaining;
        }

        /// <summary>Adds a line, or replaces the line for the same item. Either way the delivered count starts over.</summary>
        public RequestEditResult Request(string prefab, int requested, bool keepAmount = false)
        {
            if (string.IsNullOrEmpty(prefab)) return RequestEditResult.InvalidItem;
            if (requested <= 0 || requested > MaxRequested) return RequestEditResult.InvalidAmount;
            DeliveryRequest existing = _lines.FirstOrDefault(l => l.ItemPrefab == prefab);
            if (existing != null) return SetRequested(_lines.IndexOf(existing), requested, keepAmount);
            if (_lines.Count >= MaxLines) return RequestEditResult.TooManyLines;
            _lines.Add(new DeliveryRequest { ItemPrefab = prefab, Requested = requested, KeepAmount = keepAmount });
            return RequestEditResult.Ok;
        }

        /// <summary>The saved amount is a new quota. What this line had already delivered is dropped.</summary>
        public RequestEditResult SetRequested(int index, int requested, bool? keepAmount = null)
        {
            if (index < 0 || index >= _lines.Count) return RequestEditResult.NoSuchLine;
            if (requested <= 0 || requested > MaxRequested) return RequestEditResult.InvalidAmount;
            DeliveryRequest line = _lines[index];
            line.Requested = requested;
            line.Delivered = 0;
            if (keepAmount.HasValue) line.KeepAmount = keepAmount.Value;
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
        /// Replaces the lines. Each saved line starts with nothing delivered, including one that replaces a line
        /// for the same item. A refused edit changes nothing.
        /// </summary>
        public RequestEditResult Replace(IEnumerable<RequestDraft> lines)
        {
            var desired = new List<RequestDraft>();
            var seen = new HashSet<string>();
            foreach (RequestDraft line in lines ?? Enumerable.Empty<RequestDraft>())
            {
                if (string.IsNullOrEmpty(line.Prefab)) return RequestEditResult.InvalidItem;
                if (line.Requested <= 0 || line.Requested > MaxRequested) return RequestEditResult.InvalidAmount;
                if (!seen.Add(line.Prefab)) return RequestEditResult.InvalidItem;
                desired.Add(line);
            }
            if (desired.Count > MaxLines) return RequestEditResult.TooManyLines;

            _lines.Clear();
            foreach (RequestDraft line in desired)
            {
                _lines.Add(new DeliveryRequest
                {
                    ItemPrefab = line.Prefab,
                    Requested = line.Requested,
                    Delivered = 0,
                    KeepAmount = line.KeepAmount,
                });
            }
            return RequestEditResult.Ok;
        }

        /// <summary>
        /// Counts a quota delivery, and removes the line once the quota is met.
        /// A keep-amount line is left as it is: the chest, not this count, decides what is still owed.
        /// Returns the amount counted.
        /// </summary>
        public int RecordDelivered(string prefab, int amount)
        {
            DeliveryRequest line = OpenLine(prefab);
            if (line == null || amount <= 0) return 0;
            if (line.KeepAmount) return amount;
            int counted = Math.Min(amount, line.Remaining);
            line.Delivered += counted;
            if (IsComplete(line)) _lines.Remove(line);
            return counted;
        }

        public List<DeliveryRequest> ToList() =>
            _lines.Select(l => new DeliveryRequest
            {
                ItemPrefab = l.ItemPrefab,
                Requested = l.Requested,
                Delivered = l.Delivered,
                KeepAmount = l.KeepAmount,
            }).ToList();

        /// <summary>What open lines still need from storage. Pass the chest count so a keep-amount line reserves only its shortfall.</summary>
        public static Dictionary<string, int> Reserved(IEnumerable<(DeliveryLedger Ledger, Func<string, int> InChest)> sources)
        {
            var totals = new Dictionary<string, int>();
            if (sources == null) return totals;
            foreach ((DeliveryLedger ledger, Func<string, int> inChest) in sources)
            {
                if (ledger == null) continue;
                foreach (DeliveryRequest line in ledger.OpenLines)
                {
                    int have = inChest != null ? inChest(line.ItemPrefab) : 0;
                    int owed = Owed(line, have);
                    if (owed <= 0) continue;
                    totals[line.ItemPrefab] = (totals.TryGetValue(line.ItemPrefab, out int n) ? n : 0) + owed;
                }
            }
            return totals;
        }

        public static Dictionary<string, int> Reserved(IEnumerable<DeliveryLedger> ledgers) =>
            Reserved((ledgers ?? Enumerable.Empty<DeliveryLedger>()).Select(l => (l, (Func<string, int>)null)));
    }

    public readonly struct PlannedTake
    {
        public PlannedTake(string source, ItemStack item) : this(source, item, 0)
        {
        }

        public PlannedTake(string source, ItemStack item, int dropped)
        {
            Source = source;
            Item = item;
            Dropped = dropped;
        }

        public string Source { get; }

        /// <summary>Item data and amount (Item.Amount). For a pile, the part that fits in the bag.</summary>
        public ItemStack Item { get; }

        /// <summary>The rest of a dismantled pile, which becomes a ground drop. Zero for chests and drops.</summary>
        public int Dropped { get; }
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
            IEnumerable<ContainerSnapshot> sources, Vec3 from, IEnumerable<MaterialPile> piles = null)
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

            var opened = new HashSet<string>();
            while (left > 0)
            {
                MaterialPile next = PileSource.Nearest(piles, prefab, null, from, opened);
                if (next == null || room.RoomFor(next.Refund) <= 0) break;
                PlannedTake? pile = PileSource.Open(piles, prefab, null, simulated, from, opened);
                if (pile == null) break;
                int n = Math.Min(left, Math.Min(pile.Value.Item.Amount, room.RoomFor(pile.Value.Item)));
                if (n <= 0) break;
                room.Accept(pile.Value.Item, n);
                trip.Takes.Add(pile.Value);
                AddPut(trip, pile.Value.Item, n);
                left -= n;
            }
            return trip;
        }

        /// <summary>One walk: takes for every line that still fits, then the puts into the same chest.</summary>
        public static DeliveryTrip Combine(IEnumerable<(string Prefab, int Remaining)> lines, ContainerSnapshot chest,
            CarriedInventory carried, IEnumerable<ContainerSnapshot> sources, Vec3 from, IEnumerable<MaterialPile> piles = null)
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
                DeliveryTrip trip = Plan(prefab, remaining, room, sim, stock, from, piles);
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
