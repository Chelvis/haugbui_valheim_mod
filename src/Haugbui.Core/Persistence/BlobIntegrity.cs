using System;
using System.Collections.Generic;
using Haugbui.Core.Creature;
using Haugbui.Core.Progression;

namespace Haugbui.Core.Persistence
{
    /// <summary>
    /// Clamps values a readable blob can hold without inventing items or a new delivery obligation.
    /// Corrupt and future blobs are left byte-for-byte; the caller disables that object.
    /// </summary>
    public static class BlobIntegrity
    {
        public static bool RepairTomb(TombState state)
        {
            if (state == null) return false;
            bool changed = false;
            int level = state.LevelCache;
            if (level < ProgressionTable.MinLevel || level > ProgressionTable.MaxLevel)
            {
                state.LevelCache = level < ProgressionTable.MinLevel ? ProgressionTable.MinLevel : ProgressionTable.MaxLevel;
                changed = true;
            }
            if (state.Balance < 0)
            {
                state.Balance = 0;
                changed = true;
            }
            if (state.CheatedBalance < 0)
            {
                state.CheatedBalance = 0;
                changed = true;
            }
            if (state.CheatedBalance > state.Balance)
            {
                state.CheatedBalance = state.Balance;
                changed = true;
            }
            if (state.SlotIndex < -1)
            {
                state.SlotIndex = -1;
                changed = true;
            }
            if (float.IsNaN(state.Health) || float.IsInfinity(state.Health))
            {
                state.Health = -1f;
                changed = true;
            }
            int creature = state.CreatureState;
            int dormant = (int)HaugbuiState.Dormant;
            int idle = (int)HaugbuiState.Idle;
            int dead = (int)HaugbuiState.Dead;
            if (creature != dormant && creature != idle && creature != dead)
            {
                state.CreatureState = idle;
                changed = true;
            }
            return changed;
        }

        /// <summary>A line that claims more delivered than requested is closed by raising the request, so nothing further is owed.</summary>
        public static bool RepairRequests(List<DeliveryRequest> lines)
        {
            if (lines == null) return false;
            bool changed = false;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                DeliveryRequest line = lines[i];
                if (line == null || string.IsNullOrEmpty(line.ItemPrefab))
                {
                    lines.RemoveAt(i);
                    changed = true;
                    continue;
                }
                if (line.Requested < 0)
                {
                    line.Requested = 0;
                    changed = true;
                }
                if (line.Delivered < 0)
                {
                    line.Delivered = 0;
                    changed = true;
                }
                if (line.Delivered > line.Requested)
                {
                    line.Requested = line.Delivered;
                    changed = true;
                }
                if (line.Requested == 0 && line.Delivered == 0)
                {
                    lines.RemoveAt(i);
                    changed = true;
                }
            }
            return changed;
        }

        public static bool RepairJob(StationJobState job)
        {
            if (job == null) return false;
            bool changed = false;
            if (job.InputPrefab == null)
            {
                job.InputPrefab = string.Empty;
                changed = true;
            }
            if (job.OutputPrefab == null)
            {
                job.OutputPrefab = string.Empty;
                changed = true;
            }
            if (job.TombGuid == null)
            {
                job.TombGuid = string.Empty;
                changed = true;
            }
            if (job.Mode != JobMode.Finite && job.Mode != JobMode.Infinite)
            {
                job.Mode = JobMode.Finite;
                changed = true;
            }
            if (job.Target < 0)
            {
                job.Target = 0;
                changed = true;
            }
            if (job.Supplied < 0)
            {
                job.Supplied = 0;
                changed = true;
            }
            if (job.Produced < 0)
            {
                job.Produced = 0;
                changed = true;
            }
            return changed;
        }

        public static bool RepairStacks(List<ItemStack> stacks)
        {
            if (stacks == null) return false;
            bool changed = false;
            for (int i = stacks.Count - 1; i >= 0; i--)
            {
                ItemStack stack = stacks[i];
                if (stack == null || string.IsNullOrEmpty(stack.Prefab) || stack.Amount <= 0)
                {
                    stacks.RemoveAt(i);
                    changed = true;
                    continue;
                }
                if (stack.Quality < 1)
                {
                    stack.Quality = 1;
                    changed = true;
                }
                if (stack.CustomData == null)
                {
                    stack.CustomData = new Dictionary<string, string>();
                    changed = true;
                }
            }
            return changed;
        }
    }
}
