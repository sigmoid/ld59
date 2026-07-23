using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.Components;
using Quartz.EntityComponentScene.Serialization;
using Quartz.UI;
using ld59.UI.Editor;
using ld59.UI.Editor.Commands;

namespace ld59.UI;

// Reflection-driven property inspector for the in-game editor. Shows the selected entity's name
// and position, plus each component's serializable properties as editable rows, and lets you
// add/remove components by type name. Mirrors the type set ComponentSerializer supports, so it
// works for any component (Mesh3D NoCollide, light Range/Intensity/Color, etc.) with no per-type
// code. Text rows commit on Enter; bools are a toggle. All edits route through EditorHistory so
// Ctrl+Z/Ctrl+Y undo them. Tick() re-reads live values (e.g. while a gizmo drag is in progress)
// without rebuilding the panel, so displayed values track the scene in real time.
public sealed class EditorInspector
{
    private readonly Window _window;
    private readonly ScrollArea _scroll;
    private readonly SpriteFont _font;
    private readonly EditorHistory _history;
    private Entity _entity;
    private bool _showAddComponentPicker;

    private const int RowH = 26;
    private const int Pad  = 6;

    private static readonly HashSet<Type> Editable = new()
    {
        typeof(string), typeof(int), typeof(float), typeof(double), typeof(bool),
        typeof(Vector2), typeof(Vector3), typeof(Color),
    };

    // Rows that display a live value need re-reading each Tick without rebuilding the whole
    // panel (which would steal focus from whatever's being typed into). Rebuilt every Rebuild().
    private readonly List<(TextInput input, Func<string> get)> _liveText = new();
    private readonly List<(Button button, Func<bool> get)> _liveBool = new();

    public EditorInspector(Rectangle bounds, EditorHistory history)
    {
        _font = Core.DefaultFont;
        _history = history;
        // Fall back to '?' for glyphs the font lacks so labels/inputs never throw
        // "text contains characters that cannot be resolved by this SpriteFont".
        if (_font.DefaultCharacter == null && _font.Characters.Contains('?'))
            _font.DefaultCharacter = '?';

        _window = new Window(bounds, "Inspector", _font,
            ColorPalette.White, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.Black, 2);
        Core.UISystem.AddElement(_window);
        _window.SetVisibility(false);   // shown only in editor mode

        _scroll = new ScrollArea(_window.GetContentBounds());
        _window.AddChild(_scroll);

        Rebuild();
    }

    // Show/hide the whole panel (driven by editor mode).
    public void SetVisible(bool visible) => _window.SetVisibility(visible);

    // Reposition the panel window. Window.SetBounds translates all child widgets by the same
    // delta, so keep the size equal to the constructed size (only move, don't resize).
    public void MoveTo(Rectangle bounds) => _window.SetBounds(bounds);

    // True while any text field in this panel (Name, a property row) has focus, so the host can
    // suppress letter/Delete hotkeys that would otherwise fight with typing.
    public bool HasFocusedInput() => _scroll.Children.OfType<TextInput>().Any(t => t.IsFocused);

    // Point the inspector at a new entity (or null to clear).
    public void Show(Entity entity)
    {
        _entity = entity;
        _showAddComponentPicker = false;
        Rebuild();
    }

    // Re-read live values into already-built rows (skipping any TextInput currently focused, so
    // it doesn't fight with typing). Call every frame the entity might be changing externally
    // (e.g. a gizmo drag) -- cheap, and doesn't touch focus/layout like Rebuild() does.
    public void Tick()
    {
        if (_entity == null) return;
        foreach (var (input, get) in _liveText)
            if (!input.IsFocused) input.Text = get();
        foreach (var (button, get) in _liveBool)
            button.SetText(get() ? "true" : "false");
    }

    private void Rebuild()
    {
        _scroll.ClearChildren();
        _liveText.Clear();
        _liveBool.Clear();
        var c = _window.GetContentBounds();
        int y = c.Y + Pad;

        if (_entity == null)
        {
            AddLabel(c, y, "(nothing selected - left-click an object,\nor pick one from the Hierarchy panel)");
            _scroll.RefreshContentBounds();
            return;
        }

        AddHeader(c, ref y, "Entity");
        AddTextRow(c, ref y, "Name",
            () => _entity.Name,
            text => { Commit(_entity, typeof(Entity).GetProperty(nameof(Entity.Name)), text); return true; });
        AddVector3Row(c, ref y, "Position",
            () => _entity.Position3D,
            v => Commit(_entity, typeof(Entity).GetProperty(nameof(Entity.Position3D)), v));

        foreach (var comp in _entity.GetComponents())
        {
            AddComponentHeader(c, ref y, comp);
            foreach (var prop in SerializableProps(comp.GetType()))
                AddPropertyRow(c, ref y, comp, prop);
        }

        AddAddComponentSection(c, ref y);
        _scroll.RefreshContentBounds();
    }

    // Route every edit through the command stack so it's undoable.
    private void Commit(object target, PropertyInfo prop, object value) =>
        _history.Execute(new SetPropertyCommand(target, prop, value));

    private static IEnumerable<PropertyInfo> SerializableProps(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
         .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
                     && p.Name != "Entity"
                     && (Editable.Contains(p.PropertyType) || p.PropertyType.IsEnum));

    // ── row builders ─────────────────────────────────────────────────────────────
    private void AddHeader(Rectangle c, ref int y, string text)
    {
        y += 4;
        var lbl = new Label(new Rectangle(c.X + Pad, y, c.Width - 2 * Pad, RowH), text, _font,
            ColorPalette.ActualWhite, ColorPalette.DarkGreen);
        _scroll.AddChild(lbl);
        y += RowH + 2;
    }

    private void AddComponentHeader(Rectangle c, ref int y, Component comp)
    {
        y += 4;
        int removeW = 60;
        var lbl = new Label(new Rectangle(c.X + Pad, y, c.Width - 2 * Pad - removeW - 4, RowH),
            comp.GetType().Name, _font, ColorPalette.ActualWhite, ColorPalette.DarkGreen);
        _scroll.AddChild(lbl);

        var removeBtn = new Button(new Rectangle(c.Right - Pad - removeW, y, removeW, RowH),
            "Remove", _font, ColorPalette.Red, ColorPalette.DarkRed, ColorPalette.Black,
            () =>
            {
                _history.Execute(new RemoveComponentCommand(_entity, comp));
                Rebuild();
            });
        _scroll.AddChild(removeBtn);
        y += RowH + 2;
    }

    private void AddPropertyRow(Rectangle c, ref int y, Component comp, PropertyInfo prop)
    {
        if (prop.PropertyType == typeof(bool))
        {
            AddBoolRow(c, ref y, prop.Name,
                () => (bool)prop.GetValue(comp),
                v => Commit(comp, prop, v));
            return;
        }

        AddTextRow(c, ref y, prop.Name,
            () => Format(prop.GetValue(comp)),
            text =>
            {
                try { Commit(comp, prop, Convert(text, prop.PropertyType)); return true; }
                catch { return false; }
            });
    }

    private void AddVector3Row(Rectangle c, ref int y, string name, Func<Vector3> get, Action<Vector3> set)
    {
        AddTextRow(c, ref y, name,
            () => Format(get()),
            text => { try { set((Vector3)Convert(text, typeof(Vector3))); return true; } catch { return false; } });
    }

    private void AddTextRow(Rectangle c, ref int y, string name, Func<string> get, Func<string, bool> commit)
    {
        int nameW = (int)((c.Width - 2 * Pad) * 0.42f);
        int valX  = c.X + Pad + nameW + 4;
        int valW  = c.Right - Pad - valX;

        _scroll.AddChild(new Label(new Rectangle(c.X + Pad, y, nameW, RowH), name, _font, ColorPalette.Black));

        var input = new TextInput(new Rectangle(valX, y, valW, RowH), _font)
        {
            Text = get(),
        };
        // Commit on Enter; revert to the live value if the text doesn't parse.
        input.OnEnterPressed += _ => { if (!commit(input.Text)) input.Text = get(); };
        _scroll.AddChild(input);
        _liveText.Add((input, get));
        y += RowH + 2;
    }

    private void AddBoolRow(Rectangle c, ref int y, string name, Func<bool> get, Action<bool> set)
    {
        int nameW = (int)((c.Width - 2 * Pad) * 0.42f);
        int valX  = c.X + Pad + nameW + 4;
        int valW  = c.Right - Pad - valX;

        _scroll.AddChild(new Label(new Rectangle(c.X + Pad, y, nameW, RowH), name, _font, ColorPalette.Black));

        Button btn = null;
        btn = new Button(new Rectangle(valX, y, valW, RowH), get() ? "true" : "false", _font,
            ColorPalette.LightGreen, ColorPalette.Green, ColorPalette.Black,
            () => { bool nv = !get(); set(nv); btn.SetText(nv ? "true" : "false"); });
        _scroll.AddChild(btn);
        _liveBool.Add((btn, get));
        y += RowH + 2;
    }

    // "Add Component": a button that expands into a pick-list of every known component type the
    // entity doesn't already have (deduped -- EntityFactory registers both "Mesh3DComponent" and
    // "Mesh3D" for the same type, so only the shorter name is shown). Clicking a name adds it.
    private void AddAddComponentSection(Rectangle c, ref int y)
    {
        y += 4;
        _scroll.AddChild(new Button(new Rectangle(c.X + Pad, y, c.Width - 2 * Pad, RowH),
            _showAddComponentPicker ? "Add Component (cancel)" : "+ Add Component", _font,
            ColorPalette.LightGreen, ColorPalette.Green, ColorPalette.Black,
            () => { _showAddComponentPicker = !_showAddComponentPicker; Rebuild(); }));
        y += RowH + 2;

        if (!_showAddComponentPicker) return;

        foreach (var name in AvailableComponentNames())
        {
            string captured = name;
            _scroll.AddChild(new Button(new Rectangle(c.X + Pad + 12, y, c.Width - 2 * Pad - 12, RowH),
                name, _font, ColorPalette.White, ColorPalette.Green, ColorPalette.Black,
                () => AddComponent(captured)));
            y += RowH + 2;
        }
    }

    // Distinct component type names (short form preferred), excluding ones the entity already has.
    private IEnumerable<string> AvailableComponentNames() =>
        EntityFactory.KnownComponentTypeNames
            .GroupBy(EntityFactory.ResolveComponentType)
            .Where(g => _entity.GetComponent(g.Key) == null)
            .Select(g => g.OrderBy(n => n.Length).First())
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

    private void AddComponent(string typeName)
    {
        var type = EntityFactory.ResolveComponentType(typeName);
        if (type == null || _entity == null) return;

        var comp = (Component)Activator.CreateInstance(type);
        _history.Execute(new AddComponentCommand(_entity, comp));
        _showAddComponentPicker = false;
        Rebuild();
    }

    private void AddLabel(Rectangle c, int y, string text) =>
        _scroll.AddChild(new Label(new Rectangle(c.X + Pad, y, c.Width - 2 * Pad, RowH * 2), text, _font, ColorPalette.Black));

    // ── value <-> string ─────────────────────────────────────────────────────────
    private static string Format(object v) => v switch
    {
        null      => "",
        float f   => f.ToString("0.###", CultureInfo.InvariantCulture),
        double d  => d.ToString("0.###", CultureInfo.InvariantCulture),
        Vector3 p => $"{p.X:0.###},{p.Y:0.###},{p.Z:0.###}",
        Vector2 p => $"{p.X:0.###},{p.Y:0.###}",
        Color col => col.A == 255 ? $"{col.R},{col.G},{col.B}" : $"{col.R},{col.G},{col.B},{col.A}",
        _         => v.ToString(),
    };

    private static object Convert(string s, Type t)
    {
        s = s.Trim();
        if (t == typeof(string)) return s;
        if (t == typeof(int))    return int.Parse(s, CultureInfo.InvariantCulture);
        if (t == typeof(float))  return float.Parse(s, CultureInfo.InvariantCulture);
        if (t == typeof(double)) return double.Parse(s, CultureInfo.InvariantCulture);
        if (t == typeof(bool))   return bool.Parse(s);
        if (t.IsEnum)            return Enum.Parse(t, s, ignoreCase: true);

        var parts = s.Split(',').Select(p => p.Trim()).ToArray();
        if (t == typeof(Vector2))
            return new Vector2(F(parts[0]), F(parts[1]));
        if (t == typeof(Vector3))
            return new Vector3(F(parts[0]), F(parts[1]), F(parts[2]));
        if (t == typeof(Color))
            return new Color(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]),
                             parts.Length > 3 ? int.Parse(parts[3]) : 255);

        throw new FormatException($"No editor conversion for {t.Name}");

        static float F(string x) => float.Parse(x, CultureInfo.InvariantCulture);
    }
}
