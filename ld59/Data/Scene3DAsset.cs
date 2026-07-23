using System;
using System.IO;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

// Thin wrapper around a 3D scene file. GameFile.Content points to one of these.
// Exists so metadata (title, camera hints, etc.) can be added without changing the scene XML.
public class Scene3DAsset
{
    public string ScenePath   { get; set; }        // path relative to content root
    public string Title       { get; set; } = "";
    public string Mode        { get; set; } = "";  // "Walk" launches the walking sim; empty = viewer
    public string NavMeshPath { get; set; } = "";  // navmesh .obj, relative to content root (Walk mode)
    public string Ambient     { get; set; } = "";  // ambient light "r,g,b" (0-255); empty = default
    public bool   Skybox      { get; set; }        // render the procedural moon skybox

    public static Scene3DAsset Load(string contentRelativePath)
    {
        using var stream = TitleContainer.OpenStream(
            Path.Combine(Quartz.Core.Content.RootDirectory, contentRelativePath));
        var doc = XDocument.Load(stream);
        return new Scene3DAsset
        {
            ScenePath   = doc.Root?.Element("ScenePath")?.Value   ?? "",
            Title       = doc.Root?.Element("Title")?.Value       ?? "",
            Mode        = doc.Root?.Element("Mode")?.Value        ?? "",
            NavMeshPath = doc.Root?.Element("NavMeshPath")?.Value ?? "",
            Ambient     = doc.Root?.Element("Ambient")?.Value     ?? "",
            Skybox      = string.Equals(doc.Root?.Element("Skybox")?.Value, "true", StringComparison.OrdinalIgnoreCase),
        };
    }
}
