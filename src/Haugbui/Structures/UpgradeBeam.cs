using System.Linq;
using Haugbui.Core.Structures;
using UnityEngine;

namespace Haugbui.Structures
{
    /// <summary>HB-012: the vanilla workbench-extension connection effect between an upgrade and its tomb, near the player.</summary>
    internal sealed class UpgradeBeam : MonoBehaviour
    {
        private const float ShowDistance = 10f;
        private const float CheckSeconds = 0.5f;

        private static GameObject _effectPrefab;
        private static bool _effectSearched;

        private HaugbuiStructure _structure;
        private GameObject _beam;
        private float _nextCheck;

        private void Awake() => _structure = GetComponent<HaugbuiStructure>();

        private void OnDestroy() => Hide();

        private void Update()
        {
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + CheckSeconds;

            Player player = Player.m_localPlayer;
            HaugbuiStructure tomb = _structure != null ? StructureRegistry.FindTomb(_structure.BoundTombGuid) : null;
            bool show = player != null && tomb != null
                        && Vector3.Distance(player.transform.position, transform.position) <= ShowDistance
                        && Vector3.Distance(tomb.transform.position, transform.position) <= StructureRegistry.UpgradeRange;
            if (show) Show(tomb.transform.position + Vector3.up * 0.8f);
            else Hide();
        }

        private void Show(Vector3 target)
        {
            GameObject prefab = EffectPrefab();
            if (prefab == null) return;
            Vector3 start = transform.position + Vector3.up * 0.5f;
            if (_beam == null) _beam = Instantiate(prefab, start, Quaternion.identity);
            Vector3 span = target - start;
            _beam.transform.position = start;
            _beam.transform.rotation = Quaternion.LookRotation(span.normalized);
            _beam.transform.localScale = new Vector3(1f, 1f, span.magnitude);
        }

        private void Hide()
        {
            if (_beam != null) Destroy(_beam);
            _beam = null;
        }

        private static GameObject EffectPrefab()
        {
            if (_effectSearched) return _effectPrefab;
            _effectSearched = true;
            _effectPrefab = ZNetScene.instance?.m_prefabs
                .Select(p => p != null ? p.GetComponent<StationExtension>() : null)
                .FirstOrDefault(e => e != null && e.m_connectionPrefab != null)
                ?.m_connectionPrefab;
            return _effectPrefab;
        }

        internal bool IsShown => _beam != null;
    }
}
