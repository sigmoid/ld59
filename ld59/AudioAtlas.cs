using System;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;

public static class AudioAtlas
{
    public static SoundEffect Click1 { get; private set; }
    public static SoundEffect Click2 { get; private set; }
    public static SoundEffect Click3 { get; private set; }
    public static SoundEffect Click4 { get; private set; }
    public static SoundEffect Click5 { get; private set; }
    public static SoundEffect Confirmation_001 { get; private set; }
    public static SoundEffect Confirmation_002 { get; private set; }
    public static SoundEffect Confirmation_003 { get; private set; }
    public static SoundEffect Confirmation_004 { get; private set; }
    public static SoundEffect Open_001 { get; private set; }
    public static SoundEffect Glass_001 { get; private set; }
    public static SoundEffect Glass_002 { get; private set; }
    public static SoundEffect Glass_003 { get; private set; }
    public static SoundEffect Glass_004 { get; private set; }
    public static SoundEffect Glass_005 { get; private set; }
    public static SoundEffect Glass_006 { get; private set; }
    public static SoundEffect Maximize_003 { get; private set; }
    public static SoundEffect Error_004 { get; private set; }
    public static SoundEffect Error_006 { get; private set; }
    public static SoundEffect Scroll_003 { get; private set; }
    public static SoundEffect Mouse_Click_Down {get; private set;}
    public static SoundEffect Mouse_Click_Up {get; private set;}

    private static readonly SoundEffect[] _clicks = new SoundEffect[5];
    private static readonly SoundEffect[] _glass = new SoundEffect[6];
    private static readonly SoundEffect[] _glassScan = new SoundEffect[5];
    private static readonly System.Random _random = new();

    public static void PlayRandomClick() => _clicks[_random.Next(0, _clicks.Length)].Play();
    public static void PlayRandomGlass() => _glassScan[_random.Next(0, _glassScan.Length)].Play();

    public static void Load(ContentManager content)
    {
        Click1 = _clicks[0] = content.Load<SoundEffect>("audio/click1");
        Click2 = _clicks[1] = content.Load<SoundEffect>("audio/click2");
        Click3 = _clicks[2] = content.Load<SoundEffect>("audio/click3");
        Click4 = _clicks[3] = content.Load<SoundEffect>("audio/click4");
        Click5 = _clicks[4] = content.Load<SoundEffect>("audio/click5");
        Confirmation_001 = content.Load<SoundEffect>("audio/confirmation_001");
        Confirmation_002 = content.Load<SoundEffect>("audio/confirmation_002");
        Confirmation_003 = content.Load<SoundEffect>("audio/confirmation_003");
        Confirmation_004 = content.Load<SoundEffect>("audio/confirmation_004");
        Open_001 = content.Load<SoundEffect>("audio/open_001");
        Maximize_003 = content.Load<SoundEffect>("audio/maximize_003");
        Error_004 = content.Load<SoundEffect>("audio/error_004");
        Error_006 = content.Load<SoundEffect>("audio/error_006");
        Glass_001 = _glass[0] = _glassScan[0] = content.Load<SoundEffect>("audio/glass_001");
        Glass_002 = _glass[1] = _glassScan[1] = content.Load<SoundEffect>("audio/glass_002");
        Glass_003 = _glass[2] = _glassScan[2] = content.Load<SoundEffect>("audio/glass_003");
        Glass_004 = _glass[3] =                  content.Load<SoundEffect>("audio/glass_004");
        Glass_005 = _glass[4] = _glassScan[3] = content.Load<SoundEffect>("audio/glass_005");
        Glass_006 = _glass[5] = _glassScan[4] = content.Load<SoundEffect>("audio/glass_006");
        Scroll_003 = content.Load<SoundEffect>("audio/scroll_003");
        Mouse_Click_Down = content.Load<SoundEffect>("audio/mouse_click_down");
        Mouse_Click_Up = content.Load<SoundEffect>("audio/mouse_click_up");
    }
}
