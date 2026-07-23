using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

public class BrowserUI : UIPanel
{
    private Window _rootContainer;
    private Rectangle _bounds;
    private UIElement _currentPage;
    private TextInput _urlInput;
    private SpriteFont _font;

    private System.Collections.Generic.List<string> _history = new();
    private int _historyIndex = -1;

    private const string HomePage = "home.txt";
    private const int ToolbarHeight = 40;

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

        CreateToolbar(_rootContainer.GetContentBounds());
    }

    private void CreateToolbar(Rectangle content)
    {
        int btnSize = 30;
        int btnY = content.Y + (ToolbarHeight - btnSize) / 2;

        var toolbarBg = new Canvas(new Rectangle(content.X, content.Y, content.Width, ToolbarHeight), ColorPalette.LightGreen);
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

        _urlInput = new TextInput(
            new Rectangle(urlX, btnY, urlW, btnSize), Core.DefaultFont,
            placeholder: "Type a url and press Enter",
            backgroundColor: ColorPalette.ActualWhite,
            textColor: ColorPalette.DarkGreen,
            borderColor: ColorPalette.DarkGreen,
            focusedBorderColor: ColorPalette.Green,
            padding: 6, borderWidth: 2);
        _urlInput.OnEnterPressed += NavigateTyped;
        _rootContainer.AddChild(_urlInput);
    }

    /// <summary>
    /// The area a page is rendered into. Recomputed on every navigation so that pages built after
    /// the window has been dragged or resized land in the right place.
    /// </summary>
    private Rectangle GetPageBounds()
    {
        var content = _rootContainer.GetContentBounds();
        return new Rectangle(
            content.X, content.Y + ToolbarHeight,
            content.Width, content.Height - ToolbarHeight);
    }

    /// <summary>
    /// Handles a url typed into the address bar. What the player types is rarely the literal page
    /// name, so the candidates are tried in order and the first that exists wins. If none do, the
    /// typed text is navigated to anyway so the error page names what they actually asked for.
    /// </summary>
    private void NavigateTyped(string typed)
    {
        Navigate(ResolveTypedUrl(typed) ?? (typed ?? string.Empty).Trim());
        _urlInput.SetFocus(false);
    }

    private static string ResolveTypedUrl(string typed)
    {
        foreach (var candidate in WebPageLoader.ExpandTypedUrl(typed))
        {
            if (WebPageRegistry.TryGet(candidate, out _) || WebPageLoader.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private void Navigate(string url)
    {
        if (!ShowUrl(url)) { ShowError(url); return; }

        if (_historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(url);
        _historyIndex = _history.Count - 1;
    }

    private void GoBack()
    {
        if (_historyIndex <= 0) return;
        _historyIndex--;
        if (!ShowUrl(_history[_historyIndex])) ShowError(_history[_historyIndex]);
    }

    private void GoForward()
    {
        if (_historyIndex >= _history.Count - 1) return;
        _historyIndex++;
        if (!ShowUrl(_history[_historyIndex])) ShowError(_history[_historyIndex]);
    }

    /// <summary>
    /// Replaces the displayed page. Code pages registered in <see cref="WebPageRegistry"/> win over
    /// text pages in <c>Content/www/</c>. Returns false if the url resolved to neither.
    /// </summary>
    private bool ShowUrl(string url)
    {
        ClearCurrentPage();

        if (WebPageRegistry.TryGet(url, out var factory))
        {
            var root = factory(GetPageBounds(), Navigate);
            if (root == null) return false;

            WebPage.VisitedUrls.Add(url);
            _urlInput.Text = WebPageLoader.FormatDisplayUrl(url);
            _rootContainer.AddChild(root);
            _currentPage = root;
            return true;
        }

        var page = WebPageLoader.Load(url);
        if (page == null) return false;

        WebPage.VisitedUrls.Add(url);
        LoadTextPage(page);
        return true;
    }

    private void ClearCurrentPage()
    {
        if (_currentPage == null) return;
        _rootContainer.DestroyChild(_currentPage);
        _currentPage = null;
    }

    private BrowserTextArea CreateTextArea()
    {
        var area = new BrowserTextArea(GetPageBounds(), _font,
            ColorPalette.ActualWhite, ColorPalette.Black);
        _rootContainer.AddChild(area);
        _currentPage = area;
        return area;
    }

    private void LoadTextPage(WebPage page)
    {
        _urlInput.Text = WebPageLoader.FormatDisplayUrl(page.Url);

        var contentArea = CreateTextArea();
        contentArea.Text = page.DisplayText;

        foreach (var (label, href) in page.Links)
        {
            string captured = href;
            var color = ColorPalette.Black;
            contentArea.AddLink(label, color, () => Navigate(captured));
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
                contentArea.AddHighlight(info.Value, color);
            }
        }
    }

    private void ShowError(string url)
    {
        ClearCurrentPage();
        _urlInput.Text = WebPageLoader.FormatDisplayUrl(url);
        CreateTextArea().Text = "Page not found.\n\nCould not load: " + url;
    }
}
