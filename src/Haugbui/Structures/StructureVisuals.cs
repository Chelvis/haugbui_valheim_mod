using System.Collections.Generic;
using System.Linq;
using Haugbui.Diagnostics;
using Jotunn.Managers;
using UnityEngine;

namespace Haugbui.Structures
{
    internal static class StructureVisuals
    {
        public const string RootName = "HaugbuiVisual";

        internal static readonly HashSet<string> MissingParts = new HashSet<string>();

        /// <summary>"pieceId/part" → what was copied and why meshes were skipped; read by the self-test.</summary>
        internal static readonly Dictionary<string, string> PartDiagnostics = new Dictionary<string, string>();

        internal static int CopiedCount(string pieceId, string part) =>
            PartDiagnostics.TryGetValue(pieceId + "/" + part, out string text) && text.StartsWith("copied ")
                ? int.Parse(text.Substring(7).Split(';')[0])
                : -1;

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
                    if (MissingParts.Add(part.Prefab)) Log.Warning($"Visual part '{part.Prefab}' not found for {definition.PieceId}");
                    continue;
                }
                var holder = new GameObject(part.Prefab);
                holder.transform.SetParent(root.transform, false);
                holder.transform.localPosition = anchor + part.Offset;
                holder.transform.localRotation = Quaternion.Euler(part.Euler);
                holder.transform.localScale = Vector3.one * part.Scale;
                copied += CopyMeshes(source, holder.transform, definition.PieceId + "/" + part.Prefab, part.Emission);
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
        /// Copies static and skinned meshes as plain meshes. Instantiating the source prefab would create a networked
        /// object. Item prefabs keep their world model under "attach"; inactive children are alternate models.
        /// </summary>
        private static int CopyMeshes(GameObject source, Transform parent, string diagnosticsKey, Color? emission)
        {
            int count = 0;
            var skipped = new List<string>();
            GameObject floater = source.GetComponent<TombStone>()?.m_floater;
            foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                string path = PathOf(renderer.transform, source.transform);
                Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                    : renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>()?.sharedMesh
                    : null;
                string reason = mesh == null ? renderer.GetType().Name + " without mesh"
                    : floater != null && renderer.transform.IsChildOf(floater.transform) ? "floater"
                    : HiddenReason(renderer.transform, source.transform);
                if (reason != null)
                {
                    skipped.Add($"{path} ({reason})");
                    continue;
                }

                var copy = new GameObject(renderer.name);
                copy.transform.SetParent(parent, false);
                copy.transform.localPosition = source.transform.InverseTransformPoint(renderer.transform.position);
                copy.transform.localRotation = Quaternion.Inverse(source.transform.rotation) * renderer.transform.rotation;
                copy.transform.localScale = renderer.transform.lossyScale;
                copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                copy.AddComponent<MeshRenderer>().sharedMaterials = emission.HasValue
                    ? renderer.sharedMaterials.Select(m => Tinted(m, emission.Value, diagnosticsKey)).ToArray()
                    : renderer.sharedMaterials;
                count++;
            }

            if (!PartDiagnostics.ContainsKey(diagnosticsKey))
                PartDiagnostics[diagnosticsKey] = $"copied {count}" + (skipped.Count > 0 ? "; skipped " + string.Join(", ", skipped) : "");
            return count;
        }

        private static readonly Dictionary<(Material, Color), Material> TintCache = new Dictionary<(Material, Color), Material>();

        /// <summary>"pieceId/part" → shader and emission data of tinted materials; read by the self-test.</summary>
        internal static readonly Dictionary<string, string> MaterialDiagnostics = new Dictionary<string, string>();

        /// <summary>
        /// Works on a copy: the source material is shared with every vanilla object using it (e.g. all player
        /// tombstones), so editing it in place would recolour them too.
        /// </summary>
        private static Material Tinted(Material source, Color hue, string diagnosticsKey)
        {
            if (source == null || !source.HasProperty(EmissionColor)) return source;
            if (TintCache.TryGetValue((source, hue), out Material cached)) return cached;

            Color original = source.GetColor(EmissionColor);
            float intensity = Mathf.Max(original.r, original.g, original.b);
            var copy = new Material(source) { name = source.name + " (Haugbui)" };
            copy.SetColor(EmissionColor, hue * (intensity > 0f ? intensity : 1f));
            copy.EnableKeyword("_EMISSION");
            TintCache[(source, hue)] = copy;

            Texture map = source.HasProperty(EmissionMap) ? source.GetTexture(EmissionMap) : null;
            string line = $"{source.name}: shader {source.shader.name}, emission {original}, map {(map != null ? map.name : "none")}";
            MaterialDiagnostics[diagnosticsKey] = MaterialDiagnostics.TryGetValue(diagnosticsKey, out string previous)
                ? previous + "; " + line
                : line;
            return copy;
        }

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");

        private static string PathOf(Transform node, Transform root)
        {
            var parts = new List<string>();
            for (Transform t = node; t != null && t != root; t = t.parent) parts.Insert(0, t.name);
            return parts.Count == 0 ? "(root)" : string.Join("/", parts);
        }

        private static string HiddenReason(Transform node, Transform root)
        {
            for (Transform t = node; t != null && t != root; t = t.parent)
            {
                if (!t.gameObject.activeSelf) return "inactive " + t.name;
            }
            return null;
        }
    }
}
