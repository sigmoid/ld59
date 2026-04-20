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
        var mousePos = new Point(mouseState.X, mouseState.Y);
        if (!GetBoundingBox().Contains(mousePos) && mouseState.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed && !_lastLeftButtonState)
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

        var keygenIcon = Core.Content.Load<Texture2D>("images/key_icon");
        var keygenButton = new StartMenuItemUI(new Rectangle(_layoutGroup.GetBoundingBox().X, _layoutGroup.GetBoundingBox().Y + 100, _layoutGroup.GetBoundingBox().Width, 80), keygenIcon, "Keygen", () => OpenKeygen());
        _layoutGroup.AddChild(keygenButton);

        var decryptorIcon = Core.Content.Load<Texture2D>("images/decryptor_icon");
        var decryptorButton = new StartMenuItemUI(new Rectangle(_layoutGroup.GetBoundingBox().X, _layoutGroup.GetBoundingBox().Y + 200, _layoutGroup.GetBoundingBox().Width, 80), decryptorIcon, "Decryptor", () => OpenDecoder());
        _layoutGroup.AddChild(decryptorButton);

        var minefieldIcon = Core.Content.Load<Texture2D>("images/minefield_icon");
        var minefieldButton = new StartMenuItemUI(new Rectangle(_layoutGroup.GetBoundingBox().X, _layoutGroup.GetBoundingBox().Y + 300, _layoutGroup.GetBoundingBox().Width, 80), minefieldIcon, "Minefield", () => OpenMinefield());
        _layoutGroup.AddChild(minefieldButton);

        var fileExplorerIcon = Core.Content.Load<Texture2D>("images/file_folder");
        var fileExplorerButton = new StartMenuItemUI(new Rectangle(_layoutGroup.GetBoundingBox().X, _layoutGroup.GetBoundingBox().Y + 400, _layoutGroup.GetBoundingBox().Width, 80), fileExplorerIcon, "File Explorer", () => OpenFileExplorer());
        _layoutGroup.AddChild(fileExplorerButton);

        var puzzleIcon = Core.Content.Load<Texture2D>("images/puzzle_icon");
        var puzzleButton = new StartMenuItemUI(new Rectangle(_layoutGroup.GetBoundingBox().X, _layoutGroup.GetBoundingBox().Y + 500, _layoutGroup.GetBoundingBox().Width, 80), puzzleIcon, "Puzzle Solution", () => {
            var puzzleSolutionUI = new PuzzleSolutionUI(new Rectangle(150, 150, 500, 600), "The solution to the puzzle is: ...");
            Core.UISystem.AddElement(puzzleSolutionUI);
            HideMenu();
        });
        _layoutGroup.AddChild(puzzleButton);

        _rootElement.AddChild(_layoutGroup);

        Core.UISystem.AddElement(_rootElement);
    }

    private void OpenNotepad()
    {
        var textViewer = new TextViewerUI(new Rectangle(100, 100, 600, 400), new GameFile { Name = "New Text File.txt", Content = "" }, isReadOnly:false);
        Core.UISystem.AddElement(textViewer);
        HideMenu();
    }

    private void OpenKeygen()
    {
        var keygenUI = new KeygenUI(new Rectangle(150, 150, 500, 400));
        Core.UISystem.AddElement(keygenUI);
        HideMenu();
    }

    private void OpenDecoder()
    {
        var decoderUI = new DecoderUI(new Rectangle(150, 150, 700, 600));
        Core.UISystem.AddElement(decoderUI);
        HideMenu();
    }

    private void OpenMinefield()
    {
        var minefieldUI = new Minefield(new Rectangle(150, 150, 520, 620));
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
        var textViewer = new TextViewerUI(new Rectangle(150, 150, 400, 500), file);
        AddChild(textViewer);
    }

    private void HideMenu()
    {
        Core.UISystem.RemoveElement(_rootElement);
        OnClose?.Invoke();
    }
}