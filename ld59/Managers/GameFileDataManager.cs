using Microsoft.Xna.Framework;
using Quartz;

public class GameFileDataManager : IManager
{

    private GameFolder _rootFolder;
    private const string ROOT_FILE_PATH = "Content/files/root";

    public void Initialize(Scene scene)
    {
        FileLoader loader = new FileLoader();
        _rootFolder = loader.LoadFolder(ROOT_FILE_PATH);
    }

    public void Update(GameTime gameTime)
    {
    }

    public GameFolder GetRootFolder()
    {
        return _rootFolder;
    }

    public GameFolder GetFolderByPath(string path)
    {
        if(path == "") return _rootFolder;
        var parts = path.Split('/');
        GameFolder current = _rootFolder;
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            current = current.SubFolders.Find(f => f.Name == part);
            if (current == null) return null;
        }
        return current;
    }

    public GameFile GetFileByPath(string path)
    {
        var parts = path.Split('/');
        if (parts.Length == 0) return null;
        var fileName = parts[parts.Length - 1];
        var folderPath = string.Join("/", parts, 0, parts.Length - 1);
        var folder = GetFolderByPath(folderPath);
        if (folder == null) return null;
        return folder.Files.Find(f => f.Name == fileName);
    }
}