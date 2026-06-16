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

    private Button _editToggle, _saveBtn;
    private Button _runBtn, _stepBtn, _resetBtn;
    private readonly Dictionary<EditTool, Button> _toolButtons = new();
    private bool _editMode;

    private static readonly EditTool[] Tools =
    {
        EditTool.Select, EditTool.AddNode, EditTool.Connect, EditTool.Delete,
        EditTool.Lock, EditTool.Key, EditTool.Link,
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
        _rootWindow.SetTitle($"Powergrid - {name}");

        var path = $"{ScenesDir}/{name}.xml";
        _sceneView.Scene.SerializeToFile(path);
        Core.DeveloperConsole.PrintLine($"Saved: {path}");
    }

    private void CreateUI()
    {
        var title = _levelName != null ? $"Powergrid - {_levelName}" : "Powergrid - (new)";
        _rootWindow = new Window(_bounds, title, Core.DefaultFont);
        _rootWindow.SetColors(ColorPalette.ActualWhite, ColorPalette.Black, ColorPalette.ActualWhite, ColorPalette.Black);
        _rootWindow.SetCloseButtonColors(ColorPalette.Black, Color.DarkGray);
        _rootWindow.OnWindowClosed += _ => { if (Current == this) Current = null; };

        Scene scene = null;
        if (_levelName != null)
        {
            try
            {
                scene = Scene.FromFile(Core.Content, $"files/scenes/powergrid/{_levelName}.xml");
            }
            catch
            {
                Core.DeveloperConsole.PrintLine($"powergrid: could not load '{_levelName}', starting with empty scene");
            }
        }

        _sceneView = new PowergridView(_rootWindow.GetContentBounds(), scene);
        _rootWindow.AddChild(_sceneView);

        _inspector = new PowergridInspector(new Rectangle(0, 0, 10, 10), _sceneView);
        _rootWindow.AddChild(_inspector);

        BuildToolbar();
        ApplyLayout();

        Core.UISystem.AddElement(_rootWindow);
    }

    private void BuildToolbar()
    {
        Button Mk(string text, System.Action onClick) => new(
            new Rectangle(0, 0, 10, 10), text, Core.DefaultFont,
            ColorPalette.Black, ColorPalette.DarkCream, ColorPalette.ActualWhite, onClick);

        _editToggle = Mk("Edit: off", ToggleEditMode);
        _rootWindow.AddChild(_editToggle);

        // Play-mode simulation controls.
        _runBtn   = Mk("Run",   () => _sceneView.RunSimulation());
        _stepBtn  = Mk("Step",  () => _sceneView.StepSimulation());
        _resetBtn = Mk("Reset", () => _sceneView.ResetSimulation());
        _rootWindow.AddChild(_runBtn);
        _rootWindow.AddChild(_stepBtn);
        _rootWindow.AddChild(_resetBtn);

        foreach (var tool in Tools)
        {
            var captured = tool;
            var btn = Mk(ToolName(tool), () => { _sceneView.Tool = captured; RefreshToolLabels(); });
            _toolButtons[tool] = btn;
            _rootWindow.AddChild(btn);
        }

        _saveBtn = Mk("Save", () => Save());
        _rootWindow.AddChild(_saveBtn);
    }

    private static string ToolName(EditTool tool) => tool switch
    {
        EditTool.Select  => "Select",
        EditTool.AddNode => "Add",
        EditTool.Connect => "Connect",
        EditTool.Delete  => "Delete",
        EditTool.Lock    => "Lock",
        EditTool.Key     => "Key",
        EditTool.Link    => "Link",
        _ => tool.ToString(),
    };

    private void ToggleEditMode()
    {
        _editMode = !_editMode;
        _sceneView.EditMode = _editMode;
        if (_editMode)
        {
            _sceneView.Tool = EditTool.Select;
            _sceneView.ResetSimulation();   // drop any in-progress run
            _sceneView.ClearPlacedTokens(); // clean authoring slate
        }
        else
        {
            _sceneView.ResetPlayState(); // entering play: refill inventory, clear editor test tokens
        }
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

        // Play-mode run controls sit next to the Edit toggle when not editing.
        foreach (var (btn, label) in new[] { (_runBtn, "Run"), (_stepBtn, "Step"), (_resetBtn, "Reset") })
        {
            btn.SetVisibility(!_editMode);
            if (!_editMode)
                x += Place(btn, x, btnY, label) + gap;
        }

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
