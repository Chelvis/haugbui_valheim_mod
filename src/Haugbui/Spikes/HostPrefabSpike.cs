#if DEBUG
using System;
using System.Linq;
using HarmonyLib;
using Haugbui.Diagnostics;
using Haugbui.Diagnostics.Commands;
using UnityEngine;

namespace Haugbui.Spikes
{
    /// <summary>
    /// HB-T-007: tags a vanilla chest with Haugbui keys and changes its look while the mod runs. Without the mod the
    /// chest must load as a plain chest with its items and keys intact.
    /// </summary>
    internal sealed class HostPrefabSpikeCommand : HaugbuiCommand
    {
        public override string Name => "haugbui_spike_host";
        public override string Help => "SPIKE HB-T-007: 'tag' / 'untag' the nearest chest, 'info' prints its Haugbui keys.";
        public override bool IsCheat => true;

        protected override void Execute(string[] args)
        {
            Container chest = SpikeUtils.PlayerBuiltContainersNear(Player.m_localPlayer.transform.position, 5f).FirstOrDefault();
            if (chest == null)
            {
                Print("Stand within 5 m of a player-built chest.");
                return;
            }

            ZDO zdo = chest.m_nview.GetZDO();
            switch (args.Length > 0 ? args[0] : "")
            {
                case "tag":
                    if (!chest.m_nview.IsOwner()) chest.m_nview.ClaimOwnership();
                    zdo.Set(HostPrefabSpike.KindKey, HostPrefabSpike.SpikeKind);
                    zdo.Set(HostPrefabSpike.GuidKey, Guid.NewGuid().ToString("N"));
                    HostPrefabSpike.Apply(chest);
                    Print($"Tagged {chest.name}; save, quit, remove Haugbui.dll, load and save the world, re-add the DLL, load again.");
                    break;
                case "untag":
                    zdo.Set(HostPrefabSpike.KindKey, "");
                    Print("Untagged; reload the area to restore the vanilla look.");
                    break;
                case "info":
                    Print($"{chest.name}: kind='{zdo.GetString(HostPrefabSpike.KindKey)}' guid='{zdo.GetString(HostPrefabSpike.GuidKey)}' " +
                          $"items={chest.GetInventory().NrOfItems()} prefab={ZNetScene.instance.GetPrefab(zdo.GetPrefab())?.name}");
                    break;
                default:
                    Print(Help);
                    break;
            }
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.Awake))]
    internal static class HostPrefabSpike
    {
        public const string KindKey = "haugbui_kind";
        public const string GuidKey = "haugbui_guid";
        public const string SpikeKind = "spike_tomb";
        private const string VisualName = "HaugbuiSpikeVisual";

        private static void Postfix(Container __instance)
        {
            try
            {
                ZDO zdo = __instance.m_nview != null ? __instance.m_nview.GetZDO() : null;
                if (zdo != null && zdo.GetString(KindKey) == SpikeKind) Apply(__instance);
            }
            catch (Exception e)
            {
                Log.ErrorRateLimited("spike_host", $"Host prefab spike failed: {e}");
            }
        }

        public static void Apply(Container chest)
        {
            if (chest.transform.Find(VisualName) != null) return;

            foreach (Renderer renderer in chest.GetComponentsInChildren<Renderer>())
                renderer.enabled = false;

            var visual = new GameObject(VisualName);
            visual.transform.SetParent(chest.transform, false);

            GameObject tombstone = Game.instance != null ? Game.instance.m_playerPrefab.GetComponent<Player>().m_tombstone : null;
            int copied = tombstone != null ? CopyMeshes(tombstone, visual.transform) : 0;
            if (copied == 0)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.Destroy(cube.GetComponent<Collider>());
                cube.transform.SetParent(visual.transform, false);
                cube.transform.localScale = new Vector3(1f, 1.2f, 0.4f);
                cube.GetComponent<Renderer>().material.color = new Color(0.4f, 0.2f, 0.6f);
            }
            Log.Info($"Host prefab spike: applied look to {chest.name} ({copied} meshes copied)");
        }

        /// <summary>Copies only mesh renderers; instantiating the networked prefab itself would create a new ZDO.</summary>
        private static int CopyMeshes(GameObject source, Transform parent)
        {
            int count = 0;
            // The player tombstone carries a floating raft (TombStone.m_floater) that only shows in water.
            GameObject floater = source.GetComponent<TombStone>()?.m_floater;
            foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || filter.sharedMesh == null) continue;
                if (floater != null && filter.transform.IsChildOf(floater.transform)) continue;
                var copy = new GameObject(filter.name);
                copy.transform.SetParent(parent, false);
                copy.transform.localPosition = source.transform.InverseTransformPoint(filter.transform.position);
                copy.transform.localRotation = Quaternion.Inverse(source.transform.rotation) * filter.transform.rotation;
                copy.transform.localScale = filter.transform.lossyScale;
                copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                copy.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                count++;
            }
            return count;
        }
    }
}
#endif
