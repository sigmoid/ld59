using System.Collections.Generic;
using Microsoft.Xna.Framework;

public class GameFolder
{
    public string Name { get; set; }
    public bool IsHidden { get; set; } = false;
    public List<GameFile> Files { get; set; } = new List<GameFile>();
    public List<GameFolder> SubFolders { get; set; } = new List<GameFolder>();

    public bool HasNewItems()
    {
        foreach (var file in Files)
            if (!file.IsHidden && file.IsNewDiscovery && !file.IsEncrypted) return true;
        foreach (var sub in SubFolders)
            if (!sub.IsHidden && sub.HasNewItems()) return true;
        return false;
    }
}