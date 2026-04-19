using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

public class KeygenUI : UIPanel
{
    private Rectangle _bounds;
    private Window _rootContainer;
    private static Texture2D _fileIcon;
    private ProgressBar _progressBar;
    private Label _file1NameLabel;
    private Label _file2NameLabel;
    private Button _generateButton;
    private GameFile _file1;
    private GameFile _file2;
    private FileExplorerUI _fileExplorerUI;
    private float _generateTimer = 0;
    private float _generateDuration = 3; // seconds

    public KeygenUI(Rectangle bounds)
    {
        _bounds = bounds;
        _fileIcon = Core.Content.Load<Texture2D>("images/file_icon");
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
        _rootContainer = new Window(_bounds, "Keygen", Core.DefaultFont, ColorPalette.ActualWhite, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.DarkGreen, 2);
        _rootContainer.SetCloseButtonColors(ColorPalette.DarkGreen, ColorPalette.LightGreen);
        Core.UISystem.AddElement(_rootContainer);
        Core.UISystem.WindowManager.SetFocusedWindow(_rootContainer);

        var content = _rootContainer.GetContentBounds();

        var file1Button = new ImageButton(new Rectangle(content.X + content.Width / 3 - 64, content.Y + content.Height / 2 - 64 - 35, 128, 128), _fileIcon, () => OpenFileExplorer(1), hoverTintColor: ColorPalette.LightGreen);
        var file1Label = new Label(new Rectangle(file1Button.GetBoundingBox().Center.X - (int)(Core.DefaultFont.MeasureString("File A").X / 2), file1Button.GetBoundingBox().Top - 20, (int)Core.DefaultFont.MeasureString("File A").X, 30), "File A", Core.DefaultFont, ColorPalette.Black);
        _file1NameLabel = new Label(new Rectangle(file1Button.GetBoundingBox().X, file1Button.GetBoundingBox().Bottom + 5, file1Button.GetBoundingBox().Width, 30), "", Core.DefaultFont, ColorPalette.Black);
        _rootContainer.AddChild(_file1NameLabel);
        _rootContainer.AddChild(file1Button);
        _rootContainer.AddChild(file1Label);

        var file2Button = new ImageButton(new Rectangle(content.X + content.Width / 3 * 2 - 64, content.Y + content.Height / 2 - 64 - 35, 128, 128), _fileIcon, () => OpenFileExplorer(2), hoverTintColor: ColorPalette.LightGreen);
        var file2Label = new Label(new Rectangle(file2Button.GetBoundingBox().Center.X - (int)(Core.DefaultFont.MeasureString("File B").X / 2), file2Button.GetBoundingBox().Top - 20, (int)Core.DefaultFont.MeasureString("File B").X, 30), "File B", Core.DefaultFont, ColorPalette.Black);
        _file2NameLabel = new Label(new Rectangle(file2Button.GetBoundingBox().X, file2Button.GetBoundingBox().Bottom + 5, file2Button.GetBoundingBox().Width, 30), "", Core.DefaultFont, ColorPalette.Black);
        _rootContainer.AddChild(_file2NameLabel);
        _rootContainer.AddChild(file2Button);
        _rootContainer.AddChild(file2Label);

        _progressBar = new ProgressBar(new Rectangle(content.X + 50, content.Bottom - 50, content.Width - 100, 30), 0, 1, 0, true, ColorPalette.LightGreen, ColorPalette.DarkGreen);
        _rootContainer.AddChild(_progressBar);

        _generateButton = new Button(new Rectangle(content.Center.X - 60, content.Bottom - 90, 120, 30), "Generate", Core.DefaultFont, ColorPalette.DarkGreen, ColorPalette.LightGreen, ColorPalette.ActualWhite, Generate, ColorPalette.Green);
        _rootContainer.AddChild(_generateButton);
    }

    public override void Update(float deltaTime)
    {
        if(_generateTimer > 0)
        {
            _generateTimer -= deltaTime;
            _progressBar.Value = 1 - (_generateTimer / _generateDuration);
            if(_generateTimer <= 0)
            {
                _progressBar.Value = 0;
                _generateTimer = 0;
                ShowModal("Key generated successfully!");
                _generateButton.SetEnabled(true);
            }
        }
        base.Update(deltaTime);
    }

    private void OpenFileExplorer(int slot)
    {
        if (_fileExplorerUI != null) return;

        var explorerBounds = new Rectangle(_rootContainer.GetBoundingBox().X + 20, _rootContainer.GetBoundingBox().Y + 100, 700, 500);
        _fileExplorerUI = new FileExplorerUI(explorerBounds, () => _fileExplorerUI = null, file =>
        {
            if (slot == 1)
            {
                _file1 = file;
                _file1NameLabel.Text = file.Name;
            }
            else
            {
                _file2 = file;
                _file2NameLabel.Text = file.Name;
            }
            _fileExplorerUI?.Close();
            _fileExplorerUI = null;
        });
    }

    private void Generate()
    {
        if(_file1 == null || _file2 == null)
        {
            ShowModal("Please select both files before generating.");
            return;
        }

        _generateTimer = _generateDuration;
    }

    private void ShowModal(string text)
    {
        var modal = new NotificationPopup(new Rectangle(_rootContainer.GetBoundingBox().Center.X - 150, _rootContainer.GetBoundingBox().Center.Y - 50, 300, 100), text);
        Core.UISystem.AddElement(modal);
    }
}