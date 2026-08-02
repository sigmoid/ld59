using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.Graphics;

/// <summary>
/// Exponential distance fog over a rendered 3D scene. Needs the scene's linear depth map (see
/// Scene.DrawDepthPass) and camera matrices, which arrive each frame through
/// <see cref="Scene3DPostProcessEffect.Frame"/>.
///
/// Density decays exponentially with height (<see cref="HeightFalloff"/> 0 = uniform fog), and an
/// fbm field can break the haze up so it doesn't read as a flat wash. The noise fades in over
/// <see cref="NoiseDistance"/> world units, so nearby surfaces keep a stable tint.
/// </summary>
public class ExponentialFogPostProcessEffect : Scene3DPostProcessEffect
{
    /// <summary>
    /// Colour the scene fades toward. Defaults to the 1-bit palette's bright colour: mid-grey is
    /// the worst possible target for a dithered output (0.5 luminance is exactly where error
    /// diffusion produces a maximum-noise 50/50 checkerboard), so saturated fog reads as static
    /// rather than distance. Landing on the bright colour makes full fog resolve to solid white.
    /// </summary>
    public Color FogColor { get; set; } = new Color(0xff, 0xfa, 0xf0);

    /// <summary>
    /// While the 1-bit dithering stage is enabled, fade toward ITS bright colour instead of
    /// <see cref="FogColor"/>, so the two can't drift apart when that palette is retuned. Turn it
    /// off to force <see cref="FogColor"/> in every mode (e.g. to A/B a grey fog).
    /// </summary>
    public bool MatchOneBitPalette { get; set; } = true;

    // The colour actually blended this frame -- FogColor, or the 1-bit stage's bright colour when
    // that stage is live and matching is on.
    private Color EffectiveColor
    {
        get
        {
            if (!MatchOneBitPalette) return FogColor;
            var oneBit = Core.PostProcessing?.GetEffect<OneBitDitheringPostProcessEffect>();
            return oneBit is { Enabled: true } ? oneBit.BrightColor : FogColor;
        }
    }

    /// <summary>Fog accumulated per world unit at <see cref="BaseHeight"/>. 0 = no fog.</summary>
    public float Density { get; set; } = 0.002f;

    /// <summary>World units of clear air in front of the camera before fog starts accumulating.</summary>
    public float Start { get; set; } = 0f;

    /// <summary>How fast density decays with height. 0 = uniform fog at every altitude;
    /// 0.05 halves the density roughly every 14 world units of climb.</summary>
    public float HeightFalloff { get; set; } = 0.23f;

    /// <summary>The Y at which <see cref="Density"/> is the actual density (the "fog floor").</summary>
    public float BaseHeight { get; set; } = 0f;

    /// <summary>How much fog pixels with no geometry behind them get. 1 = fully fogged horizon
    /// (right for a plain background); lower it to keep a drawn skybox visible.</summary>
    public float BackgroundFog { get; set; } = 1f;

    /// <summary>
    /// Ceiling on the blend: 1 lets distance resolve to the flat fog colour, lower values hold it
    /// back so far geometry keeps some of its own value instead of vanishing into solid white.
    /// Clips the top of the curve rather than scaling it, so lowering this doesn't wash out the
    /// near-to-mid falloff that <see cref="Density"/> controls.
    /// </summary>
    public float MaxFog { get; set; } = 0.8f;

    /// <summary>
    /// Quantise the fog into this many bands (0 or 1 = smooth). Aimed at the 1-bit output: a smooth
    /// ramp is exactly what error-diffusion dithering destroys, turning the depth cue into spatial
    /// noise, whereas discrete steps come through as readable planes of haze. Same idea as the
    /// <c>posterize</c> command does for the 3D lighting -- 4-6 bands is a good starting range.
    /// </summary>
    public float Levels { get; set; } = 8f;

    /// <summary>
    /// How far the band edges are ordered-dithered, 0 (hard steps) to 1 (full 4x4 Bayer stipple).
    /// Only applies while <see cref="Levels"/> is banding. The pattern is anchored to screen pixels,
    /// so unlike the error diffusion downstream it holds still as the camera moves -- which is what
    /// stops dithered fog from boiling. Fewer bands make the stipple more prominent.
    /// </summary>
    public float Dither { get; set; } = 1f;

    /// <summary>Pixels per dither cell. Above 1 the stipple is chunkier and survives the 1-bit pass
    /// and the final upscale better; fractional values are rounded up to 1.</summary>
    public float DitherScale { get; set; } = 1f;

    /// <summary>Amplitude of the fbm density modulation. 0 skips the noise (and its cost).</summary>
    public float NoiseStrength { get; set; } = 0.4f;

    /// <summary>World-space frequency of the noise: bigger = smaller, busier wisps.</summary>
    public float NoiseScale { get; set; } = 0.003f;

    /// <summary>Distance over which the noise fades in, so near geometry isn't tinted unevenly.</summary>
    public float NoiseDistance { get; set; } = 60f;

    /// <summary>Direction the noise field drifts, in world units per second of animation.</summary>
    public Vector3 NoiseWind { get; set; } = new Vector3(0.6f, 0.05f, 0.3f);

    /// <summary>
    /// How fast the fog churns: multiplied by delta time each frame to advance the noise field.
    /// 0 freezes it, 2 runs it twice as fast as <see cref="NoiseWind"/> alone would.
    /// </summary>
    public float NoiseSpeed { get; set; } = 0.3f;

    // Animation phase, accumulated as speed * dt rather than derived from absolute elapsed time.
    // Scaling absolute time instead would teleport the whole field the moment the speed changed
    // (t seconds in, a speed change of ds jumps the phase by t*ds); integrating the rate keeps the
    // fog continuous through a live retune.
    private float _noisePhase;

    /// <summary>Replaces the output with the raw depth buffer as a grey ramp (see the
    /// <c>depthview</c> console command).</summary>
    public bool ShowDepth { get; set; }

    // Density 0 means every pixel would lerp by exactly 0 -- skip the pass (and the depth pass
    // feeding it) rather than paying for a no-op full-screen blit.
    public override bool WantsDepth => Enabled && (Density > 0f || ShowDepth);

    public override void Initialize(GraphicsDevice graphicsDevice)
    {
        Shader = Core.Content.Load<Effect>("shaders/exponential-fog");
        Priority = 10;   // before stylisation (posterize/1-bit) so the fog gets quantised too
    }

    public override void Apply(RenderTarget2D source, RenderTarget2D destination, SpriteBatch spriteBatch, GameTime gameTime)
    {
        var depth = Frame.DepthMap;

        // Advance the churn by this frame's slice. Done before the early-out below so a frame that
        // can't draw fog still doesn't leave the animation behind.
        _noisePhase += NoiseSpeed * (float)gameTime.ElapsedGameTime.TotalSeconds;

        // No depth this frame (the owner never ran a depth pass): pass the scene through untouched
        // rather than fogging against a stale or missing buffer.
        if (depth == null || depth.IsDisposed)
        {
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp);
            spriteBatch.Draw(source, Vector2.Zero, Color.White);
            spriteBatch.End();
            return;
        }

        // The depth debug view reads the depth map as its SpriteBatch source (slot 0), not through
        // DepthTexture: a pass that samples one texture always gets it in slot 0 under ps_4_0, and
        // slot 0 is what SpriteBatch overwrites with the drawn sprite. Only the fog pass proper,
        // which samples the scene colour too, can hold a second texture reliably.
        if (ShowDepth)
        {
            Shader.CurrentTechnique = Shader.Techniques["DepthDebug"];
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
            spriteBatch.Draw(depth, Vector2.Zero, Color.White);
            spriteBatch.End();
            return;
        }

        Shader.CurrentTechnique = Shader.Techniques["ExponentialFog"];
        Shader.Parameters["DepthTexture"].SetValue(depth);
        Shader.Parameters["InverseViewProjection"].SetValue(Frame.InverseViewProjection);
        Shader.Parameters["CameraPosition"].SetValue(Frame.CameraPosition);
        Shader.Parameters["FarDistance"].SetValue(Frame.FarDistance);
        Shader.Parameters["Time"].SetValue(_noisePhase);

        Shader.Parameters["FogColor"].SetValue(EffectiveColor.ToVector3());
        Shader.Parameters["FogDensity"].SetValue(Density);
        Shader.Parameters["FogStart"].SetValue(Start);
        Shader.Parameters["FogHeightFalloff"].SetValue(HeightFalloff);
        Shader.Parameters["FogBaseHeight"].SetValue(BaseHeight);
        Shader.Parameters["BackgroundFog"].SetValue(BackgroundFog);
        Shader.Parameters["MaxFog"].SetValue(MaxFog);
        Shader.Parameters["FogLevels"].SetValue(Levels);
        Shader.Parameters["FogDither"].SetValue(Dither);
        Shader.Parameters["DitherScale"].SetValue(DitherScale);
        // The dither rides the render target's pixel grid, so it needs the size in pixels.
        Shader.Parameters["Resolution"].SetValue(new Vector2(source.Width, source.Height));

        Shader.Parameters["NoiseStrength"].SetValue(NoiseStrength);
        Shader.Parameters["NoiseScale"].SetValue(NoiseScale);
        Shader.Parameters["NoiseDistance"].SetValue(NoiseDistance);
        Shader.Parameters["NoiseWind"].SetValue(NoiseWind);

        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
        spriteBatch.Draw(source, Vector2.Zero, Color.White);
        spriteBatch.End();
    }
}
