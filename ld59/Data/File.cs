using System.Collections.Generic;
using System.ComponentModel;

public class GameFile
{
    public string Name {get;set;}
    public string Content {get;set;}
    public bool IsEncrypted {get;set;}
    public List<GameInfo> Info {get;set;} = new List<GameInfo>();
    public List<string> Keys {get;set;} = new List<string>();
    public bool IsNewDiscovery {get;set;} = false;
}