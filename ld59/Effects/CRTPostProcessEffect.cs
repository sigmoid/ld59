using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.Graphics;

public class CRTPostProcessEffect : PostProcessEffect
{
    private static readonly Vector2 Curvature = new(8f, 5.5f);

    public override void Apply(RenderTarget2D source, RenderTarget2D destination, SpriteBatch spriteBatch, GameTime gameTime)
    {
        Shader.Parameters["curvature"].SetValue(Curvature);
        Shader.Parameters["screenResolution"].SetValue(1080f);
        Shader.Parameters["roundness"].SetValue(8f);
        Shader.Parameters["vignetteOpacity"].SetValue(0.25f);
        spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, null, null, Shader);
        spriteBatch.Draw(source, Vector2.Zero, Color.White);
        spriteBatch.End();
    }

    public override void Initialize(GraphicsDevice graphicsDevice)
    {
        Shader = Core.Content.Load<Effect>("shaders/crt");
        Priority = 100;

        Core.MousePositionTransform = TransformMousePoint;
    }

    public static Point TransformMousePoint(Point screenPoint)
    {
        float w = Core.ScreenWidth;
        float h = Core.ScreenHeight;
        var uv = new Vector2(screenPoint.X / w, screenPoint.Y / h);

        uv = uv * 2f - Vector2.One;
        var offset = new Vector2(Math.Abs(uv.Y) / Curvature.X, Math.Abs(uv.X) / Curvature.Y);
        uv += uv * offset * offset;
        uv = uv * 0.5f + new Vector2(0.5f, 0.5f);

        return new Point((int)(uv.X * w), (int)(uv.Y * h));
    }
}