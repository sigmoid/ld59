using System.IO;
using System.Xml.Linq;
using System.Xml;
using Quartz;

namespace ld59.UI.Editor;

// Writes the live scene back out as XML, in the same <Scene><Entities><Entity><Position3D>...
// shape EntityFactory reads (see EntityXml). Used for the editor's Ctrl+S / Save button.
public static class SceneWriter
{
    public static void Save(Scene scene, string absoluteScenePath)
    {
        var root = new XElement("Scene");
        var entities = new XElement("Entities");
        root.Add(entities);

        foreach (var e in scene.GetEntities())
            entities.Add(EntityXml.Serialize(e));

        Directory.CreateDirectory(Path.GetDirectoryName(absoluteScenePath)!);

        var settings = new XmlWriterSettings { Indent = true, IndentChars = "  " };
        using var writer = XmlWriter.Create(absoluteScenePath, settings);
        root.Save(writer);
    }
}
