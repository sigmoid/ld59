using System.Collections.Generic;

namespace ld59.UI.Editor;

// A mutation the editor can undo/redo. Do() must be idempotent (it's called once immediately
// on Execute, then again on Redo), and Undo() must fully reverse it.
public interface IEditorCommand
{
    void Do();
    void Undo();
    string Label { get; }
}

// Simple linear undo/redo stack. Every editor mutation (property edit, transform, add/delete
// entity or component) should route through Execute so Ctrl+Z/Ctrl+Y work uniformly.
public sealed class EditorHistory
{
    private const int MaxHistory = 200;

    private readonly List<IEditorCommand> _undo = new();
    private readonly List<IEditorCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string UndoLabel => CanUndo ? _undo[^1].Label : null;
    public string RedoLabel => CanRedo ? _redo[^1].Label : null;

    public void Execute(IEditorCommand cmd)
    {
        cmd.Do();
        _undo.Add(cmd);
        if (_undo.Count > MaxHistory)
            _undo.RemoveAt(0);
        _redo.Clear();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var cmd = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        cmd.Undo();
        _redo.Add(cmd);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var cmd = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        cmd.Do();
        _undo.Add(cmd);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
