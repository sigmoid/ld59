namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Microsoft.Xna.Framework;
using Quartz;

public class VelocityDampingStep : IFluidSimulationStep
{
    private readonly string _velocityName;
    private readonly float _dampingCoefficient;

    private Effect _effect;
    private string shaderPath = "shaders/fluid-simulation/velocity-damping";

    public VelocityDampingStep(string velocityName, float dampingCoefficient)
    {
        _velocityName = velocityName;
        _dampingCoefficient = dampingCoefficient;

        _effect = Core.Content.Load<Effect>(shaderPath);
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        var source = renderTargetProvider.GetCurrent(_velocityName);
        var destination = renderTargetProvider.GetTemp(_velocityName);
        device.SetRenderTarget(destination);

        var currentVelocity = renderTargetProvider.GetCurrent("velocity");
        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["velocityTexture"].SetValue(currentVelocity);
        _effect.Parameters["velocityDampingCoefficient"].SetValue(_dampingCoefficient);
        _effect.Parameters["timeStep"].SetValue(deltaTime);

        _effect.CurrentTechnique = _effect.Techniques["VelocityDamping"];

        _effect.CurrentTechnique.Passes[0].Apply();
        Utils.DrawFullScreenQuad(device, gridSize);

        device.SetRenderTarget(null);
        renderTargetProvider.Swap(_velocityName);
    }
}