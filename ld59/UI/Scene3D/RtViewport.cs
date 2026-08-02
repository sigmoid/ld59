using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ld59.UI.Scene3D;

/// <summary>
/// Maps the cursor between the on-screen rectangle a 3D view occupies and the fixed-size render
/// target it actually draws into. The target is drawn stretched across the bounds, so the same
/// proportional map is what makes a screen cursor line up with what's drawn in the target -- and it
/// stays correct when the window (and thus the bounds) is a different size than the target.
/// </summary>
public readonly struct RtViewport
{
    public readonly Rectangle Bounds;
    public readonly int Width;
    public readonly int Height;

    public RtViewport(Rectangle bounds, int width, int height)
    {
        Bounds = bounds;
        Width  = width;
        Height = height;
    }

    public float Aspect => (float)Width / Height;
    public Viewport Viewport => new Viewport(0, 0, Width, Height);
    public bool Contains(Point cursor) => Bounds.Contains(cursor);

    /// <summary>Cursor position in render-target pixels.</summary>
    public Vector2 ToPixel(Point cursor) => new Vector2(
        (cursor.X - Bounds.X) / (float)Bounds.Width  * Width,
        (cursor.Y - Bounds.Y) / (float)Bounds.Height * Height);

    /// <summary>Cursor position as a clamped [0,1) fraction -- for sampling a readback buffer whose
    /// resolution differs from both the bounds and the render target.</summary>
    public Vector2 ToUv(Point cursor) => new Vector2(
        MathHelper.Clamp((cursor.X - Bounds.X) / (float)Bounds.Width,  0f, 0.999f),
        MathHelper.Clamp((cursor.Y - Bounds.Y) / (float)Bounds.Height, 0f, 0.999f));

    /// <summary>
    /// World-space ray through the given screen point. Plain unprojection against the render
    /// target's own viewport -- not a scene raycast, so it needs no mesh/navmesh intersection.
    /// </summary>
    public Ray ScreenRay(Point cursor, Matrix view, Matrix proj)
    {
        var vp = Viewport;
        Vector2 p = ToPixel(cursor);
        Vector3 near = vp.Unproject(new Vector3(p.X, p.Y, 0f), proj, view, Matrix.Identity);
        Vector3 far  = vp.Unproject(new Vector3(p.X, p.Y, 1f), proj, view, Matrix.Identity);
        return new Ray(near, Vector3.Normalize(far - near));
    }
}
