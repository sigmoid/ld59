namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Microsoft.Xna.Framework;
using Quartz;

public class ProjectStep : IFluidSimulationStep
{
    private readonly string _velocityName;
    private readonly string _pressureName;

    private Effect _effect;
    private string shaderPath = "shaders/fluid-simulation/project";

    public ProjectStep(string velocityName, string pressureName)
    {
        _velocityName = velocityName;
        _pressureName = pressureName;

        _effect = Core.Content.Load<Effect>(shaderPath);
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        var velocityRT = renderTargetProvider.GetCurrent(_velocityName);
        var pressureRT = renderTargetProvider.GetCurrent(_pressureName);
        var obstacleRT = renderTargetProvider.GetCurrent("obstacle");

        device.SetRenderTarget(null);
        device.SetRenderTarget(velocityRT);

        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["texelSize"].SetValue(new Vector2(1f / gridSize, 1f / gridSize));
        _effect.Parameters["velocityTexture"].SetValue(velocityRT);
        _effect.Parameters["pressureTexture"].SetValue(pressureRT);
        _effect.Parameters["obstacleTexture"].SetValue(obstacleRT);

        _effect.CurrentTechnique = _effect.Techniques["Project"];

        _effect.CurrentTechnique.Passes[0].Apply();
        Utils.DrawFullScreenQuad(device, gridSize);

        device.SetRenderTarget(null);
    }
}