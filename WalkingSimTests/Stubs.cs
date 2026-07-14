using System;

// ─────────────────────────────────────────────────────────────────────────────
// Minimal stand-ins for the MonoGame math types the navmesh runtime touches, so
// NavMesh/ObjParser/WalkController compile and run headlessly. Faithful math.
// ─────────────────────────────────────────────────────────────────────────────

namespace Microsoft.Xna.Framework
{
    public struct Vector2
    {
        public float X;
        public float Y;

        public Vector2(float x, float y) { X = x; Y = y; }

        public static Vector2 Zero => new Vector2(0f, 0f);

        public float Length() => (float)Math.Sqrt(X * X + Y * Y);
        public float LengthSquared() => X * X + Y * Y;

        public static float Dot(Vector2 a, Vector2 b) => a.X * b.X + a.Y * b.Y;
        public static float Distance(Vector2 a, Vector2 b) => (a - b).Length();

        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.X - b.X, a.Y - b.Y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.X, -a.Y);
        public static Vector2 operator *(Vector2 a, float s) => new Vector2(a.X * s, a.Y * s);
        public static Vector2 operator *(float s, Vector2 a) => new Vector2(a.X * s, a.Y * s);
        public static Vector2 operator /(Vector2 a, float s) => new Vector2(a.X / s, a.Y / s);

        public override string ToString() => $"({X}, {Y})";
    }

    public struct Vector3
    {
        public float X;
        public float Y;
        public float Z;

        public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static Vector3 Zero => new Vector3(0f, 0f, 0f);
        public static Vector3 One => new Vector3(1f, 1f, 1f);
        public static Vector3 Up => new Vector3(0f, 1f, 0f);

        public float Length() => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        public float LengthSquared() => X * X + Y * Y + Z * Z;

        public static float Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);
        public static float Distance(Vector3 a, Vector3 b) => (a - b).Length();

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.X, -a.Y, -a.Z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.X * s, a.Y * s, a.Z * s);
        public static Vector3 operator *(float s, Vector3 a) => new Vector3(a.X * s, a.Y * s, a.Z * s);
        public static Vector3 operator /(Vector3 a, float s) => new Vector3(a.X / s, a.Y / s, a.Z / s);

        public override string ToString() => $"({X}, {Y}, {Z})";
    }
}
