using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

public class BrowserUI : UIPanel
{
    private Window _rootContainer;
    private Rectangle _bounds;
    private BrowserTextArea _contentArea;
    private Label _urlLabel;
    private SpriteFont _font;

    private System.Collections.Generic.List<string> _history = new();
    private int _historyIndex = -1;

    private const string HomePage = "home.txt";

    public BrowserUI(Rectangle bounds)
    {
        _font = Core.Content.Load<SpriteFont>("fonts/Browser");
        _bounds = bounds;
        CreateUI();
        Navigate(HomePage);
    }

    public override Rectangle GetBoundingBox() => _bounds;

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        _bounds = bounds;
    }

    private void CreateUI()
    {
        _rootContainer = new Window(_bounds, "LithNET", Core.DefaultFont,
            ColorPalette.ActualWhite, ColorPalette.Black,
            ColorPalette.ActualWhite, ColorPalette.Black, 2);
        _rootContainer.SetCloseButtonColors(ColorPalette.Black, Color.DarkGray);
        Core.UISystem.AddElement(_rootContainer);
        TaskbarRegistry.Register("LithNET", Core.Content.Load<Texture2D>("images/browser_icon"), _rootContainer);

        var content = _rootContainer.GetContentBounds();
        CreateToolbar(content);
        CreateContentArea(content);
    }

    private void CreateToolbar(Rectangle content)
    {
        int toolbarH = 40;
        int btnSize = 30;
        int btnY = content.Y + (toolbarH - btnSize) / 2;

        var toolbarBg = new Canvas(new Rectangle(content.X, content.Y, content.Width, toolbarH), ColorPalette.LightGreen);
        _rootContainer.AddChild(toolbarBg);

        var backBtn = new Button(
            new Rectangle(content.X + 5, btnY, btnSize, btnSize),
            "<", Core.DefaultFont,
            ColorPalette.DarkGreen, ColorPalette.Green, ColorPalette.ActualWhite,
            () => GoBack(), ColorPalette.DarkGreen);
        _rootContainer.AddChild(backBtn);

        var fwdBtn = new Button(
            new Rectangle(content.X + 5 + btnSize + 5, btnY, btnSize, btnSize),
            ">", Core.DefaultFont,
            ColorPalette.DarkGreen, ColorPalette.Green, ColorPalette.ActualWhite,
            () => GoForward(), ColorPalette.DarkGreen);
        _rootContainer.AddChild(fwdBtn);

        int urlX = content.X + 5 + (btnSize + 5) * 2 + 5;
        int urlW = content.Right - urlX - 5;
        var urlBg = new Canvas(new Rectangle(urlX, btnY, urlW, btnSize), ColorPalette.ActualWhite);
        _rootContainer.AddChild(urlBg);

        _urlLabel = new Label(
            new Rectangle(urlX + 6, btnY, urlW - 6, btnSize),
            "", Core.DefaultFont, ColorPalette.DarkGreen);
        _rootContainer.AddChild(_urlLabel);
    }

    private void CreateContentArea(Rectangle content)
    {
        int toolbarH = 40;
        var areaBounds = new Rectangle(
            content.X, content.Y + toolbarH,
            content.Width, content.Height - toolbarH);

        _contentArea = new BrowserTextArea(areaBounds, _font,
            ColorPalette.ActualWhite, ColorPalette.Black);
        _rootContainer.AddChild(_contentArea);
    }

    private void Navigate(string url)
    {
        var page = WebPageLoader.Load(url);
        if (page == null) { ShowError(url); return; }

        WebPage.VisitedUrls.Add(url);

        if (_historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(url);
        _historyIndex = _history.Count - 1;

        LoadPage(page);
    }

    private void GoBack()
    {
        if (_historyIndex <= 0) return;
        _historyIndex--;
        var page = WebPageLoader.Load(_history[_historyIndex]);
        if (page != null) LoadPage(page);
    }

    private void GoForward()
    {
        if (_historyIndex >= _history.Count - 1) return;
        _historyIndex++;
        var page = WebPageLoader.Load(_history[_historyIndex]);
        if (page != null) LoadPage(page);
    }

    private void LoadPage(WebPage page)
    {
        _urlLabel.Text = WebPageLoader.FormatDisplayUrl(page.Url);
        _contentArea.ClearLinks();
        _contentArea.ClearHighlights();
        _contentArea.Text = page.DisplayText;

        foreach (var (label, href) in page.Links)
        {
            string captured = href;
            var color = WebPage.VisitedUrls.Contains(href) ? ColorPalette.VisitedLink : ColorPalette.InfoName;
            _contentArea.AddLink(label, color, () => Navigate(captured));
        }

        if (page.Info.Count > 0)
        {
            var dataManager = Core.CurrentScene.GetManager<GameFileDataManager>();
            if (dataManager != null)
            {
                bool didUnlock = dataManager.UnlockInfo(page.Info);
                if (didUnlock)
                {
                    AudioAtlas.Confirmation_002.Play();
                    DesktopUI.ToastManager.ShowSuccess("New info unlocked!", 3, Toast.ToastPosition.TopRight);
                }
            }

            foreach (var info in page.Info)
            {
                if (string.IsNullOrEmpty(info.Value)) continue;
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
                _contentArea.AddHighlight(info.Value, color);
            }
        }
    }

    private void ShowError(string url)
    {
        _urlLabel.Text = WebPageLoader.FormatDisplayUrl(url);
        _contentArea.ClearLinks();
        _contentArea.Text = "Page not found.\n\nCould not load: " + url;
    }
}
