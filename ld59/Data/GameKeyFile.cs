using System.Collections.Generic;

public class GameKeyFile : GameFile
{
    public string Name1 { get; set; }
    public string Name2 { get; set; }
    public List<string> UnlockedFiles { get; set; } = new List<string>();
}