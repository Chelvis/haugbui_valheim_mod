using System;

namespace Haugbui.Core.Geometry
{
    /// <summary>HB-033: share of the operational disc (horizontal) whose ground is in loaded zones.</summary>
    public static class LoadedCoverage
    {
        public const float FullyLoaded = 0.999f;

        /// <returns>1 when every sample is loaded, 0 when none is.</returns>
        public static float Compute(Vec3 center, float radius, float step, Func<Vec3, bool> isLoaded)
        {
            if (radius <= 0 || step <= 0) return isLoaded(center) ? 1f : 0f;
            int samples = 0, loaded = 0;
            int n = (int)Math.Ceiling(radius / step);
            for (int ix = -n; ix <= n; ix++)
            {
                for (int iz = -n; iz <= n; iz++)
                {
                    float dx = ix * step, dz = iz * step;
                    if (dx * dx + dz * dz > radius * radius) continue;
                    samples++;
                    if (isLoaded(new Vec3(center.X + dx, center.Y, center.Z + dz))) loaded++;
                }
            }
            return samples == 0 ? 1f : (float)loaded / samples;
        }
    }
}
