using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;

namespace ld59.UI.Powergrid;

public enum EditTool { Select, AddNode, Connect, Delete, Region, Text }

/// <summary>
/// Which authoring affordances a powergrid window exposes. The player-facing launch (Start menu, an
/// in-world puzzle panel) opens with <see cref="None"/> — just the board. Opening from the developer
/// console uses <see cref="All"/> so levels can be built and checked.
/// </summary>
[Flags]
public enum PowergridFeatures
{
    None = 0,
    /// <summary>The Solve / step / clear controls under the alphabet panel.</summary>
    Solver = 1 << 0,
    /// <summary>The Edit toggle, tool buttons, inspector and Save button.</summary>
    Editor = 1 << 1,
    All = Solver | Editor,
}

/// <summary>
/// 2D viewport for a graph-colouring level. Owns a pan/zoom camera and renders the puzzle graph
/// read from a <see cref="PowergridLevelController"/>. Drawing is scissor-clipped to the viewport.
///
/// Two modes:
///  - Play: a rune palette along the bottom; drag a rune onto a node to colour it (or drag a node's
///    rune off to clear it). Edges joining two same-rune nodes turn red. Fill every node with no
///    conflicts to solve. Pan is right-drag.
///  - Edit: create/move/connect/delete nodes + author fixed-rune clues via the inspector. Pan is
///    right-drag; the left button drives the active tool.
/// </summary>
public class PowergridView : UIElement
{
    private Rectangle _bounds;
    private readonly Scene _scene;
    private PowergridLevelController _controller;
    private readonly SpriteFont _font;

    private readonly Texture2D _pixel;
    private readonly Texture2D _circleFilled;
    private readonly Texture2D _circleOutlined;

    private Vector2 _pan = Vector2.Zero;
    private float _zoom = 64f;
    private const float MinZoom = 16f;
    private const float MaxZoom = 256f;

    private const float NodeWorldRadius = 0.4f;
    private const float SnapThreshold = 3.0f;
    private const float SnapSpeed = 6f;

    /// <summary>Width of the right-hand alphabet-pyramid panel shown during play.</summary>
    private const int PyramidPanelWidth = 300;
    private const int PyramidPad = 16;
    private const int PyramidGap = 8;
    /// <summary>On-screen size of the rune icon that follows the cursor while dragging.</summary>
    private const int HandIconSize = 48;

    // Camera input
    private bool _panning;
    private Point _lastMouse;
    private int _prevScroll;
    private bool _prevLeftPressed;
    private bool _prevRightPressed;

    /// <summary>When false the solver controls are hidden and the alphabet panel takes their space —
    /// the board is play-only. Set by whoever opens the view; see <see cref="PowergridFeatures"/>.</summary>
    public bool ShowSolver { get; set; } = true;

    // Edit state
    public bool EditMode { get; set; }
    public EditTool Tool { get; set; } = EditTool.Select;
    public PowerNodeComponent Selected { get; private set; }
    public Connection SelectedConnection { get; private set; }
    public PowergridRegionComponent SelectedRegion { get; private set; }
    public PowergridTextComponent SelectedText { get; private set; }
    private bool _needsRebuild;
    private PowerNodeComponent _movingNode;
    private PowergridTextComponent _movingText;
    private Point _moveLast;
    private PowerNodeComponent _connectSource;
    private Vector2? _regionStart;

    // Play state (runtime; resets each session)
    private enum HandKind { None, FromInventory, FromNode }
    private HandKind _handKind = HandKind.None;
    private string _handRune;
    private PowerNodeComponent _handNode;
    private Point _handPos;
    private float _rejectTimer;
    private Vector2 _rejectPos;
    private bool Dragging => _handKind != HandKind.None;

    /// <summary>Remaining runes the player can still place this session (flat; repeats = quantity).</summary>
    private readonly List<string> _inventory = new();
    /// <summary>Puzzle ids whose reward has already been granted this session (rewards are permanent
    /// once earned — never revoked even if the puzzle is later unsolved).</summary>
    private readonly HashSet<string> _rewarded = new();

    // Solver state (runtime). When viewing a solution the board is overwritten; the player's own board
    // is snapshotted so "Clear" can restore it.
    private readonly List<Dictionary<PowerNodeComponent, string>> _solutions = new();
    private int _solutionIndex = -1;
    private bool _solverRun;
    private bool _solverTruncated;
    private bool _viewingSolution;
    private Dictionary<PowerNodeComponent, string> _snapBoard;
    private List<string> _snapInventory;
    private HashSet<string> _snapRewarded;
    private const int SolverAreaHeight = 150;

    public Scene Scene => _scene;
    public PowergridLevelController Controller => _controller;

    public PowergridView(Rectangle bounds, Scene scene = null)
    {
        _bounds = bounds;
        _scene = scene ?? new Scene();
        _scene.InitializeEntities();
        _controller = new PowergridLevelController(_scene);

        if (_controller.Graphs.Count > 0) _pan = _controller.Graphs[0].Centroid;

        _font = Core.DefaultFont;

        _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });

        _circleFilled   = Core.Content.Load<Texture2D>("images/powergrid/circle-filled");
        _circleOutlined = Core.Content.Load<Texture2D>("images/powergrid/circle-outlined");

        _prevScroll = Mouse.GetState().ScrollWheelValue;
        ResetPlayState();
    }

    public override Rectangle GetBoundingBox() => _bounds;
    public override void SetBounds(Rectangle bounds) => _bounds = bounds;

    /// <summary>Refills the inventory from the level config, clears placed runes (clues kept), and
    /// drops anything in hand.</summary>
    public void ResetPlayState()
    {
        ClearHand();
        _controller.ClearPlaced();
        _rewarded.Clear();

        _inventory.Clear();
        _inventory.AddRange(_controller.InitialInventory);

        _solutions.Clear();
        _solutionIndex = -1;
        _solverRun = false;
        _viewingSolution = false;
        _snapBoard = null;
    }

    /// <summary>Grants each newly-solved puzzle's reward runes into the shared inventory, once. The
    /// grant is permanent: a puzzle re-entering an unsolved state never reclaims runes it gave.</summary>
    private void GrantRewards()
    {
        foreach (var graph in _controller.Graphs)
        {
            if (!graph.IsSolved || !_rewarded.Add(graph.Id)) continue;
            foreach (var rune in graph.RewardRunes)
                _inventory.Add(rune);
        }
    }

    #region Solver

    /// <summary>The full rune budget the solver may use: the starting inventory plus every puzzle's
    /// reward (sequencing/gating is ignored — it answers "with all the runes, how many colourings?").</summary>
    private List<string> SolverBudget()
    {
        var budget = new List<string>(_controller.InitialInventory);
        foreach (var g in _controller.Graphs) budget.AddRange(g.RewardRunes);
        return budget;
    }

    /// <summary>Enumerates all valid colourings of the current graph and shows the first. Snapshots the
    /// player's board first so <see cref="ClearSolver"/> can restore it.</summary>
    public void RunSolver()
    {
        if (!_viewingSolution) CaptureBoard();

        var res = PowergridSolver.Solve(_controller.Graphs, _controller.ActiveRules, SolverBudget(), _controller.Regions);
        _solutions.Clear();
        _solutions.AddRange(res.Solutions);
        _solverTruncated = res.Truncated;
        _solverRun = true;
        _solutionIndex = _solutions.Count > 0 ? 0 : -1;
        if (_solutionIndex >= 0) DisplaySolution(_solutionIndex);
    }

    /// <summary>Steps to the next/previous solution (wraps) and shows it.</summary>
    public void StepSolution(int delta)
    {
        if (_solutions.Count == 0) return;
        _solutionIndex = ((_solutionIndex + delta) % _solutions.Count + _solutions.Count) % _solutions.Count;
        DisplaySolution(_solutionIndex);
    }

    private void CaptureBoard()
    {
        _snapBoard = new Dictionary<PowerNodeComponent, string>();
        foreach (var g in _controller.Graphs)
            foreach (var n in g.Nodes)
                if (!n.IsFixed) _snapBoard[n] = n.PlacedRune;
        _snapInventory = new List<string>(_inventory);
        _snapRewarded = new HashSet<string>(_rewarded);
    }

    private void DisplaySolution(int k)
    {
        var sol = _solutions[k];
        var remaining = SolverBudget();
        foreach (var g in _controller.Graphs)
            foreach (var n in g.Nodes)
            {
                if (n.IsFixed) continue;
                n.PlacedRune = sol.TryGetValue(n, out var r) ? r : string.Empty;
                if (!string.IsNullOrEmpty(n.PlacedRune)) remaining.Remove(n.PlacedRune);
            }
        _inventory.Clear();
        _inventory.AddRange(remaining);
        _viewingSolution = true;
        ClearHand();
    }

    /// <summary>Drops the solver view and restores the player's snapshotted board/inventory.</summary>
    public void ClearSolver()
    {
        if (_snapBoard != null)
        {
            foreach (var (node, rune) in _snapBoard) node.PlacedRune = rune;
            _inventory.Clear();
            _inventory.AddRange(_snapInventory);
            _rewarded.Clear();
            foreach (var id in _snapRewarded) _rewarded.Add(id);
        }
        _solutions.Clear();
        _solutionIndex = -1;
        _solverRun = false;
        _viewingSolution = false;
        _snapBoard = null;
    }

    /// <summary>The four solver control rects in the panel's bottom band.</summary>
    private (Rectangle solve, Rectangle prev, Rectangle next, Rectangle clear) SolverLayout()
    {
        var panel = PyramidPanel;
        const int h = 28, gap = 6;
        int x = panel.X + PyramidPad;
        int w = panel.Width - 2 * PyramidPad;
        int y = panel.Bottom - SolverAreaHeight + 8;

        var solve = new Rectangle(x, y, w, h);
        int navY = solve.Bottom + 6 + _font.LineSpacing + 4;
        const int bw = 46;
        var prev = new Rectangle(x, navY, bw, h);
        var next = new Rectangle(x + w - bw, navY, bw, h);
        var clear = new Rectangle(x, navY + h + gap, w, h);
        return (solve, prev, next, clear);
    }

    /// <summary>Handles a click on a solver control. Returns true if one was hit (so it doesn't also
    /// start a rune drag).</summary>
    private bool HandleSolverClick(Point mp)
    {
        if (!ShowSolver) return false;
        var (solve, prev, next, clear) = SolverLayout();
        if (solve.Contains(mp)) { RunSolver(); return true; }
        if (_solverRun && clear.Contains(mp)) { ClearSolver(); return true; }
        if (_solverRun && _solutions.Count > 0 && prev.Contains(mp)) { StepSolution(-1); return true; }
        if (_solverRun && _solutions.Count > 0 && next.Contains(mp)) { StepSolution(1); return true; }
        return false;
    }

    #endregion

    #region Camera transforms

    private Rectangle GraphRegion => EditMode
        ? _bounds
        : new Rectangle(_bounds.X, _bounds.Y, _bounds.Width - PyramidPanelWidth, _bounds.Height);

    /// <summary>The right-hand panel that shows the alphabet pyramid (and is the rune source) in play.</summary>
    private Rectangle PyramidPanel => new(_bounds.Right - PyramidPanelWidth, _bounds.Y, PyramidPanelWidth, _bounds.Height);

    private Vector2 WorldToScreen(Vector2 world)
    {
        var r = GraphRegion;
        return new Vector2(
            r.X + r.Width  * 0.5f + (world.X - _pan.X) * _zoom,
            r.Y + r.Height * 0.5f - (world.Y - _pan.Y) * _zoom);
    }

    private Vector2 ScreenToWorld(Point screen)
    {
        var r = GraphRegion;
        return new Vector2(
            (screen.X - (r.X + r.Width  * 0.5f)) / _zoom + _pan.X,
            _pan.Y - (screen.Y - (r.Y + r.Height * 0.5f)) / _zoom);
    }

    private PowerNodeComponent HitTestNode(Point screen)
    {
        float r = NodeWorldRadius * _zoom;
        foreach (var graph in _controller.Graphs)
            foreach (var node in graph.Nodes)
                if (Vector2.Distance(WorldToScreen(node.Entity.Position), new Vector2(screen.X, screen.Y)) <= r)
                    return node;
        return null;
    }

    #endregion

    public override void Update(float deltaTime)
    {
        var mouse = Mouse.GetState();
        var mp = Core.GetTransformedMousePoint();
        bool inside = _bounds.Contains(mp);

        bool left  = mouse.LeftButton  == ButtonState.Pressed;
        bool right = mouse.RightButton == ButtonState.Pressed;
        bool leftClick   = left  && !_prevLeftPressed;
        bool leftRelease = !left &&  _prevLeftPressed;

        int scroll = mouse.ScrollWheelValue;
        int scrollDelta = scroll - _prevScroll;
        _prevScroll = scroll;
        if (scrollDelta != 0 && inside && GraphRegion.Contains(mp))
            _zoom = MathHelper.Clamp(_zoom * MathF.Pow(1.1f, scrollDelta / 120f), MinZoom, MaxZoom);

        if (_rejectTimer > 0) _rejectTimer -= deltaTime;

        if (EditMode)
            HandleEditInput(mp, inside, left, leftClick, leftRelease);
        else
            HandlePlayInput(mp, inside, leftClick, leftRelease);

        HandlePan(mp, right, right && !_prevRightPressed, inside, GraphRegion.Contains(mp));

        _prevLeftPressed = left;
        _prevRightPressed = right;

        _scene.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(deltaTime)));

        if (_needsRebuild)
        {
            _controller = new PowergridLevelController(_scene);
            _needsRebuild = false;
            if (Selected != null && !_controller.Graphs.Any(g => g.Nodes.Contains(Selected)))
                Selected = null;
        }

        _controller.Update(deltaTime);
        if (!EditMode && !_viewingSolution) GrantRewards();
        UpdateCameraSnap(deltaTime);
    }

    /// <summary>When idle (not panning/dragging) and near a puzzle, ease the camera to centre it.</summary>
    private void UpdateCameraSnap(float deltaTime)
    {
        if (EditMode || _panning || Dragging || _controller.Graphs.Count <= 1) return;

        PuzzleGraph nearest = null;
        float best = float.MaxValue;
        foreach (var g in _controller.Graphs)
        {
            float d = Vector2.Distance(g.Centroid, _pan);
            if (d < best) { best = d; nearest = g; }
        }

        if (nearest != null && best > 0.01f && best < SnapThreshold)
            _pan = Vector2.Lerp(_pan, nearest.Centroid, Math.Min(1f, SnapSpeed * deltaTime));
    }

    private void HandlePan(Point mp, bool button, bool buttonClick, bool inside, bool overGraph)
    {
        if (buttonClick && inside && overGraph)
        {
            _panning = true;
            _lastMouse = mp;
        }
        if (!button) _panning = false;

        if (_panning && button)
        {
            var delta = mp - _lastMouse;
            _lastMouse = mp;
            _pan.X -= delta.X / _zoom;
            _pan.Y += delta.Y / _zoom;
        }
    }

    #region Play interaction (rune drag/drop)

    private void HandlePlayInput(Point mp, bool inside, bool leftClick, bool leftRelease)
    {
        if (leftClick && inside && HandleSolverClick(mp))
            return;

        if (leftClick && inside)
            TryStartDrag(mp);

        if (Dragging)
        {
            _handPos = mp;
            if (leftRelease) FinishDrag(mp);
        }
    }

    private void TryStartDrag(Point mp)
    {
        // Take a rune out of the inventory by grabbing its pyramid slot (only if any remain).
        if (PyramidPanel.Contains(mp))
        {
            foreach (var (name, rect) in PyramidSlots())
                if (rect.Contains(mp))
                {
                    if (_inventory.Remove(name))
                    {
                        _viewingSolution = false; // player is taking over from the solver view
                        _handKind = HandKind.FromInventory;
                        _handRune = name;
                        _handPos = mp;
                    }
                    return;
                }
            return;
        }

        // Or lift a placed rune off a (non-fixed) node — it stays "in hand", out of the inventory.
        if (GraphRegion.Contains(mp))
        {
            var node = HitTestNode(mp);
            if (node != null && IsNodeLocked(node))
            {
                Reject(WorldToScreen(node.Entity.Position));
                return;
            }
            if (node != null && !node.IsFixed && !string.IsNullOrEmpty(node.PlacedRune))
            {
                _viewingSolution = false; // player is taking over from the solver view
                _handKind = HandKind.FromNode;
                _handRune = node.PlacedRune;
                _handNode = node;
                node.PlacedRune = string.Empty;
                _handPos = mp;
            }
            else if (node != null && node.IsFixed)
            {
                Reject(WorldToScreen(node.Entity.Position));
            }
        }
    }

    private void FinishDrag(Point mp)
    {
        if (GraphRegion.Contains(mp))
        {
            var node = HitTestNode(mp);
            if (node != null)
            {
                // Can't place on a clue, or on a node in a still-locked puzzle: free the rune back.
                if (node.IsFixed || IsNodeLocked(node))
                {
                    Reject(WorldToScreen(node.Entity.Position));
                    _inventory.Add(_handRune);
                    ClearHand();
                    return;
                }

                // Any rune already here is displaced back into the inventory.
                if (!string.IsNullOrEmpty(node.PlacedRune)) _inventory.Add(node.PlacedRune);
                node.PlacedRune = _handRune;
                ClearHand();
                return;
            }
        }

        // Dropped on empty space or the pyramid panel: the held rune returns to the inventory.
        _inventory.Add(_handRune);
        ClearHand();
    }

    /// <summary>True when the node belongs to a puzzle that isn't yet unlocked (an earlier puzzle in
    /// the sequence is still unsolved), so the player can't place or lift runes on it.</summary>
    private bool IsNodeLocked(PowerNodeComponent node)
    {
        var graph = _controller.GraphOf(node);
        return graph != null && !graph.Unlocked;
    }

    private void ClearHand() { _handKind = HandKind.None; _handNode = null; _handRune = null; }

    private void Reject(Vector2 screenPos) { _rejectTimer = 0.4f; _rejectPos = screenPos; }

    /// <summary>Lays the whole alphabet out as a pyramid inside <see cref="PyramidPanel"/> — one row
    /// per tier, ordered by <see cref="Symbol.RowOrder"/>, each row centred — and returns every rune's
    /// on-screen slot. Cheap; recomputed on demand for both drawing and hit-testing.</summary>
    private List<(string name, Rectangle rect)> PyramidSlots()
    {
        var slots = new List<(string, Rectangle)>();
        var panel = PyramidPanel;

        var tiers = SymbolDictionary.All
            .GroupBy(s => s.Tier)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(s => s.RowOrder).ToList())
            .ToList();
        if (tiers.Count == 0) return slots;

        // The pyramid lives between the header (top) and the solver controls (bottom band). With the
        // solver hidden it runs all the way down to the panel's padding.
        int bandTop = panel.Y + PyramidPad + 30;
        int bandBottom = panel.Bottom - (ShowSolver ? SolverAreaHeight : PyramidPad);
        int cols = tiers.Max(t => t.Count);
        int inner = panel.Width - 2 * PyramidPad;
        int chipW = (inner - (cols - 1) * PyramidGap) / cols;
        int chipH = (bandBottom - bandTop - (tiers.Count - 1) * PyramidGap) / tiers.Count;
        int chip = Math.Max(8, Math.Min(Math.Min(chipW, chipH), 56));

        int totalH = tiers.Count * chip + (tiers.Count - 1) * PyramidGap;
        int y = bandTop + Math.Max(0, (bandBottom - bandTop - totalH) / 2);

        foreach (var row in tiers)
        {
            int rowW = row.Count * chip + (row.Count - 1) * PyramidGap;
            int x = panel.X + (panel.Width - rowW) / 2;
            foreach (var s in row)
            {
                slots.Add((s.Name, new Rectangle(x, y, chip, chip)));
                x += chip + PyramidGap;
            }
            y += chip + PyramidGap;
        }
        return slots;
    }

    #endregion

    #region Edit interaction

    private void HandleEditInput(Point mp, bool inside, bool left, bool leftClick, bool leftRelease)
    {
        switch (Tool)
        {
            case EditTool.AddNode:
                if (leftClick && inside && HitTestNode(mp) == null)
                    AddNodeAt(ScreenToWorld(mp));
                break;

            case EditTool.Select:
                if (leftClick && inside)
                {
                    var hitNode = HitTestNode(mp);
                    if (hitNode != null)
                    {
                        SelectOnly(node: hitNode);
                        _movingNode = hitNode;
                        _moveLast = mp;
                    }
                    else
                    {
                        var hitConn = ConnectionHitTest(mp);
                        var hitText = hitConn == null ? TextHitTest(mp) : null;
                        if (hitConn != null)
                        {
                            SelectOnly(connection: hitConn);
                        }
                        else if (hitText != null)
                        {
                            SelectOnly(text: hitText);
                            _movingText = hitText;
                            _moveLast = mp;
                        }
                        else
                        {
                            SelectOnly(region: RegionHitTest(mp));
                        }
                        _movingNode = null;
                    }
                }
                if (left && (_movingNode != null || _movingText != null))
                {
                    var delta = mp - _moveLast;
                    _moveLast = mp;
                    var worldDelta = new Vector2(delta.X / _zoom, -delta.Y / _zoom);
                    if (_movingNode != null) _movingNode.Entity.LocalPosition += worldDelta;
                    else _movingText.Entity.LocalPosition += worldDelta;
                    MarkDirty();
                }
                if (leftRelease) { _movingNode = null; _movingText = null; }
                break;

            case EditTool.Connect:
                if (leftClick && inside)
                    _connectSource = HitTestNode(mp);
                if (leftRelease)
                {
                    if (_connectSource != null)
                    {
                        var target = HitTestNode(mp);
                        if (target != null && target != _connectSource)
                            AddConnection(_connectSource, target);
                    }
                    _connectSource = null;
                }
                break;

            case EditTool.Delete:
                if (leftClick && inside)
                {
                    var node = HitTestNode(mp);
                    if (node != null) { DeleteNode(node); break; }

                    var conn = ConnectionHitTest(mp);
                    if (conn != null) { RemoveConnection(conn); break; }

                    var label = TextHitTest(mp);
                    if (label != null) { DeleteText(label); break; }

                    var region = RegionHitTest(mp);
                    if (region != null) DeleteRegion(region);
                }
                break;

            case EditTool.Region:
                if (leftClick && inside && GraphRegion.Contains(mp))
                    _regionStart = ScreenToWorld(mp);
                if (leftRelease && _regionStart.HasValue)
                {
                    CreateRegion(_regionStart.Value, ScreenToWorld(mp));
                    _regionStart = null;
                }
                break;

            case EditTool.Text:
                if (leftClick && inside && GraphRegion.Contains(mp))
                    AddTextAt(ScreenToWorld(mp));
                break;
        }
    }

    private void MarkDirty() => _needsRebuild = true;

    private string UniqueName(string prefix)
    {
        var existing = _scene.GetEntities().Select(e => e.Name).ToHashSet();
        for (int i = 1; ; i++)
        {
            var name = $"{prefix}{i}";
            if (!existing.Contains(name)) return name;
        }
    }

    private void AddNodeAt(Vector2 world)
    {
        var entity = new Entity { Name = UniqueName("node") };
        entity.LocalPosition = world;
        var node = new PowerNodeComponent();
        entity.AddComponent(node);
        _scene.AddEntity(entity);
        SelectOnly(node: node);
        MarkDirty();
    }

    /// <summary>Adds an (undirected) adjacency. Stored one-way but de-duplicated against the reverse.</summary>
    private void AddConnection(PowerNodeComponent source, PowerNodeComponent target)
    {
        var sourceName = source.Entity.Name;
        var targetName = target.Entity.Name;
        if (source.OutgoingNodeNames.Contains(targetName) || target.OutgoingNodeNames.Contains(sourceName))
            return;
        source.OutgoingNodeNames.Add(targetName);
        MarkDirty();
    }

    private void DeleteNode(PowerNodeComponent node)
    {
        var name = node.Entity.Name;
        foreach (var e in _scene.GetEntities())
        {
            var n = e.GetComponent<PowerNodeComponent>();
            if (n == null) continue;
            n.OutgoingNodeNames.Remove(name);
            n.ConnectionRuleOverrides.Remove(name);
        }
        _scene.RemoveEntity(node.Entity);
        if (Selected == node) Selected = null;
        if (SelectedConnection?.From == node || SelectedConnection?.To == node) SelectedConnection = null;
        MarkDirty();
    }

    private Connection ConnectionHitTest(Point screen)
    {
        var p = new Vector2(screen.X, screen.Y);
        foreach (var graph in _controller.Graphs)
            foreach (var conn in graph.Connections)
                if (!conn.IsVirtual && DistPointSeg(p, WorldToScreen(conn.StartPos), WorldToScreen(conn.EndPos)) <= 8f)
                    return conn;
        return null;
    }

    private void RemoveConnection(Connection conn)
    {
        // The edge may be stored on either endpoint (it's undirected).
        conn.From.OutgoingNodeNames.Remove(conn.To.Entity.Name);
        conn.To.OutgoingNodeNames.Remove(conn.From.Entity.Name);
        conn.From.ConnectionRuleOverrides.Remove(conn.To.Entity.Name);
        conn.To.ConnectionRuleOverrides.Remove(conn.From.Entity.Name);
        if (SelectedConnection == conn) SelectedConnection = null;
        MarkDirty();
    }

    private void CreateRegion(Vector2 start, Vector2 end)
    {
        float halfW = MathF.Abs(end.X - start.X) * 0.5f;
        float halfH = MathF.Abs(end.Y - start.Y) * 0.5f;
        if (halfW < 0.2f || halfH < 0.2f) return;
        var center = (start + end) * 0.5f;
        var entity = new Entity { Name = UniqueName("region") };
        entity.LocalPosition = center;
        var region = new PowergridRegionComponent { HalfWidth = halfW, HalfHeight = halfH };
        entity.AddComponent(region);
        _scene.AddEntity(entity);
        SelectOnly(region: region);
        MarkDirty();
    }

    private void DeleteRegion(PowergridRegionComponent region)
    {
        _scene.RemoveEntity(region.Entity);
        if (SelectedRegion == region) SelectedRegion = null;
        MarkDirty();
    }

    private PowergridRegionComponent RegionHitTest(Point screen)
    {
        var worldPos = ScreenToWorld(screen);
        foreach (var region in _controller.Regions)
            if (region.Contains(worldPos)) return region;
        return null;
    }

    /// <summary>Clears every selection channel, then sets the one that was passed. Node/connection/
    /// region/text selections are mutually exclusive — the inspector shows one section at a time.</summary>
    private void SelectOnly(PowerNodeComponent node = null, Connection connection = null,
        PowergridRegionComponent region = null, PowergridTextComponent text = null)
    {
        Selected = node;
        SelectedConnection = connection;
        SelectedRegion = region;
        SelectedText = text;
    }

    private void AddTextAt(Vector2 world)
    {
        var entity = new Entity { Name = UniqueName("text") };
        entity.LocalPosition = world;
        var label = new PowergridTextComponent { Text = "New text" };
        entity.AddComponent(label);
        _scene.AddEntity(entity);
        SelectOnly(text: label);
        Tool = EditTool.Select; // drop straight into editing the label we just placed
        MarkDirty();
    }

    private void DeleteText(PowergridTextComponent label)
    {
        _scene.RemoveEntity(label.Entity);
        if (SelectedText == label) SelectedText = null;
        if (_movingText == label) _movingText = null;
        MarkDirty();
    }

    private PowergridTextComponent TextHitTest(Point screen)
    {
        foreach (var label in _controller.Labels)
            if (TextScreenRect(label).Contains(screen)) return label;
        return null;
    }

    /// <summary>On-screen box a label occupies, given the current camera. Empty labels still get a
    /// clickable box so they can be selected and typed into.</summary>
    private Rectangle TextScreenRect(PowergridTextComponent label)
    {
        float scale = TextDrawScale(label);
        var size = _font.MeasureString(DisplayText(label)) * scale;
        var origin = WorldToScreen(label.Entity.Position);
        float left = label.Align switch
        {
            TextAlign.Left  => origin.X,
            TextAlign.Right => origin.X - size.X,
            _               => origin.X - size.X / 2f,
        };
        return new Rectangle((int)left, (int)(origin.Y - size.Y / 2f), (int)size.X, (int)size.Y);
    }

    /// <summary>Font scale for a label: its authored size, scaled with the camera so labels stay
    /// pinned to the world rather than the screen.</summary>
    private float TextDrawScale(PowergridTextComponent label) => label.Scale * (_zoom / 64f);

    /// <summary>What a label renders as. An empty one stands in as "(empty)" so a freshly placed label
    /// is still visible and clickable in the editor (it's skipped entirely during play).</summary>
    private static string DisplayText(PowergridTextComponent label)
        => string.IsNullOrEmpty(label.Text) ? "(empty)" : label.Text;

    private static float DistPointSeg(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float lenSq = ab.LengthSquared();
        float t = lenSq > 0 ? MathHelper.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }

    // ── Inspector ops ─────────────────────────────────────────────────────

    /// <summary>Cycles the selected node's fixed-clue rune: none → palette runes → none.</summary>
    public void CycleFixedRune()
    {
        if (Selected == null) return;
        var all = SymbolDictionary.All;
        int current = string.IsNullOrEmpty(Selected.FixedRune)
            ? -1
            : all.FindIndex(s => s.Name == Selected.FixedRune);
        int next = current + 1;
        Selected.FixedRune = next >= all.Count ? string.Empty : all[next].Name;
        MarkDirty();
    }

    public void DeleteSelected() { if (Selected != null) DeleteNode(Selected); }

    public string SelectedFixedRune => Selected?.FixedRune ?? string.Empty;

    public int SelectedInfluence => Selected?.Influence ?? 1;

    public void AdjustInfluence(int delta)
    {
        if (Selected == null) return;
        Selected.Influence = Math.Max(1, Selected.Influence + delta);
        MarkDirty();
    }

    /// <summary>Adds (delta &gt; 0) or removes (delta &lt; 0) copies of a rune in the level's inventory.</summary>
    public void AdjustInventoryRune(string rune, int delta)
    {
        if (string.IsNullOrEmpty(rune)) return;
        var lvl = EnsureLevelComponent();
        if (delta > 0)
            for (int i = 0; i < delta; i++) lvl.Inventory.Add(rune);
        else
            for (int i = 0; i < -delta && lvl.Inventory.Remove(rune); i++) { }
        MarkDirty();
    }

    // ── Per-puzzle sequence authoring (acts on the puzzle containing the selected node) ──────

    /// <summary>Id of the puzzle the selected node belongs to, or null if nothing is selected.</summary>
    public string SelectedPuzzleId => Selected == null ? null : _controller.GraphOf(Selected)?.Id;

    public int SelectedPuzzleOrder
    {
        get { var id = SelectedPuzzleId; return id == null ? 0 : FindLevelComponent()?.OrderOf(id) ?? 0; }
    }

    /// <summary>How many copies of a rune the selected puzzle's reward grants (0 if none/no selection).</summary>
    public int SelectedPuzzleRewardCount(string rune)
    {
        var id = SelectedPuzzleId;
        if (id == null) return 0;
        var list = FindLevelComponent()?.RewardOf(id);
        return list == null ? 0 : list.Count(r => r == rune);
    }

    /// <summary>Compact reward summary for the selected puzzle, e.g. "Lith x2 Axe".</summary>
    public string SelectedPuzzleRewardText
    {
        get
        {
            var id = SelectedPuzzleId;
            if (id == null) return "-";
            var list = FindLevelComponent()?.RewardOf(id);
            return list == null || list.Count == 0 ? "none" : RewardSummary(list);
        }
    }

    /// <summary>Shifts the selected puzzle's authored sequence position (clamped at 0).</summary>
    public void AdjustPuzzleOrder(int delta)
    {
        var id = SelectedPuzzleId;
        if (id == null) return;
        var lvl = EnsureLevelComponent();
        lvl.PuzzleOrder[id] = Math.Max(0, lvl.OrderOf(id) + delta);
        MarkDirty();
    }

    /// <summary>Adds (delta &gt; 0) or removes (delta &lt; 0) copies of a reward rune on the selected puzzle.</summary>
    public void AdjustPuzzleReward(string rune, int delta)
    {
        var id = SelectedPuzzleId;
        if (id == null || string.IsNullOrEmpty(rune)) return;
        var lvl = EnsureLevelComponent();
        if (!lvl.PuzzleRewards.TryGetValue(id, out var list))
            list = lvl.PuzzleRewards[id] = new List<string>();
        if (delta > 0)
            for (int i = 0; i < delta; i++) list.Add(rune);
        else
            for (int i = 0; i < -delta && list.Remove(rune); i++) { }
        if (list.Count == 0) lvl.PuzzleRewards.Remove(id);
        MarkDirty();
    }

    public int EditorInventoryCount(string rune)
    {
        foreach (var e in _scene.GetEntities())
        {
            var lvl = e.GetComponent<PowergridLevelComponent>();
            if (lvl != null) return lvl.CountOf(rune);
        }
        return 0;
    }

    /// <summary>Compact summary of the authored inventory, e.g. "Lith x2  Axe x3".</summary>
    public string EditorInventoryText
    {
        get
        {
            foreach (var e in _scene.GetEntities())
            {
                var lvl = e.GetComponent<PowergridLevelComponent>();
                if (lvl == null) continue;
                if (lvl.Inventory.Count == 0) return "(empty)";
                return string.Join("  ", lvl.DistinctRunes().Select(r => $"{r} x{lvl.CountOf(r)}"));
            }
            return "(empty)";
        }
    }

    /// <summary>Enables/disables an adjacency rule for the level.</summary>
    public void ToggleRule(ColoringRule rule)
    {
        var lvl = EnsureLevelComponent();
        if (!lvl.Rules.Remove(rule)) lvl.Rules.Add(rule);
        MarkDirty();
    }

    public bool EditorHasRule(ColoringRule rule)
    {
        foreach (var e in _scene.GetEntities())
        {
            var lvl = e.GetComponent<PowergridLevelComponent>();
            if (lvl != null) return lvl.Rules.Contains(rule);
        }
        return _controller.ActiveRules.Contains(rule);
    }

    // ── Per-connection rule override authoring ─────────────────────────────

    /// <summary>True when the selected connection has a rule override (not using level defaults).</summary>
    public bool ConnectionHasOverride => SelectedConnection?.RuleOverride != null;

    /// <summary>Whether the given rule is active in the selected connection's override. Returns false
    /// if no connection is selected or the connection has no override.</summary>
    public bool ConnectionOverrideHasRule(ColoringRule rule)
        => SelectedConnection?.RuleOverride?.Contains(rule) ?? false;

    /// <summary>Toggles a rule in the selected connection's per-connection override. If there is no
    /// override yet, creates one with just this rule. If removing the last rule, clears the override.</summary>
    public void ToggleConnectionRule(ColoringRule rule)
    {
        if (SelectedConnection == null) return;
        var owner = GetConnectionOwner(SelectedConnection);
        if (owner == null) return;
        var targetName = owner == SelectedConnection.From
            ? SelectedConnection.To.Entity.Name
            : SelectedConnection.From.Entity.Name;

        if (!owner.ConnectionRuleOverrides.TryGetValue(targetName, out var rules))
        {
            rules = new List<ColoringRule> { rule };
            owner.ConnectionRuleOverrides[targetName] = rules;
        }
        else if (!rules.Remove(rule))
        {
            rules.Add(rule);
        }

        if (rules.Count == 0)
        {
            owner.ConnectionRuleOverrides.Remove(targetName);
            SelectedConnection.RuleOverride = null;
        }
        else
        {
            SelectedConnection.RuleOverride = rules;
        }
        MarkDirty();
    }

    /// <summary>Removes the selected connection's rule override entirely (reverts to level defaults).</summary>
    public void ClearConnectionOverride()
    {
        if (SelectedConnection == null) return;
        var owner = GetConnectionOwner(SelectedConnection);
        if (owner == null) return;
        var targetName = owner == SelectedConnection.From
            ? SelectedConnection.To.Entity.Name
            : SelectedConnection.From.Entity.Name;
        owner.ConnectionRuleOverrides.Remove(targetName);
        SelectedConnection.RuleOverride = null;
        MarkDirty();
    }

    /// <summary>Returns the node that "owns" the connection (the one with the outgoing edge stored).</summary>
    private PowerNodeComponent GetConnectionOwner(Connection conn)
    {
        if (conn.From.OutgoingNodeNames.Contains(conn.To.Entity.Name)) return conn.From;
        if (conn.To.OutgoingNodeNames.Contains(conn.From.Entity.Name)) return conn.To;
        return conn.From;
    }

    // ── Region authoring ──────────────────────────────────────────────────

    public void AdjustRegionTier(int delta)
    {
        if (SelectedRegion == null) return;
        SelectedRegion.Tier = Math.Max(1, Math.Min(SymbolDictionary.All.Max(s => s.Tier), SelectedRegion.Tier + delta));
        MarkDirty();
    }

    public void AdjustRegionMax(int delta)
    {
        if (SelectedRegion == null) return;
        SelectedRegion.MaxCount = Math.Max(0, SelectedRegion.MaxCount + delta);
        MarkDirty();
    }

    public void DeleteSelectedRegion()
    {
        if (SelectedRegion != null) DeleteRegion(SelectedRegion);
    }

    // ── Text label authoring ──────────────────────────────────────────────

    public string SelectedTextContent => SelectedText?.Text ?? string.Empty;

    /// <summary>Replaces the selected label's body (driven by the inspector's text field).</summary>
    public void SetSelectedTextContent(string text)
    {
        if (SelectedText == null || SelectedText.Text == text) return;
        SelectedText.Text = text ?? string.Empty;
        MarkDirty();
    }

    public float SelectedTextScale => SelectedText?.Scale ?? 1f;

    public void AdjustTextScale(float delta)
    {
        if (SelectedText == null) return;
        SelectedText.Scale = MathHelper.Clamp(MathF.Round((SelectedText.Scale + delta) * 100f) / 100f, 0.25f, 6f);
        MarkDirty();
    }

    public TextAlign SelectedTextAlign => SelectedText?.Align ?? TextAlign.Center;

    public void CycleTextAlign()
    {
        if (SelectedText == null) return;
        SelectedText.Align = SelectedText.Align switch
        {
            TextAlign.Left   => TextAlign.Center,
            TextAlign.Center => TextAlign.Right,
            _                => TextAlign.Left,
        };
        MarkDirty();
    }

    public void DeleteSelectedText()
    {
        if (SelectedText != null) DeleteText(SelectedText);
    }

    /// <summary>The level config component if one exists, else null (does not create one).</summary>
    private PowergridLevelComponent FindLevelComponent()
    {
        foreach (var e in _scene.GetEntities())
        {
            var lvl = e.GetComponent<PowergridLevelComponent>();
            if (lvl != null) return lvl;
        }
        return null;
    }

    private PowergridLevelComponent EnsureLevelComponent()
    {
        var existing = FindLevelComponent();
        if (existing != null) return existing;
        var entity = new Entity { Name = UniqueName("level") };
        var comp = new PowergridLevelComponent();
        entity.AddComponent(comp);
        _scene.AddEntity(entity);
        return comp;
    }

    #endregion

    #region Drawing

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.End();

        var rasterizer = new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None };
        var origScissor = Core.GraphicsDevice.ScissorRectangle;
        Core.GraphicsDevice.ScissorRectangle = _bounds;

        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, rasterizer);

        spriteBatch.Draw(_pixel, _bounds, null, Color.Gray, 0f, Vector2.Zero, SpriteEffects.None, 0f);
        foreach (var graph in _controller.Graphs)
            if (TryGetPuzzleScreenRect(graph, out var rect))
                spriteBatch.Draw(_pixel, rect, null, ColorPalette.ActualWhite, 0f, Vector2.Zero, SpriteEffects.None, 0.05f);

        DrawRegions(spriteBatch);
        DrawLabels(spriteBatch);

        foreach (var graph in _controller.Graphs)
        {
            foreach (var conn in graph.Connections) DrawConnection(spriteBatch, conn);
            foreach (var node in graph.Nodes) DrawNode(spriteBatch, node);
        }

        if (EditMode) DrawEditOverlay(spriteBatch);
        else DrawPlayOverlay(spriteBatch);

        if (_rejectTimer > 0)
            DrawRing(spriteBatch, _rejectPos, NodeWorldRadius * _zoom + 6f, ColorPalette.DarkRed, 3f);

        spriteBatch.End();

        Core.GraphicsDevice.ScissorRectangle = origScissor;
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
    }

    private void DrawConnection(SpriteBatch sb, Connection conn)
    {
        var a = WorldToScreen(conn.StartPos);
        var b = WorldToScreen(conn.EndPos);
        var color = conn.Conflict ? ColorPalette.DarkRed : ColorPalette.Black;
        bool isSelected = EditMode && conn == SelectedConnection;

        if (conn.IsVirtual)
        {
            // Virtual (influence) connections: dashed, thinner, slightly lighter.
            if (isSelected) color = Color.DimGray;
            DrawDashedLine(sb, a, b, conn.Conflict ? 2f : 1.5f, color);
        }
        else
        {
            float thickness = isSelected ? 4f : conn.Conflict ? 3.5f : 2f;
            if (isSelected) color = Color.DimGray;
            DrawLine(sb, a, b, thickness, color);
        }

        // Small square marker at midpoint for connections with rule overrides.
        if (conn.RuleOverride != null)
        {
            var mid = (a + b) * 0.5f;
            const float s = 6f;
            sb.Draw(_pixel, new Rectangle((int)(mid.X - s / 2), (int)(mid.Y - s / 2), (int)s, (int)s),
                null, conn.Conflict ? ColorPalette.DarkRed : ColorPalette.Black,
                0f, Vector2.Zero, SpriteEffects.None, 0.48f);
        }
    }

    private void DrawNode(SpriteBatch sb, PowerNodeComponent node)
    {
        var center = WorldToScreen(node.Entity.Position);
        float diameter = NodeWorldRadius * 2f * _zoom;
        var dst = new Rectangle(
            (int)(center.X - diameter * 0.5f),
            (int)(center.Y - diameter * 0.5f),
            (int)diameter, (int)diameter);

        if (node.HasRune)
        {
            // Dark disc with the (white) rune on top. A pre-filled (locked) node is drawn like a
            // player-filled one, but gets a small padlock badge so the player knows it can't be moved.
            sb.Draw(_circleFilled, dst, null, ColorPalette.Black, 0f, Vector2.Zero, SpriteEffects.None, 0.5f);

            var tex = Runes.Texture(node.Rune);
            if (tex != null)
            {
                float s = diameter * 0.66f;
                var rdst = new Rectangle((int)(center.X - s / 2), (int)(center.Y - s / 2), (int)s, (int)s);
                sb.Draw(tex, rdst, null, ColorPalette.ActualWhite, 0f, Vector2.Zero, SpriteEffects.None, 0.45f);
            }
        }
        else
        {
            // Empty node: a hollow ring.
            sb.Draw(_circleOutlined, dst, null, ColorPalette.Black, 0f, Vector2.Zero, SpriteEffects.None, 0.5f);
        }

        // Faint influence aura for nodes with extended reach.
        if (node.Influence > 1)
            DrawRing(sb, center, NodeWorldRadius * node.Influence * _zoom, Color.DimGray * 0.5f, 1f);

        if (node.InConflict)
            DrawRing(sb, center, NodeWorldRadius * _zoom + 3f, ColorPalette.DarkRed, 2.5f);

        if (node.IsFixed)
            DrawLockBadge(sb, new Vector2(center.X + diameter * 0.26f, center.Y + diameter * 0.26f), diameter * 0.42f);
    }

    /// <summary>A tiny 1-bit padlock (white badge, black body + shackle) marking a node that can't be
    /// moved. The white badge keeps it legible over both the black disc and the white background.</summary>
    private void DrawLockBadge(SpriteBatch sb, Vector2 center, float size)
    {
        float half = size / 2f;
        var box = new Rectangle((int)(center.X - half), (int)(center.Y - half), (int)size, (int)size);

        // Black-bordered white badge.
        sb.Draw(_pixel, new Rectangle(box.X - 1, box.Y - 1, box.Width + 2, box.Height + 2), null,
            ColorPalette.Black, 0f, Vector2.Zero, SpriteEffects.None, 0.13f);
        sb.Draw(_pixel, box, null, ColorPalette.ActualWhite, 0f, Vector2.Zero, SpriteEffects.None, 0.12f);

        // Lock body (lower portion).
        float bodyW = size * 0.58f, bodyH = size * 0.42f;
        var body = new Rectangle((int)(center.X - bodyW / 2), (int)(box.Bottom - bodyH - size * 0.10f),
            (int)bodyW, (int)bodyH);
        sb.Draw(_pixel, body, null, ColorPalette.Black, 0f, Vector2.Zero, SpriteEffects.None, 0.11f);

        // Shackle arch above the body (its lower half is hidden behind the body).
        DrawRing(sb, new Vector2(center.X, body.Top), size * 0.18f, ColorPalette.Black, MathF.Max(1.5f, size * 0.1f));
    }

    /// <summary>Screen-space bounding box of a puzzle's nodes plus padding. False if it has no nodes.</summary>
    private bool TryGetPuzzleScreenRect(PuzzleGraph graph, out Rectangle rect)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        int count = 0;
        foreach (var n in graph.Nodes)
        {
            var s = WorldToScreen(n.Entity.Position);
            minX = MathF.Min(minX, s.X); minY = MathF.Min(minY, s.Y);
            maxX = MathF.Max(maxX, s.X); maxY = MathF.Max(maxY, s.Y);
            count++;
        }
        if (count == 0) { rect = default; return false; }

        float pad = NodeWorldRadius * _zoom + 60;
        rect = new Rectangle(
            (int)(minX - pad), (int)(minY - pad),
            (int)(maxX - minX + pad * 2), (int)(maxY - minY + pad * 2));
        return true;
    }

    private void DrawEditOverlay(SpriteBatch sb)
    {
        var mp = Core.GetTransformedMousePoint();

        if (Selected != null)
            DrawRing(sb, WorldToScreen(Selected.Entity.Position), NodeWorldRadius * _zoom + 4f, ColorPalette.Black, 2f);

        if (_connectSource != null)
            DrawLine(sb, WorldToScreen(_connectSource.Entity.Position), new Vector2(mp.X, mp.Y), 2f, Color.Gray);

        // Connection rule override labels.
        foreach (var graph in _controller.Graphs)
            foreach (var conn in graph.Connections)
                if (conn.RuleOverride != null)
                {
                    var a = WorldToScreen(conn.StartPos);
                    var b = WorldToScreen(conn.EndPos);
                    var mid = (a + b) * 0.5f;
                    var label = string.Join("+", conn.RuleOverride.Select(ColoringRules.ShortName));
                    var size = _font.MeasureString(label) * 0.7f;
                    sb.Draw(_pixel, new Rectangle((int)(mid.X - size.X / 2 - 2), (int)(mid.Y - size.Y / 2 - 1), (int)size.X + 4, (int)size.Y + 2),
                        null, ColorPalette.ActualWhite, 0f, Vector2.Zero, SpriteEffects.None, 0.17f);
                    sb.DrawString(_font, label, mid - size / 2f, ColorPalette.Black,
                        0f, Vector2.Zero, 0.7f, SpriteEffects.None, 0.16f);
                }

        // Region creation preview while dragging.
        if (Tool == EditTool.Region && _regionStart.HasValue)
            DrawWorldRect(sb, _regionStart.Value, ScreenToWorld(mp), Color.DimGray, 1.5f);

        // Selected label highlight (a screen-space box round the text).
        if (SelectedText != null)
        {
            var r = TextScreenRect(SelectedText);
            r.Inflate(4, 4);
            DrawScreenRect(sb, r, ColorPalette.Black, 2f);
        }

        // Selected region highlight.
        if (SelectedRegion != null)
        {
            var c = SelectedRegion.Entity.Position;
            var tl = new Vector2(c.X - SelectedRegion.HalfWidth, c.Y + SelectedRegion.HalfHeight);
            var br = new Vector2(c.X + SelectedRegion.HalfWidth, c.Y - SelectedRegion.HalfHeight);
            DrawWorldRect(sb, tl, br, ColorPalette.Black, 2.5f);
        }
    }

    private void DrawPlayOverlay(SpriteBatch sb)
    {
        DrawSequenceMarkers(sb);

        bool solved = _controller.IsLevelSolved;
        if (solved)
        {
            const string msg = "SOLVED";
            var size = _font.MeasureString(msg) * 1.5f;
            var pos = new Vector2(GraphRegion.X + (GraphRegion.Width - size.X) / 2f, GraphRegion.Y + 12);
            sb.DrawString(_font, msg, pos, ColorPalette.DarkGreen, 0f, Vector2.Zero, 1.5f, SpriteEffects.None, 0.2f);
        }

        // Panel last (over any overflow from the status line); the held rune stays on top of all.
        DrawPyramidPanel(sb);
        if (Dragging) DrawRuneIcon(sb, new Vector2(_handPos.X, _handPos.Y), HandIconSize, _handRune);
    }

    /// <summary>Per-puzzle sequence cues: a reward caption above each puzzle and a dim "LOCKED" veil
    /// over puzzles whose predecessors aren't solved yet.</summary>
    private void DrawSequenceMarkers(SpriteBatch sb)
    {
        foreach (var graph in _controller.Graphs)
        {
            if (!TryGetPuzzleScreenRect(graph, out var rect)) continue;

            if (!graph.Unlocked)
            {
                var veil = Rectangle.Intersect(rect, GraphRegion);
                sb.Draw(_pixel, veil, null, ColorPalette.Black * 0.45f, 0f, Vector2.Zero, SpriteEffects.None, 0.18f);
                const string locked = "LOCKED";
                var size = _font.MeasureString(locked);
                var pos = new Vector2(rect.X + (rect.Width - size.X) / 2f, rect.Y + (rect.Height - size.Y) / 2f);
                sb.DrawString(_font, locked, pos, ColorPalette.ActualWhite, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.17f);
            }
        }
    }

    /// <summary>Compact reward list, e.g. "Lith x2 Axe".</summary>
    private static string RewardSummary(IReadOnlyList<string> runes)
        => string.Join(" ", runes.GroupBy(r => r).Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key));

    /// <summary>The right-hand alphabet panel: the whole rune set laid out as a pyramid (so the tier /
    /// side relationships the rules depend on are visible). Owned runes render as a black disc with a
    /// white glyph and a small count badge; runes the player has none of render inverted (a white
    /// outlined ring with a black glyph) and can't be picked up. This panel is the rune source — drag a
    /// chip onto a node to place it.</summary>
    private void DrawPyramidPanel(SpriteBatch sb)
    {
        var panel = PyramidPanel;
        sb.Draw(_pixel, panel, null, ColorPalette.White, 0f, Vector2.Zero, SpriteEffects.None, 0.3f);
        sb.Draw(_pixel, new Rectangle(panel.X, panel.Y, 2, panel.Height), null, ColorPalette.Black,
            0f, Vector2.Zero, SpriteEffects.None, 0.31f);

        sb.DrawString(_font, "Alphabet", new Vector2(panel.X + PyramidPad, panel.Y + 10), ColorPalette.Black,
            0f, Vector2.Zero, 1f, SpriteEffects.None, 0.3f);

        var slots = PyramidSlots();
        DrawPyramidGuides(sb, slots);
        foreach (var (name, rect) in slots)
            DrawRuneChip(sb, rect, name, _inventory.Count(r => r == name));

        if (ShowSolver) DrawSolverControls(sb);
    }

    /// <summary>Solver controls in the panel's bottom band: a Solve button, the solution count, and
    /// prev/next/clear for stepping through solutions on the graph.</summary>
    private void DrawSolverControls(SpriteBatch sb)
    {
        var (solve, prev, next, clear) = SolverLayout();
        bool hasSolutions = _solverRun && _solutions.Count > 0;

        DrawTextButton(sb, solve, _viewingSolution ? "Re-solve" : "Solve", true);

        string count;
        if (!_solverRun) count = "Solver: (not run)";
        else if (_solutions.Count == 0) count = "No solutions";
        else count = $"{_solutions.Count}{(_solverTruncated ? "+" : "")} solution{(_solutions.Count == 1 ? "" : "s")}";
        sb.DrawString(_font, count, new Vector2(solve.X, solve.Bottom + 5), ColorPalette.Black,
            0f, Vector2.Zero, 0.9f, SpriteEffects.None, 0.2f);

        DrawTextButton(sb, prev, "<", hasSolutions);
        DrawTextButton(sb, next, ">", hasSolutions);
        if (hasSolutions)
        {
            var idx = $"{_solutionIndex + 1} / {_solutions.Count}{(_solverTruncated ? "+" : "")}";
            var size = _font.MeasureString(idx) * 0.9f;
            var pos = new Vector2((prev.Right + next.Left) / 2f - size.X / 2f, prev.Y + (prev.Height - size.Y) / 2f);
            sb.DrawString(_font, idx, pos, ColorPalette.Black, 0f, Vector2.Zero, 0.9f, SpriteEffects.None, 0.2f);
        }

        DrawTextButton(sb, clear, "Clear", _solverRun);
    }

    /// <summary>A filled push-button: black (enabled) or gray (disabled) with white centred text.</summary>
    private void DrawTextButton(SpriteBatch sb, Rectangle r, string label, bool enabled)
    {
        sb.Draw(_pixel, r, null, enabled ? ColorPalette.Black : Color.Gray, 0f, Vector2.Zero, SpriteEffects.None, 0.2f);
        var size = _font.MeasureString(label);
        sb.DrawString(_font, label, new Vector2(r.Center.X - size.X / 2f, r.Center.Y - size.Y / 2f),
            ColorPalette.ActualWhite, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.19f);
    }

    /// <summary>Faint guides behind the pyramid: a vertical centre line (the left/right "sidedness"
    /// divide) and a horizontal line between each tier row.</summary>
    private void DrawPyramidGuides(SpriteBatch sb, List<(string name, Rectangle rect)> slots)
    {
        if (slots.Count == 0) return;
        var gray = Color.DimGray;

        // Group slots into rows (they arrive in tier order, one Y per row).
        var rows = new List<List<Rectangle>>();
        int curY = int.MinValue;
        foreach (var (_, r) in slots)
        {
            if (r.Y != curY) { rows.Add(new List<Rectangle>()); curY = r.Y; }
            rows[^1].Add(r);
        }

        int top = rows[0][0].Top;
        int bottom = rows[^1][0].Bottom;
        int centerX = (rows[^1][0].Left + rows[^1][^1].Right) / 2;

        sb.Draw(_pixel, new Rectangle(centerX - 1, top, 3, bottom - top), null, gray,
            0f, Vector2.Zero, SpriteEffects.None, 0.28f);

        for (int i = 0; i < rows.Count - 1; i++)
        {
            var lower = rows[i + 1];
            int y = (rows[i][0].Bottom + lower[0].Top) / 2;
            sb.Draw(_pixel, new Rectangle(lower[0].Left, y - 1, lower[^1].Right - lower[0].Left, 3), null, gray,
                0f, Vector2.Zero, SpriteEffects.None, 0.28f);
        }
    }

    /// <summary>Draws one pyramid chip. Owned (count &gt; 0): filled black disc + white glyph + badge.
    /// Unavailable (count 0): foreground/background swapped — a white outlined ring + black glyph.</summary>
    private void DrawRuneChip(SpriteBatch sb, Rectangle rect, string rune, int count)
    {
        bool owned = count > 0;
        var center = rect.Center.ToVector2();

        float discSize = rect.Width * 0.95f;
        var discDst = new Rectangle((int)(center.X - discSize / 2), (int)(center.Y - discSize / 2), (int)discSize, (int)discSize);
        sb.Draw(owned ? _circleFilled : _circleOutlined, discDst, null,
            owned ? ColorPalette.Black : ColorPalette.ActualWhite, 0f, Vector2.Zero, SpriteEffects.None, 0.26f);

        var glyphColor = owned ? ColorPalette.ActualWhite : ColorPalette.Black;
        var tex = Runes.Texture(rune);
        float s = rect.Width * 0.7f;
        var dst = new Rectangle((int)(center.X - s / 2), (int)(center.Y - s / 2), (int)s, (int)s);
        if (tex != null)
            sb.Draw(tex, dst, null, glyphColor, 0f, Vector2.Zero, SpriteEffects.None, 0.25f);
        else
            DrawCenterText(sb, center, rune ?? "?", glyphColor);

        if (owned) DrawCountBadge(sb, rect, count);
    }

    /// <summary>Small remaining-count badge tucked into a chip's lower-right corner (white on a dark
    /// pill, scaled down so it never covers the glyph).</summary>
    private void DrawCountBadge(SpriteBatch sb, Rectangle slot, int count)
    {
        const float scale = 0.6f;
        var text = "x" + count;
        var size = _font.MeasureString(text) * scale;
        var pos = new Vector2(slot.Right - size.X - 3, slot.Bottom - size.Y - 2);
        sb.Draw(_pixel, new Rectangle((int)pos.X - 2, (int)pos.Y - 1, (int)size.X + 4, (int)size.Y + 1),
            null, ColorPalette.Black, 0f, Vector2.Zero, SpriteEffects.None, 0.21f);
        sb.DrawString(_font, text, pos, ColorPalette.ActualWhite, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.2f);
    }

    private void DrawRuneIcon(SpriteBatch sb, Vector2 center, float boxSize, string runeName, float alpha = 1f)
    {
        // Dark disc behind the (white) rune so it stays legible against any background.
        float disc = boxSize * 0.95f;
        var discDst = new Rectangle((int)(center.X - disc / 2), (int)(center.Y - disc / 2), (int)disc, (int)disc);
        sb.Draw(_circleFilled, discDst, null, ColorPalette.Black * alpha, 0f, Vector2.Zero, SpriteEffects.None, 0.26f);

        var tex = Runes.Texture(runeName);
        float s = boxSize * 0.8f;
        var dst = new Rectangle((int)(center.X - s / 2), (int)(center.Y - s / 2), (int)s, (int)s);
        if (tex != null)
            sb.Draw(tex, dst, null, ColorPalette.ActualWhite * alpha, 0f, Vector2.Zero, SpriteEffects.None, 0.25f);
        else
            DrawCenterText(sb, center, runeName ?? "?", ColorPalette.ActualWhite * alpha);
    }

    private void DrawCenterText(SpriteBatch sb, Vector2 center, string text, Color color)
    {
        var size = _font.MeasureString(text);
        sb.DrawString(_font, text, center - size * 0.5f, color);
    }

    /// <summary>Authored text labels, drawn in world space beneath the graph so nodes and edges always
    /// stay readable on top. In edit mode empty labels get a placeholder so they can still be found.</summary>
    private void DrawLabels(SpriteBatch sb)
    {
        foreach (var label in _controller.Labels)
        {
            if (!EditMode && string.IsNullOrEmpty(label.Text)) continue;
            var rect = TextScreenRect(label);
            sb.DrawString(_font, DisplayText(label), new Vector2(rect.X, rect.Y), ColorPalette.Black,
                0f, Vector2.Zero, TextDrawScale(label), SpriteEffects.None, 0.44f);
        }
    }

    private void DrawRegions(SpriteBatch sb)
    {
        foreach (var region in _controller.Regions)
        {
            var c = region.Entity.Position;
            var tl = new Vector2(c.X - region.HalfWidth, c.Y + region.HalfHeight);
            var br = new Vector2(c.X + region.HalfWidth, c.Y - region.HalfHeight);

            var color = region.IsViolated ? ColorPalette.DarkRed : Color.DimGray;
            float thickness = region.IsViolated ? 2.5f : 1.5f;
            DrawWorldRect(sb, tl, br, color, thickness);

            // Counter label at top-left corner: "T3: 1/2"
            var tlScreen = WorldToScreen(tl);
            var label = $"T{region.Tier}: {region.CurrentCount}/{region.MaxCount}";
            sb.DrawString(_font, label, tlScreen + new Vector2(4, 4),
                region.IsViolated ? ColorPalette.DarkRed : ColorPalette.Black,
                0f, Vector2.Zero, 0.8f, SpriteEffects.None, 0.15f);
        }
    }

    /// <summary>Draws an axis-aligned rectangle outline in screen space.</summary>
    private void DrawScreenRect(SpriteBatch sb, Rectangle r, Color color, float thickness)
    {
        var tl = new Vector2(r.Left, r.Top);
        var tr = new Vector2(r.Right, r.Top);
        var bl = new Vector2(r.Left, r.Bottom);
        var br = new Vector2(r.Right, r.Bottom);
        DrawLine(sb, tl, tr, thickness, color);
        DrawLine(sb, tr, br, thickness, color);
        DrawLine(sb, br, bl, thickness, color);
        DrawLine(sb, bl, tl, thickness, color);
    }

    /// <summary>Draws an axis-aligned rectangle outline in world space.</summary>
    private void DrawWorldRect(SpriteBatch sb, Vector2 worldTL, Vector2 worldBR, Color color, float thickness)
    {
        var tl = WorldToScreen(worldTL);
        var tr = WorldToScreen(new Vector2(worldBR.X, worldTL.Y));
        var bl = WorldToScreen(new Vector2(worldTL.X, worldBR.Y));
        var br = WorldToScreen(worldBR);
        DrawLine(sb, tl, tr, thickness, color);
        DrawLine(sb, tr, br, thickness, color);
        DrawLine(sb, br, bl, thickness, color);
        DrawLine(sb, bl, tl, thickness, color);
    }

    private void DrawRing(SpriteBatch sb, Vector2 center, float radius, Color color, float thickness)
    {
        const int segments = 24;
        Vector2 prev = center + new Vector2(radius, 0);
        for (int i = 1; i <= segments; i++)
        {
            float a = MathHelper.TwoPi * i / segments;
            var next = center + new Vector2(MathF.Cos(a) * radius, MathF.Sin(a) * radius);
            DrawLine(sb, prev, next, thickness, color);
            prev = next;
        }
    }

    private void DrawDashedLine(SpriteBatch sb, Vector2 start, Vector2 end, float thickness, Color color,
        float dashPx = 8f, float gapPx = 6f)
    {
        var diff = end - start;
        float total = diff.Length();
        if (total <= 0.0001f) return;
        var dir = diff / total;
        float t = 0f;
        bool drawing = true;
        while (t < total)
        {
            float segLen = MathF.Min(drawing ? dashPx : gapPx, total - t);
            if (drawing) DrawLine(sb, start + dir * t, start + dir * (t + segLen), thickness, color);
            t += segLen;
            drawing = !drawing;
        }
    }

    private void DrawLine(SpriteBatch sb, Vector2 start, Vector2 end, float thickness, Color color)
    {
        var diff = end - start;
        float length = diff.Length();
        if (length <= 0.0001f) return;
        float angle = MathF.Atan2(diff.Y, diff.X);
        sb.Draw(_pixel, start, null, color, angle, new Vector2(0, 0.5f),
            new Vector2(length, thickness), SpriteEffects.None, 0.49f);
    }

    #endregion

    public override void OnRemovedFromUI() => _pixel?.Dispose();
}
