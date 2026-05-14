namespace crash.FluidSimulation.Steps;

using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using Microsoft.Xna.Framework;
using Quartz;

public class BuoyancyStep : IFluidSimulationStep
{
    private string _temperatureName;
    private string _velocityName;
    private float _ambientTemperature;
    private float _heatBuoyancyConstant;
    private float _gravity;

    private Effect _effect;
    private string shaderPath = "shaders/fluid-simulation/buoyancy";

    public BuoyancyStep(string temperatureName, string velocityName, float ambientTemperature, float heatBuoyancyConstant, float gravity)
    {
        _temperatureName = temperatureName;
        _velocityName = velocityName;
        _ambientTemperature = ambientTemperature;
        _heatBuoyancyConstant = heatBuoyancyConstant;
        _gravity = gravity;

        _effect = Core.Content.Load<Effect>(shaderPath);
    }

    public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
    {
        var temperatureRT = renderTargetProvider.GetCurrent(_temperatureName);
        var velocityRT = renderTargetProvider.GetCurrent(_velocityName);
        var tempVelocityRT = renderTargetProvider.GetTemp(_velocityName);

        device.SetRenderTarget(tempVelocityRT);

        _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
        _effect.Parameters["temperatureTexture"].SetValue(temperatureRT);
        _effect.Parameters["velocityTexture"].SetValue(velocityRT);
        _effect.Parameters["ambientTemperature"].SetValue(_ambientTemperature);
        _effect.Parameters["heatBuoyancyConstant"].SetValue(_heatBuoyancyConstant);
        _effect.Parameters["gravity"].SetValue(_gravity);
        _effect.Parameters["timeStep"].SetValue(deltaTime);
        _effect.CurrentTechnique = _effect.Techniques["Buoyancy"];
        _effect.CurrentTechnique.Passes[0].Apply();

        Utils.DrawFullScreenQuad(device, gridSize);

        device.SetRenderTarget(null);

        renderTargetProvider.Swap(_velocityName);
    }
}