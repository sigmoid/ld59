
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;
using ld59.UI;

public class PinballUI : UIPanel
{
    private Rectangle _bounds;
    private Window _rootWindow;

    private Mesh3DComponent _flipperL;
    private Mesh3DComponent _flipperR;
    private Vector3 _flipperLBase;
    private Vector3 _flipperRBase;
    private float _flipperLAngle;
    private float _flipperRAngle;

    private const float FlipperMaxAngle      = 0.8f;
    private const float FlipperActivateSpeed = 22f;
    private const float FlipperReturnSpeed   = 13f;

    private PinballEngine _engine;
    private UI3DScene _sceneView;
    private PinballDebugPanel _debugView;
    private bool _debugMode;
    private bool _prevTabPressed;

    public PinballUI(Rectangle bounds)
    {
        _bounds = bounds;
        CreateUI();
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        _bounds = bounds;
    }

    public override Rectangle GetBoundingBox() => _bounds;

    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);

        var kb = Keyboard.GetState();

        bool tabDown = kb.IsKeyDown(Keys.Tab);
        if (tabDown && !_prevTabPressed)
        {
            _debugMode = !_debugMode;
            _sceneView.SetVisibility(!_debugMode);
            _debugView.SetVisibility(_debugMode);
        }
        _prevTabPressed = tabDown;

        _engine.Update(deltaTime);

        if (!_debugMode)
        {
            float leftSpeed  = kb.IsKeyDown(Keys.Z)          ? FlipperActivateSpeed : -FlipperReturnSpeed;
            float rightSpeed = kb.IsKeyDown(Keys.OemPeriod)  ? FlipperActivateSpeed : -FlipperReturnSpeed;

            _flipperLAngle = MathHelper.Clamp(_flipperLAngle + leftSpeed  * deltaTime, 0f, FlipperMaxAngle);
            _flipperRAngle = MathHelper.Clamp(_flipperRAngle + rightSpeed * deltaTime, 0f, FlipperMaxAngle);

            if (_flipperL != null)
                _flipperL.RotationEuler = _flipperLBase with { Y = _flipperLBase.Y + _flipperLAngle };
            if (_flipperR != null)
                _flipperR.RotationEuler = _flipperRBase with { Y = _flipperRBase.Y - _flipperRAngle };
        }
    }

    private void CreateUI()
    {
        _rootWindow = new Window(_bounds, "Pinball", Core.DefaultFont);
        _rootWindow.SetColors(ColorPalette.ActualWhite, ColorPalette.Black, ColorPalette.ActualWhite, ColorPalette.Black);
        _rootWindow.SetCloseButtonColors(ColorPalette.Black, Color.DarkGray);
        TaskbarRegistry.Register("Pinball", Core.Content.Load<Texture2D>("images/pinball_icon"), _rootWindow);

        var cb = _rootWindow.GetContentBounds();

        // --- 3D scene view ---
        var scene = Scene.FromFile(Core.Content, "files/scenes/pinball.xml");
        scene.SceneScale        = 0.01f;
        scene.AmbientLightColor = new Color(40, 40, 40);
        scene.LightingEnabled   = true;

        var cameraPos    = new Vector3(0f, 8f, 10f);
        var cameraTarget = new Vector3(0f, 0f, 0f);
        var cameraFov    = MathHelper.PiOver4;
        var cameraEntities = scene.FindEntitiesWithComponent<SceneCameraComponent>();
        if (cameraEntities.Count > 0)
        {
            var camEntity = cameraEntities[0];
            var cam       = camEntity.GetComponent<SceneCameraComponent>();
            cameraPos    = camEntity.Position3D * scene.SceneScale;
            cameraTarget = cam.Target * scene.SceneScale;
            cameraFov    = cam.FieldOfView;
            scene.RemoveEntity(camEntity);
        }

        var flipperLEnt = scene.FindEntityByName("Flipper_L");
        var flipperREnt = scene.FindEntityByName("Flipper_R");
        _flipperL = flipperLEnt?.GetComponent<Mesh3DComponent>();
        _flipperR = flipperREnt?.GetComponent<Mesh3DComponent>();
        if (_flipperL != null) _flipperLBase = _flipperL.RotationEuler;
        if (_flipperR != null) _flipperRBase = _flipperR.RotationEuler;

        _sceneView = new UI3DScene(cb, scene)
        {
            CameraPosition = cameraPos,
            CameraTarget   = cameraTarget,
            FieldOfView    = cameraFov,
        };
        _rootWindow.AddChild(_sceneView);

        // --- Debug view ---
        _engine   = CreateTestbedEngine(cb);
        _debugView = new PinballDebugPanel(cb, _engine);
        _debugView.SetVisibility(false);
        _rootWindow.AddChild(_debugView);

        Core.UISystem.AddElement(_rootWindow);
    }

    private static PinballEngine CreateTestbedEngine(Rectangle bounds)
    {
        var tableSize = new Vector2(bounds.Width, bounds.Height);
        var table = PinballTableLoader.Load("scenes/pinball_table.json", tableSize);
        table.AddBall(new PinballBall(10, tableSize * 0.5f));
        return new PinballEngine(table);
    }

    private sealed class PinballDebugPanel : UIElement
    {
        private Rectangle _bounds;
        private readonly PinballEngine _engine;
        private readonly Texture2D _bg;

        public PinballDebugPanel(Rectangle bounds, PinballEngine engine)
        {
            _bounds = bounds;
            _engine = engine;
            _bg = new Texture2D(Core.GraphicsDevice, 1, 1);
            _bg.SetData(new[] { Color.White });
        }

        public override Rectangle GetBoundingBox() => _bounds;
        public override void SetBounds(Rectangle bounds) => _bounds = bounds;

        public override void Update(float deltaTime)
        {
            if (Mouse.GetState().LeftButton != ButtonState.Pressed) return;
            var pt = Core.GetTransformedMousePoint();
            _engine.PlaceBalls(new Vector2(pt.X - _bounds.X, pt.Y - _bounds.Y));
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            spriteBatch.Draw(_bg, _bounds, null, new Color(15, 15, 25), 0f, Vector2.Zero, SpriteEffects.None, GetActualOrder());
            _engine.DebugDraw(spriteBatch, new Vector2(_bounds.X, _bounds.Y));
        }
    }
}
