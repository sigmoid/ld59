using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;

namespace ld59.UI.Powergrid;

public enum EditTool { Select, AddNode, Connect, Delete, Lock, Key, Link }

/// <summary>
/// 2D top-down viewport for a Powergrid level. Owns a pan/zoom camera and renders the puzzle graph
/// read from a <see cref="PowergridLevelController"/>. Drawing is scissor-clipped to the viewport
/// bounds (SolitaireContentPanel pattern).
///
/// Hosts both modes:
///  - Play: an inventory bar (holding slots + available tokens) + drag/drop of power tokens onto
///    nodes, validated by the graph; pan is left-drag on empty space.
///  - Edit: create/move/connect/delete nodes + inspector mutations; pan is right-drag.
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
    private readonly Texture2D _anchorMarker;
    private readonly Texture2D _andGate;
    private readonly Texture2D _xorGate;

    private Vector2 _pan = Vector2.Zero;
    private float _zoom = 64f;
    private const float MinZoom = 16f;
    private const float MaxZoom = 256f;

    private const float NodeWorldRadius = 0.4f;
    private const float SnapThreshold = 3.0f; // world units within which the camera eases to a puzzle
    private const float SnapSpeed = 6f;
    private const int InventoryBarHeight = 74;
    private const int SlotSize = 48;
    private const int SlotGap = 8;

    // Camera input
    private bool _panning;
    private Point _lastMouse;
    private int _prevScroll;
    private bool _prevLeftPressed;
    private bool _prevRightPressed;

    // Edit state
    public bool EditMode { get; set; }
    public EditTool Tool { get; set; } = EditTool.Select;
    public PowerNodeComponent Selected { get; private set; }
    private bool _needsRebuild;
    private PowerNodeComponent _movingNode;
    private Point _moveLast;
    private PowerNodeComponent _connectSource;

    // 2b authoring state
    public EdgeLockComponent SelectedLock { get; private set; }
    private bool _lockDragging;
    private Vector2 _lockStart;
    private EdgeLockComponent _keyLock;     // lock awaiting a key node (Key tool)
    private PowerNodeComponent _linkSource; // first node picked (Link tool)
    private const float LockHitDistance = 8f;

    // Play state (runtime; resets each session)
    private readonly List<int> _inventory = new();
    private int?[] _holding = new int?[3];
    private enum HandKind { None, FromInventory, FromHolding, FromNode }
    private HandKind _handKind = HandKind.None;
    private int _handPower;
    private int _handIndex;
    private PowerNodeComponent _handNode;
    private Point _handPos;
    private float _rejectTimer;
    private Vector2 _rejectPos;
    private bool Dragging => _handKind != HandKind.None;

    // Pulse-simulation run state (play mode). A run is started/stepped/reset via the toolbar; auto-play
    // advances one tick every TickDuration seconds and animates pulses travelling along energised edges.
    private bool _runStarted;     // a run has begun (vs. the placement phase)
    private bool _autoPlay;       // auto-advance on the timer (Run) vs. manual (Step)
    private float _tickProgress;  // [0,1] elapsed fraction of the current tick (drives pulse-dot position)
    private const float TickDuration = 0.35f;

    public Scene Scene => _scene;
    public PowergridLevelController Controller => _controller;

    public PowergridView(Rectangle bounds, Scene scene = null)
    {
        _bounds = bounds;
        _scene = scene ?? new Scene();
        _scene.InitializeEntities();
        _controller = new PowergridLevelController(_scene);

        // Frame the first puzzle on open.
        if (_controller.Graphs.Count > 0) _pan = _controller.Graphs[0].Centroid;

        _font = Core.DefaultFont;

        _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });

        _circleFilled   = Core.Content.Load<Texture2D>("images/powergrid/circle-filled");
        _circleOutlined = Core.Content.Load<Texture2D>("images/powergrid/circle-outlined");
        _anchorMarker   = TryLoad("images/powergrid/anchor");
        _andGate        = TryLoad("images/powergrid/and");
        _xorGate        = TryLoad("images/powergrid/xor");

        _prevScroll = Mouse.GetState().ScrollWheelValue;
        ResetPlayState();
    }

    private static Texture2D TryLoad(string asset)
    {
        try { return Core.Content.Load<Texture2D>(asset); }
        catch { return null; }
    }

    public override Rectangle GetBoundingBox() => _bounds;
    public override void SetBounds(Rectangle bounds) => _bounds = bounds;

    /// <summary>Reloads inventory/holding from the level config and clears any in-progress run. Drops
    /// anything in hand.</summary>
    public void ResetPlayState()
    {
        ReturnHandToOrigin();
        _inventory.Clear();
        _inventory.AddRange(_controller.InitialInventory);
        _holding = new int?[Math.Max(1, _controller.HoldingSlots)];
        foreach (var graph in _controller.Graphs)
            foreach (var n in graph.Nodes)
            {
                n.PlacedTokenPower = 0;
                n.PlacedTokenDelay = 0;
                n.HeldTokenCollected = false;
            }
        ResetSimulation();   // clears lit/energised state and re-applies the discovery baseline
    }

    // ── Pulse-simulation controls (wired to the Run/Step/Reset toolbar buttons) ──

    /// <summary>True while a run is in progress or halted (vs. the placement phase).</summary>
    public bool RunStarted => _runStarted;

    private bool AnyRunning() => _controller.Graphs.Any(g => g.IsRunning);

    /// <summary>Run (auto-play): start a fresh run if none is live, then auto-advance on the timer.</summary>
    public void RunSimulation()
    {
        if (!_runStarted || !AnyRunning())
        {
            _controller.StartAll();
            _runStarted = true;
        }
        _autoPlay = true;
        _tickProgress = 0f;
    }

    /// <summary>Step: advance exactly one tick (starting the run on the first press), no auto-play.</summary>
    public void StepSimulation()
    {
        _autoPlay = false;
        if (!_runStarted || !AnyRunning())
        {
            _controller.StartAll();   // first press shows t=0 (just-emitted anchors)
            _runStarted = true;
        }
        else
        {
            _controller.StepAll();
        }
        _tickProgress = 0f;
    }

    /// <summary>Reset back to the placement phase: nothing lit, locks closed, unsolved.</summary>
    public void ResetSimulation()
    {
        _controller.ResetAllRuns();
        _runStarted = false;
        _autoPlay = false;
        _tickProgress = 0f;
    }

    #region Camera transforms

    // Play mode reserves a bottom strip for the inventory bar; the graph fills the rest.
    private Rectangle GraphRegion => EditMode
        ? _bounds
        : new Rectangle(_bounds.X, _bounds.Y, _bounds.Width, _bounds.Height - InventoryBarHeight);

    private Rectangle BarRect => new(_bounds.X, _bounds.Bottom - InventoryBarHeight, _bounds.Width, InventoryBarHeight);

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
        {
            if (!EditMode && !graph.IsActive) continue; // inactive (locked) puzzles aren't interactable
            foreach (var node in graph.Nodes)
            {
                if (!EditMode && !node.Discovered) continue; // can't interact with what isn't revealed
                if (Vector2.Distance(WorldToScreen(node.Entity.Position), new Vector2(screen.X, screen.Y)) <= r)
                    return node;
            }
        }
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
        {
            // Scrolling over a placed emitter adjusts its firing delay; otherwise it zooms.
            var emitter = !EditMode ? HitTestNode(mp) : null;
            if (emitter != null && emitter.IsAnchor && emitter.PlacedTokenPower > 0)
                AdjustEmitterDelay(emitter, scrollDelta > 0 ? 1 : -1);
            else
                _zoom = MathHelper.Clamp(_zoom * MathF.Pow(1.1f, scrollDelta / 120f), MinZoom, MaxZoom);
        }

        if (_rejectTimer > 0) _rejectTimer -= deltaTime;

        if (EditMode)
            HandleEditInput(mp, inside, left, leftClick, leftRelease);
        else
            HandlePlayInput(mp, inside, left, leftClick, leftRelease);

        // Pan is right-drag in both modes (left is reserved for tools / token drag).
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

        if (!EditMode)
        {
            AdvanceAutoPlay(deltaTime);
            CollectHeldTokens();
        }
        UpdateCameraSnap(deltaTime);
    }

    /// <summary>While auto-playing, accumulate time and step the simulation one tick per TickDuration,
    /// stopping when every run has halted. <see cref="_tickProgress"/> drives the pulse-dot animation.</summary>
    private void AdvanceAutoPlay(float deltaTime)
    {
        if (!_runStarted || !_autoPlay) return;

        if (!AnyRunning()) { _autoPlay = false; _tickProgress = 0f; return; }

        _tickProgress += deltaTime / TickDuration;
        while (_tickProgress >= 1f)
        {
            _tickProgress -= 1f;
            if (!_controller.StepAll()) { _autoPlay = false; _tickProgress = 0f; break; }
        }
    }

    /// <summary>When idle (not panning/dragging) and near an active puzzle, ease the camera to centre it.</summary>
    private void UpdateCameraSnap(float deltaTime)
    {
        if (EditMode || _panning || Dragging || _controller.Graphs.Count <= 1) return;

        PuzzleGraph nearest = null;
        float best = float.MaxValue;
        foreach (var g in _controller.Graphs)
        {
            if (!g.IsActive) continue;
            float d = Vector2.Distance(g.Centroid, _pan);
            if (d < best) { best = d; nearest = g; }
        }

        if (nearest != null && best > 0.01f && best < SnapThreshold)
            _pan = Vector2.Lerp(_pan, nearest.Centroid, Math.Min(1f, SnapSpeed * deltaTime));
    }

    /// <summary>Discovery: a node that powers up grants the token it holds into the inventory (once).</summary>
    private void CollectHeldTokens()
    {
        foreach (var graph in _controller.Graphs)
            foreach (var n in graph.Nodes)
                if (n.IsActive && n.HeldTokenPower > 0 && !n.HeldTokenCollected)
                {
                    _inventory.Add(n.HeldTokenPower);
                    n.HeldTokenCollected = true;
                }
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

    #region Play interaction (token drag/drop)

    private void HandlePlayInput(Point mp, bool inside, bool left, bool leftClick, bool leftRelease)
    {
        if (leftClick && inside)
            TryStartDrag(mp);

        if (Dragging)
        {
            _handPos = mp;
            if (leftRelease) FinishDrag(mp);
        }
    }

    private bool TryStartDrag(Point mp)
    {
        if (BarRect.Contains(mp))
        {
            for (int i = 0; i < _holding.Length; i++)
            {
                if (_holding[i].HasValue && HoldingSlotRect(i).Contains(mp))
                {
                    _handKind = HandKind.FromHolding; _handPower = _holding[i].Value; _handIndex = i;
                    _holding[i] = null; _handPos = mp; return true;
                }
            }
            for (int i = 0; i < _inventory.Count; i++)
            {
                if (InventorySlotRect(i).Contains(mp))
                {
                    _handKind = HandKind.FromInventory; _handPower = _inventory[i]; _handIndex = i;
                    _inventory.RemoveAt(i); _handPos = mp; return true;
                }
            }
            return false;
        }

        if (GraphRegion.Contains(mp))
        {
            var node = HitTestNode(mp);
            if (node != null && node.PlacedTokenPower > 0)
            {
                var graph = _controller.GraphOf(node);
                if (graph != null && graph.CanRemovePower(node))
                {
                    _handKind = HandKind.FromNode; _handPower = node.PlacedTokenPower; _handNode = node;
                    node.PlacedTokenPower = 0; node.PlacedTokenDelay = 0; _handPos = mp; return true;
                }
                Reject(WorldToScreen(node.Entity.Position));
            }
        }
        return false;
    }

    private void FinishDrag(Point mp)
    {
        if (GraphRegion.Contains(mp))
        {
            var node = HitTestNode(mp);
            if (node != null)
            {
                var graph = _controller.GraphOf(node);
                if (node.PlacedTokenPower == 0 && graph != null && graph.CanAddPower(node, _handPower))
                {
                    node.PlacedTokenPower = _handPower;
                    ClearHand();
                    return;
                }
                Reject(WorldToScreen(node.Entity.Position));
                ReturnHandToOrigin();
                return;
            }
        }
        else if (BarRect.Contains(mp))
        {
            for (int i = 0; i < _holding.Length; i++)
            {
                if (!_holding[i].HasValue && HoldingSlotRect(i).Contains(mp))
                {
                    _holding[i] = _handPower; ClearHand(); return;
                }
            }
            // Dropped anywhere else on the bar → back to inventory.
            _inventory.Add(_handPower); ClearHand(); return;
        }

        ReturnHandToOrigin();
    }

    private void ReturnHandToOrigin()
    {
        switch (_handKind)
        {
            case HandKind.FromInventory:
                int idx = _handIndex < 0 ? 0 : (_handIndex > _inventory.Count ? _inventory.Count : _handIndex);
                _inventory.Insert(idx, _handPower); break;
            case HandKind.FromHolding:
                _holding[_handIndex] = _handPower; break;
            case HandKind.FromNode:
                if (_handNode != null) _handNode.PlacedTokenPower = _handPower; break;
        }
        ClearHand();
    }

    private void ClearHand() { _handKind = HandKind.None; _handNode = null; }

    private void Reject(Vector2 screenPos) { _rejectTimer = 0.4f; _rejectPos = screenPos; }

    /// <summary>Changes a placed emitter's firing delay, clamped to [0, tickCap-1]. Resets any
    /// in-progress run so the new timing takes effect on the next Run.</summary>
    private void AdjustEmitterDelay(PowerNodeComponent emitter, int delta)
    {
        int cap = _controller.GraphOf(emitter)?.TickCap ?? 64;
        emitter.PlacedTokenDelay = MathHelper.Clamp(emitter.PlacedTokenDelay + delta, 0, Math.Max(0, cap - 1));
        if (_runStarted) ResetSimulation();
    }

    private Rectangle HoldingSlotRect(int i)
    {
        int y = BarRect.Y + (BarRect.Height - SlotSize) / 2;
        int x = BarRect.X + SlotGap + i * (SlotSize + SlotGap);
        return new Rectangle(x, y, SlotSize, SlotSize);
    }

    private int InventoryStartX => HoldingSlotRect(_holding.Length - 1).Right + 28;

    private Rectangle InventorySlotRect(int i)
    {
        int y = BarRect.Y + (BarRect.Height - SlotSize) / 2;
        int x = InventoryStartX + i * (SlotSize + SlotGap);
        return new Rectangle(x, y, SlotSize, SlotSize);
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
                    var hit = HitTestNode(mp);
                    Selected = hit;
                    SelectedLock = hit == null ? LockHitTest(mp) : null;
                    _movingNode = hit;
                    _moveLast = mp;
                }
                if (left && _movingNode != null)
                {
                    var delta = mp - _moveLast;
                    _moveLast = mp;
                    _movingNode.Entity.LocalPosition += new Vector2(delta.X / _zoom, -delta.Y / _zoom);
                    MarkDirty();
                }
                if (leftRelease) _movingNode = null;
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

                    if (DeleteActivationAt(mp)) break;

                    var lck = LockHitTest(mp);
                    if (lck != null) DeleteLock(lck);
                }
                break;

            case EditTool.Lock:
                if (leftClick && inside) { _lockDragging = true; _lockStart = ScreenToWorld(mp); }
                if (leftRelease && _lockDragging)
                {
                    _lockDragging = false;
                    var end = ScreenToWorld(mp);
                    if (Vector2.Distance(_lockStart, end) > 0.2f) AddLock(_lockStart, end);
                }
                break;

            case EditTool.Key:
                if (leftClick && inside)
                {
                    var node = HitTestNode(mp);
                    if (node != null && _keyLock != null)
                    {
                        _keyLock.KeyNode = node.Entity.Name;
                        _keyLock = null;
                        MarkDirty();
                    }
                    else
                    {
                        _keyLock = LockHitTest(mp); // pick the lock first, then a node
                    }
                }
                break;

            case EditTool.Link:
                if (leftClick && inside)
                {
                    var node = HitTestNode(mp);
                    if (node != null)
                    {
                        if (_linkSource == null) _linkSource = node;
                        else { ToggleActivation(_linkSource, node); _linkSource = null; }
                    }
                }
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
        Selected = node;
        MarkDirty();
    }

    private void AddConnection(PowerNodeComponent source, PowerNodeComponent target)
    {
        var targetName = target.Entity.Name;
        if (!source.OutgoingNodeNames.Contains(targetName))
        {
            source.OutgoingNodeNames.Add(targetName);
            MarkDirty();
        }
    }

    private void DeleteNode(PowerNodeComponent node)
    {
        var name = node.Entity.Name;
        foreach (var e in _scene.GetEntities())
        {
            e.GetComponent<PowerNodeComponent>()?.OutgoingNodeNames.Remove(name);
            var lck = e.GetComponent<EdgeLockComponent>();
            if (lck != null && lck.KeyNode == name) lck.KeyNode = string.Empty;
        }
        _scene.RemoveEntity(node.Entity);
        if (Selected == node) Selected = null;
        if (_linkSource == node) _linkSource = null;
        MarkDirty();
    }

    // ── Locks / keys ──────────────────────────────────────────────────────
    private EdgeLockComponent LockHitTest(Point screen)
    {
        var p = new Vector2(screen.X, screen.Y);
        foreach (var graph in _controller.Graphs)
            foreach (var lck in graph.Locks)
                if (DistPointSeg(p, WorldToScreen(lck.PointA), WorldToScreen(lck.PointB)) <= LockHitDistance)
                    return lck;
        return null;
    }

    private void AddLock(Vector2 a, Vector2 b)
    {
        var entity = new Entity { Name = UniqueName("lock") };
        var lck = new EdgeLockComponent { PointA = a, PointB = b };
        entity.AddComponent(lck);
        _scene.AddEntity(entity);
        SelectedLock = lck; Selected = null;
        MarkDirty();
    }

    private void DeleteLock(EdgeLockComponent lck)
    {
        _scene.RemoveEntity(lck.Entity);
        if (SelectedLock == lck) SelectedLock = null;
        if (_keyLock == lck) _keyLock = null;
        MarkDirty();
    }

    // ── Connections ───────────────────────────────────────────────────────
    private Connection ConnectionHitTest(Point screen)
    {
        var p = new Vector2(screen.X, screen.Y);
        foreach (var graph in _controller.Graphs)
            foreach (var conn in graph.Connections)
                if (DistPointSeg(p, WorldToScreen(conn.StartPos), WorldToScreen(conn.EndPos)) <= LockHitDistance)
                    return conn;
        return null;
    }

    private void RemoveConnection(Connection conn)
    {
        conn.From.OutgoingNodeNames.Remove(conn.To.Entity.Name);
        MarkDirty();
    }

    // ── Activation links (stored by node name; puzzles are auto-detected) ──
    private void ToggleActivation(PowerNodeComponent a, PowerNodeComponent b)
    {
        if (_controller.GraphOf(a) == _controller.GraphOf(b)) return; // same puzzle — no self-link

        var lvl = EnsureLevelComponent();
        var edge = (a.Entity.Name, b.Entity.Name);
        if (!lvl.Activations.Remove(edge)) lvl.Activations.Add(edge);
        MarkDirty();
    }

    private bool DeleteActivationAt(Point screen)
    {
        var p = new Vector2(screen.X, screen.Y);
        foreach (var (from, to) in _controller.ActivationEdges)
        {
            var ga = _controller.GraphContainingNode(from);
            var gb = _controller.GraphContainingNode(to);
            if (ga == null || gb == null) continue;
            if (DistPointSeg(p, WorldToScreen(ga.Centroid), WorldToScreen(gb.Centroid)) <= LockHitDistance)
            {
                EnsureLevelComponent().Activations.Remove((from, to));
                MarkDirty();
                return true;
            }
        }
        return false;
    }

    private PowergridLevelComponent EnsureLevelComponent()
    {
        foreach (var e in _scene.GetEntities())
        {
            var lvl = e.GetComponent<PowergridLevelComponent>();
            if (lvl != null) return lvl;
        }
        var entity = new Entity { Name = UniqueName("level") };
        var comp = new PowergridLevelComponent();
        entity.AddComponent(comp);
        _scene.AddEntity(entity);
        return comp;
    }

    private static float DistPointSeg(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float lenSq = ab.LengthSquared();
        float t = lenSq > 0 ? MathHelper.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }

    // ── Inspector ops ─────────────────────────────────────────────────────
    public void ToggleAnchor() { if (Selected != null) { Selected.IsAnchor = !Selected.IsAnchor; MarkDirty(); } }
    public void ToggleGoal()   { if (Selected != null) { Selected.IsGoal   = !Selected.IsGoal;   MarkDirty(); } }

    public void CycleKind()
    {
        if (Selected == null) return;
        Selected.NodeKind = Selected.NodeKind switch
        {
            NodeKind.Normal => NodeKind.And,
            NodeKind.And => NodeKind.Xor,
            _ => NodeKind.Normal,
        };
        MarkDirty();
    }

    /// <summary>Editor stand-in for dragging a token onto the node (sets placed-token power).</summary>
    public void AdjustPlacedToken(int delta)
    {
        if (Selected == null) return;
        Selected.PlacedTokenPower = Math.Max(0, Selected.PlacedTokenPower + delta);
        MarkDirty();
    }

    public void AdjustHeldToken(int delta)
    {
        if (Selected == null) return;
        Selected.HeldTokenPower = Math.Max(0, Selected.HeldTokenPower + delta);
        MarkDirty();
    }

    public void DeleteSelected() { if (Selected != null) DeleteNode(Selected); }

    /// <summary>Clears all placed tokens (called when entering edit mode for a clean authoring slate).</summary>
    public void ClearPlacedTokens()
    {
        foreach (var graph in _controller.Graphs)
            foreach (var n in graph.Nodes)
                n.PlacedTokenPower = 0;
        MarkDirty();
    }

    /// <summary>Toggles the discovery/fog mechanic for the selected node's whole puzzle.</summary>
    public void ToggleDiscovery()
    {
        if (Selected == null) return;
        var graph = _controller.GraphOf(Selected);
        if (graph == null) return;

        var lvl = EnsureLevelComponent();
        if (graph.DiscoveryEnabled)
        {
            var names = graph.Nodes.Select(n => n.Entity.Name).ToHashSet();
            lvl.DiscoveryNodes.RemoveAll(names.Contains);
        }
        else
        {
            lvl.DiscoveryNodes.Add(Selected.Entity.Name);
        }
        MarkDirty();
    }

    public bool SelectedPuzzleDiscovery
        => Selected != null && _controller.GraphOf(Selected) is { DiscoveryEnabled: true };

    /// <summary>Effective pulse-simulation tick cap for the selected node's puzzle (0 if no selection).</summary>
    public int SelectedPuzzleTickCap
        => Selected != null && _controller.GraphOf(Selected) is { } g ? g.TickCap : 0;

    /// <summary>Adjusts (and persists) the per-puzzle tick cap for the selected node's puzzle.</summary>
    public void AdjustTickCap(int delta)
    {
        if (Selected == null) return;
        var graph = _controller.GraphOf(Selected);
        if (graph == null) return;

        int cap = Math.Max(1, graph.TickCap + delta);
        graph.TickCap = cap;                          // immediate feedback this session
        EnsureLevelComponent().TickCaps[graph.Id] = cap; // persist override keyed by puzzle id
        MarkDirty();
    }

    // ── Level config editing (inventory) ──────────────────────────────────
    public void AddInventoryToken(int power) { EnsureLevelComponent().Inventory.Add(power); MarkDirty(); }
    public void ClearInventory() { EnsureLevelComponent().Inventory.Clear(); MarkDirty(); }

    public string EditorInventoryText
    {
        get
        {
            foreach (var e in _scene.GetEntities())
            {
                var lvl = e.GetComponent<PowergridLevelComponent>();
                if (lvl != null) return lvl.Inventory.Count > 0 ? string.Join(",", lvl.Inventory) : "(empty)";
            }
            return "(empty)";
        }
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

        // Gray viewport; each puzzle gets a white background sized to its nodes.
        spriteBatch.Draw(_pixel, _bounds, null, Color.Gray, 0f, Vector2.Zero, SpriteEffects.None, 0f);
        foreach (var graph in _controller.Graphs)
            if (TryGetPuzzleScreenRect(graph, out var rect))
                spriteBatch.Draw(_pixel, rect, null, ColorPalette.ActualWhite, 0f, Vector2.Zero, SpriteEffects.None, 0.05f);

        foreach (var graph in _controller.Graphs)
        {
            foreach (var conn in graph.Connections)
                if (EditMode || (conn.From.Discovered && conn.To.Discovered))
                    DrawConnection(spriteBatch, conn);
            foreach (var lck in graph.Locks) DrawLock(spriteBatch, lck);
            foreach (var node in graph.Nodes)
                if (EditMode || node.Discovered)
                    DrawNode(spriteBatch, node);

            if (!EditMode && !graph.IsActive)
                DrawInactiveMask(spriteBatch, graph);
        }

        DrawActivationLinks(spriteBatch); // both modes

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
        var color = conn.IsActive ? ColorPalette.Black : Color.Gray;
        float thickness = conn.IsActive ? 3f : 1.5f;
        DrawLine(sb, a, b, thickness, color);
        DrawArrowHead(sb, a, b, color, thickness);

        // A pulse travelling this edge this tick: a bright dot eased from start to end across the tick.
        if (conn.IsActive && _runStarted && !EditMode)
        {
            var p = Vector2.Lerp(a, b, MathHelper.Clamp(_tickProgress, 0f, 1f));
            float r = MathF.Max(4f, _zoom * 0.10f);
            sb.Draw(_circleFilled, new Rectangle((int)(p.X - r), (int)(p.Y - r), (int)(r * 2), (int)(r * 2)),
                null, ColorPalette.ActualWhite, 0f, Vector2.Zero, SpriteEffects.None, 0.46f);
        }
    }

    private void DrawArrowHead(SpriteBatch sb, Vector2 from, Vector2 to, Color color, float thickness)
    {
        var dir = to - from;
        if (dir == Vector2.Zero) return;
        dir.Normalize();
        var tip = to;
        float size = MathF.Max(8f, _zoom * 0.16f);
        float spread = MathHelper.ToRadians(150f);
        float baseAngle = MathF.Atan2(dir.Y, dir.X);
        var left  = tip + size * new Vector2(MathF.Cos(baseAngle + spread), MathF.Sin(baseAngle + spread));
        var right = tip + size * new Vector2(MathF.Cos(baseAngle - spread), MathF.Sin(baseAngle - spread));
        DrawLine(sb, tip, left, thickness, color);
        DrawLine(sb, tip, right, thickness, color);
    }

    private void DrawLock(SpriteBatch sb, EdgeLockComponent lck)
    {
        var a = WorldToScreen(lck.PointA);
        var b = WorldToScreen(lck.PointB);
        var color = lck.IsLocked ? ColorPalette.Black : Color.Gray;
        DrawLine(sb, a, b, lck.IsLocked ? 3f : 1.5f, color);
    }

    private void DrawNode(SpriteBatch sb, PowerNodeComponent node)
    {
        var center = WorldToScreen(node.Entity.Position);
        float diameter = NodeWorldRadius * 2f * _zoom;
        var dst = new Rectangle(
            (int)(center.X - diameter * 0.5f),
            (int)(center.Y - diameter * 0.5f),
            (int)diameter, (int)diameter);

        var tex = node.IsActive ? _circleFilled : _circleOutlined;
        sb.Draw(tex, dst, null, ColorPalette.Black, 0f, Vector2.Zero, SpriteEffects.None, 0.5f);

        if (node.NodeKind == NodeKind.And) DrawCenterMark(sb, center, _andGate, "&", dst.Width);
        else if (node.NodeKind == NodeKind.Xor) DrawCenterMark(sb, center, _xorGate, "^", dst.Width);
        // A placed emitter shows its firing delay (the meaningful, player-set value), e.g. "+0", "+2".
        else if (node.PlacedTokenPower > 0) DrawCenterText(sb, center, "+" + node.PlacedTokenDelay, ColorPalette.ActualWhite);

        if (node.IsAnchor) DrawRoleMarker(sb, center, diameter, _anchorMarker, "A");
        if (node.IsGoal)   DrawRoleMarker(sb, center, diameter, null, "G");

        // Held token waiting to be discovered (shown in edit always; in play until collected).
        if (node.HeldTokenPower > 0 && (EditMode || !node.HeldTokenCollected))
            DrawHeldBadge(sb, center, diameter, node.HeldTokenPower);
    }

    private void DrawHeldBadge(SpriteBatch sb, Vector2 nodeCenter, float diameter, int power)
    {
        float s = diameter * 0.46f;
        var c = new Vector2(nodeCenter.X + diameter * 0.42f, nodeCenter.Y - diameter * 0.42f);
        sb.Draw(_circleOutlined, new Rectangle((int)(c.X - s / 2), (int)(c.Y - s / 2), (int)s, (int)s),
            null, ColorPalette.Black, 0f, Vector2.Zero, SpriteEffects.None, 0.45f);
        DrawCenterText(sb, c, power.ToString(), ColorPalette.Black);
    }

    private void DrawCenterMark(SpriteBatch sb, Vector2 center, Texture2D tex, string fallback, int boxSize)
    {
        if (tex != null)
        {
            int s = (int)(boxSize * 0.6f);
            sb.Draw(tex, new Rectangle((int)(center.X - s / 2f), (int)(center.Y - s / 2f), s, s), null, ColorPalette.Black);
            return;
        }
        DrawCenterText(sb, center, fallback, ColorPalette.ActualWhite);
    }

    private void DrawCenterText(SpriteBatch sb, Vector2 center, string text, Color color)
    {
        var size = _font.MeasureString(text);
        sb.DrawString(_font, text, center - size * 0.5f, color);
    }

    private void DrawRoleMarker(SpriteBatch sb, Vector2 center, float diameter, Texture2D tex, string fallback)
    {
        float markerSize = diameter * 0.55f;
        var pos = new Vector2(center.X, center.Y - diameter * 0.5f - markerSize * 0.5f - 2f);
        if (tex != null)
        {
            sb.Draw(tex, new Rectangle((int)(pos.X - markerSize / 2f), (int)(pos.Y - markerSize / 2f), (int)markerSize, (int)markerSize),
                null, ColorPalette.Black);
            return;
        }
        DrawCenterText(sb, pos, fallback, ColorPalette.Black);
    }

    /// <summary>Screen-space bounding box of a puzzle's (drawn) nodes plus padding. False if nothing is drawn.</summary>
    private bool TryGetPuzzleScreenRect(PuzzleGraph graph, out Rectangle rect)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        int count = 0;
        foreach (var n in graph.Nodes)
        {
            if (!EditMode && !n.Discovered) continue;
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

    private void DrawInactiveMask(SpriteBatch sb, PuzzleGraph graph)
    {
        if (!TryGetPuzzleScreenRect(graph, out var rect)) return;

        sb.Draw(_pixel, rect, null, new Color(0, 0, 0, 120), 0f, Vector2.Zero, SpriteEffects.None, 0.18f);

        const string label = "LOCKED";
        var size = _font.MeasureString(label);
        var center = new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        sb.DrawString(_font, label, center - size * 0.5f, ColorPalette.ActualWhite, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.17f);
    }

    /// <summary>Point where the ray from a rectangle's centre toward <paramref name="target"/> exits the rect.</summary>
    private static Vector2 RectEdgeToward(Rectangle r, Vector2 target)
    {
        var center = new Vector2(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        var dir = target - center;
        if (dir == Vector2.Zero) return center;
        float tx = dir.X > 0 ? (r.Right - center.X) / dir.X : dir.X < 0 ? (r.Left - center.X) / dir.X : float.MaxValue;
        float ty = dir.Y > 0 ? (r.Bottom - center.Y) / dir.Y : dir.Y < 0 ? (r.Top - center.Y) / dir.Y : float.MaxValue;
        return center + dir * MathF.Min(tx, ty);
    }

    /// <summary>White arrows from the boundary of one puzzle's background to the next (drawn in both modes).
    /// Many-to-many: each stored edge is drawn independently, so one puzzle may point to several.</summary>
    private void DrawActivationLinks(SpriteBatch sb)
    {
        foreach (var (from, to) in _controller.ActivationEdges)
        {
            var ga = _controller.GraphContainingNode(from);
            var gb = _controller.GraphContainingNode(to);
            if (ga == null || gb == null || ga == gb) continue;
            if (!TryGetPuzzleScreenRect(ga, out var ra) || !TryGetPuzzleScreenRect(gb, out var rb)) continue;

            var bc = new Vector2(rb.X + rb.Width / 2f, rb.Y + rb.Height / 2f);
            var ac = new Vector2(ra.X + ra.Width / 2f, ra.Y + ra.Height / 2f);
            var a = RectEdgeToward(ra, bc);
            var b = RectEdgeToward(rb, ac);
            DrawLine(sb, a, b, 2f, ColorPalette.ActualWhite);
            DrawArrowHead(sb, a, b, ColorPalette.ActualWhite, 2f);
        }
    }

    private void DrawEditOverlay(SpriteBatch sb)
    {
        var mp = Core.GetTransformedMousePoint();

        if (Selected != null)
            DrawRing(sb, WorldToScreen(Selected.Entity.Position), NodeWorldRadius * _zoom + 4f, ColorPalette.Black, 2f);

        if (_linkSource != null)
            DrawRing(sb, WorldToScreen(_linkSource.Entity.Position), NodeWorldRadius * _zoom + 4f, ColorPalette.Orange, 2f);

        // Lock highlights.
        if (SelectedLock != null)
            DrawLine(sb, WorldToScreen(SelectedLock.PointA), WorldToScreen(SelectedLock.PointB), 5f, ColorPalette.Black);
        if (_keyLock != null)
            DrawLine(sb, WorldToScreen(_keyLock.PointA), WorldToScreen(_keyLock.PointB), 5f, ColorPalette.Orange);

        if (_connectSource != null)
            DrawLine(sb, WorldToScreen(_connectSource.Entity.Position), new Vector2(mp.X, mp.Y), 2f, Color.Gray);

        if (_lockDragging)
            DrawLine(sb, WorldToScreen(_lockStart), new Vector2(mp.X, mp.Y), 3f, Color.Gray);
    }

    private void DrawPlayOverlay(SpriteBatch sb)
    {
        DrawInventoryBar(sb);

        var withGoals = _controller.Graphs.Where(g => g.Nodes.Any(n => n.IsGoal)).ToList();
        bool solved = withGoals.Count > 0 && withGoals.All(g => g.IsSolved);

        // Solved banner when every puzzle that has a goal is solved.
        if (solved)
        {
            const string msg = "SOLVED";
            var size = _font.MeasureString(msg) * 1.5f;
            var pos = new Vector2(GraphRegion.X + (GraphRegion.Width - size.X) / 2f, GraphRegion.Y + 12);
            sb.DrawString(_font, msg, pos, ColorPalette.DarkGreen, 0f, Vector2.Zero, 1.5f, SpriteEffects.None, 0.2f);
        }

        DrawRunStatus(sb, solved);

        if (Dragging) DrawToken(sb, new Vector2(_handPos.X, _handPos.Y), SlotSize * 0.5f, _handPower);
    }

    /// <summary>Top-left status: placement prompt, running tick counter, or halted/solved.</summary>
    private void DrawRunStatus(SpriteBatch sb, bool solved)
    {
        int tick = _controller.Graphs.Count > 0 ? _controller.Graphs.Max(g => g.CurrentTick) : 0;
        string status;
        if (!_runStarted) status = "Place tokens on anchors  -  scroll an emitter to set its delay  -  then Run";
        else if (AnyRunning()) status = $"Running...  t={tick}";
        else status = solved ? $"Solved at t={tick}" : $"Halted  t={tick}";

        var pos = new Vector2(GraphRegion.X + 10, GraphRegion.Y + 8);
        sb.DrawString(_font, status, pos, ColorPalette.Black, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.2f);
    }

    private void DrawInventoryBar(SpriteBatch sb)
    {
        var bar = BarRect;
        sb.Draw(_pixel, bar, null, ColorPalette.White, 0f, Vector2.Zero, SpriteEffects.None, 0.3f);
        sb.Draw(_pixel, new Rectangle(bar.X, bar.Y, bar.Width, 2), null, ColorPalette.Black, 0f, Vector2.Zero, SpriteEffects.None, 0.31f);

        for (int i = 0; i < _holding.Length; i++)
        {
            var r = HoldingSlotRect(i);
            DrawSlot(sb, r);
            if (_holding[i].HasValue)
                DrawToken(sb, r.Center.ToVector2(), SlotSize * 0.5f, _holding[i].Value);
        }

        for (int i = 0; i < _inventory.Count; i++)
            DrawToken(sb, InventorySlotRect(i).Center.ToVector2(), SlotSize * 0.5f, _inventory[i]);
    }

    private void DrawSlot(SpriteBatch sb, Rectangle r)
    {
        // Hollow square slot.
        sb.Draw(_pixel, new Rectangle(r.X, r.Y, r.Width, 2), null, ColorPalette.Black, 0f, Vector2.Zero, SpriteEffects.None, 0.32f);
        sb.Draw(_pixel, new Rectangle(r.X, r.Bottom - 2, r.Width, 2), null, ColorPalette.Black, 0f, Vector2.Zero, SpriteEffects.None, 0.32f);
        sb.Draw(_pixel, new Rectangle(r.X, r.Y, 2, r.Height), null, ColorPalette.Black, 0f, Vector2.Zero, SpriteEffects.None, 0.32f);
        sb.Draw(_pixel, new Rectangle(r.Right - 2, r.Y, 2, r.Height), null, ColorPalette.Black, 0f, Vector2.Zero, SpriteEffects.None, 0.32f);
    }

    private void DrawToken(SpriteBatch sb, Vector2 center, float radius, int power)
    {
        var dst = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), (int)(radius * 2), (int)(radius * 2));
        sb.Draw(_circleFilled, dst, null, ColorPalette.Black, 0f, Vector2.Zero, SpriteEffects.None, 0.25f);
        DrawCenterText(sb, center, power.ToString(), ColorPalette.ActualWhite);
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
