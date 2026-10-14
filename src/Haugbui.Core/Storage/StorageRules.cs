using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;

namespace Haugbui.Core.Storage
{
    /// <summary>What a storage container holds, as read from the game at planning time.</summary>
    public sealed class ContainerSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public Vec3 Position { get; set; }
        public int Slots { get; set; }
        public List<ItemStack> Items { get; set; } = new List<ItemStack>();

        public int FreeSlots => Math.Max(0, Slots - Items.Count);

        public bool Contains(ItemStack item) => Items.Any(i => i.SameKind(item) && i.Amount > 0);

        /// <summary>Room on matching stacks (same item, quality, world level) plus empty slots, as Inventory.AddItem fills them.</summary>
        public int RoomFor(ItemStack item)
        {
            int maxStack = Math.Max(1, item.MaxStack);
            int stackRoom = Items.Where(i => i.SameKind(item)).Sum(i => Math.Max(0, maxStack - i.Amount));
            return stackRoom + FreeSlots * maxStack;
        }

        public int StackRoomFor(ItemStack item)
        {
            int maxStack = Math.Max(1, item.MaxStack);
            return Items.Where(i => i.SameKind(item)).Sum(i => Math.Max(0, maxStack - i.Amount));
        }
    }

    public readonly struct StorageAllocation
    {
        public StorageAllocation(string containerId, int amount)
        {
            ContainerId = containerId;
            Amount = amount;
        }

        public string ContainerId { get; }
        public int Amount { get; }

        public override string ToString() => $"{ContainerId}:{Amount}";
    }

    /// <summary>
    /// HB-062/063, PRD §8.1: an item goes only into a container that already holds the same kind. Ranking: accepts the
    /// whole amount, then fewest new slots (top up existing stacks), then nearest. Never more than a container's room.
    /// </summary>
    public static class StorageSelector
    {
        public static bool IsCandidate(ContainerSnapshot container, ItemStack item) =>
            container.Contains(item) && container.RoomFor(item) > 0;

        public static List<ContainerSnapshot> Rank(ItemStack item, int amount, IEnumerable<ContainerSnapshot> containers, Vec3 from)
        {
            int maxStack = Math.Max(1, item.MaxStack);
            return containers
                .Where(c => IsCandidate(c, item))
                .Select(c => new
                {
                    Container = c,
                    Room = c.RoomFor(item),
                    NewSlots = NewSlots(Math.Min(amount, c.RoomFor(item)), c.StackRoomFor(item), maxStack),
                    Distance = Vec3.Distance(from, c.Position)
                })
                .OrderByDescending(c => c.Room >= amount)
                .ThenBy(c => c.NewSlots)
                .ThenBy(c => c.Distance)
                .ThenBy(c => c.Container.Id, StringComparer.Ordinal)
                .Select(c => c.Container)
                .ToList();
        }

        /// <summary>Splits <paramref name="amount"/> over the ranked containers; the sum may be less when room runs out.</summary>
        public static List<StorageAllocation> Plan(ItemStack item, int amount, IEnumerable<ContainerSnapshot> containers, Vec3 from)
        {
            var result = new List<StorageAllocation>();
            int left = amount;
            foreach (ContainerSnapshot container in Rank(item, amount, containers, from))
            {
                if (left <= 0) break;
                int put = Math.Min(left, container.RoomFor(item));
                if (put <= 0) continue;
                result.Add(new StorageAllocation(container.Id, put));
                left -= put;
            }
            return result;
        }

        private static int NewSlots(int amount, int stackRoom, int maxStack) =>
            (int)Math.Ceiling(Math.Max(0, amount - stackRoom) / (double)maxStack);
    }
}
