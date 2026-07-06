using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

public class StartMenuUI : UIPanel
{
    public Action OnClose;
    private Rectangle _bounds;

    private Canvas _rootElement;

    private VerticalLayoutGroup _layoutGroup;
    private bool _lastLeftButtonState = true;

    public StartMenuUI(Rectangle bounds)
    {
        _bounds = bounds;
        CreateUI();
        this.Order = 0.9f;
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
        // if user clicks outside the start menu close it
        var mouseState = Microsoft.Xna.Framework.Input.Mouse.GetState();
        if (!GetBoundingBox().Contains(Core.GetTransformedMousePoint()) && mouseState.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed && !_lastLeftButtonState)
        {
            Core.UISystem.RemoveElement(_rootElement);
            OnClose?.Invoke(); 
        }
        _lastLeftButtonState = mouseState.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed;

        base.Update(deltaTime);
    }

    private void CreateUI()
    {
        _rootElement = new Canvas(_bounds, ColorPalette.DarkGreen);
        _rootElement.Order = 0.9f;

        _layoutGroup = new VerticalLayoutGroup(new Rectangle(_bounds.X + 10, _bounds.Y + 10, _bounds.Width - 20, _bounds.Height - 20), 10);

        var notepadIcon = Core.Content.Load<Texture2D>("images/file_icon");
        var notepadButton = new StartMenuItemUI(new Rectangle(_layoutGroup.GetBoundingBox().X, _layoutGroup.GetBoundingBox().Y, _layoutGroup.GetBoundingBox().Width, 80), notepadIcon, "Notepad", () => OpenNotepad());
        _layoutGroup.AddChild(notepadButton);

        var minefieldIcon = Core.Content.Load<Texture2D>("images/minefield_icon");
        var minefieldButton = new StartMenuItemUI(new Rectangle(_layoutGroup.GetBoundingBox().X, _layoutGroup.GetBoundingBox().Y + 300, _layoutGroup.GetBoundingBox().Width, 80), minefieldIcon, "Minefield", () => OpenMinefield());
        _layoutGroup.AddChild(minefieldButton);

        var fileExplorerIcon = Core.Content.Load<Texture2D>("images/file_folder");
        var fileExplorerButton = new StartMenuItemUI(new Rectangle(_layoutGroup.GetBoundingBox().X, _layoutGroup.GetBoundingBox().Y + 400, _layoutGroup.GetBoundingBox().Width, 80), fileExplorerIcon, "File Explorer", () => OpenFileExplorer());
        _layoutGroup.AddChild(fileExplorerButton);

        var emailIcon = Core.Content.Load<Texture2D>("images/email_icon");
        var emailButton = new StartMenuItemUI(new Rectangle(_layoutGroup.GetBoundingBox().X, _layoutGroup.GetBoundingBox().Y + 600, _layoutGroup.GetBoundingBox().Width, 80), emailIcon, "Email", () => OpenEmail());
        _layoutGroup.AddChild(emailButton);

        var solitaireIcon = Core.Content.Load<Texture2D>("images/file_icon");
        var solitaireButton = new StartMenuItemUI(new Rectangle(_layoutGroup.GetBoundingBox().X, _layoutGroup.GetBoundingBox().Y + 900, _layoutGroup.GetBoundingBox().Width, 80), solitaireIcon, "Solitaire", () => {
            _ = new SolitaireUI(new Rectangle(150, 120, 960, 720));
            HideMenu();
        });
        _layoutGroup.AddChild(solitaireButton);

        // Placeholder icon until Powergrid gets its own art.
        var powergridIcon = Core.Content.Load<Texture2D>("images/puzzle_icon");
        var powergridButton = new StartMenuItemUI(new Rectangle(_layoutGroup.GetBoundingBox().X, _layoutGroup.GetBoundingBox().Y + 1000, _layoutGroup.GetBoundingBox().Width, 80), powergridIcon, "Powergrid", () => OpenPowergrid());
        _layoutGroup.AddChild(powergridButton);

        _rootElement.AddChild(_layoutGroup);

        Core.UISystem.AddElement(_rootElement);
    }

    private void OpenNotepad()
    {
        var textViewer = new TextViewerUI(new Rectangle(100, 100, 600, 400), new GameFile { Name = "New Text File.txt", Content = "" }, isReadOnly:false);
        Core.UISystem.AddElement(textViewer);
        HideMenu();
    }

    private void OpenPowergrid()
    {
        // Launch straight into the preset "basic-prog" progression for the demo.
        var levels = PowergridCommandHandler.LoadProgression("basic-prog");
        if (levels.Count > 0)
            Core.UISystem.AddElement(new PowergridUI(new Rectangle(40, 70, 1150, 720), "basic-prog", levels));
        else
            Core.UISystem.AddElement(new PowergridUI(new Rectangle(40, 70, 1150, 720)));
        HideMenu();
    }

    private void OpenMinefield()
    {
        // Sized so the per-cell rune glyphs are readable; centred on the 1920x1080 screen.
        int w = 800, h = 840;
        var minefieldUI = new Minefield(new Rectangle((Core.ScreenWidth - w) / 2, (Core.ScreenHeight - h) / 2, w, h));
        Core.UISystem.AddElement(minefieldUI);
        HideMenu();
    }

    private void OpenFileExplorer()
    {
        var fileExplorerUI = new FileExplorerUI(new Rectangle(150, 150, 700, 600), () => { /* On Close */ }, file => OpenFile(file));
        Core.UISystem.AddElement(fileExplorerUI);
        HideMenu();
    }

    private void OpenFile(GameFile file)
    {
        if (file.FileType == FileType.Image)
        {
            var imageViewer = new ImageViewerUI(file);
            Core.UISystem.AddElement(imageViewer);
        }
        else if (file.FileType == FileType.Scene3D)
        {
            var sceneViewer = new Scene3DViewerUI(file);
            Core.UISystem.AddElement(sceneViewer);
        }
        else
        {
            var textViewer = new TextViewerUI(new Rectangle(150, 150, 600, 800), file);
            AddChild(textViewer);
        }
    }

    private void OpenEmail()
    {
        var emailListUI = new EmailListUI(new Rectangle(150, 150, 700, 600));
        Core.UISystem.AddElement(emailListUI);
        HideMenu();
    }

    private void HideMenu()
    {
        Core.UISystem.RemoveElement(_rootElement);
        OnClose?.Invoke();
    }
}