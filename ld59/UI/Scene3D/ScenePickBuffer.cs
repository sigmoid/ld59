using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using ld59.UI.Editor.Gizmos;
using ld59.WalkingSim;

namespace ld59.UI.Scene3D;

/// <summary>
/// ID-buffer object picking for a 3D view, in both of its flavours:
/// <list type="bullet">
/// <item>Walk mode -- render every mesh in its interactable id colour, read the crosshair pixel,
/// and track what the player is looking at (<see cref="Hovered"/>).</item>
/// <item>Editor mode -- render every pickable entity in a unique id colour and read the pixel under
/// the cursor to resolve a click into an entity (<see cref="PickEntity"/>).</item>
/// </list>
/// Also owns the cached entity tables both passes iterate; call <see cref="Invalidate"/> whenever
/// the scene's entity list changes.
/// </summary>
public sealed class ScenePickBuffer : IDisposable
{
    private const int IdWidth  = 160;
    private const int IdHeight = 90;

    // Draw-time highlight multiplier for the object under the crosshair (see
    // Mesh3DComponent.HighlightFactor). Editor selection and walk-mode hover never apply at once,
    // so a single factor per mesh suffices.
    private const float HoverHighlight = 1.6f;

    private sealed class InteractInfo
    {
        public Entity Entity;
        public Interactable3DComponent Comp;
        public Mesh3DComponent Mesh;
    }

    private readonly Scene _scene;
    private readonly BillboardGizmoRenderer _billboards;
    private readonly Color[] _idPixel = new Color[1];

    private RenderTarget2D _idTarget;
    private Effect _idEffect;
    private int _pickFrame;
    private bool _tablesBuilt;
    private List<InteractInfo> _interactables;
    private List<Entity> _meshEntities;
    private InteractInfo _hovered;

    /// <param name="billboards">Shared with the renderer: non-mesh entities (lights, PlayerStart)
    /// are picked against the same camera-facing quad that's drawn for them, so what you click is
    /// what you see. Owned (and disposed) by the renderer.</param>
    public ScenePickBuffer(Scene scene, BillboardGizmoRenderer billboards)
    {
        _scene      = scene;
        _billboards = billboards;
    }

    /// <summary>The interactable under the crosshair, or null.</summary>
    public Interactable3DComponent Hovered => _hovered?.Comp;
    public string HoveredName => _hovered?.Entity.Name;

    /// <summary>Id sampled at screen centre on the last pick frame (debug readout).</summary>
    public int LastCrosshairId { get; private set; }

    /// <summary>The raw id buffer, for the <c>idview</c> debug overlay.</summary>
    public RenderTarget2D DebugTarget => _idTarget;

    /// <summary>
    /// Every entity the editor can click-select: all Mesh3D geometry, plus the non-mesh entities
    /// the viewport draws billboards for. Rebuilt lazily after <see cref="Invalidate"/>.
    /// </summary>
    public IReadOnlyList<Entity> Pickables
    {
        get { BuildTables(); return _meshEntities; }
    }

    /// <summary>Drop the cached entity tables -- the scene's entity list changed.</summary>
    public void Invalidate() => _tablesBuilt = false;

    /// <summary>Clear the hover tint (leaving the view, closing the window).</summary>
    public void ClearHover()
    {
        if (_hovered?.Mesh != null) _hovered.Mesh.HighlightFactor = 1f;
        _hovered = null;
    }

    // ── Walk mode ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Render the ID buffer and read the crosshair pixel every other frame (GetData forces a GPU
    /// sync). Every Mesh3D entity is drawn in its flat id colour; interactables get a non-zero red
    /// channel, everything else black -- so occlusion is correct via depth.
    /// </summary>
    public void UpdateHover(GraphicsDevice device, Matrix view, Matrix proj, bool debugView)
    {
        EnsureResources(device);
        BuildTables();

        if ((_pickFrame++ & 1) == 0)
        {
            BeginIdPass(device, view, proj, Color.Black);

            foreach (var entity in _meshEntities)
            {
                if (!entity.Visible) continue; // hidden entities are neither drawn nor pickable
                int id = entity.GetComponent<Interactable3DComponent>()?.PickId ?? 0;
                _idEffect.Parameters["IdColor"].SetValue(new Vector4(id / 255f, 0f, 0f, 1f));
                entity.DrawDepth(device, _idEffect, _scene.SceneScale);
            }

            device.SetRenderTarget(null);
            _idTarget.GetData(0, new Rectangle(IdWidth / 2, IdHeight / 2, 1, 1), _idPixel, 0, 1);
            LastCrosshairId = _idPixel[0].R;
            ResolveHover(_idPixel[0].R);
        }

        // Debug: repaint the ID buffer in high-contrast colours for the on-screen overlay. Runs
        // after the real pick read (which needs the true id colours), so it only affects display.
        if (debugView) RenderIdDebug(device, view, proj);
    }

    private void ResolveHover(int id)
    {
        InteractInfo target = null;
        if (id >= 1 && id <= _interactables.Count)
        {
            var cand = _interactables[id - 1];
            // Measure from the camera eye (not the floor) to the entity origin, against the
            // interactable's own range. The crosshair id-test already proved line-of-sight with
            // correct occlusion, so this is only a "close enough" gate. Origin-distance is still a
            // crude proxy: a large/elevated object whose pivot sits far from its visible surface
            // needs a larger per-object range (set InteractRange in the Inspector).
            if (cand.Entity.Visible && Walker != null &&
                Vector3.Distance(Walker.EyePosition, cand.Entity.Position3D) <= cand.Comp.InteractRange)
                target = cand;
        }

        if (target == _hovered) return;

        // clear the previously-hovered mesh's highlight, then tint the new one (draw-time only)
        if (_hovered?.Mesh != null) _hovered.Mesh.HighlightFactor = 1f;
        if (target?.Mesh != null)   target.Mesh.HighlightFactor   = HoverHighlight;
        _hovered = target;
    }

    /// <summary>The walker whose eye position gates interact range. Set by the owning view.</summary>
    public WalkController Walker { get; set; }

    // Re-render the mesh entities into the id target with bright, human-readable colours for the
    // debug overlay: grey = plain mesh, green = interactable, yellow = the one currently hovered.
    private void RenderIdDebug(GraphicsDevice device, Matrix view, Matrix proj)
    {
        BeginIdPass(device, view, proj, new Color(18, 18, 26));

        foreach (var entity in _meshEntities)
        {
            if (!entity.Visible) continue;
            var comp = entity.GetComponent<Interactable3DComponent>();
            Vector4 col;
            if (comp == null)
                col = new Vector4(0.20f, 0.20f, 0.24f, 1f);                       // plain mesh: grey
            else if (_hovered != null && ReferenceEquals(_hovered.Entity, entity))
                col = new Vector4(1f, 1f, 0.15f, 1f);                             // hovered: yellow
            else
                col = new Vector4(0.15f, 1f, 0.35f, 1f);                          // interactable: green
            _idEffect.Parameters["IdColor"].SetValue(col);
            entity.DrawDepth(device, _idEffect, _scene.SceneScale);
        }

        device.SetRenderTarget(null);
    }

    // ── Editor mode ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolve a viewport click into an entity: render the ID buffer with each pickable entity in a
    /// unique colour and read the pixel under the cursor. Returns null when nothing was hit.
    /// </summary>
    public Entity PickEntity(Point cursor, in RtViewport vp, Matrix view, Matrix proj, Vector3 cameraPos)
    {
        var device = Core.GraphicsDevice;
        EnsureResources(device);
        BuildTables();

        BeginIdPass(device, view, proj, Color.Black);

        // Encode the 1-based entity index across R/G/B (24 bits, ~16M ids) so the pick isn't
        // capped at 255 entities. Background clears to 0 = "nothing".
        int count = Math.Min(_meshEntities.Count, 0xFFFFFF);
        for (int i = 0; i < count; i++)
        {
            var e = _meshEntities[i];
            if (!e.Visible) continue;
            if (e.GetComponent<Mesh3DComponent>() != null)
            {
                device.DepthStencilState = DepthStencilState.Default;
                _idEffect.Parameters["IdColor"].SetValue(EncodeId(i + 1));
                e.DrawDepth(device, _idEffect, _scene.SceneScale);
            }
            else
            {
                // Non-mesh entity (light, PlayerStart): stand-in geometry for picking is a
                // camera-facing billboard, same size and place as what's drawn for it in the main
                // pass. Sizing it on screen rather than in world units also keeps a distant light
                // several texels wide in this (deliberately small) buffer, instead of collapsing to
                // sub-pixel and letting the click fall through to whatever is behind it.
                var pos = e.Position3D * _scene.SceneScale;
                _billboards.Draw(device, pos, cameraPos,
                    BillboardGizmoRenderer.WorldSizeFor(pos, cameraPos, proj), view, proj,
                    EncodeId(i + 1));
            }
        }
        device.SetRenderTarget(null);

        Vector2 uv = vp.ToUv(cursor);
        _idTarget.GetData(0, new Rectangle((int)(uv.X * IdWidth), (int)(uv.Y * IdHeight), 1, 1), _idPixel, 0, 1);

        int id = DecodeId(_idPixel[0]);
        return id >= 1 && id <= count ? _meshEntities[id - 1] : null;
    }

    // Pack a 24-bit id into R/G/B (little end in R). Alpha=1 so the pixel is written opaque.
    private static Vector4 EncodeId(int id) => new Vector4(
        (id         & 0xFF) / 255f,
        ((id >>  8) & 0xFF) / 255f,
        ((id >> 16) & 0xFF) / 255f,
        1f);

    private static int DecodeId(Color c) => c.R | (c.G << 8) | (c.B << 16);

    // ── Shared plumbing ─────────────────────────────────────────────────────────────────────

    private void BeginIdPass(GraphicsDevice device, Matrix view, Matrix proj, Color clear)
    {
        device.SetRenderTarget(_idTarget);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, clear, 1f, 0);
        device.BlendState        = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState   = RasterizerState.CullNone;

        _idEffect.CurrentTechnique = _idEffect.Techniques["IdColor"];
        _idEffect.Parameters["LightViewProjection"].SetValue(view * proj);
    }

    private void EnsureResources(GraphicsDevice device)
    {
        _idEffect ??= Core.Content.Load<Effect>("shaders/id-color");
        if (_idTarget == null || _idTarget.IsDisposed)
            _idTarget = new RenderTarget2D(device, IdWidth, IdHeight, false,
                SurfaceFormat.Color, DepthFormat.Depth24);
    }

    private void BuildTables()
    {
        if (_tablesBuilt) return;
        _tablesBuilt = true;

        _interactables = new List<InteractInfo>();
        foreach (var e in _scene.FindEntitiesWithComponent<Interactable3DComponent>())
        {
            var comp = e.GetComponent<Interactable3DComponent>();
            comp.PickId = _interactables.Count + 1; // 1..255, encoded in the red channel
            _interactables.Add(new InteractInfo
            {
                Entity = e,
                Comp = comp,
                Mesh = e.GetComponent<Mesh3DComponent>(),
            });
        }

        _meshEntities = _scene.FindEntitiesWithComponent<Mesh3DComponent>();

        // Append non-mesh entities the editor should still be able to click-select in the
        // viewport (PickEntity draws these as billboards instead of real geometry). Harmless
        // for Walk-mode hover picking too -- Entity.DrawDepth no-ops for an entity with no
        // Mesh3DComponent, so these extra entries never affect gameplay hover/interact.
        var seen = new HashSet<Entity>(_meshEntities);
        foreach (var e in _scene.FindEntitiesWithComponent<PointLightComponent>())
            if (seen.Add(e)) _meshEntities.Add(e);
        foreach (var e in _scene.FindEntitiesWithComponent<DirectionalLightComponent>())
            if (seen.Add(e)) _meshEntities.Add(e);
        var spawn = _scene.FindEntityByName("PlayerStart");
        if (spawn != null && seen.Add(spawn)) _meshEntities.Add(spawn);
    }

    public void Dispose() => _idTarget?.Dispose();
}
