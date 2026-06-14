using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;

namespace ld59.UI.Powergrid;

public enum EditTool { Select, AddNode, Connect, Delete }

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

    /// <summary>Reloads inventory/holding from the level config. Drops anything in hand.</summary>
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
                n.Discovered = false;
                n.HeldTokenCollected = false;
            }
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
        if (inside && GraphRegion.Contains(mp) && scroll != _prevScroll)
        {
            float factor = MathF.Pow(1.1f, (scroll - _prevScroll) / 120f);
            _zoom = MathHelper.Clamp(_zoom * factor, MinZoom, MaxZoom);
        }
        _prevScroll = scroll;

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

        if (!EditMode) CollectHeldTokens();
        UpdateCameraSnap(deltaTime);
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
                    node.PlacedTokenPower = 0; _handPos = mp; return true;
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
                    var hit = HitTestNode(mp);
                    if (hit != null) DeleteNode(hit);
                }
                break;
        }
    }

    private void MarkDirty() => _needsRebuild = true;

    private string UniqueNodeName()
    {
        var existing = _scene.GetEntities().Select(e => e.Name).ToHashSet();
        for (int i = 1; ; i++)
        {
            var name = $"node{i}";
            if (!existing.Contains(name)) return name;
        }
    }

    private void AddNodeAt(Vector2 world)
    {
        var entity = new Entity { Name = UniqueNodeName() };
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
        MarkDirty();
    }

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

    #endregion

    #region Drawing

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.End();

        var rasterizer = new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None };
        var origScissor = Core.GraphicsDevice.ScissorRectangle;
        Core.GraphicsDevice.ScissorRectangle = _bounds;

        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, rasterizer);

        spriteBatch.Draw(_pixel, _bounds, null, ColorPalette.ActualWhite, 0f, Vector2.Zero, SpriteEffects.None, 0f);

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
        else if (node.PlacedTokenPower > 0) DrawCenterText(sb, center, node.PlacedTokenPower.ToString(), ColorPalette.ActualWhite);

        if (node.IsAnchor) DrawRoleMarker(sb, center, diameter, _anchorMarker, "A");
        if (node.IsGoal)   DrawRoleMarker(sb, center, diameter, null, "G");
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

    private void DrawInactiveMask(SpriteBatch sb, PuzzleGraph graph)
    {
        if (graph.Nodes.Count == 0) return;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var n in graph.Nodes)
        {
            var s = WorldToScreen(n.Entity.Position);
            minX = MathF.Min(minX, s.X); minY = MathF.Min(minY, s.Y);
            maxX = MathF.Max(maxX, s.X); maxY = MathF.Max(maxY, s.Y);
        }

        float pad = NodeWorldRadius * _zoom + 16;
        var rect = new Rectangle(
            (int)(minX - pad), (int)(minY - pad),
            (int)(maxX - minX + pad * 2), (int)(maxY - minY + pad * 2));

        sb.Draw(_pixel, rect, null, new Color(0, 0, 0, 120), 0f, Vector2.Zero, SpriteEffects.None, 0.18f);

        const string label = "LOCKED";
        var size = _font.MeasureString(label);
        var center = new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        sb.DrawString(_font, label, center - size * 0.5f, ColorPalette.ActualWhite, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.17f);
    }

    private void DrawEditOverlay(SpriteBatch sb)
    {
        if (Selected != null)
            DrawRing(sb, WorldToScreen(Selected.Entity.Position), NodeWorldRadius * _zoom + 4f, ColorPalette.Black, 2f);

        if (_connectSource != null)
        {
            var from = WorldToScreen(_connectSource.Entity.Position);
            var mp = Core.GetTransformedMousePoint();
            DrawLine(sb, from, new Vector2(mp.X, mp.Y), 2f, Color.Gray);
        }
    }

    private void DrawPlayOverlay(SpriteBatch sb)
    {
        DrawInventoryBar(sb);

        // Solved banner when every puzzle that has a goal is solved.
        var withGoals = _controller.Graphs.Where(g => g.Nodes.Any(n => n.IsGoal)).ToList();
        if (withGoals.Count > 0 && withGoals.All(g => g.IsSolved))
        {
            const string msg = "SOLVED";
            var size = _font.MeasureString(msg) * 1.5f;
            var pos = new Vector2(GraphRegion.X + (GraphRegion.Width - size.X) / 2f, GraphRegion.Y + 12);
            sb.DrawString(_font, msg, pos, ColorPalette.DarkGreen, 0f, Vector2.Zero, 1.5f, SpriteEffects.None, 0.2f);
        }

        if (Dragging) DrawToken(sb, new Vector2(_handPos.X, _handPos.Y), SlotSize * 0.5f, _handPower);
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
