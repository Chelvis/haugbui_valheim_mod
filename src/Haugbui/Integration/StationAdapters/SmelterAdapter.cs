using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Haugbui.Integration.StationAdapters
{
    /// <summary>
    /// HB-T-061/062/067: smelter, blast furnace, charcoal kiln, eitr refinery, windmill, spinning wheel, and the fuel-only
    /// hot tub and frost kiln. RPC_AddOre and RPC_AddFuel do not check capacity, so the caller must (feasibility §4).
    /// </summary>
    internal sealed class SmelterAdapter : IStationAdapter
    {
        private const float MinWindPower = 0.01f;

        public bool CanHandle(MonoBehaviour station) => station is Smelter;

        public StationKind Kind(MonoBehaviour station) => ((Smelter)station).m_maxOre <= 0 ? StationKind.FuelOnly : StationKind.Smelter;

        public ZNetView View(MonoBehaviour station) => ((Smelter)station).m_nview;

        public string Name(MonoBehaviour station) => ((Smelter)station).m_name;

        public IReadOnlyList<StationRecipe> Recipes(MonoBehaviour station)
        {
            var smelter = (Smelter)station;
            var recipes = new List<StationRecipe>();
            foreach (Smelter.ItemConversion conversion in smelter.m_conversion)
            {
                if (conversion?.m_to == null) continue;
                var recipe = new StationRecipe
                {
                    Input = conversion.m_from != null ? conversion.m_from.gameObject.name : string.Empty,
                    Output = conversion.m_to.gameObject.name,
                    InputItem = conversion.m_from,
                    OutputItem = conversion.m_to,
                };
                // The blast furnace lists FlametalOreNew twice.
                if (recipes.All(r => r.Key != recipe.Key)) recipes.Add(recipe);
            }
            if (recipes.Count == 0 && Kind(station) == StationKind.FuelOnly) recipes.Add(new StationRecipe());
            return recipes;
        }

        public StationState Read(MonoBehaviour station)
        {
            var smelter = (Smelter)station;
            ZDO zdo = smelter.m_nview.GetZDO();
            var state = new StationState
            {
                Queued = zdo.GetInt(ZDOVars.s_queued),
                MaxQueue = smelter.m_maxOre,
                Fuel = zdo.GetFloat(ZDOVars.s_fuel),
                MaxFuel = smelter.m_fuelItem != null ? smelter.m_maxFuel : 0,
                FuelPerProduct = smelter.m_maxOre > 0 ? smelter.m_fuelPerProduct : 0,
                PendingOutputs = zdo.GetInt(ZDOVars.s_spawnAmount),
            };
            if (smelter.m_fuelItem != null) state.FuelPrefabs.Add(smelter.m_fuelItem.gameObject.name);
            for (int i = 0; i < state.Queued; i++)
            {
                string item = zdo.GetString("item" + i);
                if (!state.QueuedInputs.Contains(item)) state.QueuedInputs.Add(item);
            }
            // The smelter keeps one flag for its whole queue and the windmill stack, set by the last item added
            // (Smelter.QueueOre/Spawn). A clean item would clear a cheated queue, so it never goes in (both modes);
            // with vanilla stacking a cheated item may taint a clean queue, as it would for a player.
            if (state.Queued > 0 || state.PendingOutputs > 0)
            {
                bool cheated = zdo.GetBool(ZDOVars.s_cheatedQueued);
                if (cheated || Core.Storage.StackingRules.StrictItemData) state.RequiredCheated = cheated;
            }
            state.Busy = state.Queued > 0 || state.PendingOutputs > 0;

            if (smelter.m_requiresRoof && !smelter.m_haveRoof) state.Blocked = "$haugbui_station_needs_roof";
            else if (smelter.m_blockedSmoke) state.Blocked = "$haugbui_station_smoke_blocked";
            else if (smelter.m_windmill != null && smelter.m_windmill.GetPowerOutput() < MinWindPower) state.Blocked = "$haugbui_station_no_wind";
            return state;
        }

        public bool FeedOne(MonoBehaviour station, string prefab, bool cheated)
        {
            var smelter = (Smelter)station;
            ZDO zdo = smelter.m_nview.GetZDO();
            int before = zdo.GetInt(ZDOVars.s_queued);
            if (before >= smelter.m_maxOre || !smelter.m_nview.IsOwner()) return false;
            smelter.m_nview.InvokeRPC("RPC_AddOre", prefab, cheated);
            return zdo.GetInt(ZDOVars.s_queued) == before + 1;
        }

        public bool FuelOne(MonoBehaviour station)
        {
            var smelter = (Smelter)station;
            ZDO zdo = smelter.m_nview.GetZDO();
            float before = zdo.GetFloat(ZDOVars.s_fuel);
            if (smelter.m_fuelItem == null || before > smelter.m_maxFuel - 1 || !smelter.m_nview.IsOwner()) return false;
            smelter.m_nview.InvokeRPC("RPC_AddFuel");
            return zdo.GetFloat(ZDOVars.s_fuel) > before;
        }

        public bool Operate(MonoBehaviour station, Vector3 from) => false;

        /// <summary>Products appear at the output point (Smelter.Spawn) and may roll a little.</summary>
        public float OutputRadius(MonoBehaviour station) => 3f;

        public Vector3 OutputPoint(MonoBehaviour station)
        {
            var smelter = (Smelter)station;
            return smelter.m_outputPoint != null ? smelter.m_outputPoint.position : smelter.transform.position;
        }
    }

    /// <summary>HB-T-067: the shield generator only needs fuel (any of its fuel items); RPC_AddFuel has no max check.</summary>
    internal sealed class ShieldGeneratorAdapter : IStationAdapter
    {
        public bool CanHandle(MonoBehaviour station) => station is ShieldGenerator;

        public StationKind Kind(MonoBehaviour station) => StationKind.FuelOnly;

        /// <summary>ShieldGenerator sets m_nview only in Start, a frame after it is placed.</summary>
        public ZNetView View(MonoBehaviour station) => ((ShieldGenerator)station).m_nview ?? station.GetComponent<ZNetView>();

        public string Name(MonoBehaviour station) => ((ShieldGenerator)station).m_name;

        public IReadOnlyList<StationRecipe> Recipes(MonoBehaviour station) => new[] { new StationRecipe() };

        public StationState Read(MonoBehaviour station)
        {
            var shield = (ShieldGenerator)station;
            var state = new StationState
            {
                Fuel = View(station).GetZDO().GetFloat(ZDOVars.s_fuel),
                MaxFuel = shield.m_maxFuel,
            };
            foreach (ItemDrop fuel in shield.m_fuelItems)
                if (fuel != null) state.FuelPrefabs.Add(fuel.gameObject.name);
            return state;
        }

        public bool FeedOne(MonoBehaviour station, string prefab, bool cheated) => false;

        public bool FuelOne(MonoBehaviour station)
        {
            var shield = (ShieldGenerator)station;
            ZNetView view = View(station);
            ZDO zdo = view.GetZDO();
            float before = zdo.GetFloat(ZDOVars.s_fuel);
            if (before > shield.m_maxFuel - 1 || !view.IsOwner()) return false;
            view.InvokeRPC("RPC_AddFuel");
            return zdo.GetFloat(ZDOVars.s_fuel) > before;
        }

        public bool Operate(MonoBehaviour station, Vector3 from) => false;

        public Vector3 OutputPoint(MonoBehaviour station) => station.transform.position;

        public float OutputRadius(MonoBehaviour station) => 0f;
    }
}
