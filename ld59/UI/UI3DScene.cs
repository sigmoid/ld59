using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;

namespace ld59.UI;

public class UI3DScene : UIElement
{
    private Rectangle _bounds;
    private readonly Scene _scene;
    private readonly RenderTarget2D _renderTarget;
    private readonly Effect _shadowEffect;
    private readonly Texture2D _pixel;
    private readonly int _rtWidth;
    private readonly int _rtHeight;

    public Vector3 CameraPosition { get; set; } = new Vector3(0, 0, 5);
    public Vector3 CameraTarget   { get; set; } = Vector3.Zero;
    public float FieldOfView      { get; set; } = MathHelper.PiOver4;
    public float NearPlane        { get; set; } = 0.1f;
    public float FarPlane         { get; set; } = 1000f;
    public float MoveSpeed        { get; set; } = 3f;
    public float LookSensitivity  { get; set; } = 0.002f;

    public Scene Scene => _scene;

    private bool _cameraActive = false;
    private float _yaw;
    private float _pitch;
    private bool _anglesInitialized = false;
    private Point _lockCenter;
    private bool _prevLeftPressed = false;
    private bool _prevEscapePressed = false;

    public UI3DScene(Rectangle bounds, Scene scene = null)
    {
        _bounds  = bounds;
        _rtWidth  = bounds.Width;
        _rtHeight = bounds.Height;

        _scene = scene ?? new Scene();
        _scene.InitializeEntities();

        _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);

        _renderTarget = new RenderTarget2D(
            Core.GraphicsDevice,
            _rtWidth, _rtHeight,
            false, SurfaceFormat.Color,
            DepthFormat.Depth24, 0,
            RenderTargetUsage.PreserveContents);

        _shadowEffect = Core.Content.Load<Effect>("shaders/shadow-depth");
    }

    private void InitializeCameraAngles()
    {
        var dir = Vector3.Normalize(CameraTarget - CameraPosition);
        _pitch = MathF.Asin(MathHelper.Clamp(dir.Y, -1f, 1f));
        _yaw = MathF.Atan2(dir.X, dir.Z);
        _anglesInitialized = true;
    }

    public override void Update(float deltaTime)
    {
        if (!_anglesInitialized) InitializeCameraAngles();

        var mouse    = Mouse.GetState();
        var keyboard = Keyboard.GetState();
        var mousePos = new Point(mouse.X, mouse.Y);

        bool leftPressed = mouse.LeftButton == ButtonState.Pressed;
        bool escPressed  = keyboard.IsKeyDown(Keys.Escape);

        if (leftPressed && !_prevLeftPressed && _bounds.Contains(mousePos))
        {
            _cameraActive = true;
            _lockCenter   = new Point(_bounds.X + _bounds.Width / 2, _bounds.Y + _bounds.Height / 2);
            Mouse.SetPosition(_lockCenter.X, _lockCenter.Y);
            Core.Instance.IsMouseVisible = false;
        }

        if ((!leftPressed && _prevLeftPressed && _cameraActive) ||
            (escPressed && !_prevEscapePressed && _cameraActive))
        {
            _cameraActive = false;
            Core.Instance.IsMouseVisible = true;
        }

        if (_cameraActive)
        {
            var delta = new Vector2(mouse.X - _lockCenter.X, mouse.Y - _lockCenter.Y);
            Mouse.SetPosition(_lockCenter.X, _lockCenter.Y);

            _yaw   -= delta.X * LookSensitivity;
            _pitch -= delta.Y * LookSensitivity;
            _pitch  = MathHelper.Clamp(_pitch, -MathHelper.PiOver2 + 0.01f, MathHelper.PiOver2 - 0.01f);

            var forward = new Vector3(
                MathF.Cos(_pitch) * MathF.Sin(_yaw),
                MathF.Sin(_pitch),
                MathF.Cos(_pitch) * MathF.Cos(_yaw));

            var right   = Vector3.Normalize(Vector3.Cross(forward, Vector3.Up));
            var moveDir = Vector3.Zero;
            if (keyboard.IsKeyDown(Keys.W)) moveDir += forward;
            if (keyboard.IsKeyDown(Keys.S)) moveDir -= forward;
            if (keyboard.IsKeyDown(Keys.A)) moveDir -= right;
            if (keyboard.IsKeyDown(Keys.D)) moveDir += right;

            if (moveDir.LengthSquared() > 0)
                moveDir = Vector3.Normalize(moveDir);

            CameraPosition += moveDir * MoveSpeed * deltaTime;
            CameraTarget    = CameraPosition + forward;
        }

        _prevLeftPressed   = leftPressed;
        _prevEscapePressed = escPressed;

        var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(deltaTime));
        _scene.Update(gameTime);

        var device = Core.GraphicsDevice;

        // Shadow pass — renders all 6 cube faces
        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState   = RasterizerState.CullNone;
        _scene.DrawShadowPass(device, _shadowEffect);

        // Main scene pass
        device.SetRenderTarget(_renderTarget);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1f, 0);
        device.BlendState        = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState   = RasterizerState.CullNone;
        device.SamplerStates[0]  = SamplerState.LinearWrap;

        var view = Matrix.CreateLookAt(CameraPosition, CameraTarget, Vector3.Up);
        var proj = Matrix.CreatePerspectiveFieldOfView(
            FieldOfView, (float)_rtWidth / _rtHeight, NearPlane, FarPlane);

        _scene.Draw3D(device, view, proj);

        device.SetRenderTarget(null);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Draw(_renderTarget, _bounds, Color.White);

        if (_cameraActive)
        {
            int cx = _bounds.X + _bounds.Width  / 2;
            int cy = _bounds.Y + _bounds.Height / 2;
            spriteBatch.Draw(_pixel, new Rectangle(cx - 8, cy - 1, 16, 2), Color.White * 0.8f);
            spriteBatch.Draw(_pixel, new Rectangle(cx - 1, cy - 8, 2, 16), Color.White * 0.8f);
        }
    }

    public override Rectangle GetBoundingBox() => _bounds;

    public override void SetBounds(Rectangle bounds) => _bounds = bounds;

    public override void OnRemovedFromUI()
    {
        if (_cameraActive)
        {
            Core.Instance.IsMouseVisible = true;
            _cameraActive = false;
        }
        _pixel?.Dispose();
        _renderTarget?.Dispose();
    }
}
