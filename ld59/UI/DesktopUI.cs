using System.Diagnostics.Contracts;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

public class DesktopUI : UIPanel
{
    public static ToastManager ToastManager => _toastManager;
    private Canvas _taskbar;
    private HorizontalLayoutGroup _taskbarLayout;
    private Rectangle _bounds;

    private StartMenuUI _startMenuUI;
    private FileExplorerUI _fileExplorerUI;
    private static ToastManager _toastManager;

    private float _taskbarItemSize = 80;

    public DesktopUI(Rectangle bounds)
    {
        _bounds = bounds;

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
        _toastManager = new ToastManager(Core.UISystem, Core.DefaultFont, new Rectangle(_bounds.X, _bounds.Y, _bounds.Width, _bounds.Height));

        var taskBarArea = new Rectangle(0, _bounds.Height - 100, _bounds.Width, 100);
        _taskbar = new Canvas(taskBarArea, ColorPalette.Green);

        var taskbarLayoutPadding = 10;
        var taskbarLayoutArea = new Rectangle(taskBarArea.X + taskbarLayoutPadding, taskBarArea.Y + taskbarLayoutPadding, taskBarArea.Width - (taskbarLayoutPadding * 2), taskBarArea.Height - (taskbarLayoutPadding * 2));
        _taskbarLayout = new HorizontalLayoutGroup(taskbarLayoutArea, 15);

        var startButtonTexture = Core.Content.Load<Texture2D>("images/start_menu"); 
        var startButton = new ImageButton(new Rectangle(0, 0, (int)_taskbarItemSize, (int)_taskbarItemSize), startButtonTexture, () => ToggleStartMenu());
        _taskbarLayout.AddChild(startButton);

        var fileExplorerTexture = Core.Content.Load<Texture2D>("images/file_explorer");
        var fileExplorerButton = new ImageButton(new Rectangle(0, 0, (int)_taskbarItemSize, (int)_taskbarItemSize), fileExplorerTexture, () => ActivateFileExplorer());
        _taskbarLayout.AddChild(fileExplorerButton);

        _taskbar.AddChild(_taskbarLayout);
        AddChild(_taskbar);
    }

    private void ToggleStartMenu()
    {
        if (_startMenuUI == null)
        {
            _startMenuUI = new StartMenuUI(new Rectangle(5, _taskbar.GetBoundingBox().Top - 600, 400, 600));
            _startMenuUI.OnClose += () => _startMenuUI = null;
            AddChild(_startMenuUI);
        }
        else
        {
            RemoveChild(_startMenuUI);
            _startMenuUI = null;
        }
    }

    private void ActivateFileExplorer()
    {
        if (_fileExplorerUI == null)
        {
            _fileExplorerUI = new FileExplorerUI(new Rectangle(100, 100, 800, 600), () => _fileExplorerUI = null, OpenFile);
            AddChild(_fileExplorerUI);
        }
    }

    private void OpenFile(GameFile file)
    {
        var textViewer = new TextViewerUI(new Rectangle(150, 150, 400, 500), file);
        AddChild(textViewer);
    }
}