using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;

namespace ld59.UI.Editor.Gizmos;

// Camera-facing colored quad marking a non-mesh entity's position (lights, PlayerStart) in the
// editor viewport. Depth-tested normally in both the visible pass and its ID-buffer pick pass
// (drawn with the same view/proj in both, so a billboard hidden behind a wall is hidden from
// clicks too, same as a real object) -- this is what makes lights/spawns selectable in the
// viewport at all, since they have no Mesh3D geometry of their own to click on or draw into the
// existing ID buffer.
public sealed class BillboardGizmoRenderer : System.IDisposable
{
    private readonly VertexBuffer _quadVb;
    private readonly IndexBuffer _quadIb;
    private readonly Effect _effect;

    public BillboardGizmoRenderer(GraphicsDevice device)
    {
        _effect = Core.Content.Load<Effect>("shaders/id-color");

        var verts = new[]
        {
            new VertexPositionColor(new Vector3(-0.5f, -0.5f, 0f), Color.White),
            new VertexPositionColor(new Vector3( 0.5f, -0.5f, 0f), Color.White),
            new VertexPositionColor(new Vector3( 0.5f,  0.5f, 0f), Color.White),
            new VertexPositionColor(new Vector3(-0.5f,  0.5f, 0f), Color.White),
        };
        ushort[] idx = { 0, 1, 2, 0, 2, 3 };

        _quadVb = new VertexBuffer(device, VertexPositionColor.VertexDeclaration, verts.Length, BufferUsage.WriteOnly);
        _quadVb.SetData(verts);
        _quadIb = new IndexBuffer(device, IndexElementSize.SixteenBits, idx.Length, BufferUsage.WriteOnly);
        _quadIb.SetData(idx);
    }

    // Fraction of the viewport HEIGHT an icon covers, and the world-size floor that only guards a
    // camera sitting exactly on top of one.
    private const float ScreenHeightFraction = 0.05f;
    private const float MinWorldSize = 1e-3f;

    /// <summary>
    /// World edge length that draws the icon at a constant share of the viewport, whatever the
    /// distance -- a light across the level stays as visible (and as clickable) as one at arm's
    /// length, instead of shrinking to a speck. Same derivation as the transform gizmo's handle
    /// length: a perspective camera sees 2*d*tan(fovY/2) of world height at distance d, and
    /// tan(fovY/2) falls out of the projection matrix (M22 = 1/tan(fovY/2)), so this tracks the
    /// caller's FOV on its own.
    /// <para>
    /// Both the visible pass and the ID-buffer pick pass size their quads through here, so what you
    /// click stays what you see.
    /// </para>
    /// </summary>
    public static float WorldSizeFor(Vector3 worldPos, Vector3 cameraPos, Matrix proj)
    {
        float dist = Vector3.Distance(worldPos, cameraPos);
        float tanHalfFov = proj.M22 > 1e-6f ? 1f / proj.M22 : 0.41421f;   // fall back to 45 vertical FOV
        return MathF.Max(dist * 2f * tanHalfFov * ScreenHeightFraction, MinWorldSize);
    }

    public void Draw(GraphicsDevice device, Vector3 worldPos, Vector3 cameraPos, float size,
        Matrix view, Matrix proj, Vector4 color)
    {
        var world = Matrix.CreateScale(size) * Matrix.CreateBillboard(worldPos, cameraPos, Vector3.Up, null);

        _effect.CurrentTechnique = _effect.Techniques["IdColor"];
        _effect.Parameters["World"].SetValue(world);
        _effect.Parameters["LightViewProjection"].SetValue(view * proj);
        _effect.Parameters["IdColor"].SetValue(color);

        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState   = RasterizerState.CullNone;
        device.SetVertexBuffer(_quadVb);
        device.Indices = _quadIb;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 2);
        }
    }

    public void Dispose()
    {
        _quadVb?.Dispose();
        _quadIb?.Dispose();
    }
}
