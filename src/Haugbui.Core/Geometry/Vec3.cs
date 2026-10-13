using System;

namespace Haugbui.Core.Geometry
{
    public readonly struct Vec3
    {
        public static readonly Vec3 Zero = new Vec3(0, 0, 0);

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public static float Distance(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public override string ToString() => $"({X:0.##}, {Y:0.##}, {Z:0.##})";
    }
}
