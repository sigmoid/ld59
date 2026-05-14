namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Quartz;
using Microsoft.Xna.Framework;

public class VorticityConfinementStep : IFluidSimulationStep
{
    private Effect _effect;
    private string shaderPath = "FluidEffect";
    private float _vorticityScale;

    public VorticityConfinementStep(float vorticityScale)
    {
        _vorticityScale = vorticityScale;
        _effect = Core.Content.Load<Effect>(shaderPath);
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        var velocityRT = renderTargetProvider.GetCurrent("velocity");
        var tempVelocityRT = renderTargetProvider.GetTemp("velocity");
        var vorticityRT = renderTargetProvider.GetCurrent("vorticity");

        device.SetRenderTarget(tempVelocityRT);

        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["texelSize"].SetValue(new Vector2(1f / gridSize, 1f / gridSize));
        _effect.Parameters["timeStep"].SetValue(deltaTime);
        _effect.Parameters["vorticityScale"].SetValue(_vorticityScale);
        _effect.Parameters["velocityTexture"].SetValue(velocityRT);
        _effect.Parameters["vorticityTexture"].SetValue(vorticityRT);
        _effect.CurrentTechnique = _effect.Techniques["VorticityConfinement"];
        _effect.CurrentTechnique.Passes[0].Apply();

        Utils.DrawFullScreenQuad(device, gridSize);
        device.SetRenderTarget(null);

        renderTargetProvider.Swap("velocity");
    }
}
