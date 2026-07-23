using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;
using ld59.UI.Editor;

namespace ld59.UI;

// Two-tab browser (Models / Prefabs) for spawning new entities. Click a row to place it in front
// of the camera (see UI3DScene.PlaceModel / PlacePrefab); reposition afterward with the gizmo.
public sealed class EditorContentBrowser
{
    private enum Tab { Models, Prefabs }

    private readonly Window _window;
    private readonly ScrollArea _scroll;
    private readonly SpriteFont _font;
    private readonly UI3DScene _sceneView;
    private readonly string _contentSourceDir;

    private Tab _activeTab = Tab.Models;
    private Button _modelsTabBtn, _prefabsTabBtn;
    private Rectangle _scrollBounds;

    private const int RowH = 24;
    private const int TabH = 28;

    public EditorContentBrowser(Rectangle bounds, UI3DScene sceneView, string contentSourceDir)
    {
        _font = Core.DefaultFont;
        _sceneView = sceneView;
        _contentSourceDir = contentSourceDir;

        _window = new Window(bounds, "Content", _font,
            ColorPalette.White, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.Black, 2);
        Core.UISystem.AddElement(_window);
        _window.SetVisibility(false);

        var c = _window.GetContentBounds();
        int tabW = (c.Width - 8) / 2;
        _modelsTabBtn = new Button(new Rectangle(c.X, c.Y, tabW, TabH), "> Models", _font,
            ColorPalette.LightGreen, ColorPalette.Green, ColorPalette.Black, () => SwitchTab(Tab.Models));
        _prefabsTabBtn = new Button(new Rectangle(c.X + tabW + 8, c.Y, tabW, TabH), "Prefabs", _font,
            ColorPalette.White, ColorPalette.Green, ColorPalette.Black, () => SwitchTab(Tab.Prefabs));
        _window.AddChild(_modelsTabBtn);
        _window.AddChild(_prefabsTabBtn);

        // Manual refresh: the list is a snapshot taken when the browser becomes visible or the
        // tab switches, so a model/prefab added while it's already open (e.g. after re-exporting
        // from Blender) won't appear until you rescan -- this button does that without needing
        // to leave and re-enter editor mode.
        _window.AddChild(new Button(new Rectangle(c.X, c.Y + TabH + 4, c.Width, RowH),
            "Refresh List", _font, ColorPalette.White, ColorPalette.Green, ColorPalette.Black,
            Refresh));

        _scrollBounds = new Rectangle(c.X, c.Y + TabH + RowH + 8, c.Width, c.Height - TabH - RowH - 8);
        _scroll = new ScrollArea(_scrollBounds);
        _window.AddChild(_scroll);

        Refresh();
    }

    public void SetVisible(bool visible)
    {
        _window.SetVisibility(visible);
        if (visible) Refresh();
    }

    // Reposition the panel (move only -- keep the constructed size so child widgets stay laid out).
    public void MoveTo(Rectangle bounds) => _window.SetBounds(bounds);

    private void SwitchTab(Tab tab)
    {
        _activeTab = tab;
        _modelsTabBtn.SetText(tab == Tab.Models ? "> Models" : "Models");
        _prefabsTabBtn.SetText(tab == Tab.Prefabs ? "> Prefabs" : "Prefabs");
        Refresh();
    }

    private void Refresh()
    {
        _scroll.ClearChildren();
        // Lay rows out against the SCROLL area's CURRENT bounds, not the window's -- rows
        // positioned above the scroll viewport get scissor-clipped away by ScrollArea, which is
        // what hid every model except the last one. Query the live bounds rather than the
        // constructor-time _scrollBounds: MoveTo (ArrangeEditorWindows on entering the editor)
        // translates the ScrollArea, so a Refresh after a move -- switching to the Prefabs tab,
        // the Refresh List button, re-showing -- would otherwise lay rows at the pre-move
        // position and scissor-clip them all away (the "empty Prefabs list" bug).
        var c = _scroll.GetBoundingBox();

        List<string> entries = _activeTab == Tab.Models
            ? ModelCatalog.ListBuiltModels(_contentSourceDir)
            : PrefabCatalog.ListPrefabs(_contentSourceDir);

        int y = c.Y;
        foreach (var entry in entries)
        {
            string display = entry;
            string captured = entry;
            var btn = new Button(new Rectangle(c.X, y, c.Width - 20, RowH), display, _font,
                ColorPalette.White, ColorPalette.Green, ColorPalette.Black,
                () =>
                {
                    if (_activeTab == Tab.Models) _sceneView.PlaceModel(captured);
                    else _sceneView.PlacePrefab(captured);
                });
            _scroll.AddChild(btn);
            y += RowH + 2;
        }

        if (entries.Count == 0)
        {
            _scroll.AddChild(new Label(new Rectangle(c.X, y, c.Width - 20, RowH * 2),
                _activeTab == Tab.Models ? "(no built models found)" : "(no prefabs found)",
                _font, ColorPalette.Black));
        }

        _scroll.RefreshContentBounds();
    }
}
