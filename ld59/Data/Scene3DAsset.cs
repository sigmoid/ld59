using System.IO;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

// Thin wrapper around a 3D scene file. GameFile.Content points to one of these.
// Exists so metadata (title, camera hints, etc.) can be added without changing the scene XML.
public class Scene3DAsset
{
    public string ScenePath { get; set; }   // path relative to content root
    public string Title     { get; set; } = "";

    public static Scene3DAsset Load(string contentRelativePath)
    {
        using var stream = TitleContainer.OpenStream(
            Path.Combine(Quartz.Core.Content.RootDirectory, contentRelativePath));
        var doc = XDocument.Load(stream);
        return new Scene3DAsset
        {
            ScenePath = doc.Root?.Element("ScenePath")?.Value ?? "",
            Title     = doc.Root?.Element("Title")?.Value     ?? "",
        };
    }
}
