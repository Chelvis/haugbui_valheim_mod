using System.Collections.Generic;
using System.Linq;
using Haugbui.Diagnostics;
using Jotunn.Managers;
using UnityEngine;

namespace Haugbui.Creature
{
    /// <summary>HB-T-027: non-persistent Wraith clone; it never spawns naturally because no spawner lists it.</summary>
    internal static class CreaturePrefab
    {
        public const string Name = "Haugbui_Creature";
        public const string Source = "Wraith";

        // Contacts with these layers are ignored, so the body passes through them; raycast-based hits still work.
        private static readonly string[] PassThroughLayers =
        {
            "Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "blocker", "vehicle", "item"
        };

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>Lighter than the tomb's purple: _Color multiplies the texture, so a dark tint makes the body murky.</summary>
        public static readonly Color Lilac = new Color(0.82f, 0.66f, 1f);

        public static GameObject Prefab { get; private set; }

        /// <summary>Wraith smoke/fog particle systems (paths under the root); shown only while the Altar is bound (HB-053).</summary>
        public static IReadOnlyList<string> AuraPaths { get; private set; } = new string[0];

        /// <summary>Shader and colour data of the tinted materials; read by the self-test.</summary>
        public static readonly List<string> MaterialDiagnostics = new List<string>();

        public static int TintedMaterials { get; private set; }

        public static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable += Create;
        }

        private static void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            GameObject clone = PrefabManager.Instance.CreateClonedPrefab(Name, Source);
            if (clone == null)
            {
                Log.Error($"Haugbui creature disabled: vanilla prefab '{Source}' not found.");
                return;
            }

            clone.GetComponent<ZNetView>().m_persistent = false;
            CharacterDrop drops = clone.GetComponent<CharacterDrop>();
            if (drops != null) Object.DestroyImmediate(drops);
            Character character = clone.GetComponent<Character>();
            character.m_name = "$haugbui_name";
            character.m_tamed = true;
            Tint(clone);
            AuraPaths = clone.GetComponentsInChildren<ParticleSystem>(true)
                .Where(p => p.transform != clone.transform)
                .Select(p => PathOf(p.transform, clone.transform))
                .ToArray();
            clone.AddComponent<HaugbuiCreature>();
            PrefabManager.Instance.AddPrefab(clone);
            Prefab = clone;
        }

        /// <summary>
        /// Lilac, a lighter shade of the tomb's purple. Always on copies: the source materials are shared with every vanilla Wraith.
        /// </summary>
        private static void Tint(GameObject root)
        {
            var copies = new Dictionary<Material, Material>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials = renderer.sharedMaterials
                    .Select(m => m == null ? null : copies.TryGetValue(m, out Material c) ? c : copies[m] = Tinted(m))
                    .ToArray();
            }
            TintedMaterials = copies.Values.Count(m => m != null);
        }

        private static Material Tinted(Material source)
        {
            Color tint = Lilac;
            var copy = new Material(source) { name = source.name + " (Haugbui)" };
            var changed = new List<string>();
            foreach (int id in new[] { ColorId, TintColorId })
            {
                if (!source.HasProperty(id)) continue;
                Color original = source.GetColor(id);
                float value = Mathf.Max(original.r, original.g, original.b);
                copy.SetColor(id, new Color(tint.r * value, tint.g * value, tint.b * value, original.a));
                changed.Add($"{(id == ColorId ? "_Color" : "_TintColor")} {original}");
            }
            if (source.HasProperty(EmissionColorId))
            {
                Color original = source.GetColor(EmissionColorId);
                float intensity = Mathf.Max(original.r, original.g, original.b);
                if (intensity > 0f) copy.SetColor(EmissionColorId, tint * intensity);
                changed.Add($"_EmissionColor {original}");
            }
            MaterialDiagnostics.Add($"{source.name}: shader {source.shader.name}, {(changed.Count > 0 ? string.Join(", ", changed) : "no colour property")}");
            return copy;
        }

        private static string PathOf(Transform node, Transform root)
        {
            var parts = new List<string>();
            for (Transform t = node; t != null && t != root; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }

        public static void MakeIncorporeal(Character character)
        {
            int mask = LayerMask.GetMask(PassThroughLayers);
            Collider collider = character.GetCollider();
            if (collider != null) collider.excludeLayers = mask;
            Rigidbody body = character.GetComponent<Rigidbody>();
            if (body != null) body.excludeLayers = mask;
        }
    }
}
