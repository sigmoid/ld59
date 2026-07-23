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
public class WalkingSimUI : UIPanel
{
    private Window _rootContainer;
    private Rectangle _bounds;
    private Rectangle _homeBounds;   // the main window's default (centered) position
    private ld59.WalkingSim.PuzzleSolveOverlay _activeOverlay;

    public WalkingSimUI(GameFile file)
    {
        int w = 1200, h = 800;
        int x = (Core.ScreenWidth  - w) / 2;
        int y = (Core.ScreenHeight - h) / 2;
        _bounds = new Rectangle(x, y, w, h);
        _homeBounds = _bounds;

        _rootContainer = new Window(_bounds, file.Name, Core.DefaultFont,
            ColorPalette.ActualWhite, ColorPalette.Black,
            ColorPalette.ActualWhite, ColorPalette.Black, 2);
        _rootContainer.SetCloseButtonColors(ColorPalette.Black, Color.DarkGray);
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
            var walker  = new WalkController(navMesh) { MoveSpeed = 3f };
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
                NavMeshSavePath = Path.Combine(contentRoot, asset.NavMeshPath),
                ScenePath       = Path.Combine(contentRoot, asset.ScenePath),
                ShowSkybox      = asset.Skybox,
            };
            // Interacting with a puzzle object opens the focused solve view; everything else
            // routes through the dispatcher (switches on Action). Pass the walk scene so
            // reveal/hide/toggle can resolve targets.
            sceneView.OnInteract += comp =>
            {
                var puzzle = comp.Entity?.GetComponent<PuzzlePanelComponent>();
                if (puzzle != null)
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

            // On entering the editor, dock the main window top-left and fan the tool panels out
            // in the strip to its right so nothing overlaps the viewport and every panel is
            // reachable. Panels are only translated (kept at their built size) so their contents
            // stay laid out. On exit, the main window returns to its centered position.
            void ArrangeEditorWindows()
            {
                const int m = 12, gap = 12;
                _rootContainer.SetBounds(new Rectangle(m, m, _homeBounds.Width, _homeBounds.Height));

                int colA = m + _homeBounds.Width + gap;   // first panel column, right of main window
                int colB = colA + 260 + gap;              // second column (right of the 260-wide column)
                hierarchy.MoveTo(new Rectangle(colA, m,             260, 520));
                navPanel.MoveTo (new Rectangle(colA, m + 520 + gap, 260, 170));
                inspector.MoveTo(new Rectangle(colB, m,             290, 520));
                contentBrowser.MoveTo(new Rectangle(colB, m + 520 + gap, 290, 250));
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
                else
                {
                    _rootContainer.SetBounds(_homeBounds);   // restore centered position
                }
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
