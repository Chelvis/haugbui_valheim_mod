using HarmonyLib;
using Haugbui.Integration;

namespace Haugbui.Creature
{
    /// <summary>HB-142 level scaling: the damage of a Haugbui's hits and of the hits it takes.</summary>
    [HarmonyPatch]
    internal static class CombatPatches
    {
        private static bool Prepare() => GameApiSelfCheck.IsAvailable(Core.GameApi.Features.Combat);

        /// <summary>Every hit of an attack (melee, area or projectile) passes through here before it is dealt.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Attack), nameof(Attack.ModifyDamage))]
        private static void ScaleDealt(Attack __instance, HitData hitData)
        {
            HaugbuiCreature haugbui = __instance.m_character != null ? __instance.m_character.GetComponent<HaugbuiCreature>() : null;
            if (haugbui != null) haugbui.ScaleDealt(hitData);
        }

        /// <summary>Runs on the creature's owner before resistances, difficulty and world modifiers, which all multiply.</summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
        private static void ScaleTaken(Character __instance, HitData hit)
        {
            if (hit == null || __instance.m_nview == null || !__instance.m_nview.IsOwner()) return;
            HaugbuiCreature haugbui = __instance.GetComponent<HaugbuiCreature>();
            if (haugbui != null) haugbui.ScaleTaken(hit);
        }
    }
}
