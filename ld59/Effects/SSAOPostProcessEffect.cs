using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.Graphics;

/// <summary>
/// Screen-space ambient occlusion over a rendered 3D scene: darkens creases, corners and the ground
/// under objects by counting how much of each pixel's hemisphere is blocked by nearby geometry.
///
/// Runs off the same depth/id buffer as the fog and the outlines (see Scene.DrawDepthPass), with
/// normals reconstructed from depth rather than stored -- so turning this on costs four full-screen
/// passes and no extra scene draw. <see cref="Downscale"/> buys most of that back.
///
/// Ordered first in the chain (priority 5) because occlusion is a lighting term: it belongs on the
/// surface, underneath the fog that then covers it with distance, and underneath the outlines that
/// draw over both.
/// </summary>
public class SSAOPostProcessEffect : Scene3DPostProcessEffect
{
    /// <summary>
    /// Hemisphere radius in world units -- how far out geometry can shade a pixel. The first knob to
    /// reach for: too small and the effect disappears into surface-scale noise, too large and it
    /// stops reading as contact shading and just grimes up every object.
    /// </summary>
    public float Radius { get; set; } = 0.75f;

    /// <summary>
    /// World-unit slack on the occlusion test. Reconstructed normals are only as good as the depth
    /// derivative allows, so a flat surface samples slightly into itself; too low a bias shows up as
    /// a uniform grey haze over otherwise flat polygons.
    /// </summary>
    public float Bias { get; set; } = 0.03f;

    /// <summary>Hemisphere taps per pixel. Clamped to 4..32 in the shader; the cost is linear in
    /// this and it is the main thing to trade against <see cref="Downscale"/>. Low counts don't
    /// darken less, they just make the estimate noisier -- which reads as shimmer as the camera
    /// moves, because the sampling pattern is anchored to the screen and the geometry slides under
    /// it. Raise this (and <see cref="BlurRadius"/>) before touching anything else if it boils.</summary>
    public float Samples { get; set; } = 24f;

    /// <summary>Scales the darkening. 0 = no occlusion (and the pass, plus the depth pass feeding
    /// it, is skipped entirely).</summary>
    public float Intensity { get; set; } = 0.7f;

    /// <summary>Contrast exponent on the occlusion term. Above 1 keeps mid-tones open and confines
    /// the darkening to genuine creases; below 1 spreads it over everything.</summary>
    public float Power { get; set; } = 1.6f;

    /// <summary>World distance at which AO has faded out entirely (0 = never). Far geometry covers
    /// few pixels, so its hemisphere shrinks below a pixel and the sampling turns to noise -- fading
    /// it out is cheaper and steadier than sampling it harder.</summary>
    public float FadeDistance { get; set; }

    /// <summary>Bilateral blur taps each way per axis (0 = no blur). The AO pass is deliberately
    /// noisy -- a low-discrepancy pattern rotated per pixel -- and this is what resolves that noise
    /// into smooth shading without dragging it across silhouettes. Too small a radius relative to
    /// the noise leaves grain that the downstream 1-bit dithering then amplifies into flicker.</summary>
    public float BlurRadius { get; set; } = 3f;

    /// <summary>
    /// Colour the occluded pixels are blended toward. Defaults to the 1-bit palette's dark colour so
    /// shadows resolve to solid ink rather than dithering into a grey stipple.
    /// </summary>
    public Color OcclusionColor { get; set; } = new Color(0x22, 0x1f, 0x34);

    /// <summary>
    /// While the 1-bit dithering stage is enabled, darken toward ITS dark colour instead of
    /// <see cref="OcclusionColor"/>, so the two can't drift apart when that palette is retuned.
    /// </summary>
    public bool MatchOneBitPalette { get; set; } = true;

    private Color EffectiveColor
    {
        get
        {
            if (!MatchOneBitPalette) return OcclusionColor;
            var oneBit = Core.PostProcessing?.GetEffect<OneBitDitheringPostProcessEffect>();
            return oneBit is { Enabled: true } ? oneBit.DarkColor : OcclusionColor;
        }
    }

    /// <summary>
    /// Quantise the occlusion into this many bands (0 or 1 = smooth). Same idea as the fog's Levels
    /// -- discrete steps survive the downstream 1-bit error diffusion where a smooth ramp dissolves
    /// into noise -- but OFF by default here, unlike the fog, and the difference is worth knowing:
    /// fog is a smooth, stable, low-frequency field, whereas AO is a sampled estimate with grain on
    /// it. Banding grain makes pixels flip between bands under the slightest camera movement, which
    /// reads as flicker, and the Bayer stipple at 1px cells is itself a visible pattern of
    /// un-darkened pixels. Turn it on (3-5 bands, and raise <see cref="DitherScale"/> to 2) only
    /// once <see cref="Samples"/> and <see cref="BlurRadius"/> have the AO reading clean.
    /// </summary>
    public float Levels { get; set; }

    /// <summary>How far the band edges are ordered-dithered, 0 (hard steps) to 1 (full 4x4 Bayer
    /// stipple). Only applies while <see cref="Levels"/> is banding. Anchored to screen pixels, so
    /// unlike the error diffusion downstream it holds still as the camera moves.</summary>
    public float Dither { get; set; } = 1f;

    /// <summary>Pixels per dither cell. Above 1 the stipple is chunkier and survives the 1-bit pass
    /// and the final upscale better.</summary>
    public float DitherScale { get; set; } = 1f;

    // Backing field so the setter can clamp AND drop the targets, which are sized from it.
    private int _downscale = 1;

    /// <summary>
    /// Render the AO map at 1/N the scene's resolution (1 = full, 2 = half, ...). The occlusion term
    /// is low-frequency and gets blurred anyway, so half resolution costs little visually and
    /// quarters the sampling cost -- the shader upsamples it with a per-tap depth test, so it does
    /// not fringe silhouettes the way a plain bilinear fetch would. Clamped to 1..4.
    /// </summary>
    public int Downscale
    {
        get => _downscale;
        set
        {
            int clamped = MathHelper.Clamp(value, 1, 4);
            if (clamped == _downscale) return;
            _downscale = clamped;
            ReleaseTargets();   // sized from this; rebuilt on the next frame
        }
    }

    /// <summary>What the pass outputs instead of the occluded scene, for diagnosing bad AO.</summary>
    public enum DebugMode
    {
        /// <summary>Normal output.</summary>
        Off,
        /// <summary>The AO map alone, banding and all: white = lit, black = fully occluded.</summary>
        Ao,
        /// <summary>The normals reconstructed from depth, as RGB. Everything else is downstream of
        /// these, so this is the first place to look when the occlusion looks wrong.</summary>
        Normals,
    }

    /// <summary>
    /// Replaces the output with a view of the pass' inputs. Note that whatever this draws still goes
    /// through the screen-wide 1-bit dithering afterwards, so turn that off (the <c>1bit</c>
    /// command) before reading the values.
    /// </summary>
    public DebugMode Debug { get; set; } = DebugMode.Off;

    // AO map and its blur ping-pong, at the downscaled resolution. Allocated to match the source and
    // released in Dispose -- unlike Shader, these are ours, not the ContentManager's.
    private RenderTarget2D _aoTarget;
    private RenderTarget2D _blurTarget;

    // Intensity 0 means every pixel would blend by exactly 0 -- skip the pass, and let the owner skip
    // the depth pass feeding it, rather than paying for a no-op. The debug views still need it.
    public override bool WantsDepth => Enabled && (Intensity > 0f || Debug != DebugMode.Off);

    public override void Initialize(GraphicsDevice graphicsDevice)
    {
        Shader = Core.Content.Load<Effect>("shaders/ssao");
        Priority = 5;   // before the fog (10) and the outlines (20): AO is a lighting term
    }

    public override void Apply(RenderTarget2D source, RenderTarget2D destination, SpriteBatch spriteBatch, GameTime gameTime)
    {
        var gd = spriteBatch.GraphicsDevice;
        var depth = Frame.DepthMap;

        // No depth buffer this frame: pass the scene through untouched rather than shading against
        // a stale or missing one.
        if (depth == null || depth.IsDisposed)
        {
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp);
            spriteBatch.Draw(source, Vector2.Zero, Color.White);
            spriteBatch.End();
            return;
        }

        EnsureTargets(gd, source.Width, source.Height);

        var sceneResolution = new Vector2(source.Width, source.Height);
        var aoResolution    = new Vector2(_aoTarget.Width, _aoTarget.Height);

        Shader.Parameters["InverseViewProjection"].SetValue(Frame.InverseViewProjection);
        Shader.Parameters["ViewProjection"].SetValue(Frame.View * Frame.Projection);
        Shader.Parameters["CameraPosition"].SetValue(Frame.CameraPosition);
        Shader.Parameters["FarDistance"].SetValue(Frame.FarDistance);
        // Bound for every pass, not just the blurs: the composite and the AO debug view both need it
        // to depth-test their upsample taps.
        Shader.Parameters["DepthTexture"].SetValue(depth);
        Shader.Parameters["AOResolution"].SetValue(aoResolution);
        Shader.Parameters["Radius"].SetValue(Radius);
        Shader.Parameters["Bias"].SetValue(Bias);
        Shader.Parameters["SampleCount"].SetValue(Samples);
        Shader.Parameters["Intensity"].SetValue(Intensity);
        Shader.Parameters["Power"].SetValue(Power);
        Shader.Parameters["FadeDistance"].SetValue(FadeDistance);
        Shader.Parameters["BlurRadius"].SetValue(BlurRadius);
        Shader.Parameters["Levels"].SetValue(Levels);
        Shader.Parameters["Dither"].SetValue(Dither);
        Shader.Parameters["DitherScale"].SetValue(DitherScale);

        // Every pass below draws its INPUT as the SpriteBatch source, not the scene, because that is
        // the only texture slot a pass can rely on: SpriteBatch writes the drawn texture into slot 0
        // after the effect has bound its parameters, and ps_4_0 puts a single-sampler pass' texture
        // in slot 0 regardless of what register the sampler was pinned to. Composite and the blurs
        // read more than one texture, and there slot 0 really is what they're handed.

        // Normals come straight off the depth buffer, so this view needs none of the passes below.
        if (Debug == DebugMode.Normals)
        {
            gd.SetRenderTarget(destination);
            Shader.Parameters["Resolution"].SetValue(sceneResolution);
            Shader.CurrentTechnique = Shader.Techniques["DebugNormals"];
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
            spriteBatch.Draw(depth, Vector2.Zero, Color.White);
            spriteBatch.End();
            return;
        }

        // Pass 1 -- hemisphere sampling into the AO map, read off the depth/id buffer. Runs at the
        // AO resolution, which is also the grid its normal reconstruction differences over.
        gd.SetRenderTarget(_aoTarget);
        Shader.Parameters["Resolution"].SetValue(aoResolution);
        Shader.CurrentTechnique = Shader.Techniques["Occlusion"];
        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
        spriteBatch.Draw(depth, Vector2.Zero, Color.White);
        spriteBatch.End();

        // Passes 2 and 3 -- separable bilateral blur, back into the AO target so the composite has a
        // single place to look whether or not the blur ran.
        if (BlurRadius >= 1f)
        {
            gd.SetRenderTarget(_blurTarget);
            Shader.CurrentTechnique = Shader.Techniques["BlurH"];
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
            spriteBatch.Draw(_aoTarget, Vector2.Zero, Color.White);
            spriteBatch.End();

            gd.SetRenderTarget(_aoTarget);
            Shader.CurrentTechnique = Shader.Techniques["BlurV"];
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
            spriteBatch.Draw(_blurTarget, Vector2.Zero, Color.White);
            spriteBatch.End();
        }

        // The banding downstream rides the OUTPUT pixel grid, so from here on Resolution is the
        // scene's, not the AO map's.
        Shader.Parameters["Resolution"].SetValue(sceneResolution);

        // Pass 4a -- the AO map on its own, through the same depth-aware upsample the composite uses.
        // Point sampling: the interpolation happens in the shader, so letting the hardware blend the
        // taps first would defeat the depth test that keeps occlusion off the far side of an edge.
        if (Debug == DebugMode.Ao)
        {
            gd.SetRenderTarget(destination);
            Shader.CurrentTechnique = Shader.Techniques["DebugAO"];
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
            spriteBatch.Draw(_aoTarget, Vector2.Zero, Color.White);
            spriteBatch.End();
            return;
        }

        // Pass 4b -- blend the occlusion over the scene. The only pass that needs secondary textures,
        // and the only one where the SpriteBatch source is the scene colour.
        gd.SetRenderTarget(destination);
        Shader.CurrentTechnique = Shader.Techniques["Composite"];
        Shader.Parameters["AOTexture"].SetValue(_aoTarget);
        Shader.Parameters["OcclusionColor"].SetValue(EffectiveColor.ToVector3());
        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
        spriteBatch.Draw(source, Vector2.Zero, Color.White);
        spriteBatch.End();
    }

    // Single-channel data, but stored as Color: the AO map is 8-bit-per-channel worth of precision
    // either way once it's been quantised, and Color is the format every driver here handles without
    // a filtering fallback (the linear upsample in the composite depends on that).
    private void EnsureTargets(GraphicsDevice gd, int width, int height)
    {
        int w = System.Math.Max(1, width  / _downscale);
        int h = System.Math.Max(1, height / _downscale);

        if (_aoTarget != null && !_aoTarget.IsDisposed &&
            _aoTarget.Width == w && _aoTarget.Height == h) return;

        ReleaseTargets();
        _aoTarget   = new RenderTarget2D(gd, w, h);
        _blurTarget = new RenderTarget2D(gd, w, h);
    }

    private void ReleaseTargets()
    {
        _aoTarget?.Dispose();
        _blurTarget?.Dispose();
        _aoTarget = null;
        _blurTarget = null;
    }

    public override void Dispose() => ReleaseTargets();
}
