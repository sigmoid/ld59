using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using Microsoft.Xna.Framework;
using Quartz;
using Quartz.UI;

public class KeygenUI : UIPanel
{
    private Rectangle _bounds;
    private Window _rootContainer;
    private ProgressBar _progressBar;
    private Button _generateButton;
    private FileExplorerUI _fileExplorerUI;
    private float _generateTimer = 0;
    private float _generateDuration = 4;

    private readonly List<GameFile> _selectedFiles = [];
    private VerticalLayoutGroup _selectedFilesLayout;

    private VerticalLayoutGroup _scanDisplayLayout;
    private ScrollArea _scanScrollArea;
    private List<string> _scanPaths = [];
    private int _scanIndex = 0;
    private float _scanInterval = 0;
    private float _scanAccumulator = 0;

    public KeygenUI(Rectangle bounds)
    {
        _bounds = bounds;
        CreateUI();
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        _bounds = bounds;
    }

    public override Rectangle GetBoundingBox() => _bounds;

    private void CreateUI()
    {
        _rootContainer = new Window(_bounds, "Keygen", Core.DefaultFont, ColorPalette.ActualWhite, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.DarkGreen, 2);
        _rootContainer.SetCloseButtonColors(ColorPalette.DarkGreen, ColorPalette.LightGreen);
        Core.UISystem.AddElement(_rootContainer);
        Core.UISystem.WindowManager.SetFocusedWindow(_rootContainer);

        var content = _rootContainer.GetContentBounds();
        int padding = 10;
        int leftPaneWidth = (int)(content.Width * 0.33f);

        CreateLeftPane(content, leftPaneWidth, padding);
        CreateRightPane(content, leftPaneWidth, padding);
    }

    private void CreateLeftPane(Rectangle content, int paneWidth, int padding)
    {
        var paneBackground = new Canvas(new Rectangle(content.X, content.Y, paneWidth, content.Height), ColorPalette.Green);
        _rootContainer.AddChild(paneBackground);

        var titleLabel = new Label(new Rectangle(content.X + padding, content.Y + padding, paneWidth - padding * 2, 20), "Selected Files", Core.DefaultFont, ColorPalette.ActualWhite);
        _rootContainer.AddChild(titleLabel);

        int addButtonHeight = 30;
        int addButtonY = content.Y + content.Height - addButtonHeight - padding;
        int scrollTop = titleLabel.GetBoundingBox().Bottom + padding;
        int scrollHeight = addButtonY - scrollTop - padding;

        var scrollArea = new ScrollArea(new Rectangle(content.X + padding, scrollTop, paneWidth - padding * 2, scrollHeight));
        _rootContainer.AddChild(scrollArea);

        _selectedFilesLayout = new VerticalLayoutGroup(new Rectangle(content.X + padding, scrollTop, paneWidth - padding * 2, scrollHeight), 4);
        scrollArea.AddChild(_selectedFilesLayout);

        var addButton = new Button(new Rectangle(content.X + padding, addButtonY, paneWidth - padding * 2, addButtonHeight), "+ Add File", Core.DefaultFont, ColorPalette.DarkGreen, ColorPalette.LightGreen, ColorPalette.ActualWhite, OpenFileExplorer, ColorPalette.Green);
        _rootContainer.AddChild(addButton);
    }

    private void CreateRightPane(Rectangle content, int leftPaneWidth, int padding)
    {
        int rightX = content.X + leftPaneWidth + padding;
        int rightWidth = content.Width - leftPaneWidth - padding;

        int generateButtonHeight = 30;
        int progressBarHeight = 20;
        int bottomRowHeight = generateButtonHeight + progressBarHeight + padding * 3;

        int scanAreaHeight = content.Height - bottomRowHeight - padding * 2;

        _scanScrollArea = new ScrollArea(new Rectangle(rightX, content.Y + padding, rightWidth - padding, scanAreaHeight));
        _rootContainer.AddChild(_scanScrollArea);

        _scanDisplayLayout = new VerticalLayoutGroup(new Rectangle(rightX, content.Y + padding, rightWidth - padding, scanAreaHeight), 2);
        _scanScrollArea.AddChild(_scanDisplayLayout);

        int progressY = content.Y + content.Height - progressBarHeight - padding;
        _progressBar = new ProgressBar(new Rectangle(rightX, progressY, rightWidth - padding, progressBarHeight), 0, 1, 0, true, ColorPalette.LightGreen, ColorPalette.DarkGreen);
        _rootContainer.AddChild(_progressBar);

        int generateY = progressY - generateButtonHeight - padding;
        int generateX = rightX + (rightWidth - padding) / 2 - 60;
        _generateButton = new Button(new Rectangle(generateX, generateY, 120, generateButtonHeight), "Generate", Core.DefaultFont, ColorPalette.DarkGreen, ColorPalette.LightGreen, ColorPalette.ActualWhite, Generate, ColorPalette.Green);
        _rootContainer.AddChild(_generateButton);
    }

    private void RefreshSelectedFiles()
    {
        _selectedFilesLayout.ClearChildren();
        foreach (var file in _selectedFiles)
        {
            var capturedFile = file;
            var rowWidth = _selectedFilesLayout.GetBoundingBox().Width;
            var row = new HorizontalLayoutGroup(new Rectangle(_selectedFilesLayout.GetBoundingBox().X, 0, rowWidth, 28), 4);

            int deleteWidth = 24;
            var nameLabel = new Label(new Rectangle(0, 0, rowWidth - deleteWidth - 4, 28), file.Name, Core.DefaultFont, ColorPalette.DarkGreen, ColorPalette.ActualWhite);
            var deleteButton = new Button(new Rectangle(0, 0, deleteWidth, 28), "X", Core.DefaultFont, ColorPalette.DarkGreen, ColorPalette.LightGreen, ColorPalette.ActualWhite, () => RemoveFile(capturedFile), ColorPalette.Green);

            row.AddChild(nameLabel);
            row.AddChild(deleteButton);
            _selectedFilesLayout.AddChild(row);
        }
    }

    private void RemoveFile(GameFile file)
    {
        _selectedFiles.Remove(file);
        RefreshSelectedFiles();
    }

    private void OpenFileExplorer()
    {
        if (_fileExplorerUI != null) return;

        var explorerBounds = new Rectangle(_rootContainer.GetBoundingBox().X + 20, _rootContainer.GetBoundingBox().Y + 100, 700, 500);
        _fileExplorerUI = new FileExplorerUI(explorerBounds, () => _fileExplorerUI = null, file =>
        {
            if (!_selectedFiles.Contains(file))
            {
                _selectedFiles.Add(file);
                RefreshSelectedFiles();
            }
            _fileExplorerUI?.CloseWindow();
            _fileExplorerUI = null;
        });
    }

    private void CollectAllFilePaths(GameFolder folder, string currentPath, List<string> result)
    {
        foreach (var file in folder.Files)
            result.Add(currentPath + "/" + file.Name);
        foreach (var sub in folder.SubFolders)
            CollectAllFilePaths(sub, currentPath + "/" + sub.Name, result);
    }

    public override void Update(float deltaTime)
    {
        if (_generateTimer > 0)
        {
            _generateTimer -= deltaTime;
            _progressBar.Value = 1 - (_generateTimer / _generateDuration);

            _scanAccumulator += deltaTime;
            while (_scanAccumulator >= _scanInterval && _scanIndex < _scanPaths.Count)
            {
                var file = _scanPaths[_scanIndex];
                var gameFile = Core.CurrentScene.GetManager<GameFileDataManager>().GetFileByPath(file);
                var gameFileDataManager = Core.CurrentScene.GetManager<GameFileDataManager>();
                var didUnlock = gameFileDataManager.TryToDecryptFile(gameFile, _selectedFiles.Select(f => f.Name).ToList());

                var color = didUnlock ? ColorPalette.Green : ColorPalette.Red;

                if(didUnlock)
                {
                    DesktopUI.ToastManager.ShowSuccess($"Decrypted {gameFile.Name}!", 3, Toast.ToastPosition.TopRight);
                }

                var label = new Label(new Rectangle(_scanDisplayLayout.GetBoundingBox().X, _scanDisplayLayout.GetBoundingBox().Y, _scanDisplayLayout.GetBoundingBox().Width, 16), _scanPaths[_scanIndex], Core.DefaultFont, color);
                _scanDisplayLayout.AddChild(label);
                _scanIndex++;
                _scanAccumulator -= _scanInterval;
                _scanScrollArea.RefreshContentBounds();
                _scanScrollArea.ScrollToBottom();
            }

            if (_generateTimer <= 0)
            {
                _progressBar.Value = 0;
                _generateTimer = 0;
                _generateButton.SetEnabled(true);
            }
        }
        base.Update(deltaTime);
    }

    private void Generate()
    {
        if (_selectedFiles.Count == 0)
        {
            ShowModal("Please select at least one file before generating.");
            return;
        }

        _scanDisplayLayout.ClearChildren();
        _scanPaths = [];
        CollectAllFilePaths(Core.CurrentScene.GetManager<GameFileDataManager>().GetRootFolder(), "", _scanPaths);
        _scanIndex = 0;
        _scanInterval = _scanPaths.Count > 0 ? _generateDuration / _scanPaths.Count : 0.1f;
        _scanAccumulator = 0;

        _generateTimer = _generateDuration;
        _generateButton.SetEnabled(false);
    }

    private void ShowModal(string text)
    {
        var modal = new NotificationPopup(new Rectangle(_rootContainer.GetBoundingBox().Center.X - 300, _rootContainer.GetBoundingBox().Center.Y - 50, 600, 100), text);
        Core.UISystem.AddElement(modal);
    }
}
