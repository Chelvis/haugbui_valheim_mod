using System;
using System.Collections.Generic;
using Haugbui.Core.Geometry;

namespace Haugbui.Core.Structures
{
    /// <summary>
    /// HB-155 (TerritoryMode): where one owner's tomb radii overlap, a work target belongs to the nearest of that owner's
    /// tombs whose radius contains it; equal distances go to the lowest GUID, so every tomb agrees.
    /// </summary>
    public static class Territory
    {
        /// <returns>The owning tomb's GUID, or null when no tomb covers the target.</returns>
        public static string OwnerOf(Vec3 target, IEnumerable<(string Guid, Vec3 Position, float Radius)> tombs)
        {
            string best = null;
            float bestDistance = float.MaxValue;
            foreach ((string guid, Vec3 position, float radius) in tombs)
            {
                float distance = Vec3.Distance(position, target);
                if (distance > radius) continue;
                if (distance < bestDistance || (distance == bestDistance && string.CompareOrdinal(guid, best) < 0))
                {
                    best = guid;
                    bestDistance = distance;
                }
            }
            return best;
        }
    }
}
