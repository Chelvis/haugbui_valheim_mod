using Haugbui.Core.Geometry;
using Haugbui.Registry;
using UnityEngine;

namespace Haugbui.Integration
{
    /// <summary>HB-T-047: how much of an operational radius currently has its objects instantiated.</summary>
    internal static class LoadedArea
    {
        public static bool IsLoaded(Vector3 point)
        {
            if (ZoneSystem.instance == null || ZNet.instance == null) return false;
            if (!ZoneSystem.instance.IsZoneLoaded(point)) return false;
            // A tomb that stays loaded is its own active area, so work there does not wait for the player.
            if (ZNetScene.InActiveArea(point, ZNet.instance.GetReferencePosition())) return true;
            return KeepLoaded.Inside(point);
        }

        public static float Coverage(Vector3 center, float radius, float step) =>
            LoadedCoverage.Compute(WorldRegistry.ToVec3(center), radius, step, p => IsLoaded(new Vector3(p.X, p.Y, p.Z)));
    }
}
