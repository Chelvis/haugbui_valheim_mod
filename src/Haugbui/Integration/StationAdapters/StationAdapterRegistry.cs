using System.Collections.Generic;
using Haugbui.Core.GameApi;
using Haugbui.Core.Persistence;
using Haugbui.Diagnostics;
using UnityEngine;

namespace Haugbui.Integration.StationAdapters
{
    /// <summary>
    /// HB-T-058/069: the first adapter whose CanHandle accepts a station handles it. Only simple production machines are
    /// listed (HB-122): crafting stations and the obliterator never get one.
    /// </summary>
    internal static class StationAdapterRegistry
    {
        private static readonly List<(IStationAdapter Adapter, string Feature)> Adapters = new List<(IStationAdapter, string)>
        {
            (new SmelterAdapter(), Features.Smelters),
            (new ShieldGeneratorAdapter(), Features.Smelters),
        };

        /// <summary>Null when the station is not supported or its game API failed the startup self-check.</summary>
        public static IStationAdapter For(MonoBehaviour station)
        {
            if (station == null) return null;
            foreach ((IStationAdapter adapter, string feature) in Adapters)
                if (adapter.CanHandle(station)) return GameApiSelfCheck.IsAvailable(feature) ? adapter : null;
            return null;
        }

        public static string IdOf(MonoBehaviour station)
        {
            ZDO zdo = For(station)?.View(station)?.GetZDO();
            return zdo != null ? zdo.m_uid.ToString() : "station:none";
        }

        /// <summary>The supported station component on a hovered or interacted object (a switch is a child of its station).</summary>
        public static MonoBehaviour StationOf(Component part)
        {
            if (part == null) return null;
            foreach (MonoBehaviour candidate in part.GetComponentsInParent<MonoBehaviour>())
                if (For(candidate) != null) return candidate;
            return null;
        }
    }

    /// <summary>HB-T-059: the job blob on the vanilla station's ZDO (`haugbui_job`); only the ZDO owner writes.</summary>
    internal static class StationJobs
    {
        public static PersistentSlot<StationJobState> SlotOf(MonoBehaviour station)
        {
            ZDO zdo = StationAdapterRegistry.For(station)?.View(station)?.GetZDO();
            return zdo != null ? new PersistentSlot<StationJobState>(new ZdoBlobStore(zdo), ZdoKeys.Job, StationJobSerializer.Instance) : null;
        }

        /// <summary>Null when the station has no job, or its data is unreadable (kept untouched).</summary>
        public static StationJobState Read(MonoBehaviour station) => Read(station, out _);

        public static StationJobState Read(MonoBehaviour station, out PersistentSlot<StationJobState> slot)
        {
            slot = SlotOf(station);
            StationJobState job = slot?.Load();
            if (slot != null && slot.State == SlotState.ReadOnly)
                Log.ErrorRateLimited("job:" + StationAdapterRegistry.IdOf(station), $"Station {StationAdapterRegistry.IdOf(station)}: job unreadable ({slot.LastReadStatus}); kept untouched.");
            return job;
        }

        public static bool Save(MonoBehaviour station, PersistentSlot<StationJobState> slot, StationJobState job)
        {
            ZNetView view = StationAdapterRegistry.For(station)?.View(station);
            if (slot == null || view == null || !view.IsValid() || !view.IsOwner()) return false;
            return slot.TrySave(job);
        }

        /// <summary>Read, change and save in one go; false when nothing was saved.</summary>
        public static bool Edit(MonoBehaviour station, System.Func<StationJobState, bool> change)
        {
            StationJobState job = Read(station, out PersistentSlot<StationJobState> slot);
            if (slot == null || !slot.CanWrite) return false;
            job = job ?? new StationJobState();
            return change(job) && Save(station, slot, job);
        }
    }
}
