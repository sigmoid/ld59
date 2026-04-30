using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

public class FileExplorerUI : UIContainer
{
    public Action OnClose;

    private Window _rootContainer;

    private Rectangle _bounds;
    private Canvas _shortcutsCanvas;

    private ScrollArea _fileDisplayScrollArea;
    private VerticalLayoutGroup _fileDisplayLayout;
    private Texture2D _folderIcon;
    private Texture2D _fileIcon;
    private Texture2D _imageFileIcon;
    private string _currentPath = "";
    private Action<GameFile> _onOpenFile;
    private Label _filepathLabel;

    public FileExplorerUI(Rectangle bounds, Action OnClose, Action<GameFile> onOpenFile)
    {
        _bounds = bounds;
        this.OnClose = OnClose;
        _onOpenFile = onOpenFile;

        _fileIcon = Core.Content.Load<Texture2D>("images/file_icon");
        _folderIcon = Core.Content.Load<Texture2D>("images/file_folder");
        _imageFileIcon = Core.Content.Load<Texture2D>("images/image_viewer");

        CreateUI();
        SelectFolder("/");
    }

    private void CreateUI()
    {
        _rootContainer = new Window(_bounds, "File Explorer", Core.DefaultFont, ColorPalette.ActualWhite, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.DarkGreen, 2);
        _rootContainer.SetCloseButtonColors(ColorPalette.DarkGreen, ColorPalette.LightGreen);
        Core.UISystem.AddElement(_rootContainer);
        _rootContainer.OnWindowClosed += (window) => Close();
        TaskbarRegistry.Register("File Explorer", Core.Content.Load<Microsoft.Xna.Framework.Graphics.Texture2D>("images/file_folder"), _rootContainer);

        CreateFilePathDisplay();
        CreateShortcuts();
        CreateFileDisplay();
    }

    public void CloseWindow()
    {
        _rootContainer.Close();
    }

    public void Close()
    {
        OnClose?.Invoke();
    }

    public void SetPath(string path)
    {
        var parts = path.Split(new char[] {'/'}, StringSplitOptions.RemoveEmptyEntries);
        _currentPath = "";
        foreach(var part in parts)
        {
            SelectFolder(part);
        }
    }

    private void CreateFilePathDisplay()
    {
        var contentBounds = _rootContainer.GetContentBounds();
        var filepathArea = new Rectangle(contentBounds.X, contentBounds.Y, contentBounds.Width, 40);
        var filepathBackground = new Canvas(new Rectangle(filepathArea.X, filepathArea.Y, filepathArea.Width, filepathArea.Height + 10), ColorPalette.Green);
        _rootContainer.AddChild(filepathBackground);

        var backButtonSize = 30;
        var backButton = new Button(new Rectangle(filepathArea.X + 5, filepathArea.Y + 5, backButtonSize, backButtonSize), "<", Core.DefaultFont, ColorPalette.DarkGreen, ColorPalette.LightGreen, ColorPalette.ActualWhite, NavigateUp);
        _rootContainer.AddChild(backButton);

        var labelX = filepathArea.X + backButtonSize + 15;
        _filepathLabel = new Label(new Rectangle(labelX, filepathArea.Y + 10, filepathArea.Width - (labelX - filepathArea.X) - 10, 30), "", Core.DefaultFont, ColorPalette.Black, ColorPalette.ActualWhite);
        _rootContainer.AddChild(_filepathLabel);
    }

    private void CreateShortcuts()
    {
        _shortcutsCanvas = new Canvas(new Rectangle(_rootContainer.GetContentBounds().X, _filepathLabel.GetBoundingBox().Bottom + 10, (int)(_rootContainer.GetContentBounds().Width * 0.25f), _rootContainer.GetContentBounds().Height - (_filepathLabel.GetBoundingBox().Height + 16)), ColorPalette.Green);
        _rootContainer.AddChild(_shortcutsCanvas);

        var padding = 5;

        var layoutGroup = new VerticalLayoutGroup(new Rectangle(_shortcutsCanvas.GetBoundingBox().X + padding, _shortcutsCanvas.GetBoundingBox().Y + padding, _shortcutsCanvas.GetBoundingBox().Width -  padding * 2, _shortcutsCanvas.GetBoundingBox().Height - padding * 2), 5);
        var rootShortcut = new FileItemUI(new Rectangle(0, 0, layoutGroup.GetBoundingBox().Width, 40), "root", _folderIcon, () => SelectFolder("/"), null);
        layoutGroup.AddChild(rootShortcut);
        _shortcutsCanvas.AddChild(layoutGroup);
    }

    private void CreateFileDisplay()
    {
        var contentBounds = _rootContainer.GetContentBounds();
        var fileDisplayArea = new Rectangle(contentBounds.X + (int)(contentBounds.Width * 0.25f), _filepathLabel.GetBoundingBox().Bottom + 10, (int)(contentBounds.Width * 0.75f), contentBounds.Height - (_filepathLabel.GetBoundingBox().Height + 16));
        _fileDisplayScrollArea = new ScrollArea(fileDisplayArea);
        _rootContainer.AddChild(_fileDisplayScrollArea);

        var layoutPadding = 10;    
        _fileDisplayLayout = new VerticalLayoutGroup(new Rectangle(_fileDisplayScrollArea.GetBoundingBox().X + layoutPadding, _fileDisplayScrollArea.GetBoundingBox().Y + layoutPadding, _fileDisplayScrollArea.GetBoundingBox().Width - layoutPadding * 2, _fileDisplayScrollArea.GetBoundingBox().Height - layoutPadding * 2), 5);
        _fileDisplayScrollArea.AddChild(_fileDisplayLayout);
    }

    private void NavigateUp()
    {
        if (string.IsNullOrEmpty(_currentPath)) return;
        var lastSlash = _currentPath.LastIndexOf('/');
        _currentPath = lastSlash <= 0 ? "" : _currentPath.Substring(0, lastSlash);
        RefreshCurrentFolder();
    }

    private void SelectFolder(string folder)
    {
        if (folder == "/")
            _currentPath = "";
        else
            _currentPath += $"/{folder}";
        RefreshCurrentFolder();
    }

    private void RefreshCurrentFolder()
    {
        _filepathLabel.Text = _currentPath == "" ? "~/" : "~"+_currentPath;

        var lookupPath = _currentPath.StartsWith("/") ? _currentPath[1..] : _currentPath;
        var data = Core.CurrentScene.GetManager<GameFileDataManager>().GetFolderByPath(lookupPath);

        _fileDisplayLayout.ClearChildren();

        foreach(var subFolder in data.SubFolders)
        {
            var folderItem = new FileItemUI(new Rectangle(_fileDisplayLayout.GetBoundingBox().X, _fileDisplayLayout.GetBoundingBox().Y, _fileDisplayLayout.GetBoundingBox().Width, 40), subFolder.Name, _folderIcon, () => SelectFolder(subFolder.Name), null);
            _fileDisplayLayout.AddChild(folderItem);
        }

        foreach(var file in data.Files)
        {
            var icon = file.FileType == FileType.Image ? _imageFileIcon : _fileIcon;
            var fileItem = new FileItemUI(new Rectangle(_fileDisplayLayout.GetBoundingBox().X, _fileDisplayLayout.GetBoundingBox().Y, _fileDisplayLayout.GetBoundingBox().Width, 40), file.Name, icon, () => _onOpenFile?.Invoke(file), file);
            _fileDisplayLayout.AddChild(fileItem);
        }

        _fileDisplayScrollArea.RefreshContentBounds();
        _fileDisplayScrollArea.ScrollToTop();
    }
}