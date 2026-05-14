namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Microsoft.Xna.Framework;
using Quartz;

public class IgnitionStep : IFluidSimulationStep
{
    private float _ignitionTemperature;
    private float _fuelBurnTemperature;
    private float _minFuelThreshold;

    private Effect _effect;
    private string shaderPath = "shaders/fluid-simulation/ignition";

    public IgnitionStep(float fuelBurnTemperature, float ignitionTemperature, float minFuelThreshold)
    {
        _fuelBurnTemperature = fuelBurnTemperature;
        _ignitionTemperature = ignitionTemperature;
        _minFuelThreshold = minFuelThreshold;

        _effect = Core.Content.Load<Effect>(shaderPath);
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        var temperatureRT = renderTargetProvider.GetCurrent("temperature");
        var tempTemperatureRT = renderTargetProvider.GetTemp("temperature");
        var fuelRT = renderTargetProvider.GetCurrent("fuel");

        device.SetRenderTarget(tempTemperatureRT);
        device.Clear(Color.Transparent);

        _effect.CurrentTechnique = _effect.Techniques["Ignition"];
        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["fuelTexture"].SetValue(fuelRT);
        _effect.Parameters["temperatureTexture"].SetValue(temperatureRT);
        _effect.Parameters["ignitionTemperature"].SetValue(_ignitionTemperature);
        _effect.Parameters["fuelBurnTemperature"].SetValue(_fuelBurnTemperature);
        _effect.Parameters["minFuelThreshold"].SetValue(_minFuelThreshold);
        _effect.Parameters["timeStep"].SetValue(deltaTime);
        _effect.CurrentTechnique.Passes[0].Apply();

        Utils.DrawFullScreenQuad(device, gridSize);

        device.SetRenderTarget(null);

        renderTargetProvider.Swap("temperature");
    }
}