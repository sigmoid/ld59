using System.Collections.Generic;

public class GameFile
{
    public string Name {get;set;}
    public string Content {get;set;}
    public bool IsUnlocked {get;set;}
    public List<GameInfo> Info {get;set;} = new List<GameInfo>();
}