using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

public class EmailListUI : UIContainer
{
    private Rectangle _bounds;
    private Window _rootContainer;
    private VerticalLayoutGroup _listLayout;
    private ScrollArea _scrollArea;
    private const int ROW_HEIGHT = 65;

    public EmailListUI(Rectangle bounds)
    {
        _bounds = bounds;
        CreateUI();
        EmailDataManager.OnEmailDelivered += HandleNewEmail;
    }

    public override void SetBounds(Rectangle bounds) { base.SetBounds(bounds); _bounds = bounds; }
    public override Rectangle GetBoundingBox() => _bounds;

    private void CreateUI()
    {
        var emailIcon = Core.Content.Load<Texture2D>("images/email_icon");
        _rootContainer = new Window(_bounds, "Email", Core.DefaultFont,
            ColorPalette.ActualWhite, ColorPalette.Black,
            ColorPalette.ActualWhite, ColorPalette.Black, 2);
        _rootContainer.SetCloseButtonColors(ColorPalette.Black, Color.DarkGray);
        _rootContainer.OnWindowClosed += _ =>
        {
            EmailDataManager.OnEmailDelivered -= HandleNewEmail;
        };
        Core.UISystem.AddElement(_rootContainer);
        TaskbarRegistry.Register("Email", emailIcon, _rootContainer);

        var content = _rootContainer.GetContentBounds();
        _scrollArea = new ScrollArea(new Rectangle(content.X, content.Y, content.Width, content.Height));
        _rootContainer.AddChild(_scrollArea);

        _listLayout = new VerticalLayoutGroup(
            new Rectangle(_scrollArea.GetBoundingBox().X, _scrollArea.GetBoundingBox().Y,
                          _scrollArea.GetBoundingBox().Width, _scrollArea.GetBoundingBox().Height), 2);
        _scrollArea.AddChild(_listLayout);

        RebuildList();
    }

    private void RebuildList()
    {
        _listLayout.ClearChildren();

        var emailManager = Core.CurrentScene.GetManager<EmailDataManager>();
        if (emailManager == null) return;

        var inbox = emailManager.GetInbox();
        inbox.Reverse(); // most recent first

        foreach (var email in inbox)
        {
            var item = new EmailListItemUI(
                new Rectangle(_listLayout.GetBoundingBox().X, 0, _listLayout.GetBoundingBox().Width, ROW_HEIGHT),
                email,
                OpenEmail);
            _listLayout.AddChild(item);
        }

        _scrollArea.RefreshContentBounds();
    }

    private void OpenEmail(Email email)
    {
        email.IsRead = true;
        var viewer = new EmailViewerUI(new Rectangle(_bounds.X + 30, _bounds.Y + 30, 700, 600), email);
        Core.UISystem.AddElement(viewer);
        RebuildList();
    }

    private void HandleNewEmail(Email email)
    {
        RebuildList();
    }
}
