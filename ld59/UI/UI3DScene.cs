using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;
using ld59.WalkingSim;

namespace ld59.UI;

// Fly = free 6-DOF camera (the 3D scene viewer). Walk = first-person walker constrained to a
// navmesh (the walking sim); movement is flattened to the horizontal plane and height comes
// from the WalkController.
public enum CameraMode { Fly, Walk }

public class UI3DScene : UIElement
{
    private Rectangle _bounds;
    private readonly Scene _scene;
    private readonly RenderTarget2D _renderTarget;
    private readonly Effect _shadowEffect;
    private readonly Effect _dirShadowEffect;
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

    public CameraMode Mode        { get; set; } = CameraMode.Fly;
    public WalkController Walker  { get; set; }

    // Raised when the player presses the interact key while looking at an interactable.
    public event Action<Interactable3DComponent> OnInteract;

    public Scene Scene => _scene;

    private bool _cameraActive = false;
    private float _yaw;
    private float _pitch;
    private bool _anglesInitialized = false;
    private Point _lockCenter;
    private bool _prevLeftPressed = false;
    private bool _prevEscapePressed = false;
    private bool _prevTabPressed = false;

    // ── ID-buffer object picking (Walk mode) ────────────────────────────────────────
    private const int   IdWidth  = 160;
    private const int   IdHeight = 90;
    private const float InteractRange = 2.5f;

    private sealed class InteractInfo
    {
        public Entity Entity;
        public Interactable3DComponent Comp;
        public Mesh3DComponent Mesh;
        public Vector3 BaseColor;
    }

    private RenderTarget2D _idTarget;
    private Effect _idEffect;
    private readonly Color[] _idPixel = new Color[1];
    private int _pickFrame;
    private bool _tablesBuilt;
    private System.Collections.Generic.List<InteractInfo> _interactables;
    private System.Collections.Generic.List<Entity> _meshEntities;
    private InteractInfo _hovered;
    private bool _prevEPressed;

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

        _shadowEffect    = Core.Content.Load<Effect>("shaders/shadow-depth");
        _dirShadowEffect = Core.Content.Load<Effect>("shaders/shadow-depth-dir");
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
        bool tabPressed  = keyboard.IsKeyDown(Keys.Tab);

        if (leftPressed && !_prevLeftPressed && _bounds.Contains(mousePos))
        {
            _cameraActive = true;
            _lockCenter   = new Point(_bounds.X + _bounds.Width / 2, _bounds.Y + _bounds.Height / 2);
            Mouse.SetPosition(_lockCenter.X, _lockCenter.Y);
            Core.Instance.IsMouseVisible = false;
        }

        // Fly releases capture on mouse-up (hold-to-look); Walk keeps capture until released.
        // Tab releases in both modes (Escape can't be used — it quits the game globally).
        bool flyRelease = Mode == CameraMode.Fly && !leftPressed && _prevLeftPressed && _cameraActive;
        bool tabRelease = tabPressed && !_prevTabPressed && _cameraActive;
        if (flyRelease || tabRelease)
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

            if (Mode == CameraMode.Walk && Walker != null)
            {
                // Look is free (full pitch); movement is flattened to the ground plane and
                // driven through the navmesh walker. Height comes from the walker's eye position.
                var flatForward = new Vector3(MathF.Sin(_yaw), 0f, MathF.Cos(_yaw));
                var flatRight   = Vector3.Normalize(Vector3.Cross(flatForward, Vector3.Up));
                var move = Vector3.Zero;
                if (keyboard.IsKeyDown(Keys.W)) move += flatForward;
                if (keyboard.IsKeyDown(Keys.S)) move -= flatForward;
                if (keyboard.IsKeyDown(Keys.A)) move -= flatRight;
                if (keyboard.IsKeyDown(Keys.D)) move += flatRight;

                Walker.MoveSpeed = MoveSpeed;
                Walker.Move(new Vector2(move.X, move.Z), deltaTime);
                CameraPosition = Walker.EyePosition * _scene.SceneScale;
                CameraTarget   = CameraPosition + forward;
            }
            else
            {
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
        }

        _prevLeftPressed   = leftPressed;
        _prevEscapePressed = escPressed;
        _prevTabPressed    = tabPressed;

        var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(deltaTime));
        _scene.Update(gameTime);

        var device = Core.GraphicsDevice;

        // Shadow pass — renders all 6 cube faces
        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState   = RasterizerState.CullNone;
        _scene.DrawShadowPass(device, _shadowEffect, _dirShadowEffect);

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

        if (Mode == CameraMode.Walk && _cameraActive)
            UpdatePicking(device, view, proj, keyboard);
    }

    private void UpdatePicking(GraphicsDevice device, Matrix view, Matrix proj, KeyboardState keyboard)
    {
        EnsurePickResources(device);
        BuildTables();

        // Render the ID buffer and read the crosshair pixel every other frame (GetData forces
        // a GPU sync). Every Mesh3D entity is drawn in its flat id colour; interactables get a
        // non-zero red channel, everything else black — so occlusion is correct via depth.
        if ((_pickFrame++ & 1) == 0)
        {
            device.SetRenderTarget(_idTarget);
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1f, 0);
            device.BlendState        = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.Default;
            device.RasterizerState   = RasterizerState.CullNone;

            _idEffect.CurrentTechnique = _idEffect.Techniques["IdColor"];
            _idEffect.Parameters["LightViewProjection"].SetValue(view * proj);

            foreach (var entity in _meshEntities)
            {
                int id = entity.GetComponent<Interactable3DComponent>()?.Id ?? 0;
                _idEffect.Parameters["IdColor"].SetValue(new Vector4(id / 255f, 0f, 0f, 1f));
                entity.DrawDepth(device, _idEffect, _scene.SceneScale);
            }

            device.SetRenderTarget(null);
            _idTarget.GetData(0, new Rectangle(IdWidth / 2, IdHeight / 2, 1, 1), _idPixel, 0, 1);
            ResolveHover(_idPixel[0].R);
        }

        // Interact edge-detect every frame against the last known hover.
        bool ePressed = keyboard.IsKeyDown(Keys.E);
        if (ePressed && !_prevEPressed && _hovered != null)
            OnInteract?.Invoke(_hovered.Comp);
        _prevEPressed = ePressed;
    }

    private void ResolveHover(int id)
    {
        InteractInfo target = null;
        if (id >= 1 && id <= _interactables.Count)
        {
            var cand = _interactables[id - 1];
            if (Walker != null && Vector3.Distance(Walker.Position, cand.Entity.Position3D) <= InteractRange)
                target = cand;
        }

        if (target == _hovered) return;

        // restore the previously-hovered mesh, then tint the new one
        if (_hovered?.Mesh != null) _hovered.Mesh.DiffuseColor = _hovered.BaseColor;
        if (target?.Mesh != null)   target.Mesh.DiffuseColor   = target.BaseColor * 1.6f;
        _hovered = target;
    }

    private void EnsurePickResources(GraphicsDevice device)
    {
        _idEffect ??= Core.Content.Load<Effect>("shaders/id-color");
        if (_idTarget == null || _idTarget.IsDisposed)
            _idTarget = new RenderTarget2D(device, IdWidth, IdHeight, false,
                SurfaceFormat.Color, DepthFormat.Depth24);
    }

    private void BuildTables()
    {
        if (_tablesBuilt) return;
        _tablesBuilt = true;

        _interactables = new System.Collections.Generic.List<InteractInfo>();
        foreach (var e in _scene.FindEntitiesWithComponent<Interactable3DComponent>())
        {
            var comp = e.GetComponent<Interactable3DComponent>();
            var mesh = e.GetComponent<Mesh3DComponent>();
            comp.Id = _interactables.Count + 1; // 1..255, encoded in the red channel
            _interactables.Add(new InteractInfo
            {
                Entity = e,
                Comp = comp,
                Mesh = mesh,
                BaseColor = mesh?.DiffuseColor ?? Vector3.One,
            });
        }

        _meshEntities = _scene.FindEntitiesWithComponent<Mesh3DComponent>();
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

            if (Mode == CameraMode.Walk && _hovered != null)
            {
                var font   = Core.DefaultFont;
                string text = $"[E] {_hovered.Comp.PromptText}";
                var size   = font.MeasureString(text);
                int tx = cx - (int)(size.X / 2);
                int ty = cy + 24;
                spriteBatch.Draw(_pixel, new Rectangle(tx - 6, ty - 3, (int)size.X + 12, (int)size.Y + 6), Color.Black * 0.6f);
                spriteBatch.DrawString(font, text, new Vector2(tx, ty), Color.White);
            }
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
        if (_hovered?.Mesh != null) _hovered.Mesh.DiffuseColor = _hovered.BaseColor;
        _pixel?.Dispose();
        _renderTarget?.Dispose();
        _idTarget?.Dispose();
    }
}
