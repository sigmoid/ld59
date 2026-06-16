using Microsoft.Xna.Framework;

// ─────────────────────────────────────────────────────────────────────────────
// Minimal stand-ins for the Quartz/MonoGame types the Powergrid engine touches,
// so the engine compiles and runs headlessly. These mirror only the surface the
// engine uses (positions, names, component data hooks); behaviour is faithful.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Global-namespace Entity stub (the real Quartz Entity also lives in the global namespace).</summary>
public class Entity
{
    public string Name { get; set; } = "Unnamed";
    public Vector2 Position { get; set; }
}

namespace Microsoft.Xna.Framework
{
    /// <summary>Faithful 2D float vector — same math the real XNA Vector2 performs.</summary>
    public struct Vector2
    {
        public float X;
        public float Y;

        public Vector2(float x, float y) { X = x; Y = y; }

        public static Vector2 Zero => new Vector2(0f, 0f);

        /// <summary>In-place normalize (matches XNA's instance method semantics).</summary>
        public void Normalize()
        {
            float len = (float)System.Math.Sqrt(X * X + Y * Y);
            if (len > 0f) { X /= len; Y /= len; }
        }

        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.X - b.X, a.Y - b.Y);
        public static Vector2 operator *(Vector2 a, float s)   => new Vector2(a.X * s, a.Y * s);
        public static Vector2 operator *(float s, Vector2 a)   => new Vector2(a.X * s, a.Y * s);
        public static Vector2 operator /(Vector2 a, float s)   => new Vector2(a.X / s, a.Y / s);

        public static bool operator ==(Vector2 a, Vector2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);

        public override bool Equals(object obj) => obj is Vector2 v && this == v;
        public override int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode();
        public override string ToString() => $"({X}, {Y})";
    }
}

namespace Quartz.Components
{
    /// <summary>Base component stub: just the Entity back-reference and the serialize hooks the engine overrides.</summary>
    public abstract class Component
    {
        public Entity Entity { get; set; }
        public virtual string SerializeData() => string.Empty;
        public virtual void DeserializeData(string data) { }
    }
}
