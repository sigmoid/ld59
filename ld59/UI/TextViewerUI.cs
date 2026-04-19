using Microsoft.Xna.Framework;
using Quartz;
using Quartz.UI;

public class TextViewerUI : UIPanel
{
    private Window _rootContainer;

    private Rectangle _bounds;

    private GameFile _currentFile;
    private TextArea _textArea;
    private bool _isReadOnly;

    public TextViewerUI(Rectangle bounds, GameFile file, bool isReadOnly = true)
    {
        _bounds = bounds;
        _currentFile = file;
        _isReadOnly = isReadOnly;
        CreateUI();    
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        _bounds = bounds;
    }

    public override Rectangle GetBoundingBox()
    {
        return _bounds;
    }

    private void CreateUI()
    {
        _rootContainer = new Window(_bounds, _currentFile.Name, Core.DefaultFont, ColorPalette.ActualWhite, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.DarkGreen, 2);
        Core.UISystem.AddElement(_rootContainer);
        _rootContainer.SetCloseButtonColors(ColorPalette.DarkGreen, ColorPalette.LightGreen);

        var textAreaBounds = new Rectangle(_rootContainer.GetContentBounds().X + 10, _rootContainer.GetContentBounds().Y + 10, _rootContainer.GetContentBounds().Width - 20, _rootContainer.GetContentBounds().Height - 20);
        _textArea = new TextArea(textAreaBounds, Core.DefaultFont, true, _isReadOnly, ColorPalette.ActualWhite, ColorPalette.Black, ColorPalette.DarkGreen, ColorPalette.LightGreen);
        _textArea.Text = _currentFile.Content;
        _rootContainer.AddChild(_textArea);
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
            Core.UISystem.RemoveElement(_rootContainer);
        }
    }
}