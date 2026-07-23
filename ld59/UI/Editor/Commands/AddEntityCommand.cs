using System.Xml.Linq;
using Quartz;

namespace ld59.UI.Editor.Commands;

// Adds an entity to the scene. Scene.RemoveEntity always disposes the entity's components
// (Entity.Cleanup), so a disposed entity can't be resurrected -- Undo snapshots it to XML first,
// and a later Redo recreates a fresh instance from that snapshot. Entity is nulled out between
// Undo and the next Do/Redo; callers that hold a reference (selection, hierarchy) should re-read
// this property afterward rather than caching the original instance.
public sealed class AddEntityCommand : IEditorCommand
{
    private readonly Scene _scene;
    private XElement _snapshot;

    public Entity Entity { get; private set; }
    public string Label { get; }

    public AddEntityCommand(Scene scene, Entity entity, string label = null)
    {
        _scene = scene;
        Entity = entity;
        Label = label ?? $"Add {entity.Name}";
    }

    public void Do()
    {
        if (Entity == null)
            Entity = EntityXml.Deserialize(_snapshot);
        _scene.AddEntity(Entity);
    }

    public void Undo()
    {
        _snapshot = EntityXml.Serialize(Entity);
        _scene.RemoveEntity(Entity);
        Entity = null;
    }
}
