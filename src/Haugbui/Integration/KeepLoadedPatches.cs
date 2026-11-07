using System.Collections.Generic;
using HarmonyLib;
using Haugbui.Core.GameApi;

namespace Haugbui.Integration
{
    /// <summary>
    /// RemoveObjects stamps an earmark on the lists it is given, then destroys every view that lacks it.
    /// A postfix cannot put an object back. The tomb's objects have to be on the list before that loop.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "RemoveObjects")]
    internal static class KeepLoadedRetain
    {
        private static bool Prepare() => GameApiSelfCheck.IsAvailable(Features.RemoteWork);

        private static void Prefix(List<ZDO> currentNearObjects) => KeepLoaded.Retain(currentNearObjects);
    }

    [HarmonyPatch(typeof(ZNetScene), "CreateDestroyObjects")]
    internal static class KeepLoadedSpawn
    {
        private static bool Prepare() => GameApiSelfCheck.IsAvailable(Features.RemoteWork);

        private static void Postfix(ZNetScene __instance) => KeepLoaded.SpawnMissing(__instance);
    }

    [HarmonyPatch(typeof(ZoneSystem), "Update")]
    internal static class KeepLoadedZones
    {
        private static bool Prepare() => GameApiSelfCheck.IsAvailable(Features.RemoteWork);

        private static void Postfix(ZoneSystem __instance) => KeepLoaded.Poke(__instance);
    }
}
