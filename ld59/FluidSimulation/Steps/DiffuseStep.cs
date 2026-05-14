namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Quartz;
using System.Numerics;

public class DiffuseStep : IFluidSimulationStep
{
    private readonly string _targetName;
    private readonly int _iterations;

    private Effect _effect;
    private string shaderPath = "shaders/fluid-simulation/diffuse";

    public DiffuseStep(string targetName, int iterations)
    {
        _targetName = targetName;
        _iterations = iterations;

        _effect = Core.Content.Load<Effect>(shaderPath);
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        if (deltaTime <= 0.0001f)
            return;

        for (int i = 0; i < _iterations; i++)
        {
            var source = renderTargetProvider.GetCurrent(_targetName);
            var destination = renderTargetProvider.GetTemp(_targetName);

            device.SetRenderTarget(destination);

            _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
            _effect.Parameters["texelSize"].SetValue(new Vector2(1f / gridSize, 1f / gridSize));
            _effect.Parameters["sourceTexture"].SetValue(source);
            _effect.Parameters["diffusion"].SetValue(0.001f);
            _effect.Parameters["timeStep"].SetValue(deltaTime);
            _effect.CurrentTechnique = _effect.Techniques["Diffuse"];
            _effect.CurrentTechnique.Passes[0].Apply();

            Utils.DrawFullScreenQuad(device, gridSize);
            device.SetRenderTarget(null);

            renderTargetProvider.Swap(_targetName);
        }
    }
}
