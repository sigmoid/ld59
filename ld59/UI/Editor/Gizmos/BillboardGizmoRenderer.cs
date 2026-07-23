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
