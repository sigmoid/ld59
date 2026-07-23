using System;
using System.Reflection;

namespace ld59.UI.Editor.Commands;

// Generic reflection-based property edit, used by the inspector for both entity-level fields
// (Position3D) and component properties (NoCollide, light Range, ...). Captures the old value at
// construction time so Undo can restore it exactly.
public sealed class SetPropertyCommand : IEditorCommand
{
    private readonly object _target;
    private readonly PropertyInfo _property;
    private readonly object _oldValue;
    private readonly object _newValue;

    public string Label { get; }

    public SetPropertyCommand(object target, PropertyInfo property, object newValue, string label = null)
        : this(target, property, property.GetValue(target), newValue, label)
    {
    }

    // Use when the value has already been applied live before the command is created (e.g. a
    // gizmo drag that updates the property every frame) -- oldValue is what it was BEFORE the
    // drag started, captured by the caller at drag-begin, not re-read here.
    public SetPropertyCommand(object target, PropertyInfo property, object oldValue, object newValue, string label = null)
    {
        _target = target;
        _property = property;
        _oldValue = oldValue;
        _newValue = newValue;
        Label = label ?? $"Set {property.Name}";
    }

    public void Do()   => _property.SetValue(_target, _newValue);
    public void Undo() => _property.SetValue(_target, _oldValue);
}
