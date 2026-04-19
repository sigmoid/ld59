using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;

public class FileLoader
{
    public GameFolder LoadFolder(string path)
    {
        var result = new GameFolder { Name = Path.GetFileName(path) };
        var subdirs = Directory.GetDirectories(path);
        foreach (var subdir in subdirs)
        {
            var subfolder = LoadFolder(subdir);
            result.SubFolders.Add(subfolder);
        }
        var files = LoadFiles(path);
        result.Files.AddRange(files);
        return result;
    }

    private List<GameFile> LoadFiles(string path)
    {
        var fileList = new List<GameFile>();
        var files = Directory.GetFiles(path);
        foreach (var file in files)
        {
            if(file.EndsWith(".key"))
            {
                var keyFile = LoadKeyFile(file);
                fileList.Add(keyFile);
                continue;
            }

            var fileObj = LoadGameFile(file);
            fileList.Add(fileObj);
        }
        return fileList;
    }

    private GameFile LoadGameFile(string path)
    {
        var content = File.ReadAllText(path);

        var gameFileContent = "";
        var infoContent = "";
        bool pastSeperator = false;

        foreach(var line in content.Split('\n'))
        {
            if(pastSeperator)
            {
                infoContent += line + "\n";
            }
            else
            {
                gameFileContent += line + "\n";
            }
            if(line.StartsWith("---"))
            {
                pastSeperator = true;
            }
        }

        var unlocks = GetInfoUnlocks(infoContent);

        var fileObj = new GameFile { Name = Path.GetFileName(path), Content = gameFileContent, Info = unlocks };
        if(fileObj.Name.EndsWith(".dec"))
        {
            fileObj.IsUnlocked = false;
        }
        else
        {
            fileObj.IsUnlocked = true;
        }
        return fileObj;
    }

    private List<GameInfo> GetInfoUnlocks(string data)
    {
        var unlocks = new List<GameInfo>();
        foreach(var line in data.Split('\n'))
        {
            if(line.StartsWith("#"))
            {
                continue;
            }
            
            var parts = line.Split(',');
            if(parts.Length == 2)
            {
                var info = new GameInfo { Value = parts[0].Trim(), Type = Enum.Parse<InfoType>(parts[1].Trim()) };
                unlocks.Add(info);
            }
        }
        return unlocks;
    }

    private GameKeyFile LoadKeyFile(string path)
    {
        var content = File.ReadAllText(path);
        var lines = content.Split('\n');

        var keyFile = new GameKeyFile { Name = Path.GetFileName(path) };
        if (lines.Length > 0) keyFile.Name1 = lines[0].Trim();
        if (lines.Length > 1) keyFile.Name2 = lines[1].Trim();

        if (lines.Length > 2)        {
            for (int i = 2; i < lines.Length; i++)
            {
                keyFile.UnlockedFiles.Add(lines[i].Trim());
            }
        }

        keyFile.Content = GetRandomKeyData();
        keyFile.IsUnlocked = false;
        return keyFile;
    }

    private string GetRandomKeyData()
    {
        RSA rsa = RSA.Create(2048);
        var key = rsa.ExportRSAPrivateKey();

        string base64Key = System.Convert.ToBase64String(key);
    
        return base64Key;
    }
}