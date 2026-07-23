using Quartz;
using Quartz.Components;

namespace ld59.UI.Editor.Commands;

// See AddComponentCommand: RemoveComponent doesn't dispose, so this can safely re-attach the
// same instance on Undo.
public sealed class RemoveComponentCommand : IEditorCommand
{
    private readonly Entity _entity;
    private readonly Component _component;
    public string Label { get; }

    public RemoveComponentCommand(Entity entity, Component component)
    {
        _entity = entity;
        _component = component;
        Label = $"Remove {component.GetType().Name}";
    }

    public void Do()   { _entity.RemoveComponent(_component); }
    public void Undo() { _entity.AddComponent(_component); }
}
