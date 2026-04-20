public enum InfoType
{
    Name,
    Rank,
    Position,
    Codename
}

public class GameInfo
{
    public string Value {get;set;}
    public InfoType Type {get;set;}
    public bool IsUnlocked {get;set;} = false;
}