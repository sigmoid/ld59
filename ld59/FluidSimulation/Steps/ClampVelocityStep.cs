namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Quartz;
using Microsoft.Xna.Framework;

public class ClampVelocityStep : IFluidSimulationStep
{
    private string _velocityName;
    private Effect _effect;
    private string shaderPath = "shaders/fluid-simulation/clamp-velocity";
    public ClampVelocityStep(string velocityTextureName)
    {
        _velocityName = velocityTextureName;
        _effect = Core.Content.Load<Effect>(shaderPath);
    }
    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        var velocityRT = renderTargetProvider.GetCurrent(_velocityName);
        var velocityTempRT = renderTargetProvider.GetTemp(_velocityName);

        device.SetRenderTarget(velocityTempRT);

        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["texelSize"].SetValue(new Vector2(1f / gridSize, 1f / gridSize));
        _effect.Parameters["velocityTexture"].SetValue(velocityRT);
        _effect.Parameters["timeStep"].SetValue(deltaTime);
        _effect.CurrentTechnique = _effect.Techniques["ClampVelocity"];
        _effect.CurrentTechnique.Passes[0].Apply();

        Utils.DrawFullScreenQuad(device, gridSize);
        device.SetRenderTarget(null);
    }
}
