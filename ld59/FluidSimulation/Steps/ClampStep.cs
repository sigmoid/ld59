namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Microsoft.Xna.Framework;
using Quartz;

public class ClampStep : IFluidSimulationStep
{
    private readonly string _targetName;
    private Effect _effect;
    private string shaderPath = "shaders/fluid-simulation/clamp";

    public ClampStep(string targetName)
    {
        _targetName = targetName;
        _effect = Core.Content.Load<Effect>(shaderPath);
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        var source = renderTargetProvider.GetCurrent(_targetName);
        var destination = renderTargetProvider.GetTemp(_targetName);

        device.SetRenderTarget(null);
        device.SetRenderTarget(destination);

        device.Clear(Color.Transparent);

        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["sourceTexture"].SetValue(source);
        _effect.CurrentTechnique = _effect.Techniques["Clamp"];
        _effect.CurrentTechnique.Passes[0].Apply();

        Utils.DrawFullScreenQuad(device, gridSize);

        device.SetRenderTarget(null);

        renderTargetProvider.Swap(_targetName);
    }
}