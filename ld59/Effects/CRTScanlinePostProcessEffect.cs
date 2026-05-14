using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.Graphics;

public class CRTScanlinePostProcessEffect : PostProcessEffect
{
    private const float ScanBarPeriod = 25f;
    private float _scanBarPosition = 0f;

    public override void Initialize(GraphicsDevice graphicsDevice)
    {
        Shader = Core.Content.Load<Effect>("shaders/crt_scanlines");
        Priority = 2000;
    }

    public override void Apply(RenderTarget2D source, RenderTarget2D destination, SpriteBatch spriteBatch, GameTime gameTime)
    {
        _scanBarPosition += (float)gameTime.ElapsedGameTime.TotalSeconds / ScanBarPeriod;
        if (_scanBarPosition > 1f) _scanBarPosition -= 1f;

        Shader.Parameters["scanBarPosition"].SetValue(_scanBarPosition);
        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
        spriteBatch.Draw(source, Vector2.Zero, Color.White);
        spriteBatch.End();
    }
}
