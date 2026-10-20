using System.Collections.Generic;
using System.Linq;
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

        private const float InteractionAnimationSpeed = 2f;
        private const float InteractionAnimationSeconds = 1.5f;

        /// <summary>The swing's wind-up is long; playback starts this far into it (normalized time).</summary>
        private const float InteractionAnimationStart = 0.35f;

        private static string _attackTrigger;
        private float _fastAnimationUntil;
        private bool _skipWindUp;

        /// <summary>Interaction animations started (self-test).</summary>
        internal int InteractionAnimations { get; private set; }

        /// <summary>Interaction animations that jumped past the wind-up (self-test).</summary>
        internal int InteractionAnimationsHalved { get; private set; }

        /// <summary>Pickup/deposit sparkle: a short flash of the glow, and the Wraith's attack swing as the interaction gesture.</summary>
        internal void Pulse()
        {
            _pulse = LowQuality ? 0.5f : 1f;
            if (!LowQuality) PlayInteraction();
        }

        /// <summary>
        /// Purely cosmetic: triggers the default weapon's attack animation without starting an attack (no hit, no delay of
        /// the transfer). The animator keeps a changed speed only while in an attack-tagged state
        /// (CharacterAnimEvent.CustomFixedUpdate), so the speed-up ends with the swing.
        /// </summary>
        private void PlayInteraction()
        {
            Humanoid humanoid = GetComponent<Humanoid>();
            if (humanoid == null || _view == null || !_view.IsValid() || !_view.IsOwner() || humanoid.InAttack()) return;
            string trigger = AttackTrigger(humanoid);
            if (string.IsNullOrEmpty(trigger)) return;
            humanoid.GetZAnim()?.SetTrigger(trigger);
            _fastAnimationUntil = Time.time + InteractionAnimationSeconds;
            _skipWindUp = true;
            InteractionAnimations++;
        }

        /// <summary>Once the trigger reaches the attack state (or the transition into it), jump past the wind-up.</summary>
        private void SkipWindUp(Animator animator)
        {
            if (animator == null) return;
            AnimatorStateInfo state = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
            if (!state.IsTag("attack")) return;
            animator.Play(state.fullPathHash, 0, InteractionAnimationStart);
            _skipWindUp = false;
            InteractionAnimationsHalved++;
        }

        private static string AttackTrigger(Humanoid humanoid)
        {
            if (_attackTrigger != null) return _attackTrigger;
            Attack attack = humanoid.m_defaultItems?
                .Select(go => go != null ? go.GetComponent<ItemDrop>() : null)
                .Where(drop => drop != null)
                .Select(drop => drop.m_itemData.m_shared.m_attack)
                .FirstOrDefault(a => a != null && !string.IsNullOrEmpty(a.m_attackAnimation));
            if (attack == null) return _attackTrigger = string.Empty;
            // Attack.Start appends the chain level or a random index when the attack has several animations.
            string suffix = attack.m_attackChainLevels > 1 || attack.m_attackRandomAnimations >= 2 ? "0" : "";
            return _attackTrigger = attack.m_attackAnimation + suffix;
        }

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

            if (Time.time < _fastAnimationUntil)
            {
                Humanoid humanoid = GetComponent<Humanoid>();
                if (humanoid != null && _skipWindUp) SkipWindUp(humanoid.m_animator);
                if (humanoid != null && humanoid.InAttack()) humanoid.GetZAnim()?.SetSpeed(InteractionAnimationSpeed);
            }

            _pulse = Mathf.Max(0, _pulse - Time.deltaTime * 2f);
            _glow.color = color;
            _glow.intensity = intensity + _pulse * 2f;
        }
    }
}
