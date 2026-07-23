using System.Xml.Linq;
using Quartz;

namespace ld59.UI.Editor;

// 3D-aware entity <-> XML for the walking-sim editor. Entity.Serialize() (the engine's generic
// serializer) writes a 2D <Position> element and drops Z, which loses height for these scenes.
// This mirrors the format the Blender exporter and hand-authored scene XML already use
// (<Position3D>, <Component Type=.. Property Name=.. Value=.. Type=..>), so files this writes
// load with the existing EntityFactory unchanged. Used by SceneWriter (full scene save) and
// DeleteEntityCommand (snapshot-based undo, since Entity.Cleanup() disposes live components).
public static class EntityXml
{
    public static XElement Serialize(Entity e)
    {
        var el = new XElement("Entity", new XAttribute("Name", e.Name));
        if (!e.Visible)
            el.SetAttributeValue("Visible", "false");

        var pos = e.Position3D;
        el.Add(new XElement("Position3D",
            new XElement("X", pos.X.ToString("R")),
            new XElement("Y", pos.Y.ToString("R")),
            new XElement("Z", pos.Z.ToString("R"))));

        foreach (var comp in e.GetComponents())
            el.Add(comp.Serialize(null));

        return el;
    }

    public static Entity Deserialize(XElement el, string baseDirectory = null)
        => Entity.FromXElement(el, baseDirectory);
}
