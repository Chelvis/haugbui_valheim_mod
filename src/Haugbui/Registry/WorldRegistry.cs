using System;
using System.Collections.Generic;
using System.Linq;
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

        public static readonly SpatialHash<Container> Containers = new SpatialHash<Container>(CellSize);
        public static readonly SpatialHash<MonoBehaviour> Stations = new SpatialHash<MonoBehaviour>(CellSize);
        public static readonly SpatialHash<MonoBehaviour> Harvestables = new SpatialHash<MonoBehaviour>(CellSize);
        public static readonly SpatialHash<Bed> Beds = new SpatialHash<Bed>(CellSize);

        public static IReadOnlyList<ItemDrop> ItemDrops => ItemDrop.s_instances;
        public static IReadOnlyList<Character> Characters => Character.GetAllCharacters();

        public static Vec3 ToVec3(Vector3 v) => new Vec3(v.x, v.y, v.z);

        /// <summary>Containers that never move; carts, ships and attached sub-containers are left out.</summary>
        public static bool IsIndexable(Container container) =>
            container != null && container.m_nview != null && container.m_nview.GetZDO() != null
            && container.m_wagon == null && container.m_rootObjectOverride == null
            && container.GetComponentInParent<Ship>() == null;

        public static void Add(Container container)
        {
            if (!IsIndexable(container)) return;
            Containers.Add(container, ToVec3(container.transform.position));
            Track(container.gameObject).Add(() => Containers.Remove(container));
        }

        public static void Add<T>(SpatialHash<T> index, T component, ZNetView view) where T : MonoBehaviour
        {
            if (component == null || view == null || view.GetZDO() == null) return;
            index.Add(component, ToVec3(component.transform.position));
            Track(component.gameObject).Add(() => index.Remove(component));
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

            int stations = UnityEngine.Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None).Count(s => s.m_nview != null && s.m_nview.GetZDO() != null)
                           + UnityEngine.Object.FindObjectsByType<Fermenter>(FindObjectsSortMode.None).Count(s => s.m_nview != null && s.m_nview.GetZDO() != null)
                           + UnityEngine.Object.FindObjectsByType<CookingStation>(FindObjectsSortMode.None).Count(s => s.m_nview != null && s.m_nview.GetZDO() != null)
                           + UnityEngine.Object.FindObjectsByType<ShieldGenerator>(FindObjectsSortMode.None).Count(s => s.m_nview != null && s.m_nview.GetZDO() != null);
            Stations.Prune(s => s != null);
            ok = missing == 0 && stale == 0 && stations == Stations.Count;
            return $"containers {indexed.Count} indexed / {scene.Count} in scene (missing {missing}, stale {stale}); " +
                   $"stations {Stations.Count} / {stations}; harvestables {Harvestables.Count}; beds {Beds.Count}; " +
                   $"item drops {ItemDrops.Count}; characters {Characters.Count}";
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
        [HarmonyPostfix, HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        private static void WorldLoaded()
        {
            WorldRegistry.Clear();
            Coordination.Reservations.Clear();
            Brain.ProductionStatus.Clear();
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
