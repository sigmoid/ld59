using System.Collections.Generic;
using Microsoft.Xna.Framework;

public class GameFolder
{
    public string Name { get; set; }
    public List<GameFile> Files { get; set; } = new List<GameFile>();
    public List<GameFolder> SubFolders { get; set; } = new List<GameFolder>();
}