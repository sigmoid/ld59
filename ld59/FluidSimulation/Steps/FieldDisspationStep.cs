using crash.FluidSimulation;
using crash.FluidSimulation.Steps;
using crash.FluidSimulation.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;

public class FieldDissipationStep : IFluidSimulationStep
{
    private readonly float _dissipationRate;
    private readonly string _sourceName;
    private Effect _effect;
    private string shaderPath = "shaders/fluid-simulation/dissipation";

    public FieldDissipationStep(float dissipationRate, string sourceName)
    {
        _dissipationRate = dissipationRate;
        _sourceName = sourceName;

        _effect = Core.Content.Load<Effect>(shaderPath);
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        var source = renderTargetProvider.GetCurrent(_sourceName);
        var destination = renderTargetProvider.GetTemp(_sourceName);

        device.SetRenderTarget(destination);

        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["fieldTexture"].SetValue(source);
        _effect.Parameters["dissipationRate"].SetValue(_dissipationRate);
        _effect.Parameters["timeStep"].SetValue(deltaTime);
        _effect.CurrentTechnique = _effect.Techniques["Dissipate"];
        _effect.CurrentTechnique.Passes[0].Apply();

        Utils.DrawFullScreenQuad(device, gridSize);
        device.SetRenderTarget(null);

        renderTargetProvider.Swap(_sourceName);
    }
}