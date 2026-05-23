namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Quartz;

public class WindFieldStep : IFluidSimulationStep
{
    public float Strength = 1500f;
    public float Scale = 1f;
    public float EvolutionSpeed = 0.5f;

    private readonly Effect _effect;
    private float _time;

    public WindFieldStep()
    {
        _effect = Core.Content.Load<Effect>("shaders/fluid-simulation/wind-field");
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        _time += deltaTime;

        var source      = renderTargetProvider.GetCurrent("velocity");
        var destination = renderTargetProvider.GetTemp("velocity");
        device.SetRenderTarget(destination);

        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["timeStep"].SetValue(deltaTime);
        _effect.Parameters["time"].SetValue(_time);
        _effect.Parameters["windStrength"].SetValue(Strength);
        _effect.Parameters["windScale"].SetValue(Scale);
        _effect.Parameters["windEvolutionSpeed"].SetValue(EvolutionSpeed);
        _effect.Parameters["velocityTexture"].SetValue(source);

        _effect.CurrentTechnique = _effect.Techniques["WindField"];
        _effect.CurrentTechnique.Passes[0].Apply();
        Utils.DrawFullScreenQuad(device, gridSize);

        device.SetRenderTarget(null);
        renderTargetProvider.Swap("velocity");
    }

    public void DrawWind(GraphicsDevice device, int gridSize, RenderTarget2D renderTarget)
    {
        device.SetRenderTarget(renderTarget);
        device.Clear(Color.Transparent);

        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["time"].SetValue(_time);
        _effect.Parameters["windScale"].SetValue(Scale);
        _effect.Parameters["windEvolutionSpeed"].SetValue(EvolutionSpeed);

        _effect.CurrentTechnique = _effect.Techniques["VisualizeWind"];
        _effect.CurrentTechnique.Passes[0].Apply();
        Utils.DrawFullScreenQuad(device, gridSize);

        device.SetRenderTarget(null);
    }
}
