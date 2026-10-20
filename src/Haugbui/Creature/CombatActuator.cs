using Haugbui.Integration;
using UnityEngine;

namespace Haugbui.Creature
{
    /// <summary>
    /// HB-T-072: closes to the weapon's reach on the side it comes from, faces the target and swings with the vanilla
    /// Humanoid attack, at the weapon's own AI interval.
    /// </summary>
    internal sealed class CombatActuator
    {
        private const float MinAimAngle = 10f;

        public int AttacksStarted { get; private set; }

        public void Fight(HaugbuiCreature creature, Character target, float maxSpeed)
        {
            Humanoid humanoid = creature.Humanoid;
            ItemDrop.ItemData weapon = CombatGateway.Weapon(humanoid);
            float reach = weapon != null ? weapon.m_shared.m_aiAttackRange : 2f;
            Vector3 me = creature.transform.position;
            Vector3 at = target.transform.position;
            Vector3 away = me - at;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -target.transform.forward;
            float radius = target.GetRadius();

            creature.MaxSpeed = maxSpeed;
            creature.MoveTo(at + away.normalized * (radius + reach * 0.5f));
            creature.Facing = target.GetCenterPoint();

            if (weapon == null || humanoid.InAttack()) return;
            bool inReach = Vector3.Distance(at, me) - radius < reach;
            bool aimed = creature.AngleTo(at) < Mathf.Max(MinAimAngle, weapon.m_shared.m_aiAttackMaxAngle);
            bool ready = Time.time - weapon.m_lastAttackTime > weapon.m_shared.m_aiAttackInterval;
            if (inReach && aimed && ready && humanoid.StartAttack(target, false)) AttacksStarted++;
        }
    }
}
