using System.Collections.Generic;
using UnityEngine;

namespace Haugbui.Integration.StationAdapters
{
    internal enum StationKind
    {
        Smelter,
        Fermenter,
        Cooking,

        /// <summary>Only fuel is supplied (hot tub, frost kiln, shield generator); infinite jobs only.</summary>
        FuelOnly
    }

    /// <summary>One conversion of a station. Input is empty for stations that produce from fuel alone (frost kiln).</summary>
    internal sealed class StationRecipe
    {
        public string Input = string.Empty;
        public string Output = string.Empty;
        public ItemDrop InputItem;
        public ItemDrop OutputItem;
        public int Yield = 1;

        public string Key => Input + ">" + Output;
    }

    /// <summary>What a station holds right now, read from its ZDO.</summary>
    internal sealed class StationState
    {
        public int Queued;
        public int MaxQueue;

        /// <summary>Distinct inputs in the queue (manual use, HB-117).</summary>
        public readonly List<string> QueuedInputs = new List<string>();

        /// <summary>
        /// HB-061: the cheated flag the station already applies to its pending outputs; null when it has none, so any
        /// item can go in without changing another item's flag.
        /// </summary>
        public bool? RequiredCheated;

        public float Fuel;
        public int MaxFuel;
        public float FuelPerProduct;
        public readonly List<string> FuelPrefabs = new List<string>();

        /// <summary>Products waiting inside the station (windmill stack, fermenter ready to tap).</summary>
        public int PendingOutputs;

        /// <summary>Something of a job is queued, processing or waiting to come out.</summary>
        public bool Busy;

        /// <summary>HB-119: why it does not work now (localization token), reported and never bypassed.</summary>
        public string Blocked;
    }

    /// <summary>
    /// ARCHITECTURE §9: everything the production provider needs from one family of stations. Adding a station is one
    /// adapter plus its registration; the planner, job model and panel stay unchanged.
    /// </summary>
    internal interface IStationAdapter
    {
        bool CanHandle(MonoBehaviour station);
        StationKind Kind(MonoBehaviour station);
        ZNetView View(MonoBehaviour station);
        string Name(MonoBehaviour station);
        IReadOnlyList<StationRecipe> Recipes(MonoBehaviour station);
        StationState Read(MonoBehaviour station);

        /// <summary>Puts one input in through the station's own RPC; true when the station took it.</summary>
        bool FeedOne(MonoBehaviour station, string prefab, bool cheated);

        /// <summary>Adds one fuel unit through the station's own RPC; true when the station took it.</summary>
        bool FuelOne(MonoBehaviour station);

        /// <summary>Where the station drops its products; outputs are collected around it.</summary>
        Vector3 OutputPoint(MonoBehaviour station);
    }
}
