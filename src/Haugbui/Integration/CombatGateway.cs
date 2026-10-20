using System.Linq;
using Haugbui.Core.Combat;
using Haugbui.Creature;
using UnityEngine;

namespace Haugbui.Integration
{
    /// <summary>HB-141/142: what the game says about a possible target, and the Haugbui's own weapon.</summary>
    internal static class CombatGateway
    {
        private const float FleeGraceSeconds = 1f;

        /// <param name="hurtHaugbui">It damaged this Haugbui recently (attacking a Haugbui is attacking the base).</param>
        public static TargetFacts Facts(Character character, Character haugbui, Vector3 tomb, bool hurtHaugbui)
        {
            Vector3 position = character.transform.position;
            ZNetView view = character.m_nview;
            return new TargetFacts
            {
                Dead = character.IsDead() || character.GetHealth() <= 0f,
                IsPlayer = character.IsPlayer(),
                Tamed = character.IsTamed(),
                IsHaugbui = character.GetComponent<HaugbuiCreature>() != null,
                Aggressor = hurtHaugbui || Attacking(character.GetComponent<MonsterAI>()),
                Boss = character.IsBoss(),
                EnemyOfTamed = haugbui != null && BaseAI.IsEnemy(haugbui, character),
                Loaded = view != null && view.IsValid() && LoadedArea.IsLoaded(position),
                DistanceToTomb = Vector3.Distance(position, tomb),
            };
        }

        /// <summary>
        /// Monster AI that goes for a player, a Haugbui or a player-built piece and is not running away. Animal AI
        /// (deer, hares) has no MonsterAI, so it never counts. Alerted is not required: the AI already has an attack
        /// target, and it walks to player pieces without being alerted.
        /// </summary>
        public static bool Attacking(MonsterAI ai)
        {
            if (ai == null) return false;
            if (ai.m_lastFlee > 0f && Time.time - ai.m_lastFlee < ai.m_fleeInterval + FleeGraceSeconds) return false;
            Character target = ai.GetTargetCreature();
            if (target != null) return target.IsPlayer() || target.GetComponent<HaugbuiCreature>() != null;
            return ai.GetStaticTarget() is Piece piece && piece != null && piece.IsPlacedByPlayer();
        }

        /// <summary>
        /// HB-141: a random event (raid) whose area overlaps the operational radius. Uses the event itself
        /// (GetCurrentRandomEvent), not GetActiveEvent — that one is only "the local player is standing in it" and can
        /// be a forced boss event instead.
        /// </summary>
        public static bool RaidReaches(Vector3 tomb, float radius)
        {
            RandEventSystem events = RandEventSystem.instance;
            if (events == null) return false;
            RandomEvent raid = events.GetCurrentRandomEvent();
            if (raid == null) return false;
            Vector2 offset = new Vector2(tomb.x - raid.m_pos.x, tomb.z - raid.m_pos.z);
            return TargetPolicy.RaidReaches(offset.magnitude, raid.m_eventRange, radius);
        }

        public static string IdOf(Character character) =>
            character != null && character.m_nview != null && character.m_nview.IsValid() ? character.GetZDOID().ToString() : null;

        public static bool IsTargeting(Character attacker, Character target)
        {
            BaseAI ai = attacker != null ? attacker.GetBaseAI() : null;
            return ai != null && ai.GetTargetCreature() == target;
        }

        /// <summary>
        /// The Wraith's default weapon. The vanilla AI equips it when it attacks (Humanoid.GiveDefaultItems only stores
        /// weapons), and that AI never runs on a Haugbui.
        /// </summary>
        public static ItemDrop.ItemData Weapon(Humanoid humanoid)
        {
            if (humanoid == null) return null;
            ItemDrop.ItemData current = humanoid.GetCurrentWeapon();
            ItemDrop.ItemData unarmed = humanoid.m_unarmedWeapon != null ? humanoid.m_unarmedWeapon.m_itemData : null;
            if (current != null && current != unarmed) return current;
            ItemDrop.ItemData weapon = humanoid.GetInventory()?.GetAllItems().FirstOrDefault(i => i.IsWeapon());
            if (weapon != null && !humanoid.InAttack()) humanoid.EquipItem(weapon, triggerEquipEffects: false);
            return humanoid.GetCurrentWeapon();
        }

        public static string Describe(ItemDrop.ItemData weapon)
        {
            if (weapon == null) return "none";
            ItemDrop.ItemData.SharedData shared = weapon.m_shared;
            HitData.DamageTypes d = shared.m_damages;
            string prefab = weapon.m_dropPrefab != null ? weapon.m_dropPrefab.name : shared.m_name;
            return $"{prefab}: damage {d.GetTotalDamage():0.#} (blunt {d.m_blunt:0.#}, slash {d.m_slash:0.#}, pierce {d.m_pierce:0.#}, " +
                   $"fire {d.m_fire:0.#}, frost {d.m_frost:0.#}, poison {d.m_poison:0.#}, spirit {d.m_spirit:0.#}), " +
                   $"range {shared.m_aiAttackRange:0.##} m, interval {shared.m_aiAttackInterval:0.##} s, max angle {shared.m_aiAttackMaxAngle:0.#}°";
        }
    }
}
