using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Haugbui.Core.Geometry;
using Haugbui.Diagnostics;
using UnityEngine;

namespace Haugbui.Registry
{
    /// <summary>
    /// HB-T-041: live sets of world objects the Haugbui works with, filled by Awake postfixes (vanilla keeps no list of
    /// containers or stations) and indexed in 16 m cells. Only instantiated objects are listed, so an unloaded target is
    /// simply absent (HB-033). Item drops and characters move, so they come from the game's own lists instead.
    /// </summary>
    internal static class WorldRegistry
    {
        public const float CellSize = 16f;

        private static readonly HashSet<int> Hidden = new HashSet<int>();

        public static readonly SpatialHash<Container> Containers = new SpatialHash<Container>(CellSize);
        public static readonly SpatialHash<MonoBehaviour> Stations = new SpatialHash<MonoBehaviour>(CellSize);
        public static readonly SpatialHash<MonoBehaviour> Harvestables = new SpatialHash<MonoBehaviour>(CellSize);
        public static readonly SpatialHash<Bed> Beds = new SpatialHash<Bed>(CellSize);

        public static IReadOnlyList<ItemDrop> ItemDrops => ItemDrop.s_instances;
        public static IReadOnlyList<Character> Characters => Character.GetAllCharacters();

        public static Vec3 ToVec3(Vector3 v) => new Vec3(v.x, v.y, v.z);

        /// <summary>Containers that never move; carts, ships and attached sub-containers are left out.</summary>
        public static bool IsIndexable(Container container) =>
            container != null && !Hidden.Contains(container.GetInstanceID())
            && container.m_nview != null && container.m_nview.GetZDO() != null
            && container.m_wagon == null && container.m_rootObjectOverride == null
            && container.GetComponentInParent<Ship>() == null;

        /// <summary>
        /// The self-test runs where the player already lives. Chests, stations and crops that were loaded
        /// before it started are not fixtures, so the Haugbui under test does not work them.
        /// </summary>
        public static void HideLoadedWorld()
        {
            Hidden.Clear();
            Hide(Containers);
            Hide(Stations);
            Hide(Harvestables);
        }

        public static void RestoreLoadedWorld()
        {
            HashSet<int> bringBack = new HashSet<int>(Hidden);
            Hidden.Clear();
            foreach (Container container in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
                if (container != null && bringBack.Contains(container.GetInstanceID())) Add(container);
            foreach (Smelter station in UnityEngine.Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None))
                if (station != null && bringBack.Contains(station.GetInstanceID())) Add(Stations, station, station.m_nview);
            foreach (Fermenter station in UnityEngine.Object.FindObjectsByType<Fermenter>(FindObjectsSortMode.None))
                if (station != null && bringBack.Contains(station.GetInstanceID())) Add(Stations, station, station.m_nview);
            foreach (CookingStation station in UnityEngine.Object.FindObjectsByType<CookingStation>(FindObjectsSortMode.None))
                if (station != null && bringBack.Contains(station.GetInstanceID())) Add(Stations, station, station.m_nview);
            foreach (ShieldGenerator station in UnityEngine.Object.FindObjectsByType<ShieldGenerator>(FindObjectsSortMode.None))
                if (station != null && bringBack.Contains(station.GetInstanceID())) Add(Stations, station, station.m_nview);
            foreach (Pickable pickable in UnityEngine.Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None))
                if (pickable != null && bringBack.Contains(pickable.GetInstanceID())) Add(Harvestables, pickable, pickable.m_nview);
            foreach (Beehive hive in UnityEngine.Object.FindObjectsByType<Beehive>(FindObjectsSortMode.None))
                if (hive != null && bringBack.Contains(hive.GetInstanceID())) Add(Harvestables, hive, hive.m_nview);
            foreach (SapCollector collector in UnityEngine.Object.FindObjectsByType<SapCollector>(FindObjectsSortMode.None))
                if (collector != null && bringBack.Contains(collector.GetInstanceID())) Add(Harvestables, collector, collector.m_nview);
        }

        private static void Hide<T>(SpatialHash<T> index) where T : MonoBehaviour
        {
            foreach (T item in index.Items.ToList())
            {
                if (item == null) continue;
                Hidden.Add(item.GetInstanceID());
                index.Remove(item);
            }
        }

        public static void Add(Container container)
        {
            if (!IsIndexable(container)) return;
            Containers.Add(container, ToVec3(container.transform.position));
            Track(container.gameObject).Add(() => Containers.Remove(container));
        }

        public static void Add<T>(SpatialHash<T> index, T component, ZNetView view) where T : MonoBehaviour
        {
            if (component == null || Hidden.Contains(component.GetInstanceID()) || view == null || view.GetZDO() == null) return;
            index.Add(component, ToVec3(component.transform.position));
            Track(component.gameObject).Add(() => index.Remove(component));
        }

        /// <summary>
        /// The station script can run before its ZNetView creates the ZDO, so its Awake returns without registering RPCs
        /// or a registry entry. Once the view exists, finish that setup.
        /// </summary>
        public static void EnsureStation(MonoBehaviour station, ZNetView view)
        {
            if (station == null || Hidden.Contains(station.GetInstanceID())) return;
            if (view != null && view.IsValid())
            {
                if (!view.IsOwner()) view.ClaimOwnership();
                ZDO zdo = view.GetZDO();
                if (zdo != null && ZNetScene.instance != null && ZNetScene.instance.FindInstance(zdo) == null)
                    ZNetScene.instance.AddInstance(zdo, view);
            }
            if (Stations.Contains(station)) return;
            if (view != null && view.GetZDO() != null)
            {
                MethodInfo awake = station.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
                try { awake?.Invoke(station, null); }
                catch (TargetInvocationException) { }
            }
            if (!Stations.Contains(station) && view != null) Add(Stations, station, view);
        }

        /// <summary>Appends loaded containers within <paramref name="radius"/>; never allocates.</summary>
        public static void QueryContainers(Vector3 center, float radius, List<Container> results) =>
            Containers.Query(ToVec3(center), radius, results);

        public static void Clear()
        {
            Containers.Clear();
            Stations.Clear();
            Harvestables.Clear();
            Beds.Clear();
        }

        /// <summary>Debug compare against a brute-force scan of the scene (acceptance check of HB-T-041).</summary>
        public static string Verify(out bool ok)
        {
            Containers.Prune(c => c != null);
            var scene = new HashSet<Container>(UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None).Where(IsIndexable));
            var indexed = new HashSet<Container>(Containers.Items);
            int missing = scene.Count(c => !indexed.Contains(c));
            int stale = indexed.Count(c => !scene.Contains(c));

            int stations = CountVisible(UnityEngine.Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None), s => s.m_nview)
                           + CountVisible(UnityEngine.Object.FindObjectsByType<Fermenter>(FindObjectsSortMode.None), s => s.m_nview)
                           + CountVisible(UnityEngine.Object.FindObjectsByType<CookingStation>(FindObjectsSortMode.None), s => s.m_nview)
                           + CountVisible(UnityEngine.Object.FindObjectsByType<ShieldGenerator>(FindObjectsSortMode.None), s => s.m_nview);
            Stations.Prune(s => s != null);
            ok = missing == 0 && stale == 0 && stations == Stations.Count;
            return $"containers {indexed.Count} indexed / {scene.Count} in scene (missing {missing}, stale {stale}); " +
                   $"stations {Stations.Count} / {stations}; harvestables {Harvestables.Count}; beds {Beds.Count}; " +
                   $"item drops {ItemDrops.Count}; characters {Characters.Count}";
        }

        private static int CountVisible<T>(T[] found, Func<T, ZNetView> viewOf) where T : MonoBehaviour
        {
            int count = 0;
            foreach (T station in found)
            {
                if (station == null || Hidden.Contains(station.GetInstanceID())) continue;
                ZNetView view = viewOf(station);
                if (view != null && view.GetZDO() != null) count++;
            }
            return count;
        }

        private static List<Action> Track(GameObject go)
        {
            RegistryTracker tracker = go.GetComponent<RegistryTracker>() ?? go.AddComponent<RegistryTracker>();
            return tracker.OnRemoved;
        }
    }

    /// <summary>Removes its object from the registry when Unity destroys it (most tracked classes have no OnDestroy).</summary>
    internal sealed class RegistryTracker : MonoBehaviour
    {
        internal readonly List<Action> OnRemoved = new List<Action>();

        private void OnDestroy()
        {
            foreach (Action remove in OnRemoved) remove();
            OnRemoved.Clear();
        }
    }

    [HarmonyPatch]
    internal static class RegistryPatches
    {
        private static readonly HashSet<int> AwakeStarted = new HashSet<int>();

        [HarmonyPostfix, HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        private static void WorldLoaded()
        {
            AwakeStarted.Clear();
            WorldRegistry.Clear();
            Coordination.Reservations.Clear();
            Brain.ProductionStatus.Clear();
        }

        [HarmonyPrefix, HarmonyPatch(typeof(Smelter), nameof(Smelter.Awake))]
        private static void SmelterAwakeBegan(Smelter __instance) => AwakeStarted.Add(__instance.GetInstanceID());

        [HarmonyPrefix, HarmonyPatch(typeof(Fermenter), nameof(Fermenter.Awake))]
        private static void FermenterAwakeBegan(Fermenter __instance) => AwakeStarted.Add(__instance.GetInstanceID());

        [HarmonyPrefix, HarmonyPatch(typeof(CookingStation), nameof(CookingStation.Awake))]
        private static void CookingAwakeBegan(CookingStation __instance) => AwakeStarted.Add(__instance.GetInstanceID());

        /// <summary>Runs after the view has a ZDO, including when the station script already gave up.</summary>
        [HarmonyPostfix, HarmonyPatch(typeof(ZNetView), nameof(ZNetView.Awake))]
        private static void ViewReady(ZNetView __instance)
        {
            if (__instance == null || __instance.GetZDO() == null) return;
            Finish(__instance, __instance.GetComponent<Smelter>() ?? __instance.GetComponentInParent<Smelter>());
            Finish(__instance, __instance.GetComponent<Fermenter>() ?? __instance.GetComponentInParent<Fermenter>());
            Finish(__instance, __instance.GetComponent<CookingStation>() ?? __instance.GetComponentInParent<CookingStation>());
        }

        private static void Finish<T>(ZNetView view, T station) where T : MonoBehaviour
        {
            if (station == null || !AwakeStarted.Contains(station.GetInstanceID()) || WorldRegistry.Stations.Contains(station)) return;
            Guard(() => WorldRegistry.EnsureStation(station, view));
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Container), nameof(Container.Awake))]
        private static void RegisterContainer(Container __instance) => Guard(() => WorldRegistry.Add(__instance));

        [HarmonyPostfix, HarmonyPatch(typeof(Smelter), nameof(Smelter.Awake))]
        private static void RegisterSmelter(Smelter __instance) => Guard(() => WorldRegistry.Add(WorldRegistry.Stations, __instance, __instance.m_nview));

        [HarmonyPostfix, HarmonyPatch(typeof(Fermenter), nameof(Fermenter.Awake))]
        private static void RegisterFermenter(Fermenter __instance) => Guard(() => WorldRegistry.Add(WorldRegistry.Stations, __instance, __instance.m_nview));

        [HarmonyPostfix, HarmonyPatch(typeof(CookingStation), nameof(CookingStation.Awake))]
        private static void RegisterCookingStation(CookingStation __instance) => Guard(() => WorldRegistry.Add(WorldRegistry.Stations, __instance, __instance.m_nview));

        [HarmonyPostfix, HarmonyPatch(typeof(ShieldGenerator), nameof(ShieldGenerator.Start))]
        private static void RegisterShieldGenerator(ShieldGenerator __instance) => Guard(() => WorldRegistry.Add(WorldRegistry.Stations, __instance, __instance.m_nview));

        [HarmonyPostfix, HarmonyPatch(typeof(Pickable), nameof(Pickable.Awake))]
        private static void RegisterPickable(Pickable __instance) => Guard(() => WorldRegistry.Add(WorldRegistry.Harvestables, __instance, __instance.m_nview));

        [HarmonyPostfix, HarmonyPatch(typeof(Beehive), nameof(Beehive.Awake))]
        private static void RegisterBeehive(Beehive __instance) => Guard(() => WorldRegistry.Add(WorldRegistry.Harvestables, __instance, __instance.m_nview));

        [HarmonyPostfix, HarmonyPatch(typeof(SapCollector), nameof(SapCollector.Awake))]
        private static void RegisterSapCollector(SapCollector __instance) => Guard(() => WorldRegistry.Add(WorldRegistry.Harvestables, __instance, __instance.m_nview));

        [HarmonyPostfix, HarmonyPatch(typeof(Bed), nameof(Bed.Awake))]
        private static void RegisterBed(Bed __instance) => Guard(() => WorldRegistry.Add(WorldRegistry.Beds, __instance, __instance.m_nview));

        private static void Guard(Action register)
        {
            try
            {
                register();
            }
            catch (Exception e)
            {
                Log.ErrorRateLimited("registry", $"World registry: {e}");
            }
        }
    }
}
