using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

namespace ld59.UI;

// Small toolbar-style panel for navmesh authoring: bake on demand, toggle the overlay, and
// bulk-tag every Mesh3D as a navmesh obstacle (quick-start for a scene with nothing tagged yet,
// or a freshly placed prop). Mirrors the B/N hotkeys UI3DScene already has, as clickable buttons
// with live bake-result feedback.
public sealed class EditorNavMeshPanel
{
    private readonly Window _window;
    private readonly SpriteFont _font;
    private readonly UI3DScene _sceneView;
    private Label _statusLabel;

    private const int RowH = 26;
    private const int Pad = 6;

    public EditorNavMeshPanel(Rectangle bounds, UI3DScene sceneView)
    {
        _font = Core.DefaultFont;
        _sceneView = sceneView;

        _window = new Window(bounds, "Navmesh", _font,
            ColorPalette.White, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.Black, 2);
        Core.UISystem.AddElement(_window);
        _window.SetVisibility(false);

        _sceneView.OnBakeStarted   += () => _statusLabel.Text = "Baking... (running on background thread)";
        _sceneView.OnBakeCompleted += RefreshStatus;

        Build();
    }

    public void SetVisible(bool visible) => _window.SetVisibility(visible);

    // Reposition the panel (move only -- keep the constructed size so child widgets stay laid out).
    public void MoveTo(Rectangle bounds) => _window.SetBounds(bounds);

    private void Build()
    {
        var c = _window.GetContentBounds();
        int y = c.Y + Pad;

        _window.AddChild(new Button(new Rectangle(c.X + Pad, y, c.Width - 2 * Pad, RowH),
            "Bake Navmesh", _font, ColorPalette.LightGreen, ColorPalette.Green, ColorPalette.Black,
            () => _sceneView.BakeNavMesh()));
        y += RowH + 4;

        _window.AddChild(new Button(new Rectangle(c.X + Pad, y, c.Width - 2 * Pad, RowH),
            "Toggle Overlay (N)", _font, ColorPalette.White, ColorPalette.Green, ColorPalette.Black,
            () => _sceneView.ShowNavMesh = !_sceneView.ShowNavMesh));
        y += RowH + 4;

        _window.AddChild(new Button(new Rectangle(c.X + Pad, y, c.Width - 2 * Pad, RowH),
            "Tag All Meshes as Obstacles", _font, ColorPalette.Orange, ColorPalette.DarkRed, ColorPalette.Black,
            () =>
            {
                int n = _sceneView.TagAllMeshesAsObstacles();
                _statusLabel.Text = $"Tagged {n} mesh(es). Bake to apply.";
            }));
        y += RowH + 10;

        _statusLabel = new Label(new Rectangle(c.X + Pad, y, c.Width - 2 * Pad, RowH * 3),
            "(not baked yet)", _font, ColorPalette.Black);
        _window.AddChild(_statusLabel);
    }

    private void RefreshStatus()
    {
        _statusLabel.Text = _sceneView.LastBakeError != null
            ? $"Bake failed:\n{_sceneView.LastBakeError}"
            : $"{_sceneView.LastBakeSourceTris} source tris\n-> {_sceneView.LastBakeNavTris} nav tris";
    }
}
