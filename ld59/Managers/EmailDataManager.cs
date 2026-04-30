using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Quartz;

public class EmailDataManager : IManager
{
    private const string EMAIL_PATH = "Content/emails";

    private List<Email> _allEmails = new();
    private List<Email> _inbox = new();

    public static event Action<Email> OnEmailDelivered;

    public void Initialize(Scene scene)
    {
        var loader = new EmailLoader();
        _allEmails = loader.LoadAll(EMAIL_PATH);
    }

    public void Update(GameTime gameTime) { }

    /// <summary>
    /// Delivers the named .eml file to the player's inbox and fires OnEmailDelivered.
    /// Call this from gameplay code to trigger emails at the right moment.
    /// </summary>
    public void DeliverEmail(string filename)
    {
        var email = _allEmails.FirstOrDefault(e => e.FileName == filename);
        if (email == null || _inbox.Contains(email)) return;

        _inbox.Add(email);
        OnEmailDelivered?.Invoke(email);
    }

    public List<Email> GetInbox() => _inbox.ToList();

    public bool HasUnread() => _inbox.Any(e => !e.IsRead);

    public bool UnlockEmailInfo(Email email)
    {
        var dataManager = Core.CurrentScene.GetManager<GameFileDataManager>();
        if (dataManager == null) return false;
        return dataManager.UnlockInfo(email.Info);
    }
}
