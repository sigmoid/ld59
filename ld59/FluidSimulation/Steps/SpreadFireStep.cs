namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Microsoft.Xna.Framework;
using Quartz;

public class SpreadFireStep : IFluidSimulationStep
{
    private readonly int _iterations;
    private readonly float _ignitionTemperature;
    private readonly float _minFuelThreshold;
    private readonly string _temperatureName;
    private readonly string _fuelName;

    private Effect _effect;
    private string shaderPath = "shaders/fluid-simulation/spread-fire";

    public SpreadFireStep(int iterations, float ignitionTemperature, float minFuelThreshold, string temperatureName, string fuelName)
    {
        _iterations = iterations;
        _ignitionTemperature = ignitionTemperature;
        _minFuelThreshold = minFuelThreshold;
        _temperatureName = temperatureName;
        _fuelName = fuelName;

        _effect = Core.Content.Load<Effect>(shaderPath);
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        for (int i = 0; i < _iterations; i++)
        {
            var temperatureRT = renderTargetProvider.GetCurrent(_temperatureName);
            var tempTemperatureRT = renderTargetProvider.GetTemp(_temperatureName);
            var fuelRT = renderTargetProvider.GetCurrent(_fuelName);

            device.SetRenderTarget(tempTemperatureRT);

            _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
            _effect.Parameters["texelSize"].SetValue(new Vector2(1f / gridSize, 1f / gridSize));
            _effect.Parameters["fuelTexture"].SetValue(fuelRT);
            _effect.Parameters["temperatureTexture"].SetValue(temperatureRT);
            _effect.Parameters["ignitionTemperature"].SetValue(_ignitionTemperature);
            _effect.Parameters["minFuelThreshold"].SetValue(_minFuelThreshold);
            _effect.CurrentTechnique = _effect.Techniques["SpreadFire"];
            _effect.CurrentTechnique.Passes[0].Apply();

            Utils.DrawFullScreenQuad(device, gridSize);

            device.SetRenderTarget(null);

            renderTargetProvider.Swap("temperature");
        }

    }
}