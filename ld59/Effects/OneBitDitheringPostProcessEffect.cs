using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.Graphics;

public class OneBitDitheringPostProcessEffect : PostProcessEffect
{
    public Color DarkColor { get; set; } = Color.Black;
    public Color BrightColor { get; set; } = ColorPalette.White;

    private RenderTarget2D _stateA;
    private RenderTarget2D _stateB;

    public override void Initialize(GraphicsDevice graphicsDevice)
    {
        Shader = Core.Content.Load<Effect>("shaders/one_bit_dithering");
        Priority = 90;
    }

    public override void Apply(RenderTarget2D source, RenderTarget2D destination, SpriteBatch spriteBatch, GameTime gameTime)
    {
        var gd = spriteBatch.GraphicsDevice;

        if (_stateA == null || _stateA.Width != source.Width || _stateA.Height != source.Height)
        {
            _stateA?.Dispose();
            _stateB?.Dispose();
            _stateA = new RenderTarget2D(gd, source.Width, source.Height);
            _stateB = new RenderTarget2D(gd, source.Width, source.Height);
        }

        Shader.Parameters["resolution"].SetValue(new Vector2(source.Width, source.Height));

        // Pass 1 — init: pack luminance into R, zero G (error) and B (binary output)
        gd.SetRenderTarget(_stateA);
        Shader.CurrentTechnique = Shader.Techniques["InitPass"];
        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
        spriteBatch.Draw(source, Vector2.Zero, Color.White);
        spriteBatch.End();

        // Passes 2–10 — diffuse: cycle through all 9 (px,py) grid positions
        Shader.CurrentTechnique = Shader.Techniques["DiffusePass"];
        var read = _stateA;
        var write = _stateB;
        for (int j = 0; j < 3; j++)
        {
            for (int i = 0; i < 3; i++)
            {
                Shader.Parameters["px"].SetValue((float)i);
                Shader.Parameters["py"].SetValue((float)j);
                gd.SetRenderTarget(write);
                spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
                spriteBatch.Draw(read, Vector2.Zero, Color.White);
                spriteBatch.End();
                (read, write) = (write, read);
            }
        }

        // Pass 11 — composite: map B channel (0/1) to dark/bright colour
        gd.SetRenderTarget(destination);
        Shader.CurrentTechnique = Shader.Techniques["CompositePass"];
        Shader.Parameters["darkColor"].SetValue(DarkColor.ToVector3());
        Shader.Parameters["brightColor"].SetValue(BrightColor.ToVector3());
        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
        spriteBatch.Draw(read, Vector2.Zero, Color.White);
        spriteBatch.End();
    }
}
