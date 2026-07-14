using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;
using ld59.UI;
using ld59.WalkingSim;

// Desktop-app shell for the walking simulator. Modeled on Scene3DViewerUI, but launches the
// scene in Walk mode: loads the navmesh, spawns the walker at the scene's PlayerStart, and
// drives a first-person UI3DScene. Load failures fall back to a message rather than crashing
// the desktop.
public class WalkingSimUI : UIPanel
{
    private Window _rootContainer;
    private Rectangle _bounds;

    public WalkingSimUI(GameFile file)
    {
        int w = 1200, h = 800;
        int x = (Core.ScreenWidth  - w) / 2;
        int y = (Core.ScreenHeight - h) / 2;
        _bounds = new Rectangle(x, y, w, h);

        _rootContainer = new Window(_bounds, file.Name, Core.DefaultFont,
            ColorPalette.ActualWhite, ColorPalette.Black,
            ColorPalette.ActualWhite, ColorPalette.Black, 2);
        _rootContainer.SetCloseButtonColors(ColorPalette.Black, Color.DarkGray);
        Core.UISystem.AddElement(_rootContainer);
        TaskbarRegistry.Register("Walking Sim",
            Core.Content.Load<Texture2D>("images/image_viewer"), _rootContainer);

        var cb = _rootContainer.GetContentBounds();

        try
        {
            var asset = Scene3DAsset.Load(file.Content);
            var scene = Scene.FromFile(Core.Content, asset.ScenePath);
            scene.AmbientLightColor = new Color(60, 60, 70);
            scene.LightingEnabled   = true;
            scene.SceneScale        = 1f;   // UI3DScene initializes the entities

            var navMesh = LoadNavMesh(asset.NavMeshPath);
            var walker  = new WalkController(navMesh) { MoveSpeed = 3f };
            SpawnWalker(walker, scene, navMesh);

            var sceneView = new UI3DScene(cb, scene)
            {
                Mode   = CameraMode.Walk,
                Walker = walker,
            };
            // v1 interact action: surface the interactable's message. The Action verb is
            // reserved for later routing (open a file, reveal a glyph, set a story flag).
            sceneView.OnInteract += comp =>
            {
                int pw = 420, ph = 140;
                var rect = new Rectangle((Core.ScreenWidth - pw) / 2, (Core.ScreenHeight - ph) / 2, pw, ph);
                Core.UISystem.AddElement(new NotificationPopup(rect, comp.Message));
            };
            _rootContainer.AddChild(sceneView);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load walking-sim level '{file.Name}': {ex.Message}");
            _rootContainer.AddChild(new Label(cb, "Failed to load level.", Core.DefaultFont, Color.White));
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
