using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.Graphics;

/// <summary>
/// Silhouette outlines drawn from the scene depth/id buffer (see Scene.DrawDepthPass, which writes
/// a per-entity id alongside linear depth). Every boundary between two different entities -- and
/// between any entity and the background -- becomes a line of configurable thickness.
///
/// Runs after the fog so lines stay crisp through haze, which is what keeps distant shapes legible
/// once the 1-bit pass has flattened their interiors. <see cref="FadeDistance"/> softens that if
/// far-off clutter ends up too busy.
/// </summary>
public class OutlinePostProcessEffect : Scene3DPostProcessEffect
{
    /// <summary>
    /// Line colour. Defaults to the 1-bit palette's dark colour so outlines resolve to solid ink
    /// rather than dithering into a grey stipple.
    /// </summary>
    public Color OutlineColor { get; set; } = new Color(0x22, 0x1f, 0x34);

    /// <summary>
    /// While the 1-bit dithering stage is enabled, draw in ITS dark colour instead of
    /// <see cref="OutlineColor"/>, so the two can't drift apart when that palette is retuned.
    /// </summary>
    public bool MatchOneBitPalette { get; set; } = true;

    private Color EffectiveColor
    {
        get
        {
            if (!MatchOneBitPalette) return OutlineColor;
            var oneBit = Core.PostProcessing?.GetEffect<OneBitDitheringPostProcessEffect>();
            return oneBit is { Enabled: true } ? oneBit.DarkColor : OutlineColor;
        }
    }

    /// <summary>Widest line the shader's bounded dilation loop can draw.</summary>
    public const int MaxWidth = 33;   // 1 + 2 * MAX_RADIUS in outline.fx

    /// <summary>
    /// Line width in pixels, 1 = hairline. Clamped to <see cref="MaxWidth"/>, which is what bounds
    /// the per-pixel cost.
    /// <para>
    /// Even widths are honest but slightly lopsided: the extra pixel has to go on one side of the
    /// boundary, so a 2px line reads as sitting a touch inside an object's left/top edges and
    /// outside its right/bottom ones. Odd widths are perfectly centred. See the note on
    /// <c>Dilate</c> in outline.fx for why the pass can't do better cheaply.
    /// </para>
    /// </summary>
    public int Width { get; set; } = 3;

    /// <summary>How strongly the line is blended over the scene. 1 = solid.</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>World distance at which outlines have faded out entirely. 0 disables the fade and
    /// keeps every line at full strength however far away it is.</summary>
    public float FadeDistance { get; set; }

    /// <summary>What the pass outputs instead of the outlined scene, for diagnosing bad lines.</summary>
    public enum DebugMode
    {
        /// <summary>Normal output.</summary>
        Off,
        /// <summary>The id buffer, one colour per entity (dark blue = background).</summary>
        Ids,
        /// <summary>The dilated edge mask alone: white lines on black.</summary>
        Mask,
    }

    /// <summary>
    /// Replaces the output with a view of the pass' inputs. Note that whatever this draws still
    /// goes through the screen-wide 1-bit dithering afterwards, so turn that off (the <c>1bit</c>
    /// command) before reading the colours.
    /// </summary>
    public DebugMode Debug { get; set; } = DebugMode.Off;

    // Ping-pong targets for the edge mask and its horizontal dilation. Allocated to match the
    // source and released in Dispose -- unlike Shader, these are ours, not the ContentManager's.
    private RenderTarget2D _edgeMask;
    private RenderTarget2D _dilatedMask;

    // Width only widens an existing line; with no visible line there's nothing to widen, so this
    // tracks opacity rather than width.
    public override bool WantsDepth => Enabled && Opacity > 0f;

    /// <summary>
    /// Splits <see cref="Width"/> into the shader's (before, after) dilation window. A 1px mask
    /// dilated by lo back and hi forward is <c>lo + hi + 1</c> wide, so the split carries the odd
    /// pixel out: 1 -> (0,0), 2 -> (0,1), 3 -> (1,1), 4 -> (1,2).
    /// </summary>
    private Vector2 DilateWindow
    {
        get
        {
            int span = MathHelper.Clamp(Width, 1, MaxWidth) - 1;
            int lo = span / 2;
            return new Vector2(lo, span - lo);
        }
    }

    public override void Initialize(GraphicsDevice graphicsDevice)
    {
        Shader = Core.Content.Load<Effect>("shaders/outline");
        Priority = 20;   // after the fog (10), before the screen-wide stylisation passes
    }

    public override void Apply(RenderTarget2D source, RenderTarget2D destination, SpriteBatch spriteBatch, GameTime gameTime)
    {
        var gd = spriteBatch.GraphicsDevice;
        var depth = Frame.DepthMap;

        // No id buffer this frame: pass the scene through untouched rather than outlining against
        // a stale or missing one.
        if (depth == null || depth.IsDisposed)
        {
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp);
            spriteBatch.Draw(source, Vector2.Zero, Color.White);
            spriteBatch.End();
            return;
        }

        EnsureTargets(gd, source.Width, source.Height);

        var resolution = new Vector2(source.Width, source.Height);
        Shader.Parameters["Resolution"].SetValue(resolution);
        Shader.Parameters["Dilate"].SetValue(DilateWindow);

        // Every pass below draws its INPUT as the SpriteBatch source, not the scene, because that
        // is the only texture slot a pass can rely on: SpriteBatch writes the drawn texture into
        // slot 0 after the effect has bound its parameters, and ps_4_0 puts a single-sampler pass'
        // texture in slot 0 regardless of what register the sampler was pinned to. Composite is the
        // one pass that reads more than one texture, and there slot 0 really is the scene.

        // Ids come straight off the buffer, so this view needs none of the passes below.
        if (Debug == DebugMode.Ids)
        {
            gd.SetRenderTarget(destination);
            Shader.CurrentTechnique = Shader.Techniques["DebugIds"];
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
            spriteBatch.Draw(depth, Vector2.Zero, Color.White);
            spriteBatch.End();
            return;
        }

        // Pass 1 -- id discontinuities to a 1px mask, read off the depth/id buffer.
        gd.SetRenderTarget(_edgeMask);
        Shader.CurrentTechnique = Shader.Techniques["Edge"];
        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
        spriteBatch.Draw(depth, Vector2.Zero, Color.White);
        spriteBatch.End();

        // Pass 2 -- widen horizontally.
        gd.SetRenderTarget(_dilatedMask);
        Shader.CurrentTechnique = Shader.Techniques["DilateH"];
        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
        spriteBatch.Draw(_edgeMask, Vector2.Zero, Color.White);
        spriteBatch.End();

        // Pass 3a -- the bare mask: vertical dilation only, straight from the h-dilated mask.
        if (Debug == DebugMode.Mask)
        {
            gd.SetRenderTarget(destination);
            Shader.CurrentTechnique = Shader.Techniques["DebugMask"];
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
            spriteBatch.Draw(_dilatedMask, Vector2.Zero, Color.White);
            spriteBatch.End();
            return;
        }

        // Pass 3b -- widen vertically and blend the result over the scene. The only pass that needs
        // secondary textures, and the only one where the SpriteBatch source is the scene colour.
        gd.SetRenderTarget(destination);
        Shader.CurrentTechnique = Shader.Techniques["Composite"];
        Shader.Parameters["MaskTexture"].SetValue(_dilatedMask);
        Shader.Parameters["DepthTexture"].SetValue(depth);
        Shader.Parameters["OutlineColor"].SetValue(EffectiveColor.ToVector3());
        Shader.Parameters["Opacity"].SetValue(Opacity);
        Shader.Parameters["FadeDistance"].SetValue(FadeDistance);
        Shader.Parameters["FarDistance"].SetValue(Frame.FarDistance);
        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
        spriteBatch.Draw(source, Vector2.Zero, Color.White);
        spriteBatch.End();
    }

    private void EnsureTargets(GraphicsDevice gd, int width, int height)
    {
        if (_edgeMask != null && !_edgeMask.IsDisposed &&
            _edgeMask.Width == width && _edgeMask.Height == height) return;

        _edgeMask?.Dispose();
        _dilatedMask?.Dispose();
        _edgeMask    = new RenderTarget2D(gd, width, height);
        _dilatedMask = new RenderTarget2D(gd, width, height);
    }

    public override void Dispose()
    {
        _edgeMask?.Dispose();
        _dilatedMask?.Dispose();
        _edgeMask = null;
        _dilatedMask = null;
    }
}
