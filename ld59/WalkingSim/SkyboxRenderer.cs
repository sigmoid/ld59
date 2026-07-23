using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;

namespace ld59.WalkingSim;

// Draws the procedural moon skybox (shaders/skybox.fx) as a full-screen triangle before the
// scene geometry, with depth writes off so anything drawn afterward sits in front of it. Tunable
// parameters have moon-appropriate defaults: black sky, stars, a blue Earth, and a bright Sun.
// SunDir is meant to be synced each frame to the scene's directional light so the sun in the sky
// lines up with the lighting.
public sealed class SkyboxRenderer : IDisposable
{
    private readonly Effect _effect;
    private readonly VertexBuffer _tri;
    private readonly Texture2D _earthTex;   // null if the image isn't built -> flat-disk fallback
    private readonly TextureCube _skyCube;  // baked starfield / nebula background (6 faces)

    // Direction from the camera toward each body (normalized on use). SunDir only lights the Earth
    // fallback disk now that the sun itself is baked into the cubemap.
    public Vector3 SunDir   { get; set; } = Vector3.Normalize(new Vector3(0.4f, 0.5f, 0.3f));
    public Vector3 EarthDir { get; set; } = Vector3.Normalize(new Vector3(-0.5f, 0.35f, -0.55f));

    public Vector3 EarthColor { get; set; } = new Vector3(0.25f, 0.45f, 0.85f);

    public float EarthAngularRadiusDeg { get; set; } = 3f;

    // The six cubemap faces, mapped name -> CubeMapFace. Tweak this mapping if the background
    // reads rotated/mirrored for the tool that exported the faces.
    private const string SkyboxPrefix = "images/skybox/26-07-23-07-04-14_";

    public SkyboxRenderer(GraphicsDevice device)
    {
        _effect = Core.Content.Load<Effect>("shaders/skybox");
        try { _earthTex = Core.Content.Load<Texture2D>("images/earth"); }
        catch (Exception e) { Console.WriteLine($"[skybox] earth texture not found, using flat disk: {e.Message}"); }

        try { _skyCube = BuildSkyCube(device); }
        catch (Exception e) { Console.WriteLine($"[skybox] cube faces not found, using black background: {e.Message}"); }

        // Full-screen triangle in clip space (covers -1..1 with a single triangle).
        var verts = new[]
        {
            new VertexPosition(new Vector3(-1f, -1f, 0f)),
            new VertexPosition(new Vector3( 3f, -1f, 0f)),
            new VertexPosition(new Vector3(-1f,  3f, 0f)),
        };
        _tri = new VertexBuffer(device, VertexPosition.VertexDeclaration, 3, BufferUsage.WriteOnly);
        _tri.SetData(verts);
    }

    public void Draw(GraphicsDevice device, Matrix view, Matrix proj, Vector3 cameraPos)
    {
        _effect.Parameters["InvViewProj"].SetValue(Matrix.Invert(view * proj));
        _effect.Parameters["CameraPos"].SetValue(cameraPos);

        _effect.Parameters["SunDir"].SetValue(Vector3.Normalize(SunDir));

        _effect.Parameters["EarthDir"].SetValue(Vector3.Normalize(EarthDir));
        _effect.Parameters["EarthColor"].SetValue(EarthColor);
        _effect.Parameters["EarthCos"].SetValue(MathF.Cos(MathHelper.ToRadians(EarthAngularRadiusDeg)));
        _effect.Parameters["EarthTextured"].SetValue(_earthTex != null);
        if (_earthTex != null)
            _effect.Parameters["EarthTex"].SetValue(_earthTex);

        if (_skyCube != null)
            _effect.Parameters["SkyCube"].SetValue(_skyCube);

        // Background: no depth test/write, opaque, both faces.
        device.DepthStencilState = DepthStencilState.None;
        device.BlendState        = BlendState.Opaque;
        device.RasterizerState   = RasterizerState.CullNone;
        device.SetVertexBuffer(_tri);
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawPrimitives(PrimitiveType.TriangleList, 0, 1);
        }
    }

    // Loads the six face images and packs them into a cubemap. All faces must be square and the
    // same size. Names map to axes as: Right=+X, Left=-X, Top=+Y, Bottom=-Y, Front=+Z, Back=-Z.
    private static TextureCube BuildSkyCube(GraphicsDevice device)
    {
        var right  = Core.Content.Load<Texture2D>(SkyboxPrefix + "Right");
        var left   = Core.Content.Load<Texture2D>(SkyboxPrefix + "Left");
        var top    = Core.Content.Load<Texture2D>(SkyboxPrefix + "Top");
        var bottom = Core.Content.Load<Texture2D>(SkyboxPrefix + "Bottom");
        var front  = Core.Content.Load<Texture2D>(SkyboxPrefix + "Front");
        var back   = Core.Content.Load<Texture2D>(SkyboxPrefix + "Back");

        int size = right.Width;
        var cube = new TextureCube(device, size, false, SurfaceFormat.Color);
        CopyFace(cube, CubeMapFace.PositiveX, right);
        CopyFace(cube, CubeMapFace.NegativeX, left);
        CopyFace(cube, CubeMapFace.PositiveY, top);
        CopyFace(cube, CubeMapFace.NegativeY, bottom);
        CopyFace(cube, CubeMapFace.PositiveZ, front);
        CopyFace(cube, CubeMapFace.NegativeZ, back);
        return cube;
    }

    private static void CopyFace(TextureCube cube, CubeMapFace face, Texture2D src)
    {
        var data = new Color[src.Width * src.Height];
        src.GetData(data);
        cube.SetData(face, data);
    }

    public void Dispose()
    {
        _tri?.Dispose();
        _skyCube?.Dispose();
    }
}
