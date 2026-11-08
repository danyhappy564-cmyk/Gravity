using System;

namespace Gravity.Shared
{
    public readonly record struct Vec3(float X, float Y, float Z)
    {
        public static float Dist(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    public enum ActorKind { Human, Pmc, Scav, Boss }

    public readonly record struct Actor(Vec3 Pos, ActorKind Kind);
}
