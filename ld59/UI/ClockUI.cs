using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

public class ClockUI : UIElement
{
    private Label _clockLabel;
    private Label _dateLabel;

    private Canvas _backgroundCanvas;

    private Rectangle _bounds;
    private SettingsUI _settingsUI;

    public ClockUI(Rectangle bounds)
    {
        _bounds = bounds;
        CreateUI();
    }

    public override void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        base.SetBounds(bounds);

    }

    public override Rectangle GetBoundingBox()
    {
        return _bounds;
    }

    public override void Update(float deltaTime)
    {
        _clockLabel.Text = DateTime.Now.ToString("hh:mm:ss tt");
        _dateLabel.Text = DateTime.Now.ToString("MMMM dd, yyyy");

        _backgroundCanvas.Update(deltaTime);

        base.Update(deltaTime);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        _backgroundCanvas.Draw(spriteBatch);
        base.Draw(spriteBatch);
    }

    private void CreateUI()
    {
        var backgroundColor = Color.Black * 0.2f;
        _backgroundCanvas = new Canvas(_bounds, backgroundColor);

        var centerY = _bounds.Y + (_bounds.Height / 2);
        var x = _bounds.X + 40;

        var settingsIcon = Core.Content.Load<Texture2D>("images/settings_icon");
        var iconSize = 24;
        var iconElement = new ImageButton(new Rectangle(x - 16, centerY - (iconSize / 2), iconSize, iconSize), settingsIcon, () => OpenSettings());
        _backgroundCanvas.AddChild(iconElement);

        _clockLabel = new Label(new Rectangle(x + iconSize + 10, centerY - 30, _bounds.Width - iconSize - 10, 30), DateTime.Now.ToString("hh:mm:ss tt"), Core.DefaultFont, ColorPalette.ActualWhite);
        _dateLabel = new Label(new Rectangle(x + iconSize + 10, centerY, _bounds.Width - iconSize - 10, 30), DateTime.Now.ToString("MMMM dd, yyyy"), Core.DefaultFont, ColorPalette.ActualWhite);

        _backgroundCanvas.AddChild(_clockLabel);
        _backgroundCanvas.AddChild(_dateLabel);
    }

    private void OpenSettings()
    {
        if (_settingsUI != null)
        {
            TaskbarRegistry.BringToFront("Settings");
            return;
        }

        int w = 500, h = 300;
        var bounds = new Rectangle(
            (Core.ScreenWidth - w) / 2,
            (Core.ScreenHeight - h) / 2,
            w, h);
        _settingsUI = new SettingsUI(bounds);
        _settingsUI.OnClose += () => _settingsUI = null;
    }
}