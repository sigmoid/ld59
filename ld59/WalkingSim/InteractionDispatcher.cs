using Microsoft.Xna.Framework;
using Quartz;

namespace ld59.WalkingSim;

// Routes a walking-sim interaction (E on a hovered object) to an effect, based on its Action.
// This is the single place to add new interaction effects — each is one case. The component
// carries Action (verb), Target (what it acts on), and Message (display payload).
//
// Wireable effects and how they'd be implemented (add as needed):
//   "show-text"    (default) -> NotificationPopup(Message)                                [done]
//   "reveal"/"hide"/"toggle" -> set entity Target's Visible (environment triggers)        [done]
//   "play-sound"   -> AudioAtlas.<clip>.Play() (map Target -> clip)                        [trivial]
//   "reveal-file"  -> Core.CurrentScene.GetManager<GameFileDataManager>().UnlockPath(Target)  [easy]
//   "open-file"    -> GetFileByPath(Target) then the StartMenu.OpenFile switch (make shared)   [easy]
//   "unlock-info"  -> GameFileDataManager.UnlockInfo(...) (parse Target as Type:Value)      [easy]
//   "learn-glyph"  -> teach Target into a player vocabulary store + reveal UI          [new system]
public static class InteractionDispatcher
{
    // scene = the walking-sim scene, needed to resolve Target entity names for reveal/hide/toggle.
    public static void Dispatch(Interactable3DComponent comp, Scene scene)
    {
        switch (comp.Action)
        {
            case "reveal": SetVisible(scene, comp.Target, true);  break;
            case "hide":   SetVisible(scene, comp.Target, false); break;
            case "toggle": Toggle(scene, comp.Target);            break;

            // "show-text" and anything unrecognized/empty: surface the message.
            case "show-text":
            case "":
            default:
                ShowText(comp.Message);
                break;
        }
    }

    // Show/hide/toggle another entity in the world by name (environment triggers).
    private static void SetVisible(Scene scene, string entityName, bool visible)
    {
        var e = scene?.FindEntityByName(entityName);
        if (e != null) e.Visible = visible;
    }

    private static void Toggle(Scene scene, string entityName)
    {
        var e = scene?.FindEntityByName(entityName);
        if (e != null) e.Visible = !e.Visible;
    }

    private static void ShowText(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        int pw = 420, ph = 140;
        var rect = new Rectangle((Core.ScreenWidth - pw) / 2, (Core.ScreenHeight - ph) / 2, pw, ph);
        Core.UISystem.AddElement(new NotificationPopup(rect, message));
    }
}
