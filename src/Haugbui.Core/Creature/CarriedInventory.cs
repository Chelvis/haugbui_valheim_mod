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

        /// <summary>Not persisted: refreshed from the item prefab when loaded.</summary>
        public float UnitWeight { get; set; }

        /// <summary>Not persisted: refreshed from the item prefab when loaded.</summary>
        public int MaxStack { get; set; } = 1;

        public float Weight => UnitWeight * Amount;

        public bool StacksWith(ItemStack other) =>
            other.Prefab == Prefab && other.Quality == Quality && other.Variant == Variant && other.WorldLevel == WorldLevel;

        public ItemStack Copy(int amount) => new ItemStack
        {
            Prefab = Prefab, Amount = amount, Quality = Quality, Variant = Variant, WorldLevel = WorldLevel,
            UnitWeight = UnitWeight, MaxStack = MaxStack
        };
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

        public int CurrentVersion => 1;

        public void Write(List<ItemStack> value, BlobWriter writer)
        {
            writer.Write(value.Count);
            foreach (ItemStack s in value)
                writer.Write(s.Prefab).Write(s.Amount).Write(s.Quality).Write(s.Variant).Write(s.WorldLevel);
        }

        public List<ItemStack> Read(BlobReader reader)
        {
            int count = reader.ReadInt();
            if (count < 0 || count > 10000) throw new ArgumentException("stack count out of range");
            var result = new List<ItemStack>(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(new ItemStack
                {
                    Prefab = reader.ReadString(),
                    Amount = reader.ReadInt(),
                    Quality = reader.ReadInt(),
                    Variant = reader.ReadInt(),
                    WorldLevel = reader.ReadInt(),
                });
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
