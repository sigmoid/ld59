using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

public class DecoderUI : UIPanel
{
    private Rectangle _bounds;
    private Window _rootContainer;
    private static Texture2D _fileTexture;
    private static Texture2D _keyFileTexture;
    private Label _file1NameLabel;
    private Label _file2NameLabel;
    private GameFile _inputFile;
    private GameKeyFile _keyFile;
    private FileExplorerUI _fileExplorerUI;
    private TextArea _outputTextArea;
    public DecoderUI(Rectangle bounds)
    {
        _bounds = bounds;

        _fileTexture = Core.Content.Load<Texture2D>("images/file_icon");
        _keyFileTexture = Core.Content.Load<Texture2D>("images/key_file_icon");

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
        _rootContainer = new Window(_bounds, "Decoder", Core.DefaultFont, ColorPalette.ActualWhite, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.DarkGreen, 2);
        Core.UISystem.AddElement(_rootContainer);

        
        var inputFileButton = new ImageButton(new Rectangle(_rootContainer.GetContentBounds().X + 20, _rootContainer.GetContentBounds().Center.Y -128-50, 128, 128), _fileTexture, () => OpenFileExplorerForInput(), hoverTintColor: ColorPalette.LightGreen);
        var inputFileWidth = Core.DefaultFont.MeasureString("Input File").X;
        var inputFileLabel = new Label(new Rectangle(inputFileButton.GetBoundingBox().Center.X - (int)(inputFileWidth / 2), inputFileButton.GetBoundingBox().Top - 30, (int)inputFileWidth, 30), "Input File", Core.DefaultFont, ColorPalette.Black);
        _file1NameLabel = new Label(new Rectangle(inputFileButton.GetBoundingBox().X, inputFileButton.GetBoundingBox().Bottom + 5, inputFileButton.GetBoundingBox().Width, 30), "", Core.DefaultFont, ColorPalette.Black);
        
        _rootContainer.AddChild(_file1NameLabel);
        _rootContainer.AddChild(inputFileLabel);
        _rootContainer.AddChild(inputFileButton);

        var keyFileButton = new ImageButton(new Rectangle(_rootContainer.GetContentBounds().X + 20, _rootContainer.GetContentBounds().Center.Y + 50, 128, 128), _keyFileTexture, () => OpenFileExplorerForKey(), hoverTintColor: ColorPalette.LightGreen);
        var keyFileWidth = Core.DefaultFont.MeasureString("Key File").X;
        var keyFileLabel = new Label(new Rectangle(keyFileButton.GetBoundingBox().Center.X - (int)(keyFileWidth / 2), keyFileButton.GetBoundingBox().Top - 30, (int)keyFileWidth, 30), "Key File", Core.DefaultFont, ColorPalette.Black);
        _file2NameLabel = new Label(new Rectangle(keyFileButton.GetBoundingBox().X, keyFileButton.GetBoundingBox().Bottom + 5, keyFileButton.GetBoundingBox().Width, 30), "", Core.DefaultFont, ColorPalette.Black);

        _rootContainer.AddChild(_file2NameLabel);
        _rootContainer.AddChild(keyFileLabel);
        _rootContainer.AddChild(keyFileButton);

        var textAreaBounds = new Rectangle(inputFileButton.GetBoundingBox().Right + 50, _rootContainer.GetContentBounds().Y + 50, _rootContainer.GetContentBounds().Width - inputFileButton.GetBoundingBox().Width - 150, _rootContainer.GetContentBounds().Height - 100);
        _outputTextArea = new TextArea(textAreaBounds, Core.DefaultFont, false, true, ColorPalette.ActualWhite, ColorPalette.Black, ColorPalette.DarkGreen, ColorPalette.LightGreen);
        _outputTextArea.Text = "Decoded output will appear here...";
        _rootContainer.AddChild(_outputTextArea);
    }

    private void OpenFileExplorerForInput()
    {
        if(_fileExplorerUI != null)
        {
            _fileExplorerUI.CloseWindow();
            _fileExplorerUI = null;
        }
        _fileExplorerUI = new FileExplorerUI(new Rectangle(_rootContainer.GetBoundingBox().Center.X - 300, _rootContainer.GetBoundingBox().Center.Y - 200, 600, 400), null, (file) =>
        {
            _inputFile = file;
            _file1NameLabel.Text = file.Name;
            _fileExplorerUI?.CloseWindow();
            _fileExplorerUI = null;
            TryToDecrypt();
        });
        Core.UISystem.AddElement(_fileExplorerUI);
    }

    private void OpenFileExplorerForKey()
    {
        if(_fileExplorerUI != null)
        {
            _fileExplorerUI.CloseWindow();
            _fileExplorerUI = null;
        }
        _fileExplorerUI = new FileExplorerUI(new Rectangle(_rootContainer.GetBoundingBox().Center.X - 300, _rootContainer.GetBoundingBox().Center.Y - 200, 600, 400), null, (file) =>
        {
            if(file is GameKeyFile)
            {
                _keyFile = file as GameKeyFile;
                _file2NameLabel.Text = file.Name;
                _fileExplorerUI?.CloseWindow();
                _fileExplorerUI = null;
                TryToDecrypt();
            }
        });
        Core.UISystem.AddElement(_fileExplorerUI);
    }

    private void TryToDecrypt()
    {
        if(_inputFile != null && _keyFile != null)
        {
            var gameDataManager = Core.CurrentScene.GetManager<GameFileDataManager>();
            var decodedFile = gameDataManager.TryToDecryptFile(_keyFile, _inputFile);

            if(decodedFile != null)
            {
                ShowModal("Decryption successful! Saved as " + decodedFile.Name);
                _outputTextArea.Text = decodedFile.Content;
                var didUnlock = gameDataManager.UnlockData(decodedFile);
                if(didUnlock)
                {
                    DesktopUI.ToastManager.ShowSuccess($"New info unlocked!", 3, Toast.ToastPosition.TopRight);
                }
            }
            else
            {
                ShowModal("Decryption failed. The key does not match the input file.");
            }
        }
    }

    private void ShowModal(string text)
    {
        var modal = new NotificationPopup(new Rectangle(_rootContainer.GetBoundingBox().Center.X - 300, _rootContainer.GetBoundingBox().Center.Y - 50, 600, 100), text);
        Core.UISystem.AddElement(modal);
    }
    
}