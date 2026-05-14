namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Microsoft.Xna.Framework;

public class CombustionDivergenceStep : IFluidSimulationStep
{
    private readonly string _temperatureName;
    private readonly string _divergenceName;
    private readonly string _fuelName;
    private readonly float _combustionPressure;
    private readonly float _ignitionTemperature;

    public CombustionDivergenceStep(string temperatureName, string divergenceName, string fuelName, float combustionPressure, float ignitionTemperature)
    {
        _temperatureName = temperatureName;
        _divergenceName = divergenceName;
        _fuelName = fuelName;
        _combustionPressure = combustionPressure;
        _ignitionTemperature = ignitionTemperature;
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        // var fuelRT = renderTargetProvider.GetCurrent(_fuelName);
        // var temperatureRT = renderTargetProvider.GetCurrent(_temperatureName);
        // var divergenceTempRT = renderTargetProvider.GetTemp(_divergenceName);
        // var divergenceRT = renderTargetProvider.GetCurrent(_divergenceName);

        // device.SetRenderTarget(divergenceTempRT);

        // effect.Parameters["fuelTexture"].SetValue(fuelRT);
        // effect.Parameters["temperatureTexture"].SetValue(temperatureRT);
        // effect.Parameters["divergenceTexture"].SetValue(divergenceRT);
        // effect.Parameters["ignitionTemperature"].SetValue(_ignitionTemperature);
        // effect.Parameters["combustionPressure"].SetValue(_combustionPressure);

        // effect.CurrentTechnique = effect.Techniques["CombustionDivergence"];
        // effect.CurrentTechnique.Passes[0].Apply();

        // Utils.DrawFullScreenQuad(device, gridSize);

        // device.SetRenderTarget(null);
        // renderTargetProvider.Swap(_divergenceName);
    }
}