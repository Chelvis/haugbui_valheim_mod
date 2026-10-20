using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Recipes;
using Haugbui.Creature;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.Diagnostics.Commands
{
    /// <summary>
    /// The workbench build circle (<see cref="CircleProjector"/>). The segment prefab is cloned and tinted: its material
    /// is the one every workbench shares.
    /// </summary>
    internal sealed class RadiusCommand : HaugbuiCommand
    {
        private static readonly List<GameObject> Shown = new List<GameObject>();

        public override string Name => "haugbui_radius";
        public override string Help => "Haugbui: show or hide the operational radius of your tombs.";

        protected override void Execute(string[] args)
        {
            if (HideIfShown())
            {
                Print("Radius hidden.");
                return;
            }
            if (Player.m_localPlayer == null || ZNetScene.instance == null)
            {
                Print("Load a world first.");
                return;
            }

            GameObject bench = ZNetScene.instance.GetPrefab(StationPrefabs.Workbench);
            CraftingStation station = bench != null ? bench.GetComponent<CraftingStation>() : null;
            if (station == null || station.m_areaMarker == null)
            {
                Print("Workbench area marker is not available.");
                return;
            }

            int shown = 0;
            foreach (TombController tomb in TombController.All)
            {
                if (tomb == null || !tomb.IsOwner) continue;
                float radius = tomb.Attributes.Radius;
                GameObject marker = Object.Instantiate(station.m_areaMarker, tomb.transform.position, Quaternion.identity);
                marker.name = "Haugbui radius";
                CircleProjector circle = marker.GetComponent<CircleProjector>();
                if (circle == null)
                {
                    Object.Destroy(marker);
                    continue;
                }
                Prepare(circle, radius);
                marker.SetActive(true);
                marker.AddComponent<RadiusRing>().Bind(tomb, circle);
                Shown.Add(marker);
                shown++;
                Print($"{tomb.ServantName}: {radius:0.#} m (level {tomb.Attributes.Level})");
            }
            if (shown == 0) Print("No tomb of yours is loaded.");
        }

        private static bool HideIfShown()
        {
            Shown.RemoveAll(marker => marker == null);
            if (Shown.Count == 0) return false;
            foreach (GameObject marker in Shown) Object.Destroy(marker);
            Shown.Clear();
            return true;
        }

        /// <summary>More segments than the workbench uses: a 50–100 m ring would otherwise be a handful of dots.</summary>
        private static void Prepare(CircleProjector circle, float radius)
        {
            GameObject template = Object.Instantiate(circle.m_prefab);
            Tint(template);
            template.SetActive(false);
            circle.m_prefab = template;
            circle.m_radius = radius;
            circle.m_nrOfSegments = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * radius / 4f), 24, 96);
            template.transform.SetParent(circle.transform, false);
        }

        private static void Tint(GameObject root)
        {
            Color tint = StructureDefinitions.HaugbuiPurple;
            var copies = new Dictionary<Material, Material>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials = renderer.sharedMaterials
                    .Select(material => material == null ? null : copies.TryGetValue(material, out Material copy) ? copy : copies[material] = Tinted(material, tint))
                    .ToArray();
            }
            foreach (ParticleSystem particles in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particles.main;
                main.startColor = tint;
            }
            foreach (LineRenderer line in root.GetComponentsInChildren<LineRenderer>(true))
            {
                line.startColor = tint;
                line.endColor = tint;
            }
        }

        private static Material Tinted(Material source, Color tint)
        {
            var copy = new Material(source) { name = source.name + " (Haugbui radius)" };
            foreach (int id in new[] { Shader.PropertyToID("_Color"), Shader.PropertyToID("_TintColor") })
            {
                if (!source.HasProperty(id)) continue;
                Color original = source.GetColor(id);
                float value = Mathf.Max(original.r, original.g, original.b);
                copy.SetColor(id, new Color(tint.r * value, tint.g * value, tint.b * value, original.a));
            }
            int emission = Shader.PropertyToID("_EmissionColor");
            if (source.HasProperty(emission))
            {
                Color original = source.GetColor(emission);
                float intensity = Mathf.Max(0.6f, original.maxColorComponent);
                copy.SetColor(emission, tint * intensity);
            }
            return copy;
        }

        /// <summary>Keeps the circle on the tomb's current radius.</summary>
        private sealed class RadiusRing : MonoBehaviour
        {
            private TombController _tomb;
            private CircleProjector _circle;

            public void Bind(TombController tomb, CircleProjector circle)
            {
                _tomb = tomb;
                _circle = circle;
            }

            private void Update()
            {
                if (_tomb == null || _circle == null) return;
                float radius = _tomb.Attributes.Radius;
                if (Mathf.Approximately(_circle.m_radius, radius)) return;
                _circle.m_radius = radius;
                _circle.m_nrOfSegments = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * radius / 4f), 24, 96);
            }
        }
    }
}
