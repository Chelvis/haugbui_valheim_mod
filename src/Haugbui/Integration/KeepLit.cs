using Haugbui.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace Haugbui.Integration
{
    /// <summary>Shift+E on a refillable fireplace toggles keep-lit. Fuel is added through the fire's own RPC.</summary>
    internal static class KeepLit
    {
        public static bool IsRefillable(Fireplace fire) =>
            fire != null && fire.m_canRefill && !fire.m_infiniteFuel && fire.m_fuelItem != null;

        public static bool IsOn(Fireplace fire)
        {
            ZNetView view = View(fire);
            return view != null && view.GetZDO().GetInt(ZdoKeys.KeepLit) != 0;
        }

        public static void Set(Fireplace fire, bool on)
        {
            ZNetView view = View(fire);
            if (view == null) return;
            if (!view.IsOwner()) view.ClaimOwnership();
            view.GetZDO().Set(ZdoKeys.KeepLit, on ? 1 : 0);
        }

        public static float Fuel(Fireplace fire)
        {
            ZNetView view = View(fire);
            return view != null ? view.GetZDO().GetFloat(ZDOVars.s_fuel) : 0f;
        }

        /// <summary>One unit, and only when the stored fuel actually rises.</summary>
        public static bool AddOne(Fireplace fire)
        {
            ZNetView view = View(fire);
            if (view == null || fire.m_maxFuel <= 0f) return false;
            if (!view.IsOwner()) view.ClaimOwnership();
            if (!view.IsOwner()) return false;
            float before = view.GetZDO().GetFloat(ZDOVars.s_fuel);
            if (System.Math.Ceiling(before) >= fire.m_maxFuel) return false;
            view.InvokeRPC("RPC_AddFuel");
            return view.GetZDO().GetFloat(ZDOVars.s_fuel) > before;
        }

        public static string FuelPrefab(Fireplace fire) =>
            fire != null && fire.m_fuelItem != null ? Utils.GetPrefabName(fire.m_fuelItem.gameObject) : null;

        public static string IdOf(Fireplace fire)
        {
            ZNetView view = View(fire);
            return view != null ? view.GetZDO().m_uid.ToString() : null;
        }

        private static ZNetView View(Fireplace fire)
        {
            if (fire == null) return null;
            ZNetView view = fire.GetComponent<ZNetView>() ?? fire.GetComponentInParent<ZNetView>();
            return view != null && view.IsValid() ? view : null;
        }
    }

    [HarmonyPatch]
    internal static class KeepLitPatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Interact))]
        private static bool ToggleOnShift(Fireplace __instance, bool hold, bool alt, ref bool __result)
        {
            if (!alt || hold || !KeepLit.IsRefillable(__instance)) return true;
            try
            {
                if (!PrivateArea.CheckAccess(__instance.transform.position, 0f, flash: true)) return true;
                KeepLit.Set(__instance, !KeepLit.IsOn(__instance));
                __result = true;
                return false;
            }
            catch (System.Exception e)
            {
                Log.ErrorRateLimited("keep-lit", $"Keep lit: {e}");
                return true;
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetHoverText))]
        private static void Hover(Fireplace __instance, ref string __result)
        {
            if (!KeepLit.IsRefillable(__instance)) return;
            string key = KeepLit.IsOn(__instance) ? "$haugbui_keep_lit_on" : "$haugbui_keep_lit_off";
            __result = (__result ?? string.Empty) + "\n" + Localization.instance.Localize(key);
        }
    }
}
