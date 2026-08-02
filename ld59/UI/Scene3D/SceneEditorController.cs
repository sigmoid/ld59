using System;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.EntityComponentScene.Serialization;
using ld59.UI.Editor;
using ld59.UI.Editor.Commands;
using ld59.UI.Editor.Gizmos;
using ld59.WalkingSim;

namespace ld59.UI.Scene3D;

/// <summary>
/// In-game level editing for a 3D view (toggle F2): selection, transform handles, the authoring
/// hotkeys, and the entity operations behind them (place / delete / duplicate / save). Every
/// mutation routes through <see cref="History"/> so Ctrl+Z/Ctrl+Y work uniformly.
/// <para>
/// The view owns input gathering and hands it here; this class owns everything that is only true
/// while authoring, and reports state changes through its events so panels (inspector, hierarchy,
/// content browser) can follow along.
/// </para>
/// </summary>
public sealed class SceneEditorController
{
    // Draw-time highlight multiplier for the selection (see Mesh3DComponent.HighlightFactor).
    private const float SelectedHighlight = 1.8f;

    // How much bigger than the object's bounding sphere the framed view is, so focusing leaves a
    // little air around the target instead of filling the viewport edge to edge.
    private const float FocusMargin = 1.6f;
    // Fallback radius for entities with no mesh (lights, PlayerStart) -- roughly their billboard.
    private const float FocusPointRadius = 1f;

    // How far in front of the camera a newly placed model/prefab spawns. No raycast onto the
    // ground/navmesh (out of scope -- see 3d-editor-plan.md); the gizmo is how you then position
    // it precisely, and Mesh3D entities already support free placement via the translate handle.
    private const float PlaceDistance = 5f;

    // How far (world units, on X and Z) a Ctrl+D duplicate is nudged off its source so the two
    // don't perfectly overlap and the copy is grabbable by the gizmo.
    private const float DuplicateOffset = 1f;

    // How many frames of pick history to keep for the click diagnostic below.
    private const int PickHistoryFrames = 12;

    private readonly Scene _scene;
    private readonly CameraRig _camera;
    private readonly ScenePickBuffer _picker;
    private readonly KeyEdgeTracker _keys = new();

    private Entity _selected;
    private Mesh3DComponent _selectedMesh;
    private bool _prevLeftPressed;
    private Point _lastCursor;   // for the F4 live readout

    // One frame's view of the pick, kept in a short ring so a click that moves the selection can
    // report what the frames before it saw. Enough to tell apart the candidate explanations without
    // guessing: whether the cursor moved between the lit frame and the click, whether the GIZMO
    // moved under a still cursor, or whether neither did and the pick simply disagreed with itself.
    private readonly record struct PickSample(
        int Frame, Point Cursor, Vector2 CursorPx, Vector2 OriginPx, bool OriginOnScreen,
        GizmoAxis Hover, float MissPx, float Score, string Target, Vector3 TargetPos, Vector3 CamPos);

    private readonly PickSample[] _pickHistory = new PickSample[PickHistoryFrames];
    private int _pickHistoryNext;
    private int _pickHistoryCount;
    private int _frameCounter;

    /// <summary>Editor mode: forces free-fly and enables in-game authoring.</summary>
    public bool EditorMode { get; set; }
    public event Action<bool> OnEditorModeChanged;

    /// <summary>Current selection. The inspector subscribes to <see cref="OnSelectionChanged"/>.</summary>
    public Entity SelectedEntity => _selected;
    public event Action<Entity> OnSelectionChanged;

    /// <summary>Fired every frame while an entity is selected (not just on selection change), so
    /// live-value displays (the inspector) can track an in-progress gizmo drag.</summary>
    public event Action<Entity> OnEntityLiveUpdate;

    /// <summary>Fired whenever the scene's entity list changes (delete, undo/redo of add/delete,
    /// placement) so panels like the hierarchy list know to rebuild.</summary>
    public event Action OnSceneChanged;

    /// <summary>Undo/redo stack. All editor mutations route through it.</summary>
    public EditorHistory History { get; } = new EditorHistory();

    /// <summary>Move/rotate/scale handles for the selection (Q/W/E/R picks the mode).</summary>
    public TransformGizmo Gizmo { get; }

    /// <summary>Absolute path Ctrl+S writes the scene XML to (Content source dir). If null, save
    /// is a no-op.</summary>
    public string ScenePath { get; set; }

    // Picking diagnostics, surfaced as an on-screen HUD so gizmo picking can be debugged without a
    // visible stdout console (WindowsDX app).
    public string LastClickDiag { get; private set; } = "(no click)";
    public string GizmoPickDiag { get; private set; } = "(move cursor over a handle)";
    private string _lastPickDiag = "(no pick)";

    public SceneEditorController(Scene scene, CameraRig camera, ScenePickBuffer picker, GraphicsDevice device)
    {
        _scene  = scene;
        _camera = camera;
        _picker = picker;
        Gizmo   = new TransformGizmo(device);
    }

    // ── Per-frame input ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Editor hotkeys and viewport interaction. Runs before the camera update, so the gizmo sees
    /// the same camera the click was aimed at. <paramref name="textFocused"/> suppresses letter and
    /// Delete hotkeys while a text field (e.g. the inspector's Name box) has focus, so typing "w"
    /// edits the text instead of switching gizmo mode. <paramref name="pointerBlocked"/> means
    /// something is drawn over the viewport at the cursor (a tool panel, the taskbar), so a click
    /// there must not also pick or grab in the world behind it.
    /// </summary>
    public void Update(KeyboardState keyboard, MouseState mouse, Point cursor,
                       in RtViewport vp, bool textFocused, bool pointerBlocked = false)
    {
        _lastCursor = cursor;

        UpdateModeToggle(keyboard);
        UpdateHotkeys(keyboard, textFocused);
        UpdateViewportInteraction(mouse, cursor, vp, pointerBlocked);

        // Fires every frame the editor has a selection, so panels showing live values (the
        // inspector's Position/Scale/etc. text) stay in sync while a gizmo drag is moving the
        // entity -- OnSelectionChanged only fires when the SELECTION itself changes, not while
        // the selected entity's own properties are being edited.
        if (EditorMode && _selected != null)
            OnEntityLiveUpdate?.Invoke(_selected);

        _prevLeftPressed = mouse.LeftButton == ButtonState.Pressed;
    }

    private void UpdateModeToggle(KeyboardState keyboard)
    {
        if (_keys.Pressed(keyboard, Keys.F2))
        {
            EditorMode = !EditorMode;
            _camera.Mode = EditorMode ? CameraMode.Fly : CameraMode.Walk;
            if (!EditorMode) Select(null);   // clear the selection highlight (and gizmo target) on the way out
            OnEditorModeChanged?.Invoke(EditorMode);
        }

        // Editor mode always stays in free-fly -- pin this every frame so nothing (the F toggle in
        // the view, or anything else) can leave it in Walk mode while EditorMode is on. Walk mode
        // has different mouse-capture semantics (held until Tab, not released on mouse-up), so
        // leaking into it here is what caused the mouse to get stuck captured in the editor.
        if (EditorMode) _camera.Mode = CameraMode.Fly;
    }

    private void UpdateHotkeys(KeyboardState keyboard, bool textFocused)
    {
        // F4: overlay the gizmo's raycast pick volume (segments/ring/center box) for debugging.
        if (_keys.Pressed(keyboard, Keys.F4) && !textFocused && EditorMode)
        {
            Gizmo.ShowPickDebug = !Gizmo.ShowPickDebug;
            Console.WriteLine($"[gizmo] pick-volume overlay {(Gizmo.ShowPickDebug ? "ON" : "OFF")}");
        }

        // Delete: remove the selected entity from the scene.
        if (_keys.Pressed(keyboard, Keys.Delete) && EditorMode && !textFocused && _selected != null)
            DeleteSelected();

        // Ctrl+Z / Ctrl+Y: undo/redo. Ctrl+S: save the scene XML. Ctrl+D: duplicate.
        bool ctrl = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        bool undo = _keys.Edge(Keys.Z, ctrl && keyboard.IsKeyDown(Keys.Z));
        bool redo = _keys.Edge(Keys.Y, ctrl && keyboard.IsKeyDown(Keys.Y));
        bool save = _keys.Edge(Keys.S, ctrl && keyboard.IsKeyDown(Keys.S));
        bool dup  = _keys.Edge(Keys.D, ctrl && keyboard.IsKeyDown(Keys.D));
        if (EditorMode && !textFocused)
        {
            // Selection may point at an entity a delete/add command just disposed; drop it so we
            // never hold a dangling reference across an undo/redo that changed entity identity.
            if (undo && History.CanUndo) { Select(null); History.Undo(); NotifySceneChanged(); }
            if (redo && History.CanRedo) { Select(null); History.Redo(); NotifySceneChanged(); }
            if (save) SaveScene();
            if (dup && _selected != null) DuplicateSelected();
        }

        // Q/W/E/R pick the gizmo mode (none/translate/rotate/scale). Only live while the camera
        // isn't actively looking -- movement (WASD) only applies during a look-drag too, so the
        // two never compete for the same keys.
        bool none  = _keys.Pressed(keyboard, Keys.Q);
        bool move  = _keys.Pressed(keyboard, Keys.W);
        bool rot   = _keys.Pressed(keyboard, Keys.E);
        bool scale = _keys.Pressed(keyboard, Keys.R);
        if (EditorMode && !textFocused && !_camera.IsActive)
        {
            if (none)  Gizmo.Mode = GizmoMode.None;
            if (move)  Gizmo.Mode = GizmoMode.Translate;
            if (rot)   Gizmo.Mode = GizmoMode.Rotate;
            if (scale) Gizmo.Mode = GizmoMode.Scale;
        }

        // P: start the game from the current camera position.
        if (_keys.Pressed(keyboard, Keys.P) && EditorMode && !textFocused)
            StartGameFromCamera();
    }

    // Left-click (while not looking) either grabs a gizmo handle or picks the entity under the
    // cursor. While a handle is held, drag it; on release, commit the transform.
    private void UpdateViewportInteraction(MouseState mouse, Point cursor, in RtViewport vp,
                                           bool pointerBlocked)
    {
        _frameCounter++;

        if (!EditorMode || _camera.IsActive)
        {
            // Looking around (or out of the editor): nothing is under the cursor to highlight.
            Gizmo.HoverAxis = GizmoAxis.None;
            return;
        }

        var view = _camera.View;
        var proj = _camera.Projection(vp.Aspect);
        bool leftPressed = mouse.LeftButton == ButtonState.Pressed;

        // Highlight the handle under the cursor. Uses the very same CPU pick a click would run,
        // so what lights up is exactly what a click grabs. While dragging, the held axis stays
        // lit regardless of where the cursor has since travelled.
        // A viewport that fills the screen sits under the tool panels, so "in the viewport" is not
        // enough on its own -- the cursor also has to not be over something drawn on top of it.
        bool inViewport = vp.Contains(cursor) && !pointerBlocked;

        float hoverMiss = 0f, hoverScore = 0f;
        var hover = !Gizmo.IsDragging && _selected != null
            && Gizmo.HasValidTarget(_selected) && inViewport
            ? Gizmo.PickAxis(_selected, _camera.Position, vp.ToPixel(cursor), view, proj, vp.Viewport,
                             out hoverMiss, out hoverScore)
            : GizmoAxis.None;
        Gizmo.HoverAxis = hover;

        RecordPickSample(cursor, vp, view, proj, hover, hoverMiss, hoverScore);

        if (leftPressed && !_prevLeftPressed && inViewport)
        {
            var selBefore = _selected;
            bool grabbed = TryBeginGizmoDrag(cursor, vp, view, proj);
            if (!grabbed)
            {
                // No gizmo under the cursor for the CURRENT selection -> select whatever mesh is
                // there, then immediately retry the grab. This makes clicking a gizmo work in one
                // click even when a different entity was selected: the pick that first missed (old
                // selection's gizmo elsewhere) now runs against the just-selected entity, whose
                // gizmo is under the cursor. A click on an object's body (not on an arrow) still
                // just selects, because the retry misses too.
                Select(_picker.PickEntity(cursor, vp, view, proj, _camera.Position));
                grabbed = TryBeginGizmoDrag(cursor, vp, view, proj);
            }
            LastClickDiag = $"click grab={grabbed} selB={selBefore?.Name ?? "null"} "
                          + $"selA={_selected?.Name ?? "null"} {_lastPickDiag}";

            // The failure being chased: a click that was aimed at the gizmo moved the selection
            // instead. Dump the frames leading up to it while the evidence is still in the ring.
            if (!ReferenceEquals(selBefore, _selected))
                DumpPickHistory(selBefore, cursor, vp);
        }
        else if (Gizmo.IsDragging)
        {
            if (leftPressed)
                Gizmo.UpdateDrag(vp.ScreenRay(cursor, view, proj), cursor);
            else
                Gizmo.EndDrag(History);
        }

    }

    // Grabs a gizmo handle if the click hit one. Picking is a CPU raycast (TransformGizmo.PickAxis): a
    // screen-space capsule test against the projected handles, evaluated at the exact click cursor. No
    // render-target readback -> no frame-lag from the pick itself, and dragging uses the world ray
    // built from the same cursor, so grab and drag can't disagree. A miss falls through to selecting
    // whatever the scene has under the cursor -- which is the behaviour under investigation, so it
    // is deliberately left alone here while the diagnostic below gathers evidence.
    private bool TryBeginGizmoDrag(Point cursor, in RtViewport vp, Matrix view, Matrix proj)
    {
        if (_selected == null || !Gizmo.HasValidTarget(_selected))
        {
            _lastPickDiag = $"pick=SKIP sel={_selected?.Name ?? "null"} "
                          + $"valid={_selected != null && Gizmo.HasValidTarget(_selected)}";
            return false;
        }

        var axis = Gizmo.PickAxis(_selected, _camera.Position, vp.ToPixel(cursor), view, proj,
                                  vp.Viewport, out float miss, out float score);
        _lastPickDiag = $"pick={axis} sel={_selected.Name} miss={miss:0.#}px x{score:0.#}";
        if (axis == GizmoAxis.None) return false;

        Gizmo.BeginDrag(axis, _selected, _camera.Position, vp.ScreenRay(cursor, view, proj), cursor);
        return true;
    }

    // ── Click diagnostics ───────────────────────────────────────────────────────────────────
    // Instrumentation for the "clicking a handle selects the object behind it" bug. Every frame's
    // pick goes into a ring; a click that changes the selection writes the ring out. The failure is
    // rare and happens faster than it can be read off the HUD, so it has to record itself.

    /// <summary>Where <see cref="DumpPickHistory"/> writes. Next to the executable.</summary>
    public static string DiagLogPath => Path.Combine(AppContext.BaseDirectory, "gizmo-diag.log");

    private void RecordPickSample(Point cursor, in RtViewport vp, Matrix view, Matrix proj,
                                  GizmoAxis hover, float miss, float score)
    {
        bool onScreen = Gizmo.TryProjectOrigin(_selected, view, proj, vp.Viewport, out var originPx);

        _pickHistory[_pickHistoryNext] = new PickSample(
            _frameCounter, cursor, vp.ToPixel(cursor), originPx, onScreen, hover, miss, score,
            _selected?.Name ?? "null", _selected?.Position3D ?? Vector3.Zero, _camera.Position);
        _pickHistoryNext = (_pickHistoryNext + 1) % PickHistoryFrames;
        if (_pickHistoryCount < PickHistoryFrames) _pickHistoryCount++;
    }

    // Append the ring, oldest first, plus what the click itself resolved to. Cursor and gizmo origin
    // are both in render-target pixels, so their distance is directly comparable to the ~20px grab
    // tolerance -- and comparing them ACROSS frames says whether it was the cursor or the gizmo that
    // moved between the frame that lit a handle and the frame that took the click.
    private void DumpPickHistory(Entity selBefore, Point cursor, in RtViewport vp)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {DateTime.Now:HH:mm:ss.fff} selection changed by click ===");
            sb.AppendLine($"  {LastClickDiag}");
            sb.AppendLine($"  before={selBefore?.Name ?? "null"} ({selBefore?.Position3D})  "
                        + $"after={_selected?.Name ?? "null"} ({_selected?.Position3D})");
            sb.AppendLine($"  mode={Gizmo.Mode} validBefore={Gizmo.HasValidTarget(selBefore)} "
                        + $"cursor={cursor} cursorPx={vp.ToPixel(cursor)} "
                        + $"bounds={vp.Bounds} rt={vp.Width}x{vp.Height}");

            for (int i = 0; i < _pickHistoryCount; i++)
            {
                var s = _pickHistory[(_pickHistoryNext - _pickHistoryCount + i + PickHistoryFrames * 2) % PickHistoryFrames];
                float gap = s.OriginOnScreen ? Vector2.Distance(s.CursorPx, s.OriginPx) : -1f;
                sb.AppendLine($"  f{s.Frame} hover={s.Hover,-4} miss={s.MissPx,7:0.#}px x{s.Score,6:0.##} "
                            + $"cursorPx=({s.CursorPx.X:0},{s.CursorPx.Y:0}) "
                            + $"originPx=({s.OriginPx.X:0},{s.OriginPx.Y:0}) onScreen={s.OriginOnScreen} "
                            + $"cursor->origin={gap:0.#}px target={s.Target} pos={s.TargetPos} cam={s.CamPos}");
            }

            File.AppendAllText(DiagLogPath, sb.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[editor] pick diag write failed: {ex.Message}");
        }
    }

    // ── Draw-time hooks ─────────────────────────────────────────────────────────────────────

    /// <summary>Draw the transform handles over the scene (inside the view's render target).</summary>
    public void DrawGizmo(GraphicsDevice device, Matrix view, Matrix proj)
    {
        if (EditorMode && _selected != null && Gizmo.HasValidTarget(_selected))
            Gizmo.Draw(device, _selected, _camera.Position, view, proj);
    }

    /// <summary>F4 live pick readout: run the same CPU pick a click uses, at the current cursor, so
    /// the HUD shows exactly what a click would grab. Pure math -- no GPU work.</summary>
    public void UpdatePickDebugReadout(in RtViewport vp, Matrix view, Matrix proj)
    {
        if (!Gizmo.ShowPickDebug || !EditorMode || _selected == null || !Gizmo.HasValidTarget(_selected))
            return;

        var axis = Gizmo.PickAxis(_selected, _camera.Position, vp.ToPixel(_lastCursor),
                                  view, proj, vp.Viewport, out float miss, out float score);
        // x<score> is the miss as a multiple of the handle's own tolerance: under 1 grabs, under
        // NearMissScore is swallowed as aimed-at-the-gizmo, above that selects the scene behind.
        GizmoPickDiag = $"under cursor: {axis} sel={_selected.Name} miss={miss:0.#}px x{score:0.#}";
    }

    /// <summary>The first line of the editor diagnostic HUD.</summary>
    public string StatusLine =>
        $"sel={_selected?.Name ?? "none"}  mode={Gizmo.Mode}  " +
        $"valid={Gizmo.HasValidTarget(_selected)}  cam={_camera.IsActive}";

    // ── Selection ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Select an entity (null clears). Public so other editor panels (hierarchy list) can drive the
    /// same selection/highlight/inspector notification as viewport click-picking.
    /// </summary>
    public void Select(Entity e)
    {
        if (e == _selected) return;

        if (_selectedMesh != null) _selectedMesh.HighlightFactor = 1f;  // clear old highlight

        _selected     = e;
        _selectedMesh = e?.GetComponent<Mesh3DComponent>();
        if (_selectedMesh != null)
            _selectedMesh.HighlightFactor = SelectedHighlight;  // non-destructive draw-time tint

        OnSelectionChanged?.Invoke(e);
    }

    /// <summary>
    /// Frame the selection (F, Unity-style): pull the camera back far enough that the entity's
    /// bounds fit the vertical FOV, keeping the current orientation so focusing never spins the view.
    /// </summary>
    public void FocusOnSelected()
    {
        if (_selected == null) return;

        Vector3 center;
        float radius;
        var mesh = _selected.GetComponent<Mesh3DComponent>();
        if (mesh != null && mesh.TryGetWorldBounds(_scene.SceneScale, out var bounds))
        {
            center = bounds.Center;
            radius = MathF.Max(bounds.Radius, 1e-3f);
        }
        else
        {
            center = _selected.Position3D * _scene.SceneScale;
            radius = FocusPointRadius;
        }

        float dist = _camera.Frame(center, radius, FocusMargin);
        Console.WriteLine($"[editor] focused {_selected.Name} (r={radius:0.##}, dist={dist:0.##})");
    }

    // ── Entity operations ───────────────────────────────────────────────────────────────────

    /// <summary>Spawn a bare Mesh3D entity for the given content-relative model path (e.g.
    /// "models/Cube.001") in front of the camera. Undoable; selects the new entity so it can be
    /// repositioned immediately.</summary>
    public void PlaceModel(string modelPath)
    {
        string fileName = modelPath.Replace('\\', '/').Split('/')[^1];
        var entity = new Entity { Name = EntityNameProvider.GetUniqueName(fileName) };
        entity.AddComponent(new Mesh3DComponent { ModelPath = modelPath, Scale = Vector3.One });
        PlaceEntity(entity);
    }

    /// <summary>Spawn an entity from a prefab XML (content-relative path, e.g.
    /// "files/prefabs/point_light.xml") in front of the camera. Undoable; selects the new entity.</summary>
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
        Vector3 forward = Vector3.Normalize(_camera.Target - _camera.Position);
        entity.Position3D = _camera.Position + forward * PlaceDistance;

        History.Execute(new AddEntityCommand(_scene, entity));
        NotifySceneChanged();
        Select(entity);
    }

    // Remove the selected entity via the command stack (undoable). Clears selection (which notifies
    // the inspector) and invalidates the pick tables so the cached mesh list rebuilds without it.
    private void DeleteSelected()
    {
        var target = _selected;
        if (target == null) return;

        Select(null);          // restores highlight + fires OnSelectionChanged(null)
        History.Execute(new DeleteEntityCommand(_scene, target));
        NotifySceneChanged();
        Console.WriteLine($"[editor] deleted {target.Name}");
    }

    // Clone the selected entity via its XML representation (same round-trip as delete/undo, so the
    // copy carries every component and property), give it a unique name, nudge it off the original
    // so the two aren't perfectly overlapping, and add it undoably. The clone becomes the selection.
    private void DuplicateSelected()
    {
        var source = _selected;
        if (source == null) return;

        var clone = EntityXml.Deserialize(EntityXml.Serialize(source));
        clone.Name = UniqueSceneName(source.Name);
        clone.Position3D = source.Position3D + new Vector3(DuplicateOffset, 0f, DuplicateOffset);

        History.Execute(new AddEntityCommand(_scene, clone));
        NotifySceneChanged();
        Select(clone);
    }

    // Produce a name not already used by an entity in the scene. We dedupe against the live scene
    // rather than EntityNameProvider because the editor loads scenes via Entity.FromXElement, which
    // assigns names directly without registering them -- so the global registry can't be trusted to
    // know about "monolith" and would happily hand the copy the same name.
    private string UniqueSceneName(string baseName)
    {
        if (_scene.FindEntityByName(baseName) == null) return baseName;
        for (int i = 2; ; i++)
        {
            string candidate = $"{baseName}_{i}";
            if (_scene.FindEntityByName(candidate) == null) return candidate;
        }
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

    /// <summary>
    /// Spawn the walker at (the nearest walkable point to) the current camera position and drop
    /// into Walk mode -- lets you playtest right from wherever you're looking instead of always
    /// starting at the saved PlayerStart. Also moves/creates the PlayerStart entity so the new spot
    /// is what a normal (non-editor) load will spawn at too.
    /// </summary>
    public void StartGameFromCamera()
    {
        var walker = _camera.Walker;
        if (walker == null) return;

        var spawn = walker.Mesh.NearestPointApprox(_camera.Position);
        if (!walker.Spawn(spawn))
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
            _scene.AddEntity(new Entity { Name = "PlayerStart", Position3D = spawn });
            NotifySceneChanged();
        }

        EditorMode = false;
        _camera.Mode = CameraMode.Walk;
        OnEditorModeChanged?.Invoke(false);
    }

    /// <summary>
    /// Migration/quick-start helper: tag every untagged Mesh3D entity as a navmesh obstacle, so a
    /// scene authored before NavMeshObstacleComponent existed (or a freshly placed prop) can be
    /// baked immediately. Not routed through <see cref="History"/> -- it's a one-time bulk action,
    /// not a single edit worth undoing entity-by-entity.
    /// </summary>
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

    // The pick tables cache the scene's entity list; any add/remove has to drop them.
    private void NotifySceneChanged()
    {
        _picker.Invalidate();
        OnSceneChanged?.Invoke();
    }

    public void Dispose() => Gizmo?.Dispose();
}
