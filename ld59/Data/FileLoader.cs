using System.Collections.Generic;
using System.IO;

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
            var fileObj = new GameFile { Name = Path.GetFileName(file), Content = File.ReadAllText(file) };
            fileList.Add(fileObj);
        }
        return fileList;
    }
}