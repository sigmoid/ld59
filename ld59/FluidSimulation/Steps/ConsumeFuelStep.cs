namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Microsoft.Xna.Framework;
using Quartz;

public class ConsumeFuelState : IFluidSimulationStep
{
    private readonly string _fuelName;
    private readonly string _temperatureName;
    private readonly float _ignitionTemperature;
    private readonly float _fuelConsumptionRate;

    private Effect _effect;
    private string shaderPath = "shaders/fluid-simulation/consume-fuel";

    public ConsumeFuelState(string fuelName, string temperatureName, float ignitionTemperature, float fuelConsumptionRate)
    {
        _fuelName = fuelName;
        _temperatureName = temperatureName;
        _ignitionTemperature = ignitionTemperature;
        _fuelConsumptionRate = fuelConsumptionRate;
        _effect = Core.Content.Load<Effect>(shaderPath);
    }


    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        var tempFuelRT = renderTargetProvider.GetTemp(_fuelName);
        var temperatureRT = renderTargetProvider.GetCurrent(_temperatureName);
        var fuelRT = renderTargetProvider.GetCurrent(_fuelName);

        device.SetRenderTarget(tempFuelRT);
        device.Clear(Color.Transparent);
        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["temperatureTexture"].SetValue(temperatureRT);
        _effect.Parameters["fuelTexture"].SetValue(fuelRT);
        _effect.Parameters["ignitionTemperature"].SetValue(_ignitionTemperature);
        _effect.Parameters["fuelConsumptionRate"].SetValue(_fuelConsumptionRate);
        _effect.Parameters["timeStep"].SetValue(deltaTime);
        _effect.CurrentTechnique = _effect.Techniques["ConsumeFuel"];
        _effect.CurrentTechnique.Passes[0].Apply();
        Utils.DrawFullScreenQuad(device, gridSize);
        device.SetRenderTarget(null);

        renderTargetProvider.Swap(_fuelName);
    }
}