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
    private Texture2D _backgroundTexture;

    private StartMenuUI _startMenuUI;
    private FileExplorerUI _fileExplorerUI;
    private static ToastManager _toastManager;
    private ClockUI _clockUI;

    private float _taskbarItemSize = 80;
    private float _startingNoteTimer = 1;

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

    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);

        if (_startingNoteTimer > 0)
        {
            _startingNoteTimer -= deltaTime;
            if (_startingNoteTimer <= 0)
            {
                var gameDataManager = Core.CurrentScene.GetManager<GameFileDataManager>();
                var startingNote = gameDataManager.GetFileByPath("readme.txt");
                var fileExplorerUI = new TextViewerUI(new Rectangle(150, 150, 700, 600), startingNote);
                Core.UISystem.AddElement(fileExplorerUI);
            }
        }
    }

    private void CreateUI()
    {
        _toastManager = new ToastManager(Core.UISystem, Core.DefaultFont, new Rectangle(_bounds.X, _bounds.Y, _bounds.Width, _bounds.Height));

        var taskBarArea = new Rectangle(0, _bounds.Height - 100, _bounds.Width, 100);
        _taskbar = new Canvas(taskBarArea, ColorPalette.Green) { Order = 0.9f };

        var taskbarLayoutPadding = 10;
        var taskbarLayoutArea = new Rectangle(taskBarArea.X + taskbarLayoutPadding, taskBarArea.Y + taskbarLayoutPadding, taskBarArea.Width - (taskbarLayoutPadding * 2), taskBarArea.Height - (taskbarLayoutPadding * 2));
        _taskbarLayout = new HorizontalLayoutGroup(taskbarLayoutArea, 15);

        var startButtonTexture = Core.Content.Load<Texture2D>("images/start_menu"); 
        var startButton = new ImageButton(new Rectangle(0, 0, (int)_taskbarItemSize, (int)_taskbarItemSize), startButtonTexture, () => ToggleStartMenu());
        _taskbarLayout.AddChild(startButton);

        // var fileExplorerTexture = Core.Content.Load<Texture2D>("images/file_explorer");
        // var fileExplorerButton = new ImageButton(new Rectangle(0, 0, (int)_taskbarItemSize, (int)_taskbarItemSize), fileExplorerTexture, () => ActivateFileExplorer());
        // _taskbarLayout.AddChild(fileExplorerButton);

        _backgroundTexture = Core.Content.Load<Texture2D>("images/background");
        var background = new UIImage(_backgroundTexture, new Rectangle(0, 0, _bounds.Width, _bounds.Height));
        AddChild(background);

        _taskbar.AddChild(_taskbarLayout);
        Core.UISystem.AddElement(_taskbar);

        _clockUI = new ClockUI(new Rectangle(_bounds.Width - 200, taskBarArea.Top, 200, 100)) { Order = 0.9f };
        Core.UISystem.AddElement(_clockUI);
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




}