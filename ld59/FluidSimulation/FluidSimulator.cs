using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using crash.FluidSimulation.Steps;
using Quartz.Graphics;

namespace crash.FluidSimulation
{
    public class FluidSimulator
    {
        private GraphicsDevice _graphicsDevice;
        private Effect _fluidEffect;
        private SpriteBatch _spriteBatch;

        #region Render Targets

        private RenderTarget2D _velocityRT;
        private RenderTarget2D _pressureRT;
        private RenderTarget2D _pressureRT2;
        private RenderTarget2D _tempVelocityRT;
        private RenderTarget2D _divergenceRT;
        private RenderTarget2D _tempDivergenceRT;
        private RenderTarget2D _temperatureRT;
        private RenderTarget2D _tempTemperatureRT;
        private RenderTarget2D _vorticityRT;
        private RenderTarget2D _obstacleRT;
        private RenderTarget2D _tempObstacleRT;
        private RenderTarget2D _smokeRT;
        private RenderTarget2D _tempSmokeRT;

        #endregion

        #region Fluid Sim Properties

        private int _gridSize;
        private float _diffusion = 0.0001f;
        private float _forceStrength = 1.0f;
        private float _sourceStrength = 1.0f;
        private float _vorticityScale = 0.125f;

        private int diffuseIterations = 10;
        private int pressureIterations = 20;

        public float SmokeOpacity { get; set; } = 1.0f;
        public Vector2 UVOffset { get; set; } = Vector2.Zero;

        #endregion

        private List<IFluidSimulationStep> _simulationSteps;
        private RenderTargetProvider _renderTargetProvider;

        public FluidSimulator(GraphicsDevice graphicsDevice, int gridSize)
        {
            _graphicsDevice = graphicsDevice;
            _gridSize = gridSize;
            _spriteBatch = new SpriteBatch(graphicsDevice);

            CreateRenderTargets();
            InitializeRenderTargets();
            PopulateRenderTargetProvider();
            CreateSimulationSteps();
        }

        private void InitializeRenderTargets()
        {
            _graphicsDevice.SetRenderTarget(_velocityRT);
            _graphicsDevice.Clear(Color.Transparent);
            _graphicsDevice.SetRenderTarget(_temperatureRT);
            _graphicsDevice.Clear(Color.Transparent);
            _graphicsDevice.SetRenderTarget(_pressureRT);
            _graphicsDevice.Clear(Color.Transparent);
            _graphicsDevice.SetRenderTarget(_pressureRT2);
            _graphicsDevice.Clear(Color.Transparent);
            _graphicsDevice.SetRenderTarget(_vorticityRT);
            _graphicsDevice.Clear(Color.Transparent);
            _graphicsDevice.SetRenderTarget(_obstacleRT);
            _graphicsDevice.Clear(Color.Transparent);
            _graphicsDevice.SetRenderTarget(_smokeRT);
            _graphicsDevice.Clear(Color.Transparent);
            _graphicsDevice.SetRenderTarget(_tempSmokeRT);
            _graphicsDevice.Clear(Color.Transparent);
            _graphicsDevice.SetRenderTarget(null);
        }

        private void PopulateRenderTargetProvider()
        {
            _renderTargetProvider = new RenderTargetProvider();
            _renderTargetProvider.RegisterRenderTargetPair("temperature", _temperatureRT, _tempTemperatureRT);
            _renderTargetProvider.RegisterRenderTargetPair("velocity", _velocityRT, _tempVelocityRT);
            _renderTargetProvider.RegisterRenderTargetPair("divergence", _divergenceRT, _tempDivergenceRT);
            _renderTargetProvider.RegisterRenderTargetPair("pressure", _pressureRT, _pressureRT2);
            _renderTargetProvider.RegisterRenderTargetPair("vorticity", _vorticityRT, _vorticityRT);
            _renderTargetProvider.RegisterRenderTargetPair("obstacle", _obstacleRT, _tempObstacleRT);
            _renderTargetProvider.RegisterRenderTargetPair("smoke", _smokeRT, _tempSmokeRT);
        }

        private void CreateSimulationSteps()
        {
            _simulationSteps = new List<IFluidSimulationStep>
            {
                // Step 1: ADVECTION - Transport quantities along velocity field
                new AdvectFieldStep("velocity", "smoke"),

                // Step 2: DIFFUSION - Viscous and thermal diffusion
                new DiffuseStep("velocity", diffuseIterations),
                new GaussianBlurStep("temperature", 1, 8),
                new DiffuseStep("smoke", 3), // Reduced from diffuseIterations to keep smoke more detailed

                // Step 3: VORTICITY - Add turbulent swirls for more interesting smoke motion
                // new ComputeVorticityStep(),
                // new VorticityConfinementStep(_vorticityScale),

                // Step 4: EXTERNAL FORCES - Applied via AddForce() calls

                // Step 5: PROJECTION - Make velocity field divergence-free
                new ComputeDivergenceStep(),
                //new CombustionDivergenceStep("temperature", "divergence", "fuel", combustionPressure, ignitionTemperature),
                new ComputePressureStep("pressure", "divergence", pressureIterations),
                new BoundaryStep("pressure", BoundaryStep.BoundaryType.Pressure),
                new ProjectStep("velocity", "pressure"),
                new BoundaryStep("velocity", BoundaryStep.BoundaryType.Velocity),

                // Step 6: ADVECT VELOCITY
                new AdvectFieldStep("velocity", "velocity"),
                new BoundaryStep("velocity", BoundaryStep.BoundaryType.Velocity),
                new ClampStep("smoke"),

            };
        }

        public void RestartFluidSimulation(Dictionary<string, float> values)
        {
            if (values.TryGetValue("diffuseIterations", out float diffuseIterations))
                this.diffuseIterations = (int)diffuseIterations;
            if (values.TryGetValue("pressureIterations", out float pressureIterations))
                this.pressureIterations = (int)pressureIterations;

            InitializeRenderTargets();
            CreateSimulationSteps();
        }

        public void SetRenderTarget(RenderTarget2D target)
        {
            _graphicsDevice.SetRenderTarget(target);
        }

        public void LoadContent(Microsoft.Xna.Framework.Content.ContentManager content)
        {
            _fluidEffect = content.Load<Effect>("FluidEffect");
            _fluidEffect.Parameters["renderTargetSize"].SetValue(new Vector2(_gridSize, _gridSize));
        }

        public void Update(GameTime gameTime)
        {
            _fluidEffect.Parameters["timeStep"].SetValue((float)gameTime.ElapsedGameTime.TotalSeconds);
            _fluidEffect.Parameters["diffusion"].SetValue(_diffusion);
            _fluidEffect.Parameters["texelSize"].SetValue(new Vector2(1.0f / _gridSize, 1.0f / _gridSize));
            _fluidEffect.Parameters["sourceStrength"].SetValue(_sourceStrength);
            _fluidEffect.Parameters["uvOffset"].SetValue(UVOffset);

            // Set obstacle texture before simulation steps
            _fluidEffect.Parameters["obstacleTexture"].SetValue(_renderTargetProvider.GetCurrent("obstacle"));

            foreach (var step in _simulationSteps)
            {
                step.Execute(_graphicsDevice, _gridSize, _renderTargetProvider, (float)gameTime.ElapsedGameTime.TotalSeconds);
            }
        }

        public void AddForce(Vector2 position, Vector2 force, float radius)
        {
            var scaledAmount = force * _forceStrength;

            var velocityRT = _renderTargetProvider.GetCurrent("velocity");
            var tempVelocityRT = _renderTargetProvider.GetTemp("velocity");

            // Adjust position to account for UV offset
            Vector2 adjustedPosition = position + UVOffset;
            adjustedPosition = new Vector2(
                adjustedPosition.X - MathF.Floor(adjustedPosition.X),
                adjustedPosition.Y - MathF.Floor(adjustedPosition.Y)
            );

            _fluidEffect.Parameters["sourceTexture"].SetValue(velocityRT);
            _fluidEffect.Parameters["cursorPosition"].SetValue(adjustedPosition);
            _fluidEffect.Parameters["cursorValue"].SetValue(scaledAmount);
            _fluidEffect.Parameters["radius"].SetValue(radius);

            _graphicsDevice.SetRenderTarget(tempVelocityRT);

            _fluidEffect.CurrentTechnique = _fluidEffect.Techniques["AddValue"];

            _fluidEffect.CurrentTechnique.Passes[0].Apply();
            Utils.Utils.DrawFullScreenQuad(_graphicsDevice, _gridSize);


            _graphicsDevice.SetRenderTarget(null);

            _renderTargetProvider.Swap("velocity");
        }

        public void AddSmoke(Vector2 position, float amount, float radius)
        {
            var smokeRT = _renderTargetProvider.GetCurrent("smoke");
            var tempSmokeRT = _renderTargetProvider.GetTemp("smoke");

            // Adjust position to account for UV offset
            Vector2 adjustedPosition = position + UVOffset;
            adjustedPosition = new Vector2(
                adjustedPosition.X - MathF.Floor(adjustedPosition.X),
                adjustedPosition.Y - MathF.Floor(adjustedPosition.Y)
            );

            _fluidEffect.Parameters["sourceTexture"].SetValue(smokeRT);
            _fluidEffect.Parameters["cursorPosition"].SetValue(adjustedPosition);
            _fluidEffect.Parameters["cursorValue"].SetValue(amount);
            _fluidEffect.Parameters["radius"].SetValue(radius);

            _graphicsDevice.SetRenderTarget(tempSmokeRT);

            _fluidEffect.CurrentTechnique = _fluidEffect.Techniques["AddValue"];

            _fluidEffect.CurrentTechnique.Passes[0].Apply();
            Utils.Utils.DrawFullScreenQuad(_graphicsDevice, _gridSize);

            _graphicsDevice.SetRenderTarget(null);

            _renderTargetProvider.Swap("smoke");
        }

        public void SetForce(Vector2 position, Vector2 force, float radius)
        {
            var scaledAmount = force * _forceStrength;

            _fluidEffect.Parameters["sourceTexture"].SetValue(_velocityRT);
            _fluidEffect.Parameters["cursorPosition"].SetValue(position);
            _fluidEffect.Parameters["cursorValue"].SetValue(scaledAmount);
            _fluidEffect.Parameters["radius"].SetValue(radius);

            _graphicsDevice.SetRenderTarget(_renderTargetProvider.GetTemp("velocity"));

            _fluidEffect.CurrentTechnique = _fluidEffect.Techniques["SetValue"];

            _fluidEffect.CurrentTechnique.Passes[0].Apply();
            Utils.Utils.DrawFullScreenQuad(_graphicsDevice, _gridSize);


            _graphicsDevice.SetRenderTarget(null);

            _renderTargetProvider.Swap("velocity");
        }

        public void SetObstacle(Vector2 position, float radius)
        {
            _graphicsDevice.SetRenderTarget(_renderTargetProvider.GetTemp("obstacle"));
            _fluidEffect.Parameters["sourceTexture"].SetValue(_renderTargetProvider.GetCurrent("obstacle"));
            _fluidEffect.Parameters["cursorPosition"].SetValue(position);
            _fluidEffect.Parameters["cursorValue"].SetValue(100.0f);
            _fluidEffect.Parameters["radius"].SetValue(radius);

            _fluidEffect.CurrentTechnique = _fluidEffect.Techniques["SetValue"];

            _fluidEffect.CurrentTechnique.Passes[0].Apply();
            Utils.Utils.DrawFullScreenQuad(_graphicsDevice, _gridSize);

            _graphicsDevice.SetRenderTarget(null);

            _renderTargetProvider.Swap("obstacle");

            _fluidEffect.Parameters["obstacleTexture"].SetValue(_renderTargetProvider.GetCurrent("obstacle"));
        }

        public void SetObstacleTexture(Texture2D obstacleTexture)
        {
            // Directly copy the provided texture to the obstacle render target
            _graphicsDevice.SetRenderTarget(_renderTargetProvider.GetTemp("obstacle"));

            _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, null, null, null, null);
            _spriteBatch.Draw(obstacleTexture, new Rectangle(0, 0, _gridSize, _gridSize), Color.White);
            _spriteBatch.End();

            _graphicsDevice.SetRenderTarget(null);

            _renderTargetProvider.Swap("obstacle");
            _fluidEffect.Parameters["obstacleTexture"].SetValue(_renderTargetProvider.GetCurrent("obstacle"));
        }

        public void Draw(RenderTarget2D renderTarget)
        {
            _graphicsDevice.SetRenderTarget(renderTarget);
            _graphicsDevice.Clear(Color.Transparent);

            if (_fluidEffect == null)
                throw new InvalidOperationException("FluidEffect must be loaded before drawing.");

            _fluidEffect.Parameters["renderTargetSize"].SetValue(new Vector2(_gridSize, _gridSize));

            if (_fluidEffect.Parameters["pressureTexture"] != null)
                _fluidEffect.Parameters["pressureTexture"].SetValue(_renderTargetProvider.GetCurrent("pressure"));

            if (_fluidEffect.Parameters["smokeTexture"] != null)
                _fluidEffect.Parameters["smokeTexture"].SetValue(_renderTargetProvider.GetCurrent("smoke"));

            if (_fluidEffect.Parameters["smokeOpacity"] != null)
                _fluidEffect.Parameters["smokeOpacity"].SetValue(SmokeOpacity);

            _fluidEffect.CurrentTechnique = _fluidEffect.Techniques["Visualize"];
            _fluidEffect.CurrentTechnique.Passes[0].Apply();

            Utils.Utils.DrawFullScreenQuad(_graphicsDevice, _gridSize);

            _graphicsDevice.SetRenderTarget(null);
        }


        /// <summary>
        /// Gets the render target provider for accessing any render target by name.
        /// </summary>
        public IRenderTargetProvider RenderTargetProvider => _renderTargetProvider;

        public void Dispose()
        {
            _velocityRT?.Dispose();
            _pressureRT?.Dispose();
            _pressureRT2?.Dispose();
            _tempVelocityRT?.Dispose();
            _temperatureRT?.Dispose();
            _tempTemperatureRT?.Dispose();
            _divergenceRT?.Dispose();
            _vorticityRT?.Dispose();
            _smokeRT?.Dispose();
            _tempSmokeRT?.Dispose();
            _obstacleRT?.Dispose();
            _tempObstacleRT?.Dispose();
            _spriteBatch?.Dispose();
        }

        private void CreateRenderTargets()
        {
            _velocityRT = new RenderTarget2D(_graphicsDevice, _gridSize, _gridSize, false,
                SurfaceFormat.Vector2, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            _tempVelocityRT = new RenderTarget2D(_graphicsDevice, _gridSize, _gridSize, false,
                SurfaceFormat.Vector2, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            _pressureRT = new RenderTarget2D(_graphicsDevice, _gridSize, _gridSize, false,
                SurfaceFormat.Single, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            _pressureRT2 = new RenderTarget2D(_graphicsDevice, _gridSize, _gridSize, false,
                SurfaceFormat.Single, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            _divergenceRT = new RenderTarget2D(_graphicsDevice, _gridSize, _gridSize, false,
                SurfaceFormat.Single, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            _tempDivergenceRT = new RenderTarget2D(_graphicsDevice, _gridSize, _gridSize, false,
                SurfaceFormat.Single, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            _temperatureRT = new RenderTarget2D(_graphicsDevice, _gridSize, _gridSize, false,
                SurfaceFormat.Single, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            _tempTemperatureRT = new RenderTarget2D(_graphicsDevice, _gridSize, _gridSize, false,
                SurfaceFormat.Single, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            _vorticityRT = new RenderTarget2D(_graphicsDevice, _gridSize, _gridSize, false,
                SurfaceFormat.Vector2, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            _obstacleRT = new RenderTarget2D(_graphicsDevice, _gridSize, _gridSize, false,
                SurfaceFormat.Single, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            _tempObstacleRT = new RenderTarget2D(_graphicsDevice, _gridSize, _gridSize, false,
                SurfaceFormat.Single, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            _smokeRT = new RenderTarget2D(_graphicsDevice, _gridSize, _gridSize, false,
                SurfaceFormat.Single, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            _tempSmokeRT = new RenderTarget2D(_graphicsDevice, _gridSize, _gridSize, false,
                SurfaceFormat.Single, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        }
    }
}
