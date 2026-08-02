using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.Components;
using Quartz.UI;

namespace ld59.UI;

// Lists every entity in the scene (mesh, light, spawn point, ...) as a clickable row, so
// non-mesh entities -- which the viewport's ID-buffer pick can't select on its own -- are still
// reachable. Selecting a row calls back into UI3DScene.Select, the same path viewport clicks use.
public sealed class EditorHierarchyPanel
{
    private readonly Window _window;
    private readonly ScrollArea _scroll;
    private readonly SpriteFont _font;
    private readonly Action<Entity> _onSelect;

    private const int RowH = 24;
    private const int PlayBtnH = 28;

    private readonly List<Button> _rows = new();
    private Entity _selected;

    /// <summary>
    /// Absolute rectangle the rows are laid out in, as of right now. Recomputed on every use rather
    /// than cached at construction: the panel is moved after it is built (entering the editor fans
    /// the tool windows out around the viewport), and a cached rectangle goes on placing rows where
    /// the panel USED to be. Rows stranded there are invisible -- <see cref="ScrollArea"/> scissors
    /// its drawing to the panel -- but still live, because a <see cref="Button"/> hit-tests its own
    /// bounds and nothing clips input. Stranded over the 3D viewport, they swallowed viewport clicks
    /// and silently re-selected an entity, which is what made gizmo clicks land on whatever was
    /// behind the handle.
    /// </summary>
    private Rectangle RowArea
    {
        get
        {
            var c = _window.GetContentBounds();
            return new Rectangle(c.X, c.Y + PlayBtnH + 4, c.Width, c.Height - PlayBtnH - 4);
        }
    }

    public EditorHierarchyPanel(Rectangle bounds, Action<Entity> onSelect, Action onPlayFromCamera)
    {
        _font = Core.DefaultFont;
        _onSelect = onSelect;

        _window = new Window(bounds, "Hierarchy", _font,
            ColorPalette.White, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.Black, 2);
        Core.UISystem.AddElement(_window);
        _window.SetVisibility(false);

        var c = _window.GetContentBounds();

        _window.AddChild(new Button(new Rectangle(c.X, c.Y, c.Width - 20, PlayBtnH),
            "Play From Camera (P)", _font, ColorPalette.LightGreen, ColorPalette.Green, ColorPalette.Black,
            () => onPlayFromCamera()));

        _scroll = new ScrollArea(RowArea);
        _window.AddChild(_scroll);
    }

    public void SetVisible(bool visible) => _window.SetVisibility(visible);

    // Reposition the panel (move only -- keep the constructed size so child widgets stay laid out).
    public void MoveTo(Rectangle bounds)
    {
        // Window.SetBounds carries its OWN children along (the play button, the scroll area), but
        // the rows live inside the scroll area and hold absolute positions of their own, so they
        // have to be walked over to the new location explicitly or they stay behind -- on top of
        // whatever the panel just vacated, invisible and still clickable.
        _window.SetBounds(bounds);
        LayoutRows();
    }

    // Re-flag which row is highlighted (call on selection change) without rebuilding rows.
    public void SetSelected(Entity e) => _selected = e;

    // Rebuild the row list from the current scene entities. Cheap enough to call on every
    // structural change (delete, undo/redo, placement) rather than diffing.
    public void Refresh(Scene scene)
    {
        _scroll.ClearChildren();
        _rows.Clear();

        var entities = new List<Entity>(scene.GetEntities());
        entities.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        foreach (var e in entities)
        {
            string hint = ComponentHint(e);
            string label = string.IsNullOrEmpty(hint) ? e.Name : $"{e.Name}  [{hint}]";

            var target = e; // capture
            bool isSelected = ReferenceEquals(e, _selected);
            var btn = new Button(Rectangle.Empty, label, _font,
                isSelected ? ColorPalette.LightGreen : ColorPalette.White,
                ColorPalette.Green, ColorPalette.Black,
                () => _onSelect(target));
            _scroll.AddChild(btn);
            _rows.Add(btn);
        }

        LayoutRows();
    }

    // The one place row geometry is decided, so a rebuild and a move can't disagree about where the
    // rows are. Rows are created bounds-less and placed here.
    private void LayoutRows()
    {
        var c = RowArea;
        _scroll.SetBounds(c);

        int y = c.Y;
        foreach (var row in _rows)
        {
            row.SetBounds(new Rectangle(c.X, y, c.Width - 20, RowH));
            y += RowH + 2;
        }

        _scroll.RefreshContentBounds();
    }

    private static string ComponentHint(Entity e)
    {
        if (e.GetComponent<Mesh3DComponent>() != null) return "Mesh";
        if (e.GetComponent<PointLightComponent>() != null) return "Light";
        if (e.GetComponent<DirectionalLightComponent>() != null) return "Sun";
        if (e.Name == "PlayerStart") return "Spawn";
        return null;
    }
}
