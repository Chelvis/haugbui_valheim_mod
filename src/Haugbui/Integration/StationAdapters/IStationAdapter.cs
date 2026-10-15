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

        /// <summary>Cooking stations: world seconds until the item is done; it burns at twice that (CookingStation.UpdateCooking).</summary>
        public float CookSeconds;

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

        /// <summary>Actions the station waits for: tap a ready fermenter, take done or burnt items off a cooking station.</summary>
        public int ReadyActions;

        /// <summary>
        /// The item the next ready action concerns (fermenter content, first done cooking slot). The Haugbui acts only
        /// when it is its job's input, output or the burnt item, never on a player's own items.
        /// </summary>
        public string NextReady;

        /// <summary>What an item burns into (CookingStation.m_overCookedItem); null for other stations.</summary>
        public string BurntItem;

        /// <summary>A ready action that must not wait (a cooked item burns if left).</summary>
        public bool Urgent;

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

        /// <summary>Takes one ready action through the station's own RPC (tap, remove a done item); true when it happened.</summary>
        bool Operate(MonoBehaviour station, Vector3 from);

        /// <summary>Where the station drops its products; outputs are collected around it.</summary>
        Vector3 OutputPoint(MonoBehaviour station);

        /// <summary>How far from <see cref="OutputPoint"/> its products can land.</summary>
        float OutputRadius(MonoBehaviour station);
    }
}
