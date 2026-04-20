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
        // collect all of the key files by traversing the folder structure
        _keyFiles.Clear();
        CollectKeyFiles(_rootFolder);

        foreach(var keyFile in _keyFiles)
        {
            if((keyFile.Name1 == file1.Name && keyFile.Name2 == file2.Name) || (keyFile.Name1 == file2.Name && keyFile.Name2 == file1.Name))
            {
                keyFile.IsUnlocked = true;
                return keyFile.Name;
            }
        }
        return null;
    }

    public GameFile TryToDecryptFile(GameKeyFile keyFile, GameFile encryptedFile)
    {
        if((keyFile.UnlockedFiles.Contains(encryptedFile.Name)) && keyFile.IsUnlocked)
        {
            foreach(var unlockedFileName in keyFile.UnlockedFiles)
            {
                if(unlockedFileName == encryptedFile.Name)
                {
                    var resultFileName = encryptedFile.Name.Replace(".txt", ".dec");
                    var decryptedFile = GetFileByPath("decrypted/" + resultFileName);
                    if(decryptedFile != null)                    {
                        decryptedFile.IsUnlocked = true;
                        return decryptedFile;
                    }
                }
            }
        }
        return null;
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


}