using System.Linq;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.UI;
using UnityEngine;

namespace Haugbui.Structures
{
    /// <summary>
    /// HB-012: the vanilla workbench-extension connection effect between an upgrade and its tomb.
    /// A workbench draws it only while the station is in use, so this draws only while the player has the tomb open.
    /// </summary>
    internal sealed class UpgradeBeam : MonoBehaviour
    {
        private const float CheckSeconds = 0.5f;

        private static GameObject _effectPrefab;
        private static bool _effectSearched;
        private static Container _playerContainer;

        private HaugbuiStructure _structure;
        private GameObject _beam;
        private float _nextCheck;

        /// <summary>The container the local inventory is showing; null when that window is closed.</summary>
        internal static void SetPlayerContainer(Container container) => _playerContainer = container;

        private void Awake() => _structure = GetComponent<HaugbuiStructure>();

        private void OnDestroy() => Hide();

        private void Update()
        {
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + CheckSeconds;

            HaugbuiStructure tomb = _structure != null ? StructureRegistry.FindTomb(_structure.BoundTombGuid) : null;
            bool show = tomb != null && InUse(tomb)
                        && Vector3.Distance(tomb.transform.position, transform.position) <= StructureRegistry.UpgradeRange;
            if (show) Show(tomb.transform.position + Vector3.up * 0.8f);
            else Hide();
        }

        /// <summary>The tomb panel, or the tomb's own storage window. Not merely standing next to it.</summary>
        private static bool InUse(HaugbuiStructure tomb)
        {
            TombController controller = tomb.GetComponent<TombController>();
            if (controller != null && TombPanel.IsOpen && TombPanel.Current == controller) return true;
            Container own = tomb.GetComponent<Container>();
            return _playerContainer != null && own != null && _playerContainer == own;
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
