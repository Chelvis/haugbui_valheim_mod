using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Persistence;

namespace Haugbui.Core.Creature
{
    public sealed class ItemStack
    {
        public string Prefab { get; set; } = string.Empty;
        public int Amount { get; set; }
        public int Quality { get; set; } = 1;
        public int Variant { get; set; }
        public int WorldLevel { get; set; }
        public bool Cheated { get; set; }
        public long CrafterId { get; set; }
        public string CrafterName { get; set; } = string.Empty;

        /// <summary>Never null; the game's per-item custom data.</summary>
        public Dictionary<string, string> CustomData { get; set; } = new Dictionary<string, string>();

        /// <summary>Not persisted: refreshed from the item prefab when loaded.</summary>
        public float UnitWeight { get; set; }

        /// <summary>Not persisted: refreshed from the item prefab when loaded.</summary>
        public int MaxStack { get; set; } = 1;

        public float Weight => UnitWeight * Amount;

        /// <summary>The game's stacking rule (Inventory.FindFreeStackItem): same item, quality and world level.</summary>
        public bool SameKind(ItemStack other) =>
            other.Prefab == Prefab && other.Quality == Quality && other.WorldLevel == WorldLevel;

        /// <summary>Identical item data, so merging the two loses nothing (HB-061).</summary>
        public bool StacksWith(ItemStack other) =>
            SameKind(other) && other.Variant == Variant && other.Cheated == Cheated && other.CrafterId == CrafterId
            && other.CrafterName == CrafterName && SameCustomData(other.CustomData, CustomData);

        public ItemStack Copy(int amount) => new ItemStack
        {
            Prefab = Prefab, Amount = amount, Quality = Quality, Variant = Variant, WorldLevel = WorldLevel,
            Cheated = Cheated, CrafterId = CrafterId, CrafterName = CrafterName,
            CustomData = new Dictionary<string, string>(CustomData ?? new Dictionary<string, string>()),
            UnitWeight = UnitWeight, MaxStack = MaxStack
        };

        /// <summary>Key of the existing-item storage rule and of retry bookkeeping.</summary>
        public string KindKey => $"{Prefab}|q{Quality}|w{WorldLevel}";

        public override string ToString() => $"{Prefab} x{Amount}" + (Quality > 1 ? $" q{Quality}" : "") + (Cheated ? " (cheated)" : "");

        private static bool SameCustomData(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            int countA = a?.Count ?? 0, countB = b?.Count ?? 0;
            if (countA != countB) return false;
            if (countA == 0) return true;
            foreach (KeyValuePair<string, string> pair in a)
                if (!b.TryGetValue(pair.Key, out string value) || value != pair.Value) return false;
            return true;
        }
    }

    /// <summary>The Haugbui's own inventory, limited by slots and weight from its level (HB-031).</summary>
    public sealed class CarriedInventory
    {
        private readonly List<ItemStack> _stacks = new List<ItemStack>();

        public CarriedInventory(int slots, float capacity)
        {
            Slots = slots;
            Capacity = capacity;
        }

        public int Slots { get; set; }
        public float Capacity { get; set; }
        public IReadOnlyList<ItemStack> Stacks => _stacks;
        public float Weight => _stacks.Sum(s => s.Weight);
        public bool IsEmpty => _stacks.Count == 0;

        public event Action Changed;

        /// <summary>How many of <paramref name="item"/> fit, by free stack room, free slots and remaining weight.</summary>
        public int CanAccept(ItemStack item)
        {
            int maxStack = Math.Max(1, item.MaxStack);
            int room = _stacks.Where(s => s.StacksWith(item)).Sum(s => Math.Max(0, maxStack - s.Amount));
            room += Math.Max(0, Slots - _stacks.Count) * maxStack;
            if (item.UnitWeight > 0)
                room = Math.Min(room, (int)Math.Floor((Capacity - Weight) / item.UnitWeight + 1e-4));
            return Math.Max(0, Math.Min(room, item.Amount));
        }

        /// <returns>The amount actually added.</returns>
        public int Add(ItemStack item)
        {
            int toAdd = CanAccept(item);
            int left = toAdd;
            int maxStack = Math.Max(1, item.MaxStack);
            foreach (ItemStack stack in _stacks.Where(s => s.StacksWith(item)))
            {
                int moved = Math.Min(left, maxStack - stack.Amount);
                if (moved <= 0) continue;
                stack.Amount += moved;
                left -= moved;
            }
            while (left > 0)
            {
                int moved = Math.Min(left, maxStack);
                _stacks.Add(item.Copy(moved));
                left -= moved;
            }
            if (toAdd > 0) Changed?.Invoke();
            return toAdd;
        }

        /// <summary>Amount of the same kind (game stacking rule), whatever its other data.</summary>
        public int Count(ItemStack item) => _stacks.Where(s => s.SameKind(item)).Sum(s => s.Amount);

        /// <summary>Removes identical items first, then any of the same kind. Returns the amount removed.</summary>
        public int Remove(ItemStack item, int amount)
        {
            if (amount <= 0) return 0;
            int left = amount;
            foreach (ItemStack stack in _stacks.Where(s => s.StacksWith(item)).Concat(_stacks.Where(s => s.SameKind(item) && !s.StacksWith(item))).ToList())
            {
                if (left <= 0) break;
                int taken = Math.Min(left, stack.Amount);
                stack.Amount -= taken;
                left -= taken;
            }
            _stacks.RemoveAll(s => s.Amount <= 0);
            int removed = amount - left;
            if (removed > 0) Changed?.Invoke();
            return removed;
        }

        /// <summary>Removes and returns everything, e.g. to deposit it.</summary>
        public List<ItemStack> TakeAll()
        {
            var all = new List<ItemStack>(_stacks);
            _stacks.Clear();
            if (all.Count > 0) Changed?.Invoke();
            return all;
        }

        /// <summary>Restores saved stacks without applying the caps, so a level drop never deletes items.</summary>
        public void Restore(IEnumerable<ItemStack> stacks)
        {
            _stacks.Clear();
            _stacks.AddRange(stacks.Where(s => s.Amount > 0));
        }
    }

    public sealed class ItemStacksSerializer : IBlobSerializer<List<ItemStack>>
    {
        public static readonly ItemStacksSerializer Instance = new ItemStacksSerializer();

        /// <summary>v2 adds cheated flag, crafter and custom data.</summary>
        public int CurrentVersion => 2;

        public void Write(List<ItemStack> value, BlobWriter writer)
        {
            writer.Write(value.Count);
            foreach (ItemStack s in value)
            {
                writer.Write(s.Prefab).Write(s.Amount).Write(s.Quality).Write(s.Variant).Write(s.WorldLevel);
                writer.Write(s.Cheated).Write(s.CrafterId).Write(s.CrafterName);
                Dictionary<string, string> data = s.CustomData ?? new Dictionary<string, string>();
                writer.Write(data.Count);
                foreach (KeyValuePair<string, string> pair in data) writer.Write(pair.Key).Write(pair.Value);
            }
        }

        public List<ItemStack> Read(BlobReader reader)
        {
            int count = reader.ReadInt();
            if (count < 0 || count > 10000) throw new ArgumentException("stack count out of range");
            var result = new List<ItemStack>(count);
            for (int i = 0; i < count; i++)
            {
                var stack = new ItemStack
                {
                    Prefab = reader.ReadString(),
                    Amount = reader.ReadInt(),
                    Quality = reader.ReadInt(),
                    Variant = reader.ReadInt(),
                    WorldLevel = reader.ReadInt(),
                };
                if (reader.SchemaVersion >= 2)
                {
                    stack.Cheated = reader.ReadBool();
                    stack.CrafterId = reader.ReadLong();
                    stack.CrafterName = reader.ReadString();
                    int pairs = reader.ReadInt();
                    if (pairs < 0 || pairs > 1000) throw new ArgumentException("custom data count out of range");
                    for (int p = 0; p < pairs; p++) stack.CustomData[reader.ReadString()] = reader.ReadString();
                }
                result.Add(stack);
            }
            return result;
        }
    }

    public static class DepositPlanner
    {
        /// <summary>
        /// HB-175: splits items into what the destination accepts and the overflow; amounts are conserved exactly.
        /// <paramref name="accept"/> must reserve what it accepts, since later stacks compete for the same room.
        /// </summary>
        public static List<ItemStack> Deposit(IEnumerable<ItemStack> items, Func<ItemStack, int> accept)
        {
            var overflow = new List<ItemStack>();
            foreach (ItemStack item in items)
            {
                int accepted = Math.Max(0, Math.Min(item.Amount, accept(item)));
                if (accepted < item.Amount) overflow.Add(item.Copy(item.Amount - accepted));
            }
            return overflow;
        }

        public static List<ItemStack> Merge(IEnumerable<ItemStack> a, IEnumerable<ItemStack> b)
        {
            var result = new List<ItemStack>();
            foreach (ItemStack s in a.Concat(b))
            {
                ItemStack same = result.FirstOrDefault(r => r.StacksWith(s));
                if (same != null) same.Amount += s.Amount;
                else result.Add(s.Copy(s.Amount));
            }
            return result;
        }
    }
}
