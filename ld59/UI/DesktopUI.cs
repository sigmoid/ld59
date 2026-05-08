using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
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
    private List<UIElement> _taskbarAppItems = new();
    private float _startingNoteTimer = 1f; // delay the starting note a bit so it doesn't get lost in the chaos of the start

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
                var emailManager = Core.CurrentScene.GetManager<EmailDataManager>();
                emailManager.DeliverEmail("welcome.eml");
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

        _clockUI = new ClockUI(new Rectangle(_bounds.Width - 275, taskBarArea.Top, 275, 100)) { Order = 0.9f };
        Core.UISystem.AddElement(_clockUI);

        TaskbarRegistry.OnChanged += RebuildTaskbarApps;
        EmailDataManager.OnEmailDelivered += OnEmailReceived;
    }

    private void OnEmailReceived(Email email)
    {
        _toastManager.ShowSuccess($"New email: {email.Subject}", 4, Toast.ToastPosition.TopRight);

        var apps = TaskbarRegistry.GetApps();
        bool emailOpen = apps.Any(a => a.Name == "Email");
        if (emailOpen)
            TaskbarRegistry.BringToFront("Email");
        else
        {
            var emailListUI = new EmailListUI(new Rectangle(150, 150, 700, 600));
            Core.UISystem.AddElement(emailListUI);
        }
    }

    private void RebuildTaskbarApps()
    {
        foreach (var item in _taskbarAppItems)
            _taskbarLayout.RemoveChild(item);
        _taskbarAppItems.Clear();

        foreach (var (name, icon) in TaskbarRegistry.GetApps())
        {
            var item = new TaskbarItemUI(new Rectangle(0, 0, (int)_taskbarItemSize, (int)_taskbarItemSize), icon, name);
            _taskbarLayout.AddChild(item);
            _taskbarAppItems.Add(item);
        }
    }

    private void ToggleStartMenu()
    {
        if (_startMenuUI == null)
        {
            _startMenuUI = new StartMenuUI(new Rectangle(5, _taskbar.GetBoundingBox().Top - 700, 400, 700));
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