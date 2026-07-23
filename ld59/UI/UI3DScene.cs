using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;
using ld59.WalkingSim;
using ld59.UI.Editor;
using ld59.UI.Editor.Commands;
using ld59.UI.Editor.Gizmos;
using Quartz.EntityComponentScene.Serialization;

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
    public float NearPlane        { get; set; } = 1.0f;
    public float FarPlane         { get; set; } = 70000f;
    public float MoveSpeed        { get; set; } = 3f;
    // Shift-to-boost multiplier for the free-fly (editor) camera.
    public float FlyBoostMultiplier { get; set; } = 6f;
    public float LookSensitivity  { get; set; } = 0.002f;

    public CameraMode Mode        { get; set; } = CameraMode.Fly;
    public WalkController Walker  { get; set; }

    // Raised when the player presses the interact key while looking at an interactable.
    public event Action<Interactable3DComponent> OnInteract;

    public Scene Scene => _scene;

    // Procedural moon skybox (stars + Earth + Sun). Off by default; enabled per scene.
    public bool ShowSkybox { get; set; }
    private SkyboxRenderer _skybox;

    // Debug navmesh overlay (toggle N) and free-fly camera (toggle F) for inspecting the level.
    public bool ShowNavMesh { get; set; }
    private NavMeshDebugRenderer _navDebug;
    private bool _prevNavKey;
    private bool _prevFlyKey;
    private bool _prevEditorKey;
    private bool _prevLookPressed;
    private bool _prevDeleteKey;

    // Current editor selection. The inspector subscribes to OnSelectionChanged.
    public Entity SelectedEntity { get; private set; }
    public event Action<Entity> OnSelectionChanged;
    // Fired every frame while an entity is selected in editor mode (not just on selection
    // change), so live-value displays (the inspector) can track an in-progress gizmo drag.
    public event Action<Entity> OnEntityLiveUpdate;
    private Mesh3DComponent _selectedMesh;
    private Vector3 _selectedBaseColor;

    // Fired whenever the scene's entity list changes (delete, undo/redo of add/delete,
    // placement) so panels like the hierarchy list know to rebuild.
    public event Action OnSceneChanged;

    // Editor mode (toggle F2): forces free-fly and enables in-game authoring (bake with B).
    public bool EditorMode { get; set; }
    public event Action<bool> OnEditorModeChanged;
    // Absolute path the baked navmesh OBJ is written to (Content source dir). If null, bake
    // still updates the live mesh + overlay but doesn't persist to disk.
    public string NavMeshSavePath { get; set; }
    // Absolute path Ctrl+S writes the scene XML to (Content source dir). If null, save is a no-op.
    public string ScenePath { get; set; }

    // Undo/redo. All editor mutations (inspector edits, transforms, add/delete) route through
    // this so Ctrl+Z/Ctrl+Y work uniformly.
    public EditorHistory History { get; } = new EditorHistory();
    private bool _prevUndoKey, _prevRedoKey, _prevSaveKey;

    // Move/rotate/scale handles for the selection (Q/W/E/R picks the mode). Optional callback
    // so the host UI can report whether a text field (e.g. the inspector) currently has focus --
    // letter hotkeys (Q/W/E/R/N/F/B) and Delete are suppressed while typing.
    public TransformGizmo Gizmo { get; }
    public Func<bool> IsTextInputFocused { get; set; }
    private bool _prevGizmoNoneKey, _prevGizmoMoveKey, _prevGizmoRotKey, _prevGizmoScaleKey;
    private bool _prevPlayKey;

    // Billboard icons marking non-mesh entities (lights, PlayerStart) in editor mode, so they're
    // visible and click-selectable even though they have no Mesh3D geometry of their own.
    private readonly BillboardGizmoRenderer _billboards;
    private const float BillboardSize = 0.6f;

    private bool _cameraActive = false;
    private float _yaw;
    private float _pitch;
    private bool _anglesInitialized = false;
    private Point _lockCenter;
    private bool _prevLeftPressed = false;
    private bool _prevEscapePressed = false;
    private bool _prevTabPressed = false;
    private bool _captureSuspended = false;

    // Release the mouse and stop re-capturing on click, so a modal (e.g. the puzzle solve view)
    // can use the cursor. Restore with ResumeCapture.
    public void SuspendCapture()
    {
        _captureSuspended = true;
        _cameraActive = false;
        Core.Instance.IsMouseVisible = true;
    }

    public void ResumeCapture()
    {
        _captureSuspended = false;
    }

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
    private System.Collections.Generic.List<Entity> _puzzlePanels;
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

        Gizmo = new TransformGizmo(Core.GraphicsDevice);
        _billboards = new BillboardGizmoRenderer(Core.GraphicsDevice);
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

        // Finalize a background navmesh bake on the main thread once Recast has finished.
        FinishBake();

        var mouse    = Mouse.GetState();
        var keyboard = Keyboard.GetState();
        var mousePos = new Point(mouse.X, mouse.Y);

        bool leftPressed  = mouse.LeftButton  == ButtonState.Pressed;
        bool rightPressed = mouse.RightButton == ButtonState.Pressed;
        bool escPressed   = keyboard.IsKeyDown(Keys.Escape);
        bool tabPressed   = keyboard.IsKeyDown(Keys.Tab);

        // Suppress letter/Delete hotkeys while a text field (e.g. the inspector's Name box) has
        // focus, so typing "w" or pressing Delete edits the text instead of firing a hotkey.
        bool textFocused = IsTextInputFocused?.Invoke() ?? false;

        // In editor mode, look with the RIGHT button (Unreal-style) so LEFT-click is free for
        // selecting objects and clicking the inspector. Otherwise (Walk/Fly) look with left.
        bool lookPressed = EditorMode ? rightPressed : leftPressed;

        // Editor mode (F2): forces free-fly so you can author from a detached camera.
        bool editorKey = keyboard.IsKeyDown(Keys.F2);
        if (editorKey && !_prevEditorKey)
        {
            EditorMode = !EditorMode;
            Mode = EditorMode ? CameraMode.Fly : CameraMode.Walk;
            if (EditorMode) ShowNavMesh = true;
            OnEditorModeChanged?.Invoke(EditorMode);
        }
        _prevEditorKey = editorKey;

        // Editor mode always stays in free-fly -- pin this every frame so nothing (the F toggle
        // below, or anything else) can leave it in Walk mode while EditorMode is on. Walk mode
        // has different mouse-capture semantics (held until Tab, not released on mouse-up), so
        // leaking into it here is what caused the mouse to get stuck captured in the editor.
        if (EditorMode) Mode = CameraMode.Fly;

        // Debug toggles: N overlays the navmesh, F detaches into free-fly (and back to Walk).
        // The F toggle is disabled in editor mode -- the editor camera is always Fly.
        bool navKey = keyboard.IsKeyDown(Keys.N);
        if (navKey && !_prevNavKey && !textFocused) ShowNavMesh = !ShowNavMesh;
        _prevNavKey = navKey;

        bool flyKey = keyboard.IsKeyDown(Keys.F);
        if (flyKey && !_prevFlyKey && !textFocused && !EditorMode && Walker != null)
            Mode = Mode == CameraMode.Walk ? CameraMode.Fly : CameraMode.Walk;
        _prevFlyKey = flyKey;

        // Delete: remove the selected entity from the scene.
        bool deleteKey = keyboard.IsKeyDown(Keys.Delete);
        if (deleteKey && !_prevDeleteKey && EditorMode && !textFocused && SelectedEntity != null)
            DeleteSelected();
        _prevDeleteKey = deleteKey;

        // Ctrl+Z / Ctrl+Y: undo/redo. Ctrl+S: save the scene XML to its source path.
        bool ctrlHeld = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        bool undoKey  = ctrlHeld && keyboard.IsKeyDown(Keys.Z);
        bool redoKey  = ctrlHeld && keyboard.IsKeyDown(Keys.Y);
        bool saveKey  = ctrlHeld && keyboard.IsKeyDown(Keys.S);
        if (EditorMode && !textFocused)
        {
            // Selection may point at an entity a delete/add command just disposed; drop it so we
            // never hold a dangling reference across an undo/redo that changed entity identity.
            if (undoKey && !_prevUndoKey && History.CanUndo)
            { Select(null); History.Undo(); _tablesBuilt = false; OnSceneChanged?.Invoke(); }
            if (redoKey && !_prevRedoKey && History.CanRedo)
            { Select(null); History.Redo(); _tablesBuilt = false; OnSceneChanged?.Invoke(); }
            if (saveKey && !_prevSaveKey) SaveScene();
        }
        _prevUndoKey = undoKey;
        _prevRedoKey = redoKey;
        _prevSaveKey = saveKey;

        // Q/W/E/R pick the gizmo mode (none/translate/rotate/scale). Only live while the camera
        // isn't actively looking -- movement (WASD) only applies during a look-drag too, so the
        // two never compete for the same keys.
        bool gizmoNoneKey  = keyboard.IsKeyDown(Keys.Q);
        bool gizmoMoveKey  = keyboard.IsKeyDown(Keys.W);
        bool gizmoRotKey   = keyboard.IsKeyDown(Keys.E);
        bool gizmoScaleKey = keyboard.IsKeyDown(Keys.R);
        if (EditorMode && !textFocused && !_cameraActive)
        {
            if (gizmoNoneKey  && !_prevGizmoNoneKey)  Gizmo.Mode = GizmoMode.None;
            if (gizmoMoveKey  && !_prevGizmoMoveKey)  Gizmo.Mode = GizmoMode.Translate;
            if (gizmoRotKey   && !_prevGizmoRotKey)   Gizmo.Mode = GizmoMode.Rotate;
            if (gizmoScaleKey && !_prevGizmoScaleKey) Gizmo.Mode = GizmoMode.Scale;
        }
        _prevGizmoNoneKey  = gizmoNoneKey;
        _prevGizmoMoveKey  = gizmoMoveKey;
        _prevGizmoRotKey   = gizmoRotKey;
        _prevGizmoScaleKey = gizmoScaleKey;

        // P: start the game from the current camera position (spawns the walker there, snapping
        // to the nearest navmesh point if the camera itself isn't over walkable ground).
        bool playKey = keyboard.IsKeyDown(Keys.P);
        if (playKey && !_prevPlayKey && EditorMode && !textFocused)
            StartGameFromCamera();
        _prevPlayKey = playKey;

        // Editor: left-click (while not looking) either grabs a gizmo handle or picks the entity
        // under the cursor. While a handle is held, drag it; on release, commit the transform.
        if (EditorMode && !_cameraActive)
        {
            var editView = Matrix.CreateLookAt(CameraPosition, CameraTarget, Vector3.Up);
            var editProj = Matrix.CreatePerspectiveFieldOfView(
                FieldOfView, (float)_rtWidth / _rtHeight, NearPlane, FarPlane);

            if (leftPressed && !_prevLeftPressed && _bounds.Contains(mousePos))
            {
                if (!TryBeginGizmoDrag(mousePos, editView, editProj))
                    PickSelection(mousePos);
            }
            else if (Gizmo.IsDragging)
            {
                if (leftPressed)
                    Gizmo.UpdateDrag(ScreenRay(mousePos, editView, editProj), mousePos);
                else
                    Gizmo.EndDrag(History);
            }
        }

        // Fires every frame the editor has a selection, so panels showing live values (the
        // inspector's Position/Scale/etc. text) stay in sync while a gizmo drag is moving the
        // entity -- OnSelectionChanged only fires when the SELECTION itself changes, not while
        // the selected entity's own properties are being edited.
        if (EditorMode && SelectedEntity != null)
            OnEntityLiveUpdate?.Invoke(SelectedEntity);

        if (!_captureSuspended && lookPressed && !_prevLookPressed && _bounds.Contains(mousePos))
        {
            _cameraActive = true;
            _lockCenter   = new Point(_bounds.X + _bounds.Width / 2, _bounds.Y + _bounds.Height / 2);
            Mouse.SetPosition(_lockCenter.X, _lockCenter.Y);
            Core.Instance.IsMouseVisible = false;
        }

        // Fly releases capture on look-button-up (hold-to-look); Walk keeps capture until released.
        // Tab releases in both modes (Escape can't be used — it quits the game globally).
        bool flyRelease = Mode == CameraMode.Fly && !lookPressed && _prevLookPressed && _cameraActive;
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

                // Hold Shift to fly much faster (handy for crossing a large level in the editor).
                bool boost = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
                float speed = MoveSpeed * (boost ? FlyBoostMultiplier : 1f);
                CameraPosition += moveDir * speed * deltaTime;
                CameraTarget    = CameraPosition + forward;
            }
        }

        _prevLeftPressed   = leftPressed;
        _prevLookPressed   = lookPressed;
        _prevEscapePressed = escPressed;
        _prevTabPressed    = tabPressed;

        var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(deltaTime));
        _scene.Update(gameTime);

        var device = Core.GraphicsDevice;

        // Puzzle-panel surface pass — render each puzzle to its own texture before the main pass.
        if (Mode == CameraMode.Walk)
        {
            _puzzlePanels ??= _scene.FindEntitiesWithComponent<PuzzlePanelComponent>();
            foreach (var e in _puzzlePanels)
                if (e.Visible)
                    e.GetComponent<PuzzlePanelComponent>().RenderSurface(device, Core.SpriteBatch);
        }

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

        // Procedural skybox: fills the background before the scene draws over it. Sync the sun to
        // the scene's directional light so the sky's sun matches the lighting direction.
        if (ShowSkybox)
        {
            _skybox ??= new SkyboxRenderer(device);
            var dl = _scene.FindEntitiesWithComponent<DirectionalLightComponent>();
            if (dl.Count > 0)
                _skybox.SunDir = dl[0].GetComponent<DirectionalLightComponent>().Direction;
            _skybox.Draw(device, view, proj, CameraPosition);

            // Skybox disabled depth + set its own states; restore before the scene draws.
            device.DepthStencilState = DepthStencilState.Default;
            device.BlendState        = BlendState.Opaque;
            device.RasterizerState   = RasterizerState.CullNone;
        }

        _scene.Draw3D(device, view, proj);

        if (EditorMode)
        {
            BuildTables();
            foreach (var e in _meshEntities)
            {
                if (!e.Visible || e.GetComponent<Mesh3DComponent>() != null) continue;
                bool isSelected = ReferenceEquals(e, SelectedEntity);
                var color = BillboardColorFor(e) * (isSelected ? 1.6f : 1f);
                _billboards.Draw(device, e.Position3D * _scene.SceneScale, CameraPosition,
                    BillboardSize, view, proj, color);
            }
        }

        if (ShowNavMesh && Walker?.Mesh != null)
        {
            _navDebug ??= new NavMeshDebugRenderer(device, Walker.Mesh);
            _navDebug.Draw(device, Matrix.CreateScale(_scene.SceneScale), view, proj);
        }

        if (EditorMode && SelectedEntity != null && Gizmo.HasValidTarget(SelectedEntity))
            Gizmo.Draw(device, SelectedEntity, CameraPosition, view, proj);

        device.SetRenderTarget(null);

        if (Mode == CameraMode.Walk && _cameraActive)
            UpdatePicking(device, view, proj, keyboard);
    }

    // Last bake outcome, for the navmesh panel to display.
    public int LastBakeSourceTris { get; private set; }
    public int LastBakeNavTris { get; private set; }
    public string LastBakeError { get; private set; }
    public bool IsBaking => _bakeTask != null;
    public event Action OnBakeStarted;
    public event Action OnBakeCompleted;
    private System.Threading.Tasks.Task<SceneNavBaker.Result> _bakeTask;
    private int _bakeSourceTris;

    // In-process navmesh bake. Geometry is gathered on the main thread (GPU vertex-buffer readback
    // isn't thread-safe), then Recast -- the slow part -- runs on a background thread so the game
    // keeps rendering. FinishBake() (polled from Update) rebinds the walker, rebuilds the overlay's
    // GPU buffers, and persists the OBJ once the background work completes. Public so the navmesh
    // panel's Bake button and the B hotkey can both trigger it.
    public void BakeNavMesh()
    {
        if (Walker == null || IsBaking) return;

        NavMeshBaker.TriangleSoup soup;
        try
        {
            soup = SceneNavBaker.GatherSoup(_scene, _scene.SceneScale, out _bakeSourceTris);
        }
        catch (Exception ex)
        {
            LastBakeError = ex.Message;
            Console.WriteLine($"[bake] failed (gather): {ex.Message}");
            OnBakeCompleted?.Invoke();
            return;
        }

        var p = NavMeshBaker.BakeParams.Default;
        int sourceTris = _bakeSourceTris;
        Console.WriteLine("[bake] running Recast on a background thread...");
        OnBakeStarted?.Invoke();
        _bakeTask = System.Threading.Tasks.Task.Run(() => SceneNavBaker.BakeFromSoup(soup, p, sourceTris));
    }

    // Poll the background bake and finalize on the main thread when it's done. Called each frame.
    private void FinishBake()
    {
        if (_bakeTask == null || !_bakeTask.IsCompleted) return;

        var task = _bakeTask;
        _bakeTask = null;   // clears IsBaking

        if (task.IsFaulted)
        {
            LastBakeError = task.Exception?.GetBaseException().Message ?? "unknown error";
            Console.WriteLine($"[bake] failed: {LastBakeError}");
            OnBakeCompleted?.Invoke();
            return;
        }

        var result = task.Result;
        Walker.Rebind(result.NavMesh);

        _navDebug?.Dispose();
        _navDebug = new NavMeshDebugRenderer(Core.GraphicsDevice, result.NavMesh);
        ShowNavMesh = true;

        LastBakeSourceTris = result.SourceTris;
        LastBakeNavTris    = result.NavMesh.Triangles.Length;
        LastBakeError      = null;

        if (!string.IsNullOrEmpty(NavMeshSavePath))
        {
            SceneNavBaker.WriteObj(result.NavSoup, NavMeshSavePath);
            Console.WriteLine($"[bake] {result.SourceTris} src tris -> {result.NavMesh.Triangles.Length} nav tris; wrote {NavMeshSavePath}");
        }
        else
        {
            Console.WriteLine($"[bake] {result.SourceTris} src tris -> {result.NavMesh.Triangles.Length} nav tris (not persisted)");
        }
        OnBakeCompleted?.Invoke();
    }

    // Migration/quick-start helper: tag every untagged Mesh3D entity as a navmesh obstacle, so a
    // scene authored before NavMeshObstacleComponent existed (or a freshly placed prop) can be
    // baked immediately. Not routed through EditorHistory -- it's a one-time bulk action, not a
    // single edit worth undoing entity-by-entity.
    public int TagAllMeshesAsObstacles()
    {
        int tagged = 0;
        foreach (var e in _scene.FindEntitiesWithComponent<Mesh3DComponent>())
        {
            if (e.GetComponent<NavMeshObstacleComponent>() != null) continue;
            e.AddComponent(new NavMeshObstacleComponent());
            tagged++;
        }
        return tagged;
    }

    // Build a world-space ray through the given screen point, using the render target's own
    // pixel dimensions (not the game window's) since mouse coords are in window space and the
    // viewport we're unprojecting against is the offscreen render target. This is plain
    // ray/plane-vs-camera math for gizmo dragging -- not a scene raycast, so it needs no
    // mesh/navmesh intersection support.
    private Ray ScreenRay(Point mousePos, Matrix view, Matrix proj)
    {
        var viewport = new Viewport(0, 0, _rtWidth, _rtHeight);
        float px = mousePos.X - _bounds.X;
        float py = mousePos.Y - _bounds.Y;
        Vector3 near = viewport.Unproject(new Vector3(px, py, 0f), proj, view, Matrix.Identity);
        Vector3 far  = viewport.Unproject(new Vector3(px, py, 1f), proj, view, Matrix.Identity);
        return new Ray(near, Vector3.Normalize(far - near));
    }

    // Renders just the gizmo's handles (not entities) into a freshly cleared ID buffer, ignoring
    // depth so a handle is pickable exactly where it's visible (always on top). Returns true and
    // starts a drag if a handle was hit.
    private bool TryBeginGizmoDrag(Point mousePos, Matrix view, Matrix proj)
    {
        if (SelectedEntity == null || !Gizmo.HasValidTarget(SelectedEntity)) return false;

        var device = Core.GraphicsDevice;
        EnsurePickResources(device);

        device.SetRenderTarget(_idTarget);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1f, 0);
        device.BlendState = BlendState.Opaque;
        Gizmo.DrawForPicking(device, SelectedEntity, CameraPosition, view, proj);
        device.SetRenderTarget(null);

        float u = MathHelper.Clamp((mousePos.X - _bounds.X) / (float)_bounds.Width,  0f, 0.999f);
        float v = MathHelper.Clamp((mousePos.Y - _bounds.Y) / (float)_bounds.Height, 0f, 0.999f);
        int px = (int)(u * IdWidth), py = (int)(v * IdHeight);
        _idTarget.GetData(0, new Rectangle(px, py, 1, 1), _idPixel, 0, 1);

        var axis = TransformGizmo.DecodeAxis(DecodeId(_idPixel[0]));
        if (axis == GizmoAxis.None) return false;

        Gizmo.BeginDrag(axis, SelectedEntity, CameraPosition, ScreenRay(mousePos, view, proj), mousePos);
        return true;
    }

    // Editor selection: render the ID buffer with each mesh entity in a unique colour, read the
    // pixel under the cursor, and select that entity. Reuses the same id-colour path as hover.
    private void PickSelection(Point mousePos)
    {
        var device = Core.GraphicsDevice;
        EnsurePickResources(device);
        BuildTables();

        var view = Matrix.CreateLookAt(CameraPosition, CameraTarget, Vector3.Up);
        var proj = Matrix.CreatePerspectiveFieldOfView(
            FieldOfView, (float)_rtWidth / _rtHeight, NearPlane, FarPlane);

        device.SetRenderTarget(_idTarget);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1f, 0);
        device.BlendState        = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState   = RasterizerState.CullNone;
        _idEffect.CurrentTechnique = _idEffect.Techniques["IdColor"];
        _idEffect.Parameters["LightViewProjection"].SetValue(view * proj);

        // Encode the 1-based entity index across R/G/B (24 bits, ~16M ids) so the pick isn't
        // capped at 255 entities. Background clears to 0 = "nothing".
        int count = Math.Min(_meshEntities.Count, 0xFFFFFF);
        for (int i = 0; i < count; i++)
        {
            var e = _meshEntities[i];
            if (!e.Visible) continue;
            var mesh = e.GetComponent<Mesh3DComponent>();
            if (mesh != null)
            {
                device.DepthStencilState = DepthStencilState.Default;
                _idEffect.Parameters["IdColor"].SetValue(EncodeId(i + 1));
                e.DrawDepth(device, _idEffect, _scene.SceneScale);
            }
            else
            {
                // Non-mesh entity (light, PlayerStart): stand-in geometry for picking is a
                // camera-facing billboard, same as what's drawn for it in the main pass.
                _billboards.Draw(device, e.Position3D * _scene.SceneScale, CameraPosition,
                    BillboardSize, view, proj, EncodeId(i + 1));
            }
        }
        device.SetRenderTarget(null);

        float u = MathHelper.Clamp((mousePos.X - _bounds.X) / (float)_bounds.Width,  0f, 0.999f);
        float v = MathHelper.Clamp((mousePos.Y - _bounds.Y) / (float)_bounds.Height, 0f, 0.999f);
        int px = (int)(u * IdWidth), py = (int)(v * IdHeight);
        _idTarget.GetData(0, new Rectangle(px, py, 1, 1), _idPixel, 0, 1);

        int id = DecodeId(_idPixel[0]);
        Select(id >= 1 && id <= count ? _meshEntities[id - 1] : null);
    }

    // Icon color for a non-mesh entity's billboard: yellow for point lights, orange for the sun,
    // cyan for the spawn point.
    private static Vector4 BillboardColorFor(Entity e)
    {
        if (e.GetComponent<PointLightComponent>() != null) return new Vector4(1f, 0.9f, 0.3f, 1f);
        if (e.GetComponent<DirectionalLightComponent>() != null) return new Vector4(1f, 0.6f, 0.2f, 1f);
        return new Vector4(0.3f, 0.9f, 1f, 1f); // PlayerStart / other
    }

    // Pack a 24-bit id into R/G/B (little end in R). Alpha=1 so the pixel is written opaque.
    private static Vector4 EncodeId(int id) => new Vector4(
        (id        & 0xFF) / 255f,
        ((id >>  8) & 0xFF) / 255f,
        ((id >> 16) & 0xFF) / 255f,
        1f);

    private static int DecodeId(Color c) => c.R | (c.G << 8) | (c.B << 16);

    // Select an entity in the editor (null clears selection). Public so other editor panels
    // (hierarchy list, light-billboard picking) can drive the same selection/highlight/inspector
    // notification as viewport click-picking.
    public void Select(Entity e)
    {
        if (e == SelectedEntity) return;

        if (_selectedMesh != null) _selectedMesh.DiffuseColor = _selectedBaseColor;  // restore

        SelectedEntity = e;
        _selectedMesh  = e?.GetComponent<Mesh3DComponent>();
        if (_selectedMesh != null)
        {
            _selectedBaseColor = _selectedMesh.DiffuseColor;
            _selectedMesh.DiffuseColor = _selectedBaseColor * 1.8f;  // highlight
        }

        OnSelectionChanged?.Invoke(e);
    }

    // How far in front of the camera a newly placed model/prefab spawns. No raycast onto the
    // ground/navmesh (out of scope -- see 3d-editor-plan.md); the gizmo is how you then position
    // it precisely, and Mesh3D entities already support free placement via the translate handle.
    private const float PlaceDistance = 5f;

    // Spawn a bare Mesh3D entity for the given content-relative model path (e.g. "models/Cube.001")
    // in front of the camera. Undoable; selects the new entity so it can be repositioned immediately.
    public void PlaceModel(string modelPath)
    {
        string fileName = modelPath.Replace('\\', '/').Split('/')[^1];
        var entity = new Entity { Name = EntityNameProvider.GetUniqueName(fileName) };
        entity.AddComponent(new Mesh3DComponent { ModelPath = modelPath, Scale = Vector3.One });
        PlaceEntity(entity);
    }

    // Spawn an entity from a prefab XML (content-relative path, e.g. "files/prefabs/point_light.xml")
    // in front of the camera. Undoable; selects the new entity.
    public void PlacePrefab(string prefabContentPath)
    {
        Entity entity;
        try
        {
            entity = Entity.FromContentFile(Core.Content, prefabContentPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[editor] failed to load prefab '{prefabContentPath}': {ex.Message}");
            return;
        }
        entity.Name = EntityNameProvider.GetUniqueName(entity.Name);
        PlaceEntity(entity);
    }

    private void PlaceEntity(Entity entity)
    {
        Vector3 forward = Vector3.Normalize(CameraTarget - CameraPosition);
        entity.Position3D = CameraPosition + forward * PlaceDistance;

        History.Execute(new AddEntityCommand(_scene, entity));
        _tablesBuilt = false;
        OnSceneChanged?.Invoke();
        Select(entity);
    }

    // Spawn the walker at (the nearest walkable point to) the current camera position and drop
    // into Walk mode -- lets you playtest right from wherever you're looking instead of always
    // starting at the saved PlayerStart. Also moves/creates the PlayerStart entity so the new
    // spot is what a normal (non-editor) load will spawn at too.
    public void StartGameFromCamera()
    {
        if (Walker == null) return;

        var spawn = Walker.Mesh.NearestPointApprox(CameraPosition);
        if (!Walker.Spawn(spawn))
        {
            Console.WriteLine("[editor] start-from-camera: no walkable navmesh nearby");
            return;
        }

        var existing = _scene.FindEntityByName("PlayerStart");
        if (existing != null)
        {
            existing.Position3D = spawn;
        }
        else
        {
            var entity = new Entity { Name = "PlayerStart", Position3D = spawn };
            _scene.AddEntity(entity);
            _tablesBuilt = false;
            OnSceneChanged?.Invoke();
        }

        EditorMode = false;
        Mode = CameraMode.Walk;
        OnEditorModeChanged?.Invoke(false);
    }

    // Remove the selected entity from the scene via the command stack (undoable). Clears
    // selection (which notifies the inspector) and invalidates the pick tables so the cached
    // mesh list rebuilds without the deleted entity.
    private void DeleteSelected()
    {
        var target = SelectedEntity;
        if (target == null) return;

        Select(null);          // restores highlight + fires OnSelectionChanged(null)
        History.Execute(new DeleteEntityCommand(_scene, target));
        _tablesBuilt = false;
        OnSceneChanged?.Invoke();
        Console.WriteLine($"[editor] deleted {target.Name}");
    }

    // Write the scene back out to ScenePath (Content source dir). No-op if ScenePath is unset.
    private void SaveScene()
    {
        if (string.IsNullOrEmpty(ScenePath))
        {
            Console.WriteLine("[editor] save skipped: no ScenePath set");
            return;
        }
        try
        {
            SceneWriter.Save(_scene, ScenePath);
            Console.WriteLine($"[editor] saved {ScenePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[editor] save failed: {ex.Message}");
        }
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
                if (!entity.Visible) continue; // hidden entities are neither drawn nor pickable
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
            if (cand.Entity.Visible && Walker != null &&
                Vector3.Distance(Walker.Position, cand.Entity.Position3D) <= InteractRange)
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

        // Append non-mesh entities the editor should still be able to click-select in the
        // viewport (PickSelection draws these as billboards instead of real geometry). Harmless
        // for Walk-mode hover picking too -- Entity.DrawDepth no-ops for an entity with no
        // Mesh3DComponent, so these extra entries never affect gameplay hover/interact.
        var seen = new System.Collections.Generic.HashSet<Entity>(_meshEntities);
        foreach (var e in _scene.FindEntitiesWithComponent<PointLightComponent>())
            if (seen.Add(e)) _meshEntities.Add(e);
        foreach (var e in _scene.FindEntitiesWithComponent<DirectionalLightComponent>())
            if (seen.Add(e)) _meshEntities.Add(e);
        var spawn = _scene.FindEntityByName("PlayerStart");
        if (spawn != null && seen.Add(spawn)) _meshEntities.Add(spawn);
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

    // Fired when this view is removed (e.g. the walking-sim window closes) so any modal it owns
    // (the puzzle solve overlay) can close itself too.
    public event Action Removed;

    public override void OnRemovedFromUI()
    {
        Removed?.Invoke();
        if (_cameraActive)
        {
            Core.Instance.IsMouseVisible = true;
            _cameraActive = false;
        }
        if (_hovered?.Mesh != null) _hovered.Mesh.DiffuseColor = _hovered.BaseColor;
        _pixel?.Dispose();
        _renderTarget?.Dispose();
        _idTarget?.Dispose();
        _navDebug?.Dispose();
        Gizmo?.Dispose();
        _billboards?.Dispose();
        _skybox?.Dispose();
    }
}
