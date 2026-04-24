using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Quartz;

public class GameFileDataManager : IManager
{

    private GameFolder _rootFolder;
    private List<GameKeyFile> _keyFiles = new List<GameKeyFile>();
    private const string ROOT_FILE_PATH = "Content/files/root";
    private List<GameInfo> _unlockedInfo = new List<GameInfo>();

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

    public string GenerateKeyFile(GameFile file1, GameFile file2)
    {

        return null;
    }

    public bool TryToDecryptFile(GameFile encryptedFile, List<string> keys)
    {
        if(!encryptedFile.IsEncrypted)
        {
            return false;
        }

        if(keys.All(k => encryptedFile.Keys.Contains(k)) && encryptedFile.Keys.All(k => keys.Contains(k)))
        {
            encryptedFile.IsEncrypted = false;
            encryptedFile.IsNewDiscovery = true;
            return true;
        }

        return false;
    }

    public List<GameInfo> GetAllInfoOfType(InfoType type)
    {
        List<GameInfo> result = new List<GameInfo>();
        _unlockedInfo.Where(i => i.Type == type).ToList().ForEach(i => result.Add(i));
        return result;
    }

    public bool UnlockData(GameFile file)
    {
        bool didUnlock = false;

        if(file.IsEncrypted)
        {
            return false;
        }

        foreach (var info in file.Info)
        {
            info.IsUnlocked = true;
            if(!_unlockedInfo.Contains(info))
            {
                didUnlock = true;
                _unlockedInfo.Add(info);
            }
        }

        return didUnlock;
    }

    private void CollectKeyFiles(GameFolder folder)
    {
        foreach (var file in folder.Files)
        {
            if (file is GameKeyFile keyFile)
            {
                _keyFiles.Add(keyFile);
            }
        }
        foreach (var subfolder in folder.SubFolders)
        {
            CollectKeyFiles(subfolder);
        }
    }

    private GameFile SearchForFileInDirectory(string directory, string fileName)
    {
        var folder = GetFolderByPath(directory);
        if (folder == null) return null;
        var res = folder.Files.Find(f => f.Name == fileName);
        if(res == null)
        {
            foreach(var subfolder in folder.SubFolders)
            {
                res = SearchForFileInDirectory(directory + "/" + subfolder.Name, fileName);
                if(res != null) return res;
            }
        }
        return res;
    }

}