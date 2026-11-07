using System.Collections.Generic;
using Haugbui.Structures;
using UnityEngine;
using UnityEngine.Rendering;

namespace Haugbui.Creature
{
    /// <summary>Find Haugbui, from the tomb button or from Shift+F on the tomb hover, turns the camera and marks it for a few seconds.</summary>
    internal static class ServantFinder
    {
        internal static bool Look(TombController tomb)
        {
            Player player = Player.m_localPlayer;
            HaugbuiCreature creature = tomb != null ? tomb.Creature : null;
            if (player == null || creature == null)
            {
                player?.Message(MessageHud.MessageType.Center, "$haugbui_msg_ping_inside");
                return false;
            }

            Vector3 flat = creature.transform.position - player.transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.04f) player.transform.rotation = Quaternion.LookRotation(flat.normalized);
            player.SetMouseLookForward(true);
            Vector3 eye = player.transform.position + Vector3.up * 1.6f;
            Vector3 aim = creature.transform.position + Vector3.up * 1.2f;
            Vector3 dir = aim - eye;
            float angle = Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;
            player.SetMouseLook(new Vector2(0f, angle));
            CreaturePing.Show(creature);
            return true;
        }
    }

    /// <summary>
    /// Hidden/Internal-Colored is a built-in shader whose ZTest can be Always, so this pass draws through walls.
    /// </summary>
    internal sealed class CreaturePing : MonoBehaviour
    {
        private const float Seconds = 5f;

        private static Material _material;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Material[]> _saved = new List<Material[]>();
        private float _until;

        internal bool Showing => Time.time < _until;

        internal static void Show(HaugbuiCreature creature)
        {
            if (creature == null) return;
            CreaturePing ping = creature.GetComponent<CreaturePing>() ?? creature.gameObject.AddComponent<CreaturePing>();
            ping.Arm();
        }

        internal void Clear()
        {
            _until = 0f;
            Restore();
        }

        private void Arm()
        {
            _until = Time.time + Seconds;
            if (_renderers.Count > 0 || !EnsureMaterial()) return;
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                _renderers.Add(renderer);
                _saved.Add(renderer.sharedMaterials);
                var marked = new Material[renderer.sharedMaterials.Length + 1];
                renderer.sharedMaterials.CopyTo(marked, 0);
                marked[marked.Length - 1] = _material;
                renderer.sharedMaterials = marked;
            }
        }

        private void LateUpdate()
        {
            if (_renderers.Count > 0 && Time.time >= _until) Restore();
        }

        private void OnDestroy() => Restore();

        private void Restore()
        {
            for (int i = 0; i < _renderers.Count && i < _saved.Count; i++)
                if (_renderers[i] != null) _renderers[i].sharedMaterials = _saved[i];
            _renderers.Clear();
            _saved.Clear();
        }

        private static bool EnsureMaterial()
        {
            if (_material != null) return true;
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return false;
            _material = new Material(shader) { name = "Haugbui ping" };
            _material.SetColor("_Color", new Color(0.37f, 1f, 0.75f, 0.9f));
            _material.SetFloat("_ZTest", (float)CompareFunction.Always);
            _material.SetFloat("_ZWrite", 0f);
            _material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            _material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            return true;
        }
    }
}
