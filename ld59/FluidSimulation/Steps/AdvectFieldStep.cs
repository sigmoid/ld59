namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Microsoft.Xna.Framework;
using Quartz;

public class AdvectFieldStep : IFluidSimulationStep
{
    private readonly string _velocityName;
    private readonly string _sourceName;

    private Effect _effect;
    private string _shaderPath = "shaders/fluid-simulation/advect";

    public AdvectFieldStep(string velocityName, string sourceName)
    {
        _velocityName = velocityName;
        _sourceName = sourceName;
        _effect = Core.Content.Load<Effect>(_shaderPath);
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        var source = renderTargetProvider.GetCurrent(_sourceName);
        var destination = renderTargetProvider.GetTemp(_sourceName);
        device.SetRenderTarget(destination);

        var currentVelocity = renderTargetProvider.GetCurrent("velocity");
        var obstacleRT = renderTargetProvider.GetCurrent("obstacle");
        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["timeStep"].SetValue(deltaTime);
        _effect.Parameters["texelSize"].SetValue(new Vector2(1f / gridSize, 1f / gridSize));
        _effect.Parameters["velocityTexture"].SetValue(currentVelocity);
        _effect.Parameters["sourceTexture"].SetValue(source);
        _effect.Parameters["obstacleTexture"].SetValue(obstacleRT);

        _effect.CurrentTechnique = _effect.Techniques["Advect"];

        _effect.CurrentTechnique.Passes[0].Apply();
        Utils.DrawFullScreenQuad(device, gridSize);

        device.SetRenderTarget(null);
        renderTargetProvider.Swap(_sourceName);
    }
}