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

    private Entity _selected;

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

        _scrollBounds = new Rectangle(c.X, c.Y + PlayBtnH + 4, c.Width, c.Height - PlayBtnH - 4);
        _scroll = new ScrollArea(_scrollBounds);
        _window.AddChild(_scroll);
    }

    private readonly Rectangle _scrollBounds;

    public void SetVisible(bool visible) => _window.SetVisibility(visible);

    // Reposition the panel (move only -- keep the constructed size so child widgets stay laid out).
    public void MoveTo(Rectangle bounds) => _window.SetBounds(bounds);

    // Re-flag which row is highlighted (call on selection change) without rebuilding rows.
    public void SetSelected(Entity e) => _selected = e;

    // Rebuild the row list from the current scene entities. Cheap enough to call on every
    // structural change (delete, undo/redo, placement) rather than diffing.
    public void Refresh(Scene scene)
    {
        _scroll.ClearChildren();
        var c = _scrollBounds;

        int y = c.Y;
        var entities = new List<Entity>(scene.GetEntities());
        entities.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        foreach (var e in entities)
        {
            string hint = ComponentHint(e);
            string label = string.IsNullOrEmpty(hint) ? e.Name : $"{e.Name}  [{hint}]";

            var target = e; // capture
            bool isSelected = ReferenceEquals(e, _selected);
            var btn = new Button(new Rectangle(c.X, y, c.Width - 20, RowH), label, _font,
                isSelected ? ColorPalette.LightGreen : ColorPalette.White,
                ColorPalette.Green, ColorPalette.Black,
                () => _onSelect(target));
            _scroll.AddChild(btn);
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
