using System.Linq;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Integration;
using TMPro;
using UnityEngine;

namespace Haugbui.Creature
{
    /// <summary>HB-T-036/105: countdown to its return (death or a new tomb's first dusk), 1 Hz, hidden beyond 20 m.</summary>
    public sealed class CountdownLabel : MonoBehaviour
    {
        private const float CullDistance = 20f;

        private static TMP_FontAsset _font;

        private TombController _tomb;
        private TextMeshPro _text;
        private float _next;

        internal bool HasFont => _text != null && _text.font != null;

        internal Transform Label => _text != null ? _text.transform : null;

        internal string Shown => _text != null && _text.gameObject.activeSelf ? _text.text : string.Empty;

        private void Start()
        {
            _tomb = GetComponent<TombController>();
            var go = new GameObject("haugbui_countdown");
            // Inactive until the font is set; otherwise TMP looks for its default font, which Valheim does not ship.
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 2.4f;
            // Billboard points +forward at the camera, but TMP text reads from its -forward side: invert it.
            go.AddComponent<Billboard>().m_invert = true;
            _text = go.AddComponent<TextMeshPro>();
            if (_font == null)
                _font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().OrderByDescending(f => f.name.Contains("Averia")).FirstOrDefault();
            if (_font != null) _text.font = _font;
            _text.fontSize = 4f;
            _text.alignment = TextAlignmentOptions.Center;
            _text.color = new Color(0.85f, 0.7f, 1f);
        }

        private void Update()
        {
            if (_text == null || Time.time < _next) return;
            _next = Time.time + 1f;

            double remaining = Remaining();
            Player player = Player.m_localPlayer;
            bool near = player != null && Vector3.Distance(player.transform.position, transform.position) <= CullDistance;
            bool show = remaining > 0 && near;
            if (show) _text.text = PresencePolicy.FormatCountdown(remaining);
            if (_text.gameObject.activeSelf != show) _text.gameObject.SetActive(show);
        }

        /// <summary>Non-owners read the state straight from the ZDO, so every peer sees the same countdown.</summary>
        private double Remaining()
        {
            if (_tomb == null) return 0;
            if (_tomb.IsOwner) return _tomb.WaitRemaining;
            ZDO zdo = _tomb.Structure.Zdo;
            if (zdo == null || TombStateSerializer.Instance.TryDeserialize(new ZdoBlobStore(zdo).Get(ZdoKeys.TombState), out TombState state) != BlobReadStatus.Ok)
                return 0;
            return PresencePolicy.WaitRemaining(_tomb.InputFor(state, state.CreatureState == (int)HaugbuiState.Dead));
        }
    }
}
