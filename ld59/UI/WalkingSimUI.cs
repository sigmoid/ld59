using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;
using ld59.UI;
using ld59.UI.Editor;
using ld59.WalkingSim;

// Desktop-app shell for the walking simulator. Modeled on Scene3DViewerUI, but launches the
// scene in Walk mode: loads the navmesh, spawns the walker at the scene's PlayerStart, and
// drives a first-person UI3DScene. Load failures fall back to a message rather than crashing
// the desktop.
//
// Unlike the other apps this one is the desktop's backdrop: it fills the screen, sits behind the
// taskbar and every other window, and can't be dragged around (see Window.IsBackgroundWindow).
// The taskbar and start menu stay clickable on top of it, and the apps they launch open over the
// world rather than under it.
public class WalkingSimUI : UIPanel
{
    private Window _rootContainer;
    private Rectangle _bounds;
    private ld59.WalkingSim.PuzzleSolveOverlay _activeOverlay;

    public WalkingSimUI(GameFile file)
    {
        _bounds = new Rectangle(0, 0, Core.ScreenWidth, Core.ScreenHeight);

        // No border and square corners: the backdrop has no edges to draw, and at fullscreen the
        // rounded-corner masks would be four screen-sized textures for a couple of pixels.
        _rootContainer = new Window(_bounds, file.Name, Core.DefaultFont,
            ColorPalette.ActualWhite, ColorPalette.Black,
            ColorPalette.ActualWhite, ColorPalette.Black, borderThickness: 0, titleBarRadius: 0);
        _rootContainer.SetCloseButtonColors(ColorPalette.Black, Color.DarkGray);
        _rootContainer.IsBackgroundWindow = true;
        _rootContainer.SetBounds(_bounds);   // undo the new-window cascade offset -- this one is pinned
        Core.UISystem.AddElement(_rootContainer);
        // Closing the window must also close any open puzzle (RemoveElement doesn't fire child
        // cleanup, so hook the window's close event directly).
        _rootContainer.OnWindowClosed += _ => _activeOverlay?.ForceClose();
        TaskbarRegistry.Register("Walking Sim",
            Core.Content.Load<Texture2D>("images/image_viewer"), _rootContainer);

        var cb = _rootContainer.GetContentBounds();

        try
        {
            var asset = Scene3DAsset.Load(file.Content);
            var scene = Scene.FromFile(Core.Content, asset.ScenePath);
            scene.AmbientLightColor = ParseColor(asset.Ambient, new Color(60, 60, 70));
            scene.LightingEnabled   = true;
            scene.SceneScale        = 1f;   // UI3DScene initializes the entities

            var navMesh = LoadNavMesh(asset.NavMeshPath);
            var walker  = new WalkController(navMesh);
            SpawnWalker(walker, scene, navMesh);

            // Prefer the Content SOURCE dir (under version control) so bakes/saves survive a
            // rebuild; fall back to the runtime content dir (still usable this session) if the
            // source tree can't be located, e.g. a published build with no project file nearby.
            string contentRoot = EditorPaths.FindContentSourceDir();
            if (contentRoot == null)
            {
                contentRoot = Core.Content.RootDirectory;
                Console.WriteLine("[editor] could not locate Content source dir; " +
                                   "bake/save will write to the runtime content dir instead");
            }

            var sceneView = new UI3DScene(cb, scene)
            {
                Mode   = CameraMode.Walk,
                Walker = walker,
                // Walk/fly speeds are separate knobs: strolling pace on foot, but a fast detached
                // camera so the editor can cross the level. The camera drives the walker's speed,
                // so this has to live here rather than on the WalkController.
                WalkSpeed = 13f,
                NavMeshSavePath = Path.Combine(contentRoot, asset.NavMeshPath),
                ScenePath       = Path.Combine(contentRoot, asset.ScenePath),
                ShowSkybox      = asset.Skybox,
            };

            // Exponential fog over the 3D pass. Enabled here (rather than by default on every
            // UI3DScene) so other 3D views -- the pinball table, the scene previewer -- keep their
            // flat look. Tune live with the `fog` console command; a scene that draws its own sky
            // keeps it visible instead of fading the horizon to the fog colour.
            sceneView.Fog.Enabled = true;
            sceneView.Fog.BackgroundFog = asset.Skybox ? 0.35f : 1f;

            // Silhouette outlines off the same depth/id pass the fog already runs, so they cost
            // three full-screen blits and no extra scene draw. Tune with `outline`.
            sceneView.Outline.Enabled = true;

            // Ambient occlusion off that same depth pass -- again no extra scene draw, but unlike
            // the fog and the outlines it is not nearly free: it samples a hemisphere per pixel.
            // `ssao downscale 2` roughly quarters that if the frame gets tight. Tune with `ssao`.
            sceneView.Ssao.Enabled = true;

            // The view covers the whole screen, so a click on the taskbar, the start menu or an app
            // window floating over the world lands "inside" it too. The pointer only belongs to the
            // world when the backdrop really is the topmost thing under the cursor.
            sceneView.OwnsPointer = () =>
                Core.UISystem.GetElementAtPosition(Core.GetTransformedMousePosition()) == _rootContainer;

            // Interacting with a puzzle object opens the focused solve view; everything else
            // routes through the dispatcher (switches on Action). Pass the walk scene so
            // reveal/hide/toggle can resolve targets.
            sceneView.OnInteract += comp =>
            {
                var puzzle = comp.Entity?.GetComponent<PuzzlePanelComponent>();
                // Only open the solve view for a configured puzzle whose scene actually loads;
                // an unconfigured/broken panel falls through to the normal interaction dispatch.
                if (puzzle != null && puzzle.GetOrCreateView(new Rectangle(0, 0, 1, 1)) != null)
                {
                    _activeOverlay = new PuzzleSolveOverlay(puzzle, sceneView, scene);
                    Core.UISystem.AddElement(_activeOverlay);
                }
                else
                    InteractionDispatcher.Dispatch(comp, scene);
            };
            _rootContainer.AddChild(sceneView);

            // Editor inspector: a panel on the right that reflects the selected entity's
            // properties (Position, Mesh3D NoCollide, light Range/Intensity/Color, ...).
            var inspector = new EditorInspector(new Rectangle(_bounds.Right - 300, _bounds.Y, 290, 520), sceneView.History);
            sceneView.IsTextInputFocused = () => inspector.HasFocusedInput();
            sceneView.OnSelectionChanged += entity => inspector.Show(entity);
            sceneView.OnEntityLiveUpdate += _ => inspector.Tick();

            // Hierarchy: a panel on the left listing every entity (incl. lights/spawns the
            // viewport can't click-select), so selection isn't limited to meshes.
            var hierarchy = new EditorHierarchyPanel(new Rectangle(_bounds.X, _bounds.Y, 260, 520),
                entity => sceneView.Select(entity), () => sceneView.StartGameFromCamera());
            sceneView.OnSelectionChanged += entity => { hierarchy.SetSelected(entity); hierarchy.Refresh(scene); };
            sceneView.OnSceneChanged     += () => hierarchy.Refresh(scene);

            // Navmesh panel: bake button, overlay toggle, bulk-tag migration helper.
            var navPanel = new EditorNavMeshPanel(new Rectangle(_bounds.X, _bounds.Bottom - 180, 260, 170), sceneView);

            // Content browser: spawn built models or authored prefabs in front of the camera.
            var contentBrowser = new EditorContentBrowser(
                new Rectangle(_bounds.Right - 300, _bounds.Bottom - 260, 290, 250), sceneView, contentRoot);

            // The main window is the fullscreen backdrop and never moves, so on entering the editor
            // the tool panels overlay it instead of sitting beside it: hierarchy + navmesh down the
            // left edge, inspector + content browser down the right, leaving the middle of the
            // viewport clear. Panels are only translated (kept at their built size) so their
            // contents stay laid out.
            void ArrangeEditorWindows()
            {
                const int m = 12, gap = 12;
                int colA = cb.Left  + m;          // left column
                int colB = cb.Right - 290 - m;    // right column, flush to the far edge
                hierarchy.MoveTo(new Rectangle(colA, cb.Top + m,             260, 520));
                navPanel.MoveTo (new Rectangle(colA, cb.Top + m + 520 + gap, 260, 170));
                inspector.MoveTo(new Rectangle(colB, cb.Top + m,             290, 520));
                contentBrowser.MoveTo(new Rectangle(colB, cb.Top + m + 520 + gap, 290, 250));
            }

            sceneView.OnEditorModeChanged += on =>
            {
                inspector.SetVisible(on);
                hierarchy.SetVisible(on);
                navPanel.SetVisible(on);
                contentBrowser.SetVisible(on);
                if (on)
                {
                    hierarchy.Refresh(scene);
                    ArrangeEditorWindows();
                }
                // Leaving the editor only hides the panels -- the backdrop window is already
                // fullscreen and pinned, so there is no position to restore.
            };
        }
        catch (Exception ex)
        {
            // Show the error visibly (black on the window's white background) instead of a
            // white-on-white label, so load failures aren't a blank white window.
            Logger.Error($"Failed to load walking-sim level '{file.Name}': {ex}");
            _rootContainer.AddChild(new Label(cb,
                $"Failed to load level:\n{ex.GetType().Name}: {ex.Message}",
                Core.DefaultFont, Color.Black));
        }
    }

    private static NavMesh LoadNavMesh(string navMeshPath)
    {
        if (string.IsNullOrEmpty(navMeshPath))
            throw new InvalidOperationException("Scene3DAsset has no NavMeshPath for Walk mode.");

        using var stream = TitleContainer.OpenStream(
            Path.Combine(Core.Content.RootDirectory, navMeshPath));
        using var reader = new StreamReader(stream);
        return ObjParser.LoadNavMesh(reader);
    }

    // Parse "r,g,b" (0-255) into a Color; returns the fallback if empty/malformed.
    private static Color ParseColor(string s, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        var parts = s.Split(',');
        if (parts.Length < 3) return fallback;
        if (int.TryParse(parts[0].Trim(), out int r) &&
            int.TryParse(parts[1].Trim(), out int g) &&
            int.TryParse(parts[2].Trim(), out int b))
            return new Color(r, g, b);
        return fallback;
    }

    private static void SpawnWalker(WalkController walker, Scene scene, NavMesh navMesh)
    {
        var start = scene.FindEntityByName("PlayerStart");
        if (start != null && walker.Spawn(start.Position3D))
            return;

        // fall back to the centroid of the first triangle so the level is still playable
        if (navMesh.Triangles.Length > 0)
        {
            var t = navMesh.Triangles[0];
            var centroid = (navMesh.Vertices[t.V0] + navMesh.Vertices[t.V1] + navMesh.Vertices[t.V2]) / 3f;
            walker.Spawn(centroid);
        }
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        _bounds = bounds;
    }

    public override Rectangle GetBoundingBox() => _bounds;
}
