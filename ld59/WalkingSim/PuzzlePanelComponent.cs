using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.Components;
using ld59.UI.Powergrid;

namespace ld59.WalkingSim;

// Turns an entity into a powergrid-puzzle object. The entity should also carry an
// Interactable3DComponent (so it's hoverable/E-triggerable); on interact the walking sim opens
// a focused solve view (PuzzleSolveOverlay) on this puzzle's scene. When solved, the specific
// solution is matched against Outcomes ("which-solution mapping") and the matched effect fires
// through InteractionDispatcher.
//
// Scene XML: Type="PuzzlePanel" with:
//   LevelPath  the powergrid scene, e.g. "files/scenes/powergrid/basic-1.xml"
//   Outcomes   a mini-DSL mapping a solution to an effect (see ParseOutcomes):
//     "Gate=Sun&Lever=Moon=>reveal:Bridge | Gate=Moon=>reveal:Pit | =>show-text::Solved!"
//   PanelWidth/PanelHeight  the on-object surface size in pixels (Phase 3 render)
public class PuzzlePanelComponent : Component
{
    public string LevelPath   { get; set; } = "";
    public string Outcomes    { get; set; } = "";
    public int    PanelWidth  { get; set; } = 1024;   // surface render-target resolution
    public int    PanelHeight { get; set; } = 640;
    public float  PanelSize   { get; set; } = 2.0f;   // world width of the panel (meters)
    public float  PanelYOffset { get; set; } = 1.0f;  // panel centre above the entity origin
    public float  PanelYaw    { get; set; } = 0f;     // facing, degrees around Y (0 = normal +Z)

    // Set while the focused solve overlay is open, so the surface render freezes (the shared
    // PowergridView is being drawn at screen bounds, not the panel's).
    public bool SurfaceSuspended { get; set; }

    public struct Outcome
    {
        public Dictionary<string, string> Constraints; // node name -> required rune (all must match)
        public string Action;
        public string Target;
        public string Message;
    }

    private List<Outcome> _parsed;
    private PowergridView _view;
    private RenderTarget2D _surface;
    private Effect _quadEffect;
    private readonly VertexPositionTexture[] _quad = new VertexPositionTexture[4];

    // Lazily load the puzzle scene + view (needs GraphicsDevice/Content, so not at load time).
    public PowergridView GetOrCreateView(Rectangle bounds)
    {
        if (_view == null)
        {
            var scene = Scene.FromFile(Core.Content, LevelPath);
            // In-world panels are player-facing: no auto-solver.
            _view = new PowergridView(bounds, scene) { ShowSolver = false };
        }
        else
        {
            _view.SetBounds(bounds);
        }
        return _view;
    }

    public bool HasView => _view != null;

    // Render the (view-only) puzzle to the panel's render target. Called from UI3DScene's
    // offscreen pass before the main 3D pass, unless the focused overlay has it suspended.
    public void RenderSurface(GraphicsDevice device, SpriteBatch spriteBatch)
    {
        if (SurfaceSuspended) return;

        _surface ??= new RenderTarget2D(device, PanelWidth, PanelHeight, false,
            SurfaceFormat.Color, DepthFormat.Depth24);

        var view = GetOrCreateView(new Rectangle(0, 0, PanelWidth, PanelHeight));
        view.SetBounds(new Rectangle(0, 0, PanelWidth, PanelHeight));

        var prevTargets = device.GetRenderTargets();
        device.SetRenderTarget(_surface);
        device.Clear(new Color(18, 20, 28));
        spriteBatch.Begin();
        view.Draw(spriteBatch);   // PowergridView manages its own scissor sub-batch internally
        spriteBatch.End();
        device.SetRenderTargets(prevTargets);
    }

    public override void Draw3D(GraphicsDevice device, Matrix view, Matrix projection, SceneLightData lights)
    {
        if (_surface == null) return;
        _quadEffect ??= Core.Content.Load<Effect>("shaders/textured-quad");

        // Fixed, world-mounted orientation (part of the level) — the panel faces PanelYaw.
        var center  = Entity.Position3D + new Vector3(0, PanelYOffset, 0);
        float yaw   = MathHelper.ToRadians(PanelYaw);
        var forward = new Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw)); // panel normal (readable side)
        var right   = Vector3.Normalize(Vector3.Cross(Vector3.Up, forward));
        var up      = Vector3.Up;

        float halfW = PanelSize * 0.5f;
        float halfH = halfW * PanelHeight / PanelWidth;   // keep the RT aspect
        var tl = center - right * halfW + up * halfH;
        var tr = center + right * halfW + up * halfH;
        var br = center + right * halfW - up * halfH;
        var bl = center - right * halfW - up * halfH;

        _quad[0] = new VertexPositionTexture(tl, new Vector2(0, 0));
        _quad[1] = new VertexPositionTexture(tr, new Vector2(1, 0));
        _quad[2] = new VertexPositionTexture(br, new Vector2(1, 1));
        _quad[3] = new VertexPositionTexture(bl, new Vector2(0, 1));

        _quadEffect.Parameters["World"].SetValue(Matrix.Identity);
        _quadEffect.Parameters["View"].SetValue(view);
        _quadEffect.Parameters["Projection"].SetValue(projection);
        _quadEffect.Parameters["ScreenTexture"].SetValue(_surface);

        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState   = RasterizerState.CullNone;
        foreach (var pass in _quadEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleList,
                new[] { _quad[0], _quad[1], _quad[2], _quad[0], _quad[2], _quad[3] }, 0, 2);
        }
    }

    public override void Dispose(bool disposing)
    {
        _surface?.Dispose();
    }

    // Read the current placement as {node name -> rune}.
    public Dictionary<string, string> ReadSolution()
    {
        var sol = new Dictionary<string, string>();
        if (_view?.Controller == null) return sol;
        foreach (var graph in _view.Controller.Graphs)
            foreach (var node in graph.Nodes)
                if (node.Entity != null && !string.IsNullOrEmpty(node.Rune))
                    sol[node.Entity.Name] = node.Rune;
        return sol;
    }

    public bool IsSolved => _view?.Controller?.IsLevelSolved == true;

    // First outcome whose constraints all match the solution, or null.
    public Outcome? Match(Dictionary<string, string> solution)
    {
        foreach (var o in ParsedOutcomes())
        {
            bool ok = true;
            foreach (var kv in o.Constraints)
                if (!solution.TryGetValue(kv.Key, out var rune) || rune != kv.Value) { ok = false; break; }
            if (ok) return o;
        }
        return null;
    }

    private List<Outcome> ParsedOutcomes()
    {
        if (_parsed == null) _parsed = ParseOutcomes(Outcomes);
        return _parsed;
    }

    // "constraints => action:target:message | constraints => ..."  (constraints: "node=rune&node=rune", may be empty)
    public static List<Outcome> ParseOutcomes(string dsl)
    {
        var list = new List<Outcome>();
        if (string.IsNullOrWhiteSpace(dsl)) return list;

        foreach (var rawRule in dsl.Split('|'))
        {
            var rule = rawRule.Trim();
            if (rule.Length == 0) continue;

            int arrow = rule.IndexOf("=>");
            if (arrow < 0) continue;
            string condPart = rule.Substring(0, arrow).Trim();
            string effPart  = rule.Substring(arrow + 2).Trim();

            var constraints = new Dictionary<string, string>();
            foreach (var c in condPart.Split('&'))
            {
                var t = c.Trim();
                if (t.Length == 0) continue;
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                constraints[t.Substring(0, eq).Trim()] = t.Substring(eq + 1).Trim();
            }

            var eff = effPart.Split(':');
            list.Add(new Outcome
            {
                Constraints = constraints,
                Action  = eff.Length > 0 ? eff[0].Trim() : "",
                Target  = eff.Length > 1 ? eff[1].Trim() : "",
                Message = eff.Length > 2 ? string.Join(":", eff, 2, eff.Length - 2).Trim() : "",
            });
        }
        return list;
    }
}
