using Microsoft.Xna.Framework;
using Quartz;
using Quartz.UI;

public class EmailViewerUI : UIContainer
{
    private Rectangle _bounds;
    private readonly Email _email;
    private Window _rootContainer;

    public EmailViewerUI(Rectangle bounds, Email email)
    {
        _bounds = bounds;
        _email = email;

        var emailManager = Core.CurrentScene.GetManager<EmailDataManager>();
        var didUnlock = emailManager?.UnlockEmailInfo(email);

        if (didUnlock == true)
        {
            AudioAtlas.Confirmation_002.Play();
            DesktopUI.ToastManager.ShowSuccess("New info unlocked!", 3, Toast.ToastPosition.TopRight);
        }

        CreateUI();
    }

    public override void SetBounds(Rectangle bounds) { base.SetBounds(bounds); _bounds = bounds; }
    public override Rectangle GetBoundingBox() => _bounds;

    private void CreateUI()
    {
        _rootContainer = new Window(_bounds, _email.Subject ?? "Email", Core.DefaultFont,
            ColorPalette.ActualWhite, ColorPalette.DarkGreen,
            ColorPalette.ActualWhite, ColorPalette.DarkGreen, 2);
        _rootContainer.SetCloseButtonColors(ColorPalette.DarkGreen, ColorPalette.LightGreen);
        Core.UISystem.AddElement(_rootContainer);

        var content = _rootContainer.GetContentBounds();

        // Header strip
        int headerHeight = 120;
        var headerArea = new Rectangle(content.X + 5, content.Y + 5, content.Width - 10, headerHeight);
        var headerTextArea = new TextArea(headerArea, Core.DefaultFont, false, true,
            ColorPalette.White, ColorPalette.DarkCream, ColorPalette.LightGreen, ColorPalette.DarkGreen);
        headerTextArea.Text =
            $"From:    {_email.From}\n" +
            $"To:      {_email.To}\n" +
            $"Subject: {_email.Subject}\n" +
            $"Date:    {_email.Date}";
        _rootContainer.AddChild(headerTextArea);

        // Body
        int bodyY = content.Y + headerHeight + 10;
        var bodyArea = new Rectangle(content.X + 5, bodyY, content.Width - 10, content.Bottom - bodyY - 5);
        var bodyTextArea = new TextArea(bodyArea, Core.DefaultFont, true, true,
            ColorPalette.ActualWhite, ColorPalette.Black, ColorPalette.DarkGreen, ColorPalette.LightGreen);
        bodyTextArea.Text = _email.Body;

        foreach (var info in _email.Info)
        {
            if (!info.IsUnlocked || string.IsNullOrEmpty(info.Value)) continue;
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
            bodyTextArea.AddHighlight(info.Value, color);
        }

        _rootContainer.AddChild(bodyTextArea);
    }
}
