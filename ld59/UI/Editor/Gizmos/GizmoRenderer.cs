using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;

namespace ld59.UI.Editor.Gizmos;

// Draws the editor gizmo handles. Uses the authored gizmo models under models/editor
// (arrow = translate, rotate ring, scale handle), each a single +Z-pointing handle that the
// TransformGizmo instantiates three times (rotated onto X/Y/Z). Falls back to a plain box if a
// model can't be loaded so the editor still works. Everything is drawn through the id-color
// shader as a flat unlit color, which doubles as the visible axis color and the pick id.
public sealed class GizmoRenderer : IDisposable
{
    private readonly Effect _effect;

    // Fallback geometry if the gizmo models aren't available.
    private readonly VertexBuffer _cubeVb;
    private readonly IndexBuffer _cubeIb;

    private readonly Model _translateModel, _rotateModel, _scaleModel;
    // Per-model native radius (bounding-sphere), so a target size divided by this scales any
    // model to a consistent on-screen size regardless of how it was exported.
    private readonly float _translateReach, _rotateReach, _scaleReach;

    public GizmoRenderer(GraphicsDevice device)
    {
        _effect = Core.Content.Load<Effect>("shaders/id-color");

        var verts = new[]
        {
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3( 0.5f, -0.5f, -0.5f),
            new Vector3( 0.5f,  0.5f, -0.5f), new Vector3(-0.5f,  0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f,  0.5f), new Vector3( 0.5f, -0.5f,  0.5f),
            new Vector3( 0.5f,  0.5f,  0.5f), new Vector3(-0.5f,  0.5f,  0.5f),
        };
        var vpc = new VertexPositionColor[verts.Length];
        for (int i = 0; i < verts.Length; i++) vpc[i] = new VertexPositionColor(verts[i], Color.White);

        ushort[] idx =
        {
            0,1,2, 0,2,3,  5,4,7, 5,7,6,  4,0,3, 4,3,7,
            1,5,6, 1,6,2,  3,2,6, 3,6,7,  4,5,1, 4,1,0,
        };

        _cubeVb = new VertexBuffer(device, VertexPositionColor.VertexDeclaration, vpc.Length, BufferUsage.WriteOnly);
        _cubeVb.SetData(vpc);
        _cubeIb = new IndexBuffer(device, IndexElementSize.SixteenBits, idx.Length, BufferUsage.WriteOnly);
        _cubeIb.SetData(idx);

        _translateModel = TryLoad("models/editor/arrow-gizmo");
        _rotateModel    = TryLoad("models/editor/rotate-gizmo");
        _scaleModel     = TryLoad("models/editor/scale-gizmo");
        _translateReach = Reach(_translateModel);
        _rotateReach    = Reach(_rotateModel);
        _scaleReach     = Reach(_scaleModel);
    }

    private static Model TryLoad(string path)
    {
        try { return Core.Content.Load<Model>(path); }
        catch (Exception e) { Console.WriteLine($"[gizmo] couldn't load {path} (using box fallback): {e.Message}"); return null; }
    }

    private static float Reach(Model m)
    {
        if (m == null) return 1f;
        float r = 0f;
        foreach (var mesh in m.Meshes)
        {
            // Measure in the same space we render in: BoundingSphere is in the mesh's LOCAL space,
            // but DrawModel applies mesh.ParentBone.Transform -- which carries the FBX unit scale
            // (e.g. a 100x cm->m factor). Transform the sphere by the bone so the reach reflects
            // the model's actual rendered size; otherwise the gizmo comes out scaled by that factor.
            var s = mesh.BoundingSphere.Transform(mesh.ParentBone.Transform);
            r = MathF.Max(r, s.Radius);
        }
        return r <= 1e-4f ? 1f : r;
    }

    public Model ModelFor(GizmoMode mode) => mode switch
    {
        GizmoMode.Translate => _translateModel,
        GizmoMode.Rotate    => _rotateModel,
        GizmoMode.Scale     => _scaleModel,
        _ => null,
    };

    public float ReachFor(GizmoMode mode) => mode switch
    {
        GizmoMode.Translate => _translateReach,
        GizmoMode.Rotate    => _rotateReach,
        GizmoMode.Scale     => _scaleReach,
        _ => 1f,
    };

    // Draws a loaded gizmo model in a flat color. depthTest expects the depth buffer to have been
    // cleared just before (so the gizmo sits on top of the scene) while still resolving its own
    // front/back faces correctly -- a solid arrow needs internal depth, unlike a thin box.
    public void DrawModel(GraphicsDevice device, Model model, Matrix world, Matrix view, Matrix proj, Vector4 color, bool depthTest)
    {
        _effect.CurrentTechnique = _effect.Techniques["IdColor"];
        _effect.Parameters["LightViewProjection"].SetValue(view * proj);
        _effect.Parameters["IdColor"].SetValue(color);
        device.DepthStencilState = depthTest ? DepthStencilState.Default : DepthStencilState.None;
        device.RasterizerState   = RasterizerState.CullNone;

        foreach (var mesh in model.Meshes)
        {
            _effect.Parameters["World"].SetValue(mesh.ParentBone.Transform * world);
            foreach (var part in mesh.MeshParts)
            {
                if (part.PrimitiveCount == 0) continue;
                device.SetVertexBuffer(part.VertexBuffer);
                device.Indices = part.IndexBuffer;
                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    device.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                        part.VertexOffset, part.StartIndex, part.PrimitiveCount);
                }
            }
        }
    }

    public void DrawBox(GraphicsDevice device, Matrix world, Matrix view, Matrix proj, Vector4 color, bool depthTest)
    {
        _effect.CurrentTechnique = _effect.Techniques["IdColor"];
        _effect.Parameters["World"].SetValue(world);
        _effect.Parameters["LightViewProjection"].SetValue(view * proj);
        _effect.Parameters["IdColor"].SetValue(color);

        device.DepthStencilState = depthTest ? DepthStencilState.Default : DepthStencilState.None;
        device.RasterizerState   = RasterizerState.CullNone;
        device.SetVertexBuffer(_cubeVb);
        device.Indices = _cubeIb;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 12);
        }
    }

    public void Dispose()
    {
        _cubeVb?.Dispose();
        _cubeIb?.Dispose();
    }
}
