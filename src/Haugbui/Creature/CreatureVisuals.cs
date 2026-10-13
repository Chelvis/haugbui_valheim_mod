using System.Collections.Generic;
using Haugbui.Core.Creature;
using Haugbui.Integration;
using UnityEngine;

namespace Haugbui.Creature
{
    /// <summary>
    /// HB-T-037/038: cheap per-state light cues, and the Wraith's own smoke aura shown only while the Altar is bound (HB-053).
    /// Read from the creature ZDO on every peer.
    /// </summary>
    public sealed class CreatureVisuals : MonoBehaviour
    {
        private static readonly Color Idle = new Color(0.7f, 0.65f, 1f);
        private static readonly Color Working = new Color(0.55f, 0.85f, 1f);
        private static readonly Color Alert = new Color(1f, 0.25f, 0.2f);
        private static readonly Color Homeward = new Color(0.45f, 0.35f, 0.7f);

        private ZNetView _view;
        private Light _glow;
        private readonly List<GameObject> _aura = new List<GameObject>();
        private bool? _auraShown;
        private float _pulse;

        internal static bool LowQuality { get; set; }

        internal HaugbuiState ShownState { get; private set; }
        internal bool DarkAuraShown => _auraShown == true;
        internal int AuraParts => _aura.Count;
        internal float GlowIntensity => _glow != null ? _glow.intensity : 0f;

        private void Awake()
        {
            _view = GetComponent<ZNetView>();
            _glow = CreateLight("haugbui_glow", 4f);
            foreach (string path in CreaturePrefab.AuraPaths)
            {
                Transform part = transform.Find(path);
                if (part != null) _aura.Add(part.gameObject);
            }
            ShowAura(false);
        }

        private Light CreateLight(string name, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 1.2f;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        /// <summary>Pickup/deposit sparkle: a short flash of the glow.</summary>
        internal void Pulse() => _pulse = LowQuality ? 0.5f : 1f;

        internal void SetDarkAura(bool on)
        {
            if (_view != null && _view.IsValid() && _view.IsOwner()) _view.GetZDO().Set(ZdoKeys.DarkAura, on);
        }

        private void ShowAura(bool on)
        {
            if (_auraShown == on) return;
            _auraShown = on;
            foreach (GameObject part in _aura) part.SetActive(on);
        }

        private void Update()
        {
            if (_view == null || !_view.IsValid()) return;
            ZDO zdo = _view.GetZDO();
            ShownState = (HaugbuiState)zdo.GetInt(ZdoKeys.CreatureState);
            ShowAura(zdo.GetBool(ZdoKeys.DarkAura));

            Color color;
            float intensity;
            switch (ShownState)
            {
                case HaugbuiState.Traveling:
                case HaugbuiState.Interacting:
                    color = Working; intensity = 1.4f; break;
                case HaugbuiState.Combat:
                case HaugbuiState.Fleeing:
                    color = Alert; intensity = 1.8f; break;
                case HaugbuiState.Returning:
                case HaugbuiState.Entering:
                    color = Homeward; intensity = 0.7f; break;
                default:
                    color = Idle; intensity = 0.9f; break;
            }
            if (LowQuality) intensity *= 0.5f;

            _pulse = Mathf.Max(0, _pulse - Time.deltaTime * 2f);
            _glow.color = color;
            _glow.intensity = intensity + _pulse * 2f;
        }
    }
}
