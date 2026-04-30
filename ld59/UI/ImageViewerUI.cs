using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

public class ImageViewerUI : UIPanel
{
    private Window _rootContainer;
    private Rectangle _bounds;

    public ImageViewerUI(GameFile file)
    {
        int titleBarHeight = (int)Core.DefaultFont.MeasureString("A").Y + 10;
        int padding = 10;
        int borderThickness = 2;

        if (file.IsEncrypted)
        {
            int windowWidth = 400;
            int windowHeight = 150;
            int x = (Core.ScreenWidth - windowWidth) / 2;
            int y = (Core.ScreenHeight - windowHeight) / 2;
            _bounds = new Rectangle(x, y, windowWidth, windowHeight);
            CreateEncryptedUI(file.Name);
        }
        else
        {
            var texture = Core.Content.Load<Texture2D>(file.Content);
            int windowWidth = texture.Width + padding * 2 + borderThickness * 2;
            int windowHeight = texture.Height + padding * 2 + titleBarHeight + borderThickness;
            int x = (Core.ScreenWidth - windowWidth) / 2;
            int y = (Core.ScreenHeight - windowHeight) / 2;
            _bounds = new Rectangle(x, y, windowWidth, windowHeight);
            CreateUI(texture, file.Name, padding);
        }
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        _bounds = bounds;
    }

    public override Rectangle GetBoundingBox() => _bounds;

    private void CreateUI(Texture2D texture, string name, int padding)
    {
        _rootContainer = new Window(_bounds, name, Core.DefaultFont,
            ColorPalette.ActualWhite, ColorPalette.DarkGreen,
            ColorPalette.ActualWhite, ColorPalette.DarkGreen, 2);
        Core.UISystem.AddElement(_rootContainer);
        TaskbarRegistry.Register("Image Viewer", Core.Content.Load<Texture2D>("images/image_viewer"), _rootContainer);
        _rootContainer.SetCloseButtonColors(ColorPalette.DarkGreen, ColorPalette.LightGreen);

        var content = _rootContainer.GetContentBounds();
        var imageBounds = new Rectangle(content.X + padding, content.Y + padding, texture.Width, texture.Height);
        var image = new UIImage(texture, imageBounds, Color.White);
        _rootContainer.AddChild(image);
    }

    private void CreateEncryptedUI(string name)
    {
        _rootContainer = new Window(_bounds, name, Core.DefaultFont,
            ColorPalette.ActualWhite, ColorPalette.DarkGreen,
            ColorPalette.ActualWhite, ColorPalette.DarkGreen, 2);
        Core.UISystem.AddElement(_rootContainer);
        TaskbarRegistry.Register("Image Viewer", Core.Content.Load<Texture2D>("images/image_viewer"), _rootContainer);
        _rootContainer.SetCloseButtonColors(ColorPalette.DarkGreen, ColorPalette.LightGreen);

        var content = _rootContainer.GetContentBounds();
        var label = new Label(new Rectangle(content.X + 10, content.Y + 10, content.Width - 20, content.Height - 20), "This file is encrypted.", Core.DefaultFont, ColorPalette.DarkGreen);
        _rootContainer.AddChild(label);
    }
}
