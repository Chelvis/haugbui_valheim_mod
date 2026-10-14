using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Haugbui.Core.Structures;
using UnityEngine;

namespace Haugbui.Structures
{
    internal static class PlacementCheck
    {
        private const float RecheckSeconds = 0.25f;
        private const float RecheckDistance = 0.25f;

        private static readonly HashSet<Bed> Beds = new HashSet<Bed>();

        private static StructureKind _cachedKind;
        private static Vector3 _cachedPosition;
        private static float _cachedTime = float.NegativeInfinity;
        private static PlacementResult _cachedResult;

        public static float BedDistance { get; set; } = 50f;
        public static float TombSpacing { get; set; } = 5f;
        public static float SameOwnerSpacing { get; set; } = 100f;

        public static void TrackBed(Bed bed) => Beds.Add(bed);

        public static PlacementResult Check(StructureKind kind, Vector3 position, Player player)
        {
            if (kind == StructureKind.Tomb)
            {
                Beds.RemoveWhere(b => b == null);
                IEnumerable<BedInfo> beds = Beds.Select(b => new BedInfo(StructureRegistry.ToVec3(b.transform.position), b.GetOwner()));
                var tombs = StructureRegistry.Tombs
                    .Select(t => (StructureRegistry.ToVec3(t.transform.position), t.TombState?.OwnerId ?? 0L)).ToList();
                PlacementResult result = PlacementRules.CheckTomb(StructureRegistry.ToVec3(position), player.GetPlayerID(), beds,
                    tombs, BedDistance, TombSpacing, SameOwnerSpacing);
                // HB-138: one tomb per servant slot.
                if (result == PlacementResult.Ok && !Integration.ServantRegistry.HasVacantSlot(player.GetPlayerID())) return PlacementResult.MaxTombs;
                return result;
            }
            if (TombProgression.IsUpgrade(kind)) return CheckUpgrade(kind, position, out _);
            return PlacementResult.Ok;
        }

        public static PlacementResult CheckUpgrade(StructureKind kind, Vector3 position, out TombInfo tomb) =>
            PlacementRules.CheckUpgrade(kind, StructureRegistry.ToVec3(position), StructureRegistry.TombInfos(),
                StructureRegistry.UpgradeRange, out tomb);

        /// <summary>Throttled for the placement ghost, which moves every frame.</summary>
        public static PlacementResult CheckGhost(StructureKind kind, Vector3 position, Player player)
        {
            if (kind == _cachedKind && Time.time - _cachedTime < RecheckSeconds
                                    && Vector3.Distance(position, _cachedPosition) < RecheckDistance)
                return _cachedResult;

            _cachedKind = kind;
            _cachedPosition = position;
            _cachedTime = Time.time;
            _cachedResult = Check(kind, position, player);
            return _cachedResult;
        }
    }

    [HarmonyPatch]
    internal static class PlacementPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Bed), nameof(Bed.Awake))]
        private static void TrackBed(Bed __instance) => PlacementCheck.TrackBed(__instance);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacementGhost))]
        private static void MarkGhostInvalid(Player __instance)
        {
            GameObject ghost = __instance.m_placementGhost;
            if (ghost == null || __instance.m_placementStatus != Player.PlacementStatus.Valid) return;
            HaugbuiStructure structure = ghost.GetComponent<HaugbuiStructure>();
            if (structure == null) return;

            if (PlacementCheck.CheckGhost(structure.Kind, ghost.transform.position, __instance) != PlacementResult.Ok)
            {
                __instance.m_placementStatus = Player.PlacementStatus.Invalid;
                __instance.SetPlacementGhostValid(false);
            }
        }

        /// <summary>Replaces the generic "invalid placement" message with the Haugbui reason.</summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static bool ExplainInvalidPlacement(Player __instance, Piece piece, ref bool __result)
        {
            HaugbuiStructure structure = piece != null ? piece.GetComponent<HaugbuiStructure>() : null;
            GameObject ghost = __instance.m_placementGhost;
            if (structure == null || ghost == null) return true;

            PlacementResult result = PlacementCheck.Check(structure.Kind, ghost.transform.position, __instance);
            if (result == PlacementResult.Ok) return true;

            __instance.Message(MessageHud.MessageType.Center, "$" + PlacementMessages.KeyFor(result));
            __result = false;
            return false;
        }
    }
}
