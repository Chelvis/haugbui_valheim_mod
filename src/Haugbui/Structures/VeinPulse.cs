using System.Collections.Generic;
using UnityEngine;

namespace Haugbui.Structures
{
    /// <summary>
    /// Fades the one emission colour already on the vein material. No extra light or texture is added.
    /// </summary>
    internal sealed class VeinPulse : MonoBehaviour
    {
        public const float Period = 5f;

        public static readonly Color Full = new Color(0.55f, 0.08f, 1f) * 1.15f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private Material[] _materials;

        public static void Attach(GameObject holder)
        {
            var materials = new List<Material>();
            foreach (Renderer renderer in holder.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && material.HasProperty(EmissionColorId))
                        materials.Add(material);
                }
            }
            if (materials.Count == 0) return;
            holder.AddComponent<VeinPulse>()._materials = materials.ToArray();
        }

        /// <summary>0 at the ends of the loop, 1 from 30% through 70%.</summary>
        public static float Level(float u)
        {
            if (u <= 0.3f) return u / 0.3f;
            if (u <= 0.7f) return 1f;
            return (1f - u) / 0.3f;
        }

        private void Update()
        {
            float level = Level(Mathf.Repeat(Time.time, Period) / Period);
            Color color = Full * level;
            for (int i = 0; i < _materials.Length; i++)
            {
                if (_materials[i] != null)
                    _materials[i].SetColor(EmissionColorId, color);
            }
        }
    }
}
