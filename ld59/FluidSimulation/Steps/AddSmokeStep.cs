namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Microsoft.Xna.Framework;
using Quartz;

public class AddSmokeStep : IFluidSimulationStep
{
    private string _temperatureName;
    private string _fuelName;
    private string _smokeName;
    private float _smokeEmissionRate;
    private float _minFuelThreshold;
    private float _ignitionTemperature;

    private Effect _effect;
    private string _shaderPath = "shaders/fluid-simulation/add-smoke";

    public AddSmokeStep(string temperatureName, string fuelName, string smokeName, float smokeEmissionRate, float minFuelThreshold, float ignitionTemperature)
    {
        _temperatureName = temperatureName;
        _fuelName = fuelName;
        _smokeName = smokeName;
        _smokeEmissionRate = smokeEmissionRate;
        _minFuelThreshold = minFuelThreshold;
        _ignitionTemperature = ignitionTemperature;
        _effect = Core.Content.Load<Effect>(_shaderPath);
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        var temperatureRT = renderTargetProvider.GetCurrent(_temperatureName);
        var fuelRT = renderTargetProvider.GetCurrent(_fuelName);
        var smokeRT = renderTargetProvider.GetCurrent(_smokeName);
        var tempSmokeRT = renderTargetProvider.GetTemp(_smokeName);

        device.SetRenderTarget(tempSmokeRT);

        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["temperatureTexture"].SetValue(temperatureRT);
        _effect.Parameters["fuelTexture"].SetValue(fuelRT);
        _effect.Parameters["smokeTexture"].SetValue(smokeRT);
        _effect.Parameters["smokeEmissionRate"].SetValue(_smokeEmissionRate);
        _effect.Parameters["timeStep"].SetValue(deltaTime);
        _effect.Parameters["minFuelThreshold"].SetValue(_minFuelThreshold);
        _effect.Parameters["ignitionTemperature"].SetValue(_ignitionTemperature);
        _effect.CurrentTechnique = _effect.Techniques["AddSmoke"];
        _effect.CurrentTechnique.Passes[0].Apply();

        Utils.DrawFullScreenQuad(device, gridSize);

        device.SetRenderTarget(null);

        renderTargetProvider.Swap(_smokeName);
    }
}