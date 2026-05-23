namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Quartz;

public class WindDivergenceStep : IFluidSimulationStep
{
    public float Intensity = 1600f;
    public float Scale = 2.5f;
    public float EvolutionSpeed = 0.25f;

    private readonly Effect _effect;
    private float _time;

    public WindDivergenceStep()
    {
        _effect = Core.Content.Load<Effect>("shaders/fluid-simulation/wind-divergence");
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        _time += deltaTime;

        var source      = renderTargetProvider.GetCurrent("divergence");
        var destination = renderTargetProvider.GetTemp("divergence");
        device.SetRenderTarget(destination);

        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["time"].SetValue(_time);
        _effect.Parameters["divergenceIntensity"].SetValue(Intensity);
        _effect.Parameters["noiseScale"].SetValue(Scale);
        _effect.Parameters["evolutionSpeed"].SetValue(EvolutionSpeed);
        _effect.Parameters["divergenceTexture"].SetValue(source);

        _effect.CurrentTechnique = _effect.Techniques["WindDivergence"];
        _effect.CurrentTechnique.Passes[0].Apply();
        Utils.DrawFullScreenQuad(device, gridSize);

        device.SetRenderTarget(null);
        renderTargetProvider.Swap("divergence");
    }
}
