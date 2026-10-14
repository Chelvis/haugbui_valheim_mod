using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Persistence;
using Haugbui.Diagnostics;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.Integration
{
    /// <summary>
    /// HB-T-104: servant slots per player, saved with the world as global key values ("haugbui_slot_&lt;owner&gt;_&lt;n&gt; &lt;hex&gt;").
    /// Verified in ZoneSystem (1.0.17): the world save writes every m_globalKeys entry that is not a server option, and
    /// loading re-adds them through GlobalKeyAdd, which lower-cases the whole text (hence the hex value). Writes go straight
    /// to m_globalKeys/m_globalKeysValues: SetGlobalKey would route an RPC and log every new text in the player profile.
    /// Not sent to other peers (single-player first, ARCHITECTURE §15).
    /// </summary>
    internal static class ServantRegistry
    {
        private const float TombLookupCacheSeconds = 5f;
        private static readonly Dictionary<string, (bool Exists, float Until)> TombLookups = new Dictionary<string, (bool, float)>();

        public static bool Available => ZoneSystem.instance != null;

        public static int MaxSlots => HaugbuiPlugin.Instance.Settings.MaxHaugbuisPerPlayer.Value;

        public static ServantSlot Read(long owner, int index)
        {
            if (!Available || index < 0) return null;
            if (!ZoneSystem.instance.m_globalKeysValues.TryGetValue(ServantSlots.KeyName(owner, index), out string text)) return null;
            BlobReadStatus status = ServantSlots.Decode(text, out ServantSlot slot);
            if (status != BlobReadStatus.Ok)
            {
                Log.ErrorRateLimited("slot:" + owner + ":" + index, $"Servant slot {index} of player {owner} unreadable ({status}); left untouched.");
                return null;
            }
            slot.Index = index;
            return slot;
        }

        public static Dictionary<int, ServantSlot> ReadAll(long owner)
        {
            var slots = new Dictionary<int, ServantSlot>();
            if (!Available) return slots;
            foreach (string key in ZoneSystem.instance.m_globalKeysValues.Keys.ToList())
            {
                if (!ServantSlots.TryParseIndex(key, owner, out int index)) continue;
                ServantSlot slot = Read(owner, index);
                if (slot != null) slots[index] = slot;
            }
            return slots;
        }

        public static void Write(long owner, ServantSlot slot)
        {
            if (!Available || slot == null || slot.Index < 0) return;
            ZoneSystem zones = ZoneSystem.instance;
            string name = ServantSlots.KeyName(owner, slot.Index);
            string value = ServantSlots.Encode(slot);
            if (zones.m_globalKeysValues.TryGetValue(name, out string old))
            {
                if (old == value) return;
                zones.m_globalKeys.Remove((name + " " + old).TrimEnd());
            }
            zones.m_globalKeys.Add(name + " " + value);
            zones.m_globalKeysValues[name] = value;
        }

        public static void Update(long owner, int index, Action<ServantSlot> change)
        {
            ServantSlot slot = Read(owner, index);
            if (slot == null) return;
            change(slot);
            Write(owner, slot);
        }

        public static bool HasVacantSlot(long owner) => ServantSlots.LowestVacant(ReadAll(owner), MaxSlots, TombExists) >= 0;

        /// <summary>
        /// HB-138: binds the tomb to its own slot when that slot is still its own or free, else to the lowest vacant one,
        /// and takes over what the slot remembers (name, pending death, toggles). Returns null when every slot is bound.
        /// </summary>
        public static ServantSlot Bind(TombState state)
        {
            if (!Available || state == null) return null;
            Dictionary<int, ServantSlot> slots = ReadAll(state.OwnerId);
            int index = state.SlotIndex;
            if (index < 0 || slots.TryGetValue(index, out ServantSlot claimed) && claimed.TombGuid != state.Guid
                && !claimed.IsVacant && TombExists(claimed.TombGuid))
                index = ServantSlots.LowestVacant(slots, Math.Max(MaxSlots, 0), TombExists);
            if (index < 0) return null;

            bool known = slots.TryGetValue(index, out ServantSlot slot);
            if (!known) slot = new ServantSlot { Index = index, Name = ServantSlots.DefaultName(index), Toggles = state.Toggles };
            if (slot.TombGuid != state.Guid)
            {
                if (known)
                {
                    state.Toggles = slot.Toggles;
                    if (!double.IsNaN(slot.DeathTime))
                    {
                        state.DeathTime = slot.DeathTime;
                        state.CreatureState = (int)Core.Creature.HaugbuiState.Dead;
                    }
                }
                slot.TombGuid = state.Guid;
            }
            if (string.IsNullOrEmpty(slot.Name)) slot.Name = ServantSlots.DefaultName(index);
            state.SlotIndex = index;
            Write(state.OwnerId, slot);
            return slot;
        }

        /// <summary>A tomb counts while it is loaded or anywhere in the world data (an unloaded zone is not "destroyed").</summary>
        public static bool TombExists(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return false;
            if (StructureRegistry.FindTomb(guid) != null) return true;
            if (TombLookups.TryGetValue(guid, out (bool Exists, float Until) cached) && Time.time < cached.Until) return cached.Exists;
            bool exists = StructureRegistry.TombExistsInWorld(guid);
            TombLookups[guid] = (exists, Time.time + TombLookupCacheSeconds);
            return exists;
        }

        public static void ForgetLookups() => TombLookups.Clear();

#if DEBUG
        /// <summary>Self-test isolation: takes the player's slots out of the world and gives back exactly what was there.</summary>
        public static Dictionary<string, string> Park(long owner)
        {
            var parked = new Dictionary<string, string>();
            if (!Available) return parked;
            foreach (KeyValuePair<string, string> pair in ZoneSystem.instance.m_globalKeysValues.ToList())
                if (ServantSlots.TryParseIndex(pair.Key, owner, out _)) parked[pair.Key] = pair.Value;
            RemoveAll(owner);
            return parked;
        }

        public static void Unpark(long owner, Dictionary<string, string> parked)
        {
            if (!Available) return;
            RemoveAll(owner);
            foreach (KeyValuePair<string, string> pair in parked)
            {
                ZoneSystem.instance.m_globalKeys.Add(pair.Key + " " + pair.Value);
                ZoneSystem.instance.m_globalKeysValues[pair.Key] = pair.Value;
            }
            ForgetLookups();
        }

        private static void RemoveAll(long owner)
        {
            ZoneSystem zones = ZoneSystem.instance;
            foreach (KeyValuePair<string, string> pair in zones.m_globalKeysValues.ToList())
            {
                if (!ServantSlots.TryParseIndex(pair.Key, owner, out _)) continue;
                zones.m_globalKeys.Remove((pair.Key + " " + pair.Value).TrimEnd());
                zones.m_globalKeysValues.Remove(pair.Key);
            }
        }
#endif
    }
}
