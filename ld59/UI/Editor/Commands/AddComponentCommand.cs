using Quartz;
using Quartz.Components;

namespace ld59.UI.Editor.Commands;

// Entity.RemoveComponent does not dispose the component (only Scene's queued entity-removal path
// does, via Entity.Cleanup), so add/remove of a component can safely reuse the same instance
// across undo/redo without recreating it.
public sealed class AddComponentCommand : IEditorCommand
{
    private readonly Entity _entity;
    private readonly Component _component;
    public string Label { get; }

    public AddComponentCommand(Entity entity, Component component)
    {
        _entity = entity;
        _component = component;
        Label = $"Add {component.GetType().Name}";
    }

    public void Do()   { _entity.AddComponent(_component); _component.Initialize(); }
    public void Undo() { _entity.RemoveComponent(_component); }
}
