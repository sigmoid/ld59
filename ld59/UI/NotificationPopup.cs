using Microsoft.Xna.Framework;
using Quartz;
using Quartz.UI;

public class NotificationPopup : UIPanel
{
    private Window _rootContainer;
    private TextArea _messageLabel;
    private Rectangle _bounds;
    private string _message;

    public NotificationPopup(Rectangle bounds, string message)
    {
        _bounds = bounds;
        _message = message;

        CreateUI();
    }

    private void CreateUI()
    {
        _rootContainer = new Window(_bounds, "Notification", Core.DefaultFont, ColorPalette.ActualWhite, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.DarkGreen, 2);
        Core.UISystem.AddElement(_rootContainer);
        _rootContainer.SetCloseButtonColors(ColorPalette.DarkGreen, ColorPalette.LightGreen);
        Core.UISystem.WindowManager.SetFocusedWindow(_rootContainer);

        var labelBounds = new Rectangle(_rootContainer.GetContentBounds().X + 10, _rootContainer.GetContentBounds().Y + 10, _rootContainer.GetContentBounds().Width - 20, _rootContainer.GetContentBounds().Height - 20);
        _messageLabel = new TextArea(labelBounds, Core.DefaultFont, true);
        _messageLabel.Text = _message;
        _rootContainer.AddChild(_messageLabel);
    }

    private void Close()
    {
        // Remove from parent if it exists (this will trigger proper cleanup)
        var parent = GetParent() as UIContainer;

        if (parent != null)
        {
            parent?.DestroyChild(this);
        }
        else
        {
            _rootContainer.Close();
            Core.UISystem.RemoveElement(_rootContainer);
        }
    }
}