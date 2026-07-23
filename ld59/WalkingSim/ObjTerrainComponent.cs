using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.Components;

namespace ld59.WalkingSim;

// Renders level terrain straight from an OBJ, building the vertex buffer itself (flat-shaded
// per-face normals) and drawing with the point-light shader — exactly how BoxPrimitive3D works.
// This deliberately bypasses the content model pipeline: OpenAssetImporter emits a runtime-
// unreadable MaterialContent for OBJ/FBX-imported models, which throws in Content.Load<Model>.
// The OBJ is /copy'd into content and read raw via TitleContainer (same file the navmesh bakes from).
public class ObjTerrainComponent : Component
{
    public string  ObjPath { get; set; }              // content-relative, copied (not built)
    public Color   Color   { get; set; } = Color.Gray;
    public Vector3 Scale   { get; set; } = Vector3.One;

    private const int MaxLights = 16;

    private VertexBuffer _vb;
    private IndexBuffer  _ib;
    private Effect       _effect;
    private int          _primitiveCount;

    private readonly Vector4[] _lightPositions = new Vector4[MaxLights];
    private readonly Vector4[] _lightColors    = new Vector4[MaxLights];

    public override void Initialize()
    {
        if (string.IsNullOrEmpty(ObjPath)) return;
        _effect = Core.Content.Load<Effect>("shaders/point-light");
        BuildFromObj();
    }

    private void BuildFromObj()
    {
        using var stream = TitleContainer.OpenStream(Path.Combine(Core.Content.RootDirectory, ObjPath));
        using var reader = new StreamReader(stream);
        var (verts, faces) = ObjParser.Parse(reader);

        var vertices = new Quartz.VertexPositionColorNormal[faces.Count * 3];
        var indices  = new int[faces.Count * 3];

        for (int f = 0; f < faces.Count; f++)
        {
            var (ia, ib, ic) = faces[f];
            var a = verts[ia];
            var b = verts[ib];
            var c = verts[ic];
            var normal = Vector3.Cross(b - a, c - a);
            normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.Up;

            int vi = f * 3;
            vertices[vi]     = new Quartz.VertexPositionColorNormal(a, Color, normal);
            vertices[vi + 1] = new Quartz.VertexPositionColorNormal(b, Color, normal);
            vertices[vi + 2] = new Quartz.VertexPositionColorNormal(c, Color, normal);
            indices[vi] = vi; indices[vi + 1] = vi + 1; indices[vi + 2] = vi + 2;
        }

        _primitiveCount = faces.Count;
        _vb = new VertexBuffer(Core.GraphicsDevice, Quartz.VertexPositionColorNormal.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
        _vb.SetData(vertices);
        _ib = new IndexBuffer(Core.GraphicsDevice, IndexElementSize.ThirtyTwoBits, indices.Length, BufferUsage.WriteOnly);
        _ib.SetData(indices);
    }

    public Matrix WorldMatrix =>
        Matrix.CreateScale(Scale) * Matrix.CreateTranslation(Entity.Position3D);

    public override void Draw3D(GraphicsDevice device, Matrix view, Matrix projection, SceneLightData lights)
    {
        if (_vb == null) return;

        _effect.Parameters["World"].SetValue(WorldMatrix);
        _effect.Parameters["View"].SetValue(view);
        _effect.Parameters["Projection"].SetValue(projection);
        _effect.Parameters["AmbientColor"].SetValue(lights.AmbientColor.ToVector3());

        int count = Math.Min(lights.PointLights.Count, MaxLights);
        for (int i = 0; i < count; i++)
        {
            var l = lights.PointLights[i];
            _lightPositions[i] = new Vector4(l.Position, l.Range);
            _lightColors[i]    = new Vector4(l.Color * l.Intensity, 0f);
        }
        for (int i = count; i < MaxLights; i++)
        {
            _lightPositions[i] = Vector4.Zero;
            _lightColors[i]    = Vector4.Zero;
        }
        _effect.Parameters["LightPositions"].SetValue(_lightPositions);
        _effect.Parameters["LightColors"].SetValue(_lightColors);
        _effect.Parameters["NumLights"].SetValue((float)count);

        if (lights.DirectionalLight.HasValue)
        {
            var dl = lights.DirectionalLight.Value;
            _effect.Parameters["HasDirLight"].SetValue(true);
            _effect.Parameters["DirLightDirection"].SetValue(dl.Direction);
            _effect.Parameters["DirLightColor"].SetValue(dl.Color);
            _effect.Parameters["DirLightIntensity"].SetValue(dl.Intensity);
        }
        else
        {
            _effect.Parameters["HasDirLight"].SetValue(false);
        }

        device.SetVertexBuffer(_vb);
        device.Indices = _ib;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _primitiveCount);
        }
    }

    public override void Dispose(bool disposing)
    {
        _vb?.Dispose();
        _ib?.Dispose();
    }
}
