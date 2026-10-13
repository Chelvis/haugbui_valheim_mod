using Haugbui.Diagnostics;
using Jotunn.Managers;
using UnityEngine;

namespace Haugbui.Structures
{
    internal static class StructureVisuals
    {
        public const string RootName = "HaugbuiVisual";

        /// <summary>Idempotent: hides the host meshes when requested and adds copies of the parts' meshes.</summary>
        public static void Apply(GameObject target, StructureDefinition definition)
        {
            if (target.transform.Find(RootName) != null) return;

            float? hostTop = definition.HideHost ? (float?)null : LocalTop(target);
            if (definition.HideHost)
            {
                foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(target.transform, false);
            Vector3 anchor = new Vector3(0, hostTop ?? 0f, 0);

            int copied = 0;
            foreach (VisualPart part in definition.Parts)
            {
                GameObject source = PrefabManager.Cache.GetPrefab<GameObject>(part.Prefab);
                if (source == null)
                {
                    Log.Warning($"Visual part '{part.Prefab}' not found for {definition.PieceId}");
                    continue;
                }
                var holder = new GameObject(part.Prefab);
                holder.transform.SetParent(root.transform, false);
                holder.transform.localPosition = anchor + part.Offset;
                holder.transform.localRotation = Quaternion.Euler(part.Euler);
                holder.transform.localScale = Vector3.one * part.Scale;
                copied += CopyMeshes(source, holder.transform);
            }

            if (copied == 0 && definition.HideHost)
            {
                Log.Warning($"{definition.PieceId}: no visual parts found; showing the host instead.");
                foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = true;
            }
        }

        /// <summary>Highest point of the visible host meshes in the root's space; works on prefabs outside the scene.</summary>
        private static float? LocalTop(GameObject target)
        {
            float? top = null;
            foreach (MeshFilter filter in target.GetComponentsInChildren<MeshFilter>())
            {
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || filter.sharedMesh == null) continue;
                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    float y = target.transform.InverseTransformPoint(filter.transform.TransformPoint(corner)).y;
                    if (top == null || y > top) top = y;
                }
            }
            return top;
        }

        /// <summary>
        /// Copies only static meshes. Instantiating the source prefab would create a networked object, and item prefabs
        /// also carry an "attach" model for when they are equipped, which must be skipped.
        /// </summary>
        private static int CopyMeshes(GameObject source, Transform parent)
        {
            int count = 0;
            GameObject floater = source.GetComponent<TombStone>()?.m_floater;
            foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || filter.sharedMesh == null || !IsShownInWorld(filter.transform, source.transform)) continue;
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

        private static bool IsShownInWorld(Transform node, Transform root)
        {
            for (Transform t = node; t != null && t != root; t = t.parent)
            {
                if (!t.gameObject.activeSelf || t.name.ToLowerInvariant().Contains("attach")) return false;
            }
            return true;
        }
    }
}
