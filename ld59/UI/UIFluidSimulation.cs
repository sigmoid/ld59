using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;
using crash.FluidSimulation;

namespace ld59.UI
{
    public class UIFluidSimulation : UIElement
    {
        private FluidSimulator _simulator;
        private RenderTarget2D _outputRT;
        private Rectangle _bounds;

        private Vector2 _prevMouseUV;
        private bool _showVelocity;
        private KeyboardState _prevKeyboard;

        public FluidSimulator Simulator => _simulator;

        public UIFluidSimulation(Rectangle bounds, int gridSize = 256)
        {
            _bounds = bounds;
            _simulator = new FluidSimulator(Core.GraphicsDevice, gridSize);
            _simulator.LoadContent(Core.Content);
            _simulator.SmokeOpacity = 255f;
            _outputRT = new RenderTarget2D(
                Core.GraphicsDevice, gridSize, gridSize,
                false, SurfaceFormat.Color, DepthFormat.None, 0,
                RenderTargetUsage.PreserveContents);
        }

        public override void Update(float deltaTime)
        {
            var mouseState = Mouse.GetState();
            var mousePos = Core.GetTransformedMousePosition();

            var uv = new Vector2(
                (mousePos.X - _bounds.X) / _bounds.Width,
                (mousePos.Y - _bounds.Y) / _bounds.Height);

            if (deltaTime > 0f)
            {
                var delta = EaseOutQuartForce((uv - _prevMouseUV) / deltaTime, 32f);
                _simulator.AddForceDiffuse(_prevMouseUV, delta * 4f, 0.035f);
            }
            _prevMouseUV = uv;

            var keyboard = Keyboard.GetState();
            if (keyboard.IsKeyDown(Keys.V) && !_prevKeyboard.IsKeyDown(Keys.V))
                _showVelocity = !_showVelocity;
            _prevKeyboard = keyboard;

            var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(deltaTime));
            _simulator.Update(gameTime);
            if (_showVelocity)
                _simulator.DrawVelocity(_outputRT);
            else
                _simulator.Draw(_outputRT);
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (_outputRT != null && !_outputRT.IsDisposed)
                spriteBatch.Draw(_outputRT, _bounds, Color.White);
        }

        public override Rectangle GetBoundingBox() => _bounds;

        public override void SetBounds(Rectangle bounds) => _bounds = bounds;

        private static Vector2 EaseOutQuartForce(Vector2 v, float maxMag)
        {
            float mag = v.Length();
            if (mag < 1e-5f) return Vector2.Zero;
            float t = MathF.Min(mag / maxMag, 1f);
            float eased = 1f - MathF.Pow(1f - t, 4f);
            return v / mag * eased * maxMag;
        }

        public override void OnRemovedFromUI()
        {
            _simulator?.Dispose();
            _outputRT?.Dispose();
        }
    }

    // Ticks a UIFluidSimulation every frame without rendering it to screen.
    // Used to pre-warm the simulation during the splash/spinner sequence.
    public class FluidPrewarmer(UIFluidSimulation fluidSim, Vector2 smokeCenter) : UIElement
    {
        private float _smokeTimer;

        public override void Update(float deltaTime)
        {
            _smokeTimer += deltaTime * 2;
            var smokeOffset = new Vector2(
                (float)Math.Sin(_smokeTimer) * 0.01f,
                (float)Math.Cos(_smokeTimer * 1.5f) * 0.01f);
            fluidSim.Simulator.AddSmoke(smokeCenter + smokeOffset, 10f * deltaTime, 0.02f);
            fluidSim.Update(deltaTime);
        }

        public override void Draw(SpriteBatch spriteBatch) { }

        public override Rectangle GetBoundingBox() => Rectangle.Empty;
    }
}
