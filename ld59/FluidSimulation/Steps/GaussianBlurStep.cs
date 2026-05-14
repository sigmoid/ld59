using Microsoft.Xna.Framework.Graphics;
using crash.FluidSimulation.Utils;
using System.Text;
using Quartz;
using System.Numerics;

namespace crash.FluidSimulation.Steps
{
    public class GaussianBlurStep : IFluidSimulationStep
    {
        private readonly string _targetName;
        private readonly float _blurRadius;
        private readonly int _kernelSize;

        private Effect _effect;
        private string shaderPath = "shaders/fluid-simulation/gaussian-blur";

        public GaussianBlurStep(string targetName, float blurRadius = 1.0f, int kernelSize = 9)
        {
            _targetName = targetName;
            _blurRadius = blurRadius;
            _kernelSize = kernelSize;

            _effect = Core.Content.Load<Effect>(shaderPath);
        }

        public void Execute(GraphicsDevice device, int gridSize, IRenderTargetProvider renderTargetProvider, float deltaTime)
        {
            _effect.Parameters["renderTargetSize"].SetValue(new Vector2(gridSize, gridSize));
            _effect.Parameters["texelSize"].SetValue(new Vector2(1f / gridSize, 1f / gridSize));
            _effect.Parameters["blurRadius"].SetValue(_blurRadius);
            _effect.Parameters["blurKernelSize"].SetValue(_kernelSize);

            var source = renderTargetProvider.GetCurrent(_targetName);
            var temp = renderTargetProvider.GetTemp(_targetName);

            device.SetRenderTarget(temp);
            _effect.Parameters["sourceTexture"].SetValue(source);
            _effect.CurrentTechnique = _effect.Techniques["GaussianBlurHorizontal"];
            _effect.CurrentTechnique.Passes[0].Apply();
            Utils.Utils.DrawFullScreenQuad(device, gridSize);
            device.SetRenderTarget(null);

            renderTargetProvider.Swap(_targetName);

            var horizontalResult = renderTargetProvider.GetCurrent(_targetName);
            var finalResult = renderTargetProvider.GetTemp(_targetName);

            device.SetRenderTarget(finalResult);
            _effect.Parameters["sourceTexture"].SetValue(horizontalResult);
            _effect.CurrentTechnique = _effect.Techniques["GaussianBlurVertical"];
            _effect.CurrentTechnique.Passes[0].Apply();
            Utils.Utils.DrawFullScreenQuad(device, gridSize);
            device.SetRenderTarget(null);

            renderTargetProvider.Swap(_targetName);
        }
    }
}
