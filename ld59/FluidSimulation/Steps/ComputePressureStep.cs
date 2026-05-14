namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Microsoft.Xna.Framework;
using Quartz;

public class ComputePressureStep : IFluidSimulationStep
{
    private int iterations;

    private string pressureTarget;
    private string divergenceTarget;

    private Effect _effect;
    private string shaderPath = "shaders/fluid-simulation/compute-pressure";

    public ComputePressureStep(string pressureTarget, string divergenceTarget, int iterations)
    {
        this.pressureTarget = pressureTarget;
        this.divergenceTarget = divergenceTarget;
        this.iterations = iterations;
        _effect = Core.Content.Load<Effect>(shaderPath);
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        var divergence = renderTargetProvider.GetCurrent(divergenceTarget);
        var obstacleRT = renderTargetProvider.GetCurrent("obstacle");
        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["texelSize"].SetValue(new Vector2(1f / gridSize, 1f / gridSize));
        _effect.Parameters["divergenceTexture"].SetValue(divergence);
        _effect.Parameters["obstacleTexture"].SetValue(obstacleRT);

        var renderTarget = renderTargetProvider.GetTemp(pressureTarget);
        device.SetRenderTarget(renderTarget);
        device.Clear(Color.Black);

        var tempRenderTarget = renderTargetProvider.GetTemp(pressureTarget);
        device.SetRenderTarget(tempRenderTarget);
        device.Clear(Color.Black);

        device.SetRenderTarget(null);

        for (int i = 0; i < iterations; i++)
        {
            var read = renderTargetProvider.GetCurrent(pressureTarget);
            var write = renderTargetProvider.GetTemp(pressureTarget);
            device.SetRenderTarget(write);
            _effect.Parameters["sourceTexture"].SetValue(read);
            _effect.CurrentTechnique = _effect.Techniques["JacobiPressure"];
            _effect.CurrentTechnique.Passes[0].Apply();
            Utils.DrawFullScreenQuad(device, gridSize);

            renderTargetProvider.Swap(pressureTarget);
        }

        if (iterations % 2 != 0)
        {
            renderTargetProvider.Swap(pressureTarget);
        }
    }
}