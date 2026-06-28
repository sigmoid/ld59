using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;
using ld59.UI.Powergrid;

public class PowergridUI : UIPanel
{
    private const string ScenesDir = "Content/files/scenes/powergrid";

    public static PowergridUI Current { get; private set; }

    private Rectangle     _bounds;
    private Window        _rootWindow;
    private PowergridView _sceneView;
    private PowergridInspector _inspector;
    private string        _levelName;

    // Progression mode (optional): an ordered list of level names. The player advances to the next
    // once the current level is fully solved. Null when opened on a single level.
    private readonly List<string> _progression;
    private readonly string _progName;
    private int _progIndex;

    private Button _editToggle, _saveBtn, _nextBtn;
    private readonly Dictionary<EditTool, Button> _toolButtons = new();
    private bool _editMode;

    private static readonly EditTool[] Tools =
    {
        EditTool.Select, EditTool.AddNode, EditTool.Connect, EditTool.Delete, EditTool.Region,
    };

    private const int ToolbarHeight = 40;
    private const int InspectorWidth = 234;
    private const int ButtonHeight = 30;

    public PowergridUI(Rectangle bounds, string levelName = null)
    {
        _bounds    = bounds;
        _levelName = levelName;
        CreateUI();
        Current = this;
    }

    /// <summary>Opens a level <em>progression</em>: an ordered list of level names. Starts on the first
    /// and lets the player advance once each is fully solved.</summary>
    public PowergridUI(Rectangle bounds, string progressionName, List<string> progression)
    {
        _bounds      = bounds;
        _progName    = progressionName;
        _progression = progression;
        _progIndex   = 0;
        _levelName   = progression.Count > 0 ? progression[0] : null;
        CreateUI();
        Current = this;
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        _bounds = bounds;
    }

    public override Rectangle GetBoundingBox() => _bounds;

    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);
        RefreshToolLabels();

        // In a progression, offer the next level once the current one is fully solved.
        if (_nextBtn != null)
        {
            bool canAdvance = _progression != null && !_editMode
                && _progIndex < _progression.Count - 1
                && _sceneView?.Controller?.IsLevelSolved == true;
            _nextBtn.SetVisibility(canAdvance);
        }
    }

    public void Save(string name = null)
    {
        name ??= _levelName;
        if (string.IsNullOrWhiteSpace(name))
        {
            Core.DeveloperConsole.PrintLine("powergrid-save: provide a name — scene has no file yet");
            return;
        }

        _levelName = name;
        _rootWindow.SetTitle(TitleForCurrent());

        var path = $"{ScenesDir}/{name}.xml";
        _sceneView.Scene.SerializeToFile(path);
        Core.DeveloperConsole.PrintLine($"Saved: {path}");
    }

    private void CreateUI()
    {
        _rootWindow = new Window(_bounds, TitleForCurrent(), Core.DefaultFont);
        _rootWindow.SetColors(ColorPalette.ActualWhite, ColorPalette.Black, ColorPalette.ActualWhite, ColorPalette.Black);
        _rootWindow.SetCloseButtonColors(ColorPalette.Black, Color.DarkGray);
        _rootWindow.OnWindowClosed += _ => { if (Current == this) Current = null; };

        BuildToolbar();
        LoadLevel(_levelName);

        Core.UISystem.AddElement(_rootWindow);
    }

    /// <summary>(Re)loads a level into the window, swapping the scene view + inspector. Always enters in
    /// play mode. Used both for the initial open and for advancing through a progression.</summary>
    private void LoadLevel(string name)
    {
        _levelName = name;

        if (_inspector != null) { _rootWindow.DestroyChild(_inspector); _inspector = null; }
        if (_sceneView != null) { _rootWindow.DestroyChild(_sceneView); _sceneView = null; }

        Scene scene = null;
        if (name != null)
        {
            try
            {
                scene = Scene.FromFile(Core.Content, $"files/scenes/powergrid/{name}.xml");
            }
            catch
            {
                Core.DeveloperConsole.PrintLine($"powergrid: could not load '{name}', starting with empty scene");
            }
        }

        _sceneView = new PowergridView(_rootWindow.GetContentBounds(), scene);
        _rootWindow.AddChild(_sceneView);

        _inspector = new PowergridInspector(new Rectangle(0, 0, 10, 10), _sceneView);
        _rootWindow.AddChild(_inspector);

        _editMode = false;
        _sceneView.EditMode = false;

        _rootWindow.SetTitle(TitleForCurrent());
        ApplyLayout();
        RefreshToolLabels();
    }

    private string TitleForCurrent()
    {
        if (_progression != null)
            return $"Powergrid - {_progName} ({_progIndex + 1}/{_progression.Count}): {_levelName}";
        return _levelName != null ? $"Powergrid - {_levelName}" : "Powergrid - (new)";
    }

    /// <summary>Advances to the next level in the progression (no-op if there isn't one).</summary>
    private void AdvanceLevel()
    {
        if (_progression == null || _progIndex >= _progression.Count - 1) return;
        _progIndex++;
        LoadLevel(_progression[_progIndex]);
    }

    private void BuildToolbar()
    {
        Button Mk(string text, System.Action onClick) => new(
            new Rectangle(0, 0, 10, 10), text, Core.DefaultFont,
            ColorPalette.Black, ColorPalette.DarkCream, ColorPalette.ActualWhite, onClick);

        _editToggle = Mk("Edit: off", ToggleEditMode);
        _rootWindow.AddChild(_editToggle);

        foreach (var tool in Tools)
        {
            var captured = tool;
            var btn = Mk(ToolName(tool), () => { _sceneView.Tool = captured; RefreshToolLabels(); });
            _toolButtons[tool] = btn;
            _rootWindow.AddChild(btn);
        }

        _saveBtn = Mk("Save", () => Save());
        _rootWindow.AddChild(_saveBtn);

        // Only meaningful in a progression; visibility is driven each frame in Update.
        _nextBtn = Mk("Next Level >", AdvanceLevel);
        _nextBtn.SetVisibility(false);
        _rootWindow.AddChild(_nextBtn);
    }

    private static string ToolName(EditTool tool) => tool switch
    {
        EditTool.Select  => "Select",
        EditTool.AddNode => "Add",
        EditTool.Connect => "Connect",
        EditTool.Delete  => "Delete",
        EditTool.Region  => "Region",
        _ => tool.ToString(),
    };

    private void ToggleEditMode()
    {
        _editMode = !_editMode;
        _sceneView.EditMode = _editMode;
        if (_editMode)
            _sceneView.Tool = EditTool.Select;
        else
            _sceneView.ResetPlayState(); // entering play: clear any test runes for a fresh board
        ApplyLayout();
        RefreshToolLabels();
    }

    private void ApplyLayout()
    {
        var content = _rootWindow.GetContentBounds();
        int btnY = content.Y + (ToolbarHeight - ButtonHeight) / 2;
        const int pad = 6;
        const int gap = 6;

        int x = content.X + pad;
        x += Place(_editToggle, x, btnY, "Edit: off") + gap;

        foreach (var tool in Tools)
        {
            var btn = _toolButtons[tool];
            btn.SetVisibility(_editMode);
            if (_editMode)
                x += Place(btn, x, btnY, $"[{ToolName(tool)}]") + gap; // size for the widest (active) label
        }

        _saveBtn.SetVisibility(_editMode);
        if (_editMode)
            x += Place(_saveBtn, x, btnY, "Save") + gap;

        // Next-level button sits at the far right of the toolbar (play mode only; see Update).
        if (_nextBtn != null)
        {
            int w = (int)Core.DefaultFont.MeasureString("Next Level >").X + 20;
            _nextBtn.SetBounds(new Rectangle(content.Right - w - pad, btnY, w, ButtonHeight));
        }

        int viewTop = content.Y + ToolbarHeight;
        int viewH = content.Height - ToolbarHeight;
        int viewW = content.Width - (_editMode ? InspectorWidth : 0);
        _sceneView.SetBounds(new Rectangle(content.X, viewTop, viewW, viewH));

        _inspector.SetVisibility(_editMode);
        if (_editMode)
            _inspector.SetBounds(new Rectangle(content.Right - InspectorWidth, viewTop, InspectorWidth, viewH));
    }

    /// <summary>Sizes a button's background to fit the given (widest-expected) text and positions it. Returns its width.</summary>
    private static int Place(Button btn, int x, int y, string widestText)
    {
        int w = (int)Core.DefaultFont.MeasureString(widestText).X + 20;
        btn.SetBounds(new Rectangle(x, y, w, ButtonHeight));
        return w;
    }

    private void RefreshToolLabels()
    {
        _editToggle.SetText(_editMode ? "Edit: ON" : "Edit: off");
        foreach (var (tool, btn) in _toolButtons)
            btn.SetText(_sceneView.Tool == tool ? $"[{ToolName(tool)}]" : ToolName(tool));
    }
}
