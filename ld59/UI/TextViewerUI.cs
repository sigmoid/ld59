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

        var dataManager = Core.CurrentScene.GetManager<GameFileDataManager>();
        var didUnlock = dataManager?.UnlockData(file);

        if(file.IsNewDiscovery)
        {
            file.IsNewDiscovery = false;
        }

        if(didUnlock == true)
        {
            AudioAtlas.Confirmation_002.Play();
            DesktopUI.ToastManager.ShowSuccess($"New info unlocked!", 3, Toast.ToastPosition.TopRight);
        }

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
        _rootContainer = new Window(_bounds, _currentFile.Name, Core.DefaultFont, ColorPalette.ActualWhite, ColorPalette.Black, ColorPalette.ActualWhite, ColorPalette.Black, 2);
        Core.UISystem.AddElement(_rootContainer);
        TaskbarRegistry.Register("Notepad", Core.Content.Load<Microsoft.Xna.Framework.Graphics.Texture2D>("images/file_icon"), _rootContainer);
        _rootContainer.SetCloseButtonColors(ColorPalette.Black, Color.DarkGray);

        var textAreaBounds = new Rectangle(_rootContainer.GetContentBounds().X + 10, _rootContainer.GetContentBounds().Y + 10, _rootContainer.GetContentBounds().Width - 20, _rootContainer.GetContentBounds().Height - 20);
        _textArea = new TextArea(textAreaBounds, Core.DefaultFont, true, _isReadOnly, ColorPalette.ActualWhite, ColorPalette.Black, ColorPalette.DarkGreen, ColorPalette.LightGreen);
        
        if(_currentFile.IsEncrypted)
        {
            _textArea.Text = "This file is encrypted. Find the necessary information to unlock it\n";
        }
        else
        {
            _textArea.Text = _currentFile.Content;
            foreach (var info in _currentFile.Info)
            {
                if (!info.IsUnlocked || string.IsNullOrEmpty(info.Value)) continue;
                var color = info.Type switch
                {
                    InfoType.Name         => ColorPalette.InfoName,
                    InfoType.Rank         => ColorPalette.InfoRank,
                    InfoType.Position     => ColorPalette.InfoPosition,
                    InfoType.Codename     => ColorPalette.InfoCodename,
                    InfoType.Verb         => ColorPalette.InfoVerb,
                    InfoType.CauseOfDeath => ColorPalette.InfoCauseOfDeath,
                    _                     => ColorPalette.Black
                };
                _textArea.AddHighlight(info.Value, color);
            }
        }
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