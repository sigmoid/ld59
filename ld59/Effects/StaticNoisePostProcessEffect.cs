using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.Graphics;

public class StaticNoisePostProcessEffect : PostProcessEffect
{
    public float Intensity { get; set; } = 0.05f;

    public override void Apply(RenderTarget2D source, RenderTarget2D destination, SpriteBatch spriteBatch, GameTime gameTime)
    {
        Shader.Parameters["time"].SetValue((float)gameTime.TotalGameTime.TotalSeconds);
        Shader.Parameters["intensity"].SetValue(Intensity);
        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
        spriteBatch.Draw(source, Vector2.Zero, Color.White);
        spriteBatch.End();
    }

    public override void Initialize(GraphicsDevice graphicsDevice)
    {
        Shader = Core.Content.Load<Effect>("shaders/static_noise");
        Priority = 50;
    }
}
