using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ld59.WalkingSim;

// Debug overlay for the navmesh: translucent filled triangles (depth-tested, so they hug the
// terrain and read as coverage) plus a bright wireframe drawn x-ray (no depth test, so the
// mesh outline is visible even through hills). Makes holes, disconnected islands, and missing
// ground obvious in-game. Geometry is uploaded once; toggle it from UI3DScene.
public sealed class NavMeshDebugRenderer : IDisposable
{
    private readonly BasicEffect _effect;
    private readonly VertexBuffer _fillVb;
    private readonly VertexBuffer _lineVb;
    private readonly int _fillPrims;
    private readonly int _linePrims;

    // Pull the fill slightly toward the camera so it wins against the coincident floor instead
    // of z-fighting it.
    private static readonly RasterizerState FillRaster = new()
    {
        CullMode = CullMode.None,
        DepthBias = -5e-5f,
        SlopeScaleDepthBias = -1f,
    };

    private static readonly RasterizerState LineRaster = new() { CullMode = CullMode.None };

    public NavMeshDebugRenderer(GraphicsDevice device, NavMesh mesh)
    {
        _effect = new BasicEffect(device)
        {
            VertexColorEnabled = true,
            LightingEnabled    = false,
            TextureEnabled     = false,
        };

        var fillColor = new Color(60, 210, 110, 110);   // translucent green (non-premultiplied)
        var lineColor = new Color(180, 255, 200, 255);

        int tc = mesh.Triangles.Length;
        var fill  = new VertexPositionColor[tc * 3];
        var lines = new VertexPositionColor[tc * 6];
        int fi = 0, li = 0;
        for (int t = 0; t < tc; t++)
        {
            var tri = mesh.Triangles[t];
            Vector3 a = mesh.Vertices[tri.V0], b = mesh.Vertices[tri.V1], c = mesh.Vertices[tri.V2];

            fill[fi++] = new VertexPositionColor(a, fillColor);
            fill[fi++] = new VertexPositionColor(b, fillColor);
            fill[fi++] = new VertexPositionColor(c, fillColor);

            lines[li++] = new VertexPositionColor(a, lineColor); lines[li++] = new VertexPositionColor(b, lineColor);
            lines[li++] = new VertexPositionColor(b, lineColor); lines[li++] = new VertexPositionColor(c, lineColor);
            lines[li++] = new VertexPositionColor(c, lineColor); lines[li++] = new VertexPositionColor(a, lineColor);
        }

        _fillPrims = tc;
        _linePrims = tc * 3;

        if (tc > 0)
        {
            _fillVb = new VertexBuffer(device, VertexPositionColor.VertexDeclaration, fill.Length, BufferUsage.WriteOnly);
            _fillVb.SetData(fill);
            _lineVb = new VertexBuffer(device, VertexPositionColor.VertexDeclaration, lines.Length, BufferUsage.WriteOnly);
            _lineVb.SetData(lines);
        }
    }

    public void Draw(GraphicsDevice device, Matrix world, Matrix view, Matrix proj)
    {
        if (_fillVb == null) return;

        _effect.World      = world;
        _effect.View       = view;
        _effect.Projection = proj;

        var prevBlend  = device.BlendState;
        var prevDepth  = device.DepthStencilState;
        var prevRaster = device.RasterizerState;

        device.BlendState = BlendState.NonPremultiplied;

        // Filled triangles: test depth (occluded by terrain in front) but don't write it.
        device.DepthStencilState = DepthStencilState.DepthRead;
        device.RasterizerState   = FillRaster;
        device.SetVertexBuffer(_fillVb);
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawPrimitives(PrimitiveType.TriangleList, 0, _fillPrims);
        }

        // Wireframe: x-ray (no depth test) so the outline shows through everything.
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState   = LineRaster;
        device.SetVertexBuffer(_lineVb);
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawPrimitives(PrimitiveType.LineList, 0, _linePrims);
        }

        device.BlendState        = prevBlend;
        device.DepthStencilState = prevDepth;
        device.RasterizerState   = prevRaster;
    }

    public void Dispose()
    {
        _fillVb?.Dispose();
        _lineVb?.Dispose();
        _effect?.Dispose();
    }
}
