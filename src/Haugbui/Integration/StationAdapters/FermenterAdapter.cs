using System;
using System.Collections.Generic;
using UnityEngine;

namespace Haugbui.Integration.StationAdapters
{
    /// <summary>
    /// HB-T-066: one base at a time through RPC_AddItem(hash, cheated) while empty, RPC_Tap when ready; the meads appear at
    /// the output point after m_tapDelay (Fermenter.DelayedTap). RPC_AddItem does not check cover, so the adapter does
    /// (HB-119: an exposed or unroofed fermenter is reported, never loaded).
    /// </summary>
    internal sealed class FermenterAdapter : IStationAdapter
    {
        private const float MinCover = 0.7f;

#if DEBUG
        /// <summary>Self-test: the test fermenter stands under the open sky.</summary>
        internal static bool IgnoreCoverForTests { get; set; }
#else
        private static bool IgnoreCoverForTests => false;
#endif

        public bool CanHandle(MonoBehaviour station) => station is Fermenter;

        public StationKind Kind(MonoBehaviour station) => StationKind.Fermenter;

        public ZNetView View(MonoBehaviour station) => ((Fermenter)station).m_nview;

        public string Name(MonoBehaviour station) => ((Fermenter)station).m_name;

        public IReadOnlyList<StationRecipe> Recipes(MonoBehaviour station)
        {
            var recipes = new List<StationRecipe>();
            foreach (Fermenter.ItemConversion conversion in ((Fermenter)station).m_conversion)
            {
                if (conversion?.m_from == null || conversion.m_to == null) continue;
                recipes.Add(new StationRecipe
                {
                    Input = conversion.m_from.gameObject.name,
                    Output = conversion.m_to.gameObject.name,
                    InputItem = conversion.m_from,
                    OutputItem = conversion.m_to,
                    Yield = Math.Max(1, conversion.m_producedItems),
                });
            }
            return recipes;
        }

        public StationState Read(MonoBehaviour station)
        {
            var fermenter = (Fermenter)station;
            ZDO zdo = fermenter.m_nview.GetZDO();
            int content = zdo.GetInt(ZDOVars.s_content);
            string input = content != 0 ? PrefabName(content) : null;
            var state = new StationState { MaxQueue = 1, Queued = content != 0 ? 1 : 0, Busy = content != 0 };
            if (input != null) state.QueuedInputs.Add(input);
            if (content != 0 && Ready(fermenter, zdo))
            {
                state.ReadyActions = 1;
                state.NextReady = input;
                state.PendingOutputs = 1;
            }
            if (IgnoreCoverForTests) return state;
            Cover.GetCoverForPoint(fermenter.m_roofCheckPoint.position, out float cover, out bool roof);
            if (!roof) state.Blocked = "$haugbui_station_needs_roof";
            else if (cover < MinCover) state.Blocked = "$haugbui_station_exposed";
            return state;
        }

        /// <summary>Fermenter.GetStatus: ready once the fermentation time passed (the start time resets while exposed).</summary>
        private static bool Ready(Fermenter fermenter, ZDO zdo)
        {
            long start = zdo.GetLong(ZDOVars.s_startTime, 0L);
            if (start == 0) return false;
            return (ZNet.instance.GetTime() - new DateTime(start)).TotalSeconds > fermenter.m_fermentationDuration;
        }

        private static string PrefabName(int hash)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(hash) : null;
            return prefab != null ? prefab.name : hash.ToString();
        }

        public bool FeedOne(MonoBehaviour station, string prefab, bool cheated)
        {
            var fermenter = (Fermenter)station;
            ZDO zdo = fermenter.m_nview.GetZDO();
            if (zdo.GetInt(ZDOVars.s_content) != 0 || !fermenter.m_nview.IsOwner() || Read(station).Blocked != null) return false;
            int hash = prefab.GetStableHashCode();
            fermenter.m_nview.InvokeRPC("RPC_AddItem", hash, cheated);
            return zdo.GetInt(ZDOVars.s_content) == hash;
        }

        public bool FuelOne(MonoBehaviour station) => false;

        public bool Operate(MonoBehaviour station, Vector3 from)
        {
            var fermenter = (Fermenter)station;
            ZDO zdo = fermenter.m_nview.GetZDO();
            if (!fermenter.m_nview.IsOwner() || zdo.GetInt(ZDOVars.s_content) == 0 || !Ready(fermenter, zdo)) return false;
            fermenter.m_nview.InvokeRPC("RPC_Tap");
            return zdo.GetInt(ZDOVars.s_content) == 0;
        }

        public Vector3 OutputPoint(MonoBehaviour station)
        {
            var fermenter = (Fermenter)station;
            return fermenter.m_outputPoint != null ? fermenter.m_outputPoint.position : fermenter.transform.position;
        }

        public float OutputRadius(MonoBehaviour station) => 3f;
    }

    /// <summary>
    /// HB-T-068: cooking station, iron cooking station, stone oven, frost foundry. Raw items go into free slots through
    /// RPC_AddItem(name, cheated); RPC_RemoveDoneItem takes the first done or burnt slot off, spawning it with a push.
    /// An item burns at twice its cook time (CookingStation.UpdateCooking), so the provider loads only what it can take
    /// off in time and treats done items as urgent.
    /// </summary>
    internal sealed class CookingStationAdapter : IStationAdapter
    {
        private const int StatusNotDone = 0;

        public bool CanHandle(MonoBehaviour station) => station is CookingStation;

        public StationKind Kind(MonoBehaviour station) => StationKind.Cooking;

        public ZNetView View(MonoBehaviour station) => ((CookingStation)station).m_nview;

        public string Name(MonoBehaviour station) => ((CookingStation)station).m_name;

        public IReadOnlyList<StationRecipe> Recipes(MonoBehaviour station)
        {
            var recipes = new List<StationRecipe>();
            foreach (CookingStation.ItemConversion conversion in ((CookingStation)station).m_conversion)
            {
                if (conversion?.m_from == null || conversion.m_to == null) continue;
                recipes.Add(new StationRecipe
                {
                    Input = conversion.m_from.gameObject.name,
                    Output = conversion.m_to.gameObject.name,
                    InputItem = conversion.m_from,
                    OutputItem = conversion.m_to,
                    CookSeconds = conversion.m_cookTime,
                });
            }
            return recipes;
        }

        public StationState Read(MonoBehaviour station)
        {
            var cooking = (CookingStation)station;
            ZDO zdo = cooking.m_nview.GetZDO();
            var state = new StationState
            {
                MaxQueue = cooking.m_slots.Length,
                BurntItem = cooking.m_overCookedItem != null ? cooking.m_overCookedItem.name : null,
            };
            for (int i = 0; i < cooking.m_slots.Length; i++)
            {
                string item = zdo.GetString("slot" + i);
                if (item == "") continue;
                state.Queued++;
                state.Busy = true;
                int status = zdo.GetInt("slotstatus" + i);
                if (status == StatusNotDone)
                {
                    if (!state.QueuedInputs.Contains(item)) state.QueuedInputs.Add(item);
                    continue;
                }
                state.ReadyActions++;
                state.Urgent = true;
                if (state.NextReady == null) state.NextReady = item;
            }
            if (cooking.m_useFuel && cooking.m_fuelItem != null)
            {
                state.Fuel = zdo.GetFloat(ZDOVars.s_fuel);
                state.MaxFuel = cooking.m_maxFuel;
                state.FuelPrefabs.Add(cooking.m_fuelItem.gameObject.name);
            }
            bool fire = cooking.m_requireFire && cooking.IsFireLit();
            bool fuel = cooking.m_useFuel && state.Fuel > 0f;
            if (!fire && !fuel && !(cooking.m_useFuel && state.MaxFuel > 0)) state.Blocked = "$haugbui_station_no_fire";
            return state;
        }

        public bool FeedOne(MonoBehaviour station, string prefab, bool cheated)
        {
            var cooking = (CookingStation)station;
            if (!cooking.m_nview.IsOwner()) return false;
            int before = Occupied(cooking);
            if (before >= cooking.m_slots.Length) return false;
            cooking.m_nview.InvokeRPC("RPC_AddItem", prefab, cheated);
            return Occupied(cooking) == before + 1;
        }

        public bool FuelOne(MonoBehaviour station)
        {
            var cooking = (CookingStation)station;
            ZDO zdo = cooking.m_nview.GetZDO();
            float before = zdo.GetFloat(ZDOVars.s_fuel);
            if (!cooking.m_useFuel || cooking.m_fuelItem == null || before > cooking.m_maxFuel - 1 || !cooking.m_nview.IsOwner()) return false;
            cooking.m_nview.InvokeRPC("RPC_AddFuel");
            return zdo.GetFloat(ZDOVars.s_fuel) > before;
        }

        public bool Operate(MonoBehaviour station, Vector3 from)
        {
            var cooking = (CookingStation)station;
            if (!cooking.m_nview.IsOwner()) return false;
            int before = Occupied(cooking);
            cooking.m_nview.InvokeRPC("RPC_RemoveDoneItem", from, 1);
            return Occupied(cooking) == before - 1;
        }

        private static int Occupied(CookingStation cooking)
        {
            ZDO zdo = cooking.m_nview.GetZDO();
            int n = 0;
            for (int i = 0; i < cooking.m_slots.Length; i++)
                if (zdo.GetString("slot" + i) != "") n++;
            return n;
        }

        public Vector3 OutputPoint(MonoBehaviour station)
        {
            var cooking = (CookingStation)station;
            return cooking.m_spawnPoint != null ? cooking.m_spawnPoint.position : cooking.transform.position;
        }

        /// <summary>Done items are pushed out at m_spawnForce (CookingStation.SpawnItem), so they land a few metres away.</summary>
        public float OutputRadius(MonoBehaviour station) => 6f;
    }
}
