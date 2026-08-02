using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.Graphics;
using Quartz.Input;
using Quartz.UI;
using ld59.UI.Editor;
using ld59.UI.Editor.Gizmos;
using ld59.UI.Scene3D;
using ld59.WalkingSim;

namespace ld59.UI;

// Fly = free 6-DOF camera (the 3D scene viewer). Walk = first-person walker constrained to a
// navmesh (the walking sim); movement is flattened to the horizontal plane and height comes
// from the WalkController.
public enum CameraMode { Fly, Walk }

/// <summary>
/// A 3D scene embedded in the desktop UI: renders a <see cref="Scene"/> to an offscreen target,
/// steers a fly/walk camera through it, and -- in editor mode (F2) -- lets you author it in place.
/// <para>
/// This class is the wiring: it gathers input, orders the frame, and forwards the public surface
/// other UI panels and console commands use. The work lives in <see cref="ld59.UI.Scene3D"/>:
/// <see cref="CameraRig"/> (camera + mouse capture), <see cref="Scene3DRenderer"/> (render passes
/// and post-processing), <see cref="ScenePickBuffer"/> (id-buffer picking and hover),
/// <see cref="SceneEditorController"/> (selection, gizmo, entity operations),
/// <see cref="NavMeshBakeJob"/> (background bakes) and <see cref="Scene3DHud"/> (2D overlays).
/// </para>
/// </summary>
public class UI3DScene : UIElement
{
    private Rectangle _bounds;
    private readonly int _rtWidth;
    private readonly int _rtHeight;

    private readonly Scene _scene;
    private readonly CameraRig _camera;
    private readonly Scene3DRenderer _renderer;
    private readonly ScenePickBuffer _picker;
    private readonly SceneEditorController _editor;
    private readonly NavMeshBakeJob _bake = new();
    private readonly Scene3DHud _hud = new();
    private readonly KeyEdgeTracker _keys = new();

    // Every live 3D view, so console commands (`fog`, `depthview`) can reach whatever scene the
    // user currently has open without the command needing a handle on the window that owns it.
    private static readonly List<UI3DScene> _instances = new();
    public static IReadOnlyList<UI3DScene> Instances => _instances;

    // Debug: toggled by the `idview` console command. Shows the ID-buffer as a picture-in-picture
    // plus a text readout of the id sampled at the crosshair -- so you can see whether an object
    // renders into the pick buffer at all and whether it's recognised as interactable. Runs even
    // without mouse capture.
    public static bool DebugIdView;

    // Debug: toggled by the `depthview` console command. Shows the linear depth buffer as a
    // picture-in-picture plus the world distance sampled at the crosshair, so the depth pass can
    // be sanity-checked on its own (before/without any effect that consumes it).
    public static bool DebugDepthView;

    public Scene Scene => _scene;

    public UI3DScene(Rectangle bounds, Scene scene = null)
    {
        _bounds   = bounds;
        _rtWidth  = bounds.Width;
        _rtHeight = bounds.Height;

        _scene = scene ?? new Scene();
        _scene.InitializeEntities();

        _camera   = new CameraRig();
        _renderer = new Scene3DRenderer(Core.GraphicsDevice, _scene, _rtWidth, _rtHeight);
        _picker   = new ScenePickBuffer(_scene, _renderer.Billboards);
        _editor   = new SceneEditorController(_scene, _camera, _picker, Core.GraphicsDevice);

        // Entering the editor turns the navmesh overlay on; so does a finished bake, which also
        // rebinds the walker to the new mesh (the renderer rebuilds its overlay buffers to match).
        _editor.OnEditorModeChanged += on => { if (on) ShowNavMesh = true; };
        _bake.Succeeded += result =>
        {
            _camera.Walker?.Rebind(result.NavMesh);
            ShowNavMesh = true;
        };

        _instances.Add(this);
    }

    // ── Camera (forwarded to the rig) ───────────────────────────────────────────────────────

    public Vector3 CameraPosition { get => _camera.Position; set => _camera.Position = value; }
    public Vector3 CameraTarget   { get => _camera.Target;   set => _camera.Target   = value; }
    public float FieldOfView { get => _camera.FieldOfView; set => _camera.FieldOfView = value; }
    public float NearPlane   { get => _camera.NearPlane;   set => _camera.NearPlane   = value; }
    public float FarPlane    { get => _camera.FarPlane;    set => _camera.FarPlane    = value; }
    /// <summary>Free-fly speed (world units/second). Shift multiplies it by
    /// <see cref="FlyBoostMultiplier"/>.</summary>
    public float FlySpeed  { get => _camera.FlySpeed;  set => _camera.FlySpeed  = value; }
    /// <summary>On-foot speed (world units/second). Overrides the walker's own MoveSpeed.</summary>
    public float WalkSpeed { get => _camera.WalkSpeed; set => _camera.WalkSpeed = value; }
    public float FlyBoostMultiplier { get => _camera.FlyBoostMultiplier; set => _camera.FlyBoostMultiplier = value; }
    public float LookSensitivity    { get => _camera.LookSensitivity;    set => _camera.LookSensitivity    = value; }
    public bool DebugLook  { get => _camera.DebugLook; set => _camera.DebugLook = value; }
    public CameraMode Mode { get => _camera.Mode;      set => _camera.Mode      = value; }

    public WalkController Walker
    {
        get => _camera.Walker;
        set { _camera.Walker = value; _picker.Walker = value; }
    }

    /// <summary>
    /// Optional test for whether the pointer really belongs to this view: false when something else
    /// -- another window, the taskbar, the start menu -- is drawn over the viewport at the cursor.
    /// A view that fills the screen (the walking-sim backdrop) sets this so a click meant for the
    /// desktop doesn't also grab mouse-look or pick an entity in the world behind it.
    /// Null = the view always owns the pointer.
    /// </summary>
    public Func<bool> OwnsPointer { get => _camera.CanCapture; set => _camera.CanCapture = value; }

    public void SuspendCapture() => _camera.SuspendCapture();
    public void ResumeCapture()  => _camera.ResumeCapture();

    // ── Rendering (forwarded to the renderer) ───────────────────────────────────────────────

    /// <summary>Procedural moon skybox (stars + Earth + Sun). Off by default; enabled per scene.</summary>
    public bool ShowSkybox { get => _renderer.ShowSkybox; set => _renderer.ShowSkybox = value; }

    /// <summary>The view's own post-process chain -- add scene-aware effects here.</summary>
    public PostProcessManager PostProcess => _renderer.PostProcess;

    /// <summary>Exponential fog over this view. Disabled by default; the walking sim turns it on
    /// and the <c>fog</c> console command tunes it live.</summary>
    public ExponentialFogPostProcessEffect Fog => _renderer.Fog;

    /// <summary>Silhouette outlines from the depth pass' entity ids. Tuned live with the
    /// <c>outline</c> console command.</summary>
    public OutlinePostProcessEffect Outline => _renderer.Outline;

    /// <summary>Screen-space ambient occlusion over this view. Disabled by default; tuned live with
    /// the <c>ssao</c> console command.</summary>
    public SSAOPostProcessEffect Ssao => _renderer.Ssao;

    /// <summary>World distance the depth pass' 1.0 encodes.</summary>
    public float DepthFarDistance { get => _renderer.DepthFarDistance; set => _renderer.DepthFarDistance = value; }

    /// <summary>Debug navmesh overlay (toggle N).</summary>
    public bool ShowNavMesh { get; set; }

    // ── Editor (forwarded to the controller) ────────────────────────────────────────────────

    /// <summary>Editor mode (toggle F2): forces free-fly and enables in-game authoring.</summary>
    public bool EditorMode { get => _editor.EditorMode; set => _editor.EditorMode = value; }
    public event Action<bool> OnEditorModeChanged
    {
        add    => _editor.OnEditorModeChanged += value;
        remove => _editor.OnEditorModeChanged -= value;
    }

    public Entity SelectedEntity => _editor.SelectedEntity;
    public event Action<Entity> OnSelectionChanged
    {
        add    => _editor.OnSelectionChanged += value;
        remove => _editor.OnSelectionChanged -= value;
    }

    /// <summary>Fired every frame while an entity is selected, so live-value displays (the
    /// inspector) can track an in-progress gizmo drag.</summary>
    public event Action<Entity> OnEntityLiveUpdate
    {
        add    => _editor.OnEntityLiveUpdate += value;
        remove => _editor.OnEntityLiveUpdate -= value;
    }

    /// <summary>Fired whenever the scene's entity list changes, so panels like the hierarchy list
    /// know to rebuild.</summary>
    public event Action OnSceneChanged
    {
        add    => _editor.OnSceneChanged += value;
        remove => _editor.OnSceneChanged -= value;
    }

    public EditorHistory History => _editor.History;
    public TransformGizmo Gizmo  => _editor.Gizmo;

    /// <summary>Absolute path Ctrl+S writes the scene XML to. If null, save is a no-op.</summary>
    public string ScenePath { get => _editor.ScenePath; set => _editor.ScenePath = value; }

    /// <summary>Optional callback so the host UI can report whether a text field (e.g. the
    /// inspector) currently has focus -- letter hotkeys and Delete are suppressed while typing.</summary>
    public Func<bool> IsTextInputFocused { get; set; }

    public void Select(Entity e) => _editor.Select(e);
    public void FocusOnSelected() => _editor.FocusOnSelected();
    public void PlaceModel(string modelPath) => _editor.PlaceModel(modelPath);
    public void PlacePrefab(string prefabContentPath) => _editor.PlacePrefab(prefabContentPath);
    public void StartGameFromCamera() => _editor.StartGameFromCamera();
    public int TagAllMeshesAsObstacles() => _editor.TagAllMeshesAsObstacles();

    // ── Navmesh bake (forwarded to the job) ─────────────────────────────────────────────────

    /// <summary>Absolute path the baked navmesh OBJ is written to. If null, a bake still updates
    /// the live mesh + overlay but doesn't persist to disk.</summary>
    public string NavMeshSavePath { get => _bake.SavePath; set => _bake.SavePath = value; }

    public int LastBakeSourceTris => _bake.LastSourceTris;
    public int LastBakeNavTris    => _bake.LastNavTris;
    public string LastBakeError   => _bake.LastError;
    public bool IsBaking          => _bake.IsRunning;

    public event Action OnBakeStarted
    {
        add    => _bake.Started += value;
        remove => _bake.Started -= value;
    }

    public event Action OnBakeCompleted
    {
        add    => _bake.Completed += value;
        remove => _bake.Completed -= value;
    }

    /// <summary>Kick off a background navmesh bake. Public so the navmesh panel's Bake button and
    /// the hotkey can both trigger it.</summary>
    public void BakeNavMesh()
    {
        if (_camera.Walker == null) return;
        _bake.Start(_scene, _scene.SceneScale);
    }

    // ── Interaction ─────────────────────────────────────────────────────────────────────────

    /// <summary>Raised when the player presses the interact key while looking at an interactable.</summary>
    public event Action<Interactable3DComponent> OnInteract;

    /// <summary>Fired when this view is removed (e.g. the walking-sim window closes) so any modal
    /// it owns (the puzzle solve overlay) can close itself too.</summary>
    public event Action Removed;

    // ── Frame ───────────────────────────────────────────────────────────────────────────────

    private RtViewport Viewport => new RtViewport(_bounds, _rtWidth, _rtHeight);

    public override void Update(float deltaTime)
    {
        var vp       = Viewport;
        // Gated views: while the game is blocked (developer console open) these report nothing
        // held and no buttons down, which is what silences movement, the editor hotkeys and
        // click-picking here without any of them needing to know the console exists.
        var mouse    = GameInput.Mouse;
        var keyboard = GameInput.Keyboard;
        var cursor   = new Point(mouse.X, mouse.Y);

        // Finalize a background navmesh bake on the main thread once Recast has finished.
        _bake.Poll();

        // Suppress letter/Delete hotkeys while a text field has focus, so typing "w" or pressing
        // Delete edits the text instead of firing a hotkey.
        bool textFocused = IsTextInputFocused?.Invoke() ?? false;

        // Only the editor's per-frame hover needs to know this every frame; mouse capture asks
        // OwnsPointer for itself, at the click edge.
        bool pointerBlocked = EditorMode && !(OwnsPointer?.Invoke() ?? true);

        _editor.Update(keyboard, mouse, cursor, vp, textFocused, pointerBlocked);
        HandleViewHotkeys(keyboard, textFocused);

        // In editor mode, look with the RIGHT button (Unreal-style) so LEFT-click is free for
        // selecting objects and clicking the inspector. Otherwise (Walk/Fly) look with left.
        bool lookPressed = EditorMode
            ? mouse.RightButton == ButtonState.Pressed
            : mouse.LeftButton  == ButtonState.Pressed;

        _camera.Update(deltaTime, keyboard, vp, cursor, lookPressed,
                       keyboard.IsKeyDown(Keys.Tab), _scene.SceneScale);

        _scene.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(deltaTime)));

        RenderFrame(vp, deltaTime);

        if (Mode == CameraMode.Walk && (_camera.IsActive || DebugIdView))
        {
            _picker.UpdateHover(Core.GraphicsDevice, _camera.View, _camera.Projection(vp.Aspect), DebugIdView);

            // Interact edge-detect every frame against the last known hover. GameInput tracks the
            // edge globally, so this is safe to read from inside a conditional -- and it swallows
            // the frame input unblocks, so a key held while the console was open doesn't fire.
            if (GameInput.Pressed(Keys.E) && _picker.Hovered != null)
                OnInteract?.Invoke(_picker.Hovered);
        }
    }

    // Hotkeys the view owns rather than the editor: N toggles the navmesh overlay, F frames the
    // selection in the editor (Unity-style) or detaches into free-fly outside it, F3 logs mouse-look
    // diagnostics.
    private void HandleViewHotkeys(KeyboardState keyboard, bool textFocused)
    {
        if (_keys.Pressed(keyboard, Keys.N) && !textFocused)
            ShowNavMesh = !ShowNavMesh;

        if (_keys.Pressed(keyboard, Keys.F) && !textFocused)
        {
            if (EditorMode) _editor.FocusOnSelected();
            else if (Walker != null) Mode = Mode == CameraMode.Walk ? CameraMode.Fly : CameraMode.Walk;
        }

        if (_keys.Pressed(keyboard, Keys.F3) && !textFocused)
        {
            DebugLook = !DebugLook;
            Console.WriteLine($"[look] debug logging {(DebugLook ? "ON" : "OFF")}");
        }
    }

    // The scene pass, then the overlays that draw on top of it inside the same render target.
    private void RenderFrame(in RtViewport vp, float deltaTime)
    {
        var view = _camera.View;
        var proj = _camera.Projection(vp.Aspect);
        var device = Core.GraphicsDevice;

        // Puzzle-panel surface pass -- render each puzzle to its own texture before the main pass.
        if (Mode == CameraMode.Walk) _renderer.RenderPuzzleSurfaces();

        _renderer.SampleDepthForDebug = DebugDepthView;
        _renderer.BeginScene(view, proj, _camera.Position, deltaTime);

        // Billboard icons marking non-mesh entities (lights, PlayerStart) in editor mode, so
        // they're visible and click-selectable even without Mesh3D geometry of their own.
        if (EditorMode)
            _renderer.DrawBillboards(_picker.Pickables, SelectedEntity, _camera.Position, view, proj);

        if (ShowNavMesh) _renderer.DrawNavMesh(Walker?.Mesh, view, proj);

        _editor.DrawGizmo(device, view, proj);
        _editor.UpdatePickDebugReadout(vp, view, proj);

        _renderer.EndScene();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Draw(_renderer.Output, _bounds, Color.White);

        if (DebugIdView && _picker.DebugTarget is { IsDisposed: false } idTarget)
            _hud.DrawIdDebug(spriteBatch, _bounds, idTarget, _picker.LastCrosshairId, _picker.HoveredName);

        if (DebugDepthView && _scene.DepthMap is { IsDisposed: false } depthMap)
            _hud.DrawDepthDebug(spriteBatch, _bounds, depthMap, Viewport,
                _renderer.LastCrosshairDepth, DepthFarDistance);

        if (_camera.IsActive)
        {
            _hud.DrawCrosshair(spriteBatch, _bounds);
            if (Mode == CameraMode.Walk && _picker.Hovered != null)
                _hud.DrawInteractPrompt(spriteBatch, _bounds, _picker.Hovered.PromptText);
        }

        if (EditorMode)
        {
            _hud.DrawEditorDiag(spriteBatch, _bounds, _editor.StatusLine, _editor.LastClickDiag);
            if (Gizmo.ShowPickDebug)
                _hud.DrawGizmoPickDiag(spriteBatch, _bounds, _editor.GizmoPickDiag);
        }
    }

    public override Rectangle GetBoundingBox() => _bounds;

    public override void SetBounds(Rectangle bounds) => _bounds = bounds;

    public override void OnRemovedFromUI()
    {
        Removed?.Invoke();
        _camera.ReleaseCapture();
        _picker.ClearHover();
        _instances.Remove(this);

        _picker.Dispose();
        _editor.Dispose();
        _renderer.Dispose();
        _hud.Dispose();
    }
}
