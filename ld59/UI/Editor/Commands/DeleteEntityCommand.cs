using System.Xml.Linq;
using Quartz;

namespace ld59.UI.Editor.Commands;

// Mirror of AddEntityCommand: removes an entity, snapshotting it to XML first since
// Scene.RemoveEntity disposes its components. Undo recreates a fresh instance from the
// snapshot and re-adds it. See AddEntityCommand for why Entity's identity changes across
// undo/redo cycles.
public sealed class DeleteEntityCommand : IEditorCommand
{
    private readonly Scene _scene;
    private XElement _snapshot;

    public Entity Entity { get; private set; }
    public string Label { get; }

    public DeleteEntityCommand(Scene scene, Entity entity)
    {
        _scene = scene;
        Entity = entity;
        Label = $"Delete {entity.Name}";
    }

    public void Do()
    {
        _snapshot = EntityXml.Serialize(Entity);
        _scene.RemoveEntity(Entity);
        Entity = null;
    }

    public void Undo()
    {
        Entity = EntityXml.Deserialize(_snapshot);
        _scene.AddEntity(Entity);
    }
}
