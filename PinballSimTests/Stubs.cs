using System;

// ─────────────────────────────────────────────────────────────────────────────
// Minimal stand-ins for the MonoGame / Quartz types the PinballEngine touches, so
// the engine compiles and runs headlessly. The math types are faithful; the
// graphics types exist only to satisfy compilation of DebugDraw (never called).
// ─────────────────────────────────────────────────────────────────────────────

namespace Microsoft.Xna.Framework
{
    /// <summary>Faithful 2D float vector — same math the real XNA Vector2 performs.</summary>
    public struct Vector2
    {
        public float X;
        public float Y;

        public Vector2(float x, float y) { X = x; Y = y; }

        public static Vector2 Zero  => new Vector2(0f, 0f);
        public static Vector2 UnitX => new Vector2(1f, 0f);

        public float Length()        => (float)System.Math.Sqrt(X * X + Y * Y);
        public float LengthSquared() => X * X + Y * Y;

        public void Normalize()
        {
            float len = Length();
            if (len > 0f) { X /= len; Y /= len; }
        }

        public static Vector2 Normalize(Vector2 v)
        {
            float len = v.Length();
            return len > 0f ? new Vector2(v.X / len, v.Y / len) : new Vector2(0f, 0f);
        }

        public static float Dot(Vector2 a, Vector2 b)      => a.X * b.X + a.Y * b.Y;
        public static float Distance(Vector2 a, Vector2 b) => (a - b).Length();
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) =>
            new Vector2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.X - b.X, a.Y - b.Y);
        public static Vector2 operator -(Vector2 a)            => new Vector2(-a.X, -a.Y);
        public static Vector2 operator *(Vector2 a, float s)   => new Vector2(a.X * s, a.Y * s);
        public static Vector2 operator *(float s, Vector2 a)   => new Vector2(a.X * s, a.Y * s);
        public static Vector2 operator /(Vector2 a, float s)   => new Vector2(a.X / s, a.Y / s);

        public static bool operator ==(Vector2 a, Vector2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public override bool Equals(object obj) => obj is Vector2 v && this == v;
        public override int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode();
        public override string ToString() => $"({X}, {Y})";
    }

    /// <summary>3D float vector — only the surface the ramp code reads.</summary>
    public struct Vector3
    {
        public float X;
        public float Y;
        public float Z;
        public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    /// <summary>RGBA color stub — structural only (graphics path is never executed).</summary>
    public struct Color
    {
        public byte R, G, B, A;
        public Color(byte r, byte g, byte b, byte a) { R = r; G = g; B = b; A = a; }
        public Color(int r, int g, int b) { R = (byte)r; G = (byte)g; B = (byte)b; A = 255; }
        public static Color White => new Color(255, 255, 255, 255);

        public static bool operator ==(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;
        public static bool operator !=(Color a, Color b) => !(a == b);
        public override bool Equals(object obj) => obj is Color c && this == c;
        public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;
    }

    /// <summary>Opens content streams from the filesystem in the headless harness.</summary>
    public static class TitleContainer
    {
        public static System.IO.Stream OpenStream(string path) => System.IO.File.OpenRead(path);
    }
}

namespace Microsoft.Xna.Framework.Graphics
{
    public sealed class GraphicsDevice { }

    public sealed class Texture2D
    {
        public Texture2D(GraphicsDevice device, int width, int height) { }
        public void SetData<T>(T[] data) { }
    }

    public enum SpriteEffects { None }

    public sealed class SpriteBatch
    {
        public void Draw(Texture2D texture, Vector2 position, object sourceRectangle,
                         Color color, float rotation, Vector2 origin, Vector2 scale,
                         SpriteEffects effects, float layerDepth)
        { }
    }
}

namespace Quartz
{
    /// <summary>Static stand-in for Quartz.Core — only the members the engine/loader read.</summary>
    public static class Core
    {
        public static StubInputManager InputManager { get; set; } = new StubInputManager();
        public static StubContent      Content      { get; set; } = new StubContent();
        public static Microsoft.Xna.Framework.Graphics.GraphicsDevice GraphicsDevice { get; set; }
    }

    public sealed class StubContent
    {
        public string RootDirectory { get; set; } = "Content";
    }

    public sealed class StubButton
    {
        public bool IsHeld { get; set; }
    }

    /// <summary>Lets the harness drive flipper input by toggling button names.</summary>
    public sealed class StubInputManager
    {
        public readonly System.Collections.Generic.HashSet<string> Held = new();
        public StubButton GetButton(string name) => new StubButton { IsHeld = Held.Contains(name) };
    }
}
