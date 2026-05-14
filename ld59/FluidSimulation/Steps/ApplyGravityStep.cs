namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Microsoft.Xna.Framework;
using Quartz;

public class ApplyGravityStep : IFluidSimulationStep
{
    private readonly string _velocityName;
    private readonly float _gravity;

    private Effect _effect;
    private string _shaderPath = "shaders/fluid-simulation/apply-gravity";

    public ApplyGravityStep(string velocityName, float gravity)
    {
        _velocityName = velocityName;
        _gravity = gravity;
        _effect = Core.Content.Load<Effect>(_shaderPath);
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        var source = renderTargetProvider.GetCurrent(_velocityName);
        var destination = renderTargetProvider.GetTemp(_velocityName);
        device.SetRenderTarget(destination);

        _effect.Parameters["velocityTexture"].SetValue(source);
        _effect.Parameters["gravity"].SetValue(_gravity);
        _effect.Parameters["timeStep"].SetValue(deltaTime);
        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));

        _effect.CurrentTechnique = _effect.Techniques["ApplyGravity"];

        _effect.CurrentTechnique.Passes[0].Apply();
        Utils.DrawFullScreenQuad(device, gridSize);

        device.SetRenderTarget(null);
        renderTargetProvider.Swap(_velocityName);
    }
}