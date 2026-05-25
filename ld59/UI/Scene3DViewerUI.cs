using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;
using ld59.UI;

public class Scene3DViewerUI : UIPanel
{
    private Window _rootContainer;
    private Rectangle _bounds;

    public Scene3DViewerUI(GameFile file)
    {
        int w = 800, h = 700;
        int x = (Core.ScreenWidth  - w) / 2;
        int y = (Core.ScreenHeight - h) / 2;
        _bounds = new Rectangle(x, y, w, h);

        _rootContainer = new Window(_bounds, file.Name, Core.DefaultFont,
            ColorPalette.ActualWhite, ColorPalette.Black,
            ColorPalette.ActualWhite, ColorPalette.Black, 2);
        _rootContainer.SetCloseButtonColors(ColorPalette.Black, Color.DarkGray);
        Core.UISystem.AddElement(_rootContainer);

        var cb = _rootContainer.GetContentBounds();
        var scene = LoadScene(file);
        scene.AmbientLightColor = new Color(0.4f, 0.4f, 0.4f, 1f);
        scene.SceneScale = 0.01f;
        var sceneView = new UI3DScene(cb, scene);
        sceneView.CameraPosition = new Vector3(0f, 1.5f, 4f);
        sceneView.CameraTarget   = Vector3.Zero;
        _rootContainer.AddChild(sceneView);
    }

    private static Scene LoadScene(GameFile file)
    {
        if (file.FileType == FileType.Scene3D && !string.IsNullOrEmpty(file.Content))
        {
            try
            {
                var asset = Scene3DAsset.Load(file.Content);
                return Scene.FromFile(Core.Content, asset.ScenePath);
            }
            catch { }
        }
        return BuildDemoScene();
    }

    private static Scene BuildDemoScene()
    {
        var scene = new Scene();
        var entity = new Entity { Name = "DemoBox" };
        entity.AddComponent(new BoxPrimitive3DComponent
        {
            Scale = new Vector3(1.5f, 1.5f, 1.5f),
            Color = Color.SteelBlue,
        });
        scene.AddEntity(entity);
        return scene;
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        _bounds = bounds;
    }

    public override Rectangle GetBoundingBox() => _bounds;
}
