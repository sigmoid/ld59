namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Quartz;

public class RadianceStep : IFluidSimulationStep
{
    private readonly string _temperatureName;
    private readonly float _ambientTemperature;
    private readonly float _maxTemperature;
    private readonly float _coolingRate;

    private Effect _effect;
    private string shaderPath = "shaders/fluid-simulation/radiance";

    public RadianceStep(string temperatureName, float ambientTemperature, float maxTemperature, float coolingRate)
    {
        _temperatureName = temperatureName;
        _ambientTemperature = ambientTemperature;
        _maxTemperature = maxTemperature;
        _coolingRate = coolingRate;

        _effect = Core.Content.Load<Effect>(shaderPath);
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        var temperatureRT = renderTargetProvider.GetCurrent(_temperatureName);
        var temperatureTempRT = renderTargetProvider.GetTemp(_temperatureName);

        device.SetRenderTarget(temperatureTempRT);
        device.Clear(Color.Transparent);
        _effect.CurrentTechnique = _effect.Techniques["Radiance"];
        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["temperatureTexture"].SetValue(temperatureRT);
        _effect.Parameters["ambientTemperature"].SetValue(_ambientTemperature);
        _effect.Parameters["maxTemperature"].SetValue(_maxTemperature);
        _effect.Parameters["timeStep"].SetValue(deltaTime);
        _effect.Parameters["coolingRate"].SetValue(_coolingRate);
        _effect.CurrentTechnique.Passes[0].Apply();
        Utils.DrawFullScreenQuad(device, gridSize);
        device.SetRenderTarget(null);

        renderTargetProvider.Swap(_temperatureName);
    }
}