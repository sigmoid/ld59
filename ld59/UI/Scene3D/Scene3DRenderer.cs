using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.Graphics;
using Quartz.Util;
using ld59.UI.Editor.Gizmos;
using ld59.WalkingSim;

namespace ld59.UI.Scene3D;

/// <summary>
/// The render pipeline for one 3D view: shadow pass, optional skybox, the scene itself, a
/// view-local post-process chain, and the editor overlays that draw on top of the result.
/// <para>
/// Everything lands in <see cref="Output"/>, a fixed-size render target the view then blits to
/// screen. A typical frame is <c>RenderPuzzleSurfaces</c> (walk mode) -> <c>BeginScene</c> ->
/// overlays (billboards / navmesh / gizmo) -> <c>EndScene</c>.
/// </para>
/// </summary>
public sealed class Scene3DRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Scene _scene;
    private readonly int _width;
    private readonly int _height;

    private readonly RenderTarget2D _renderTarget;
    private readonly Effect _shadowEffect;
    private readonly Effect _dirShadowEffect;
    private readonly BillboardGizmoRenderer _billboards;

    private SkyboxRenderer _skybox;
    private NavMeshDebugRenderer _navDebug;
    private NavMesh _navDebugSource;

    // A post chain of its own, sized to this view's render target rather than the screen, so the 3D
    // scene can be filtered before the editor overlays (gizmo/navmesh/billboards) draw on top --
    // those need to stay crisp and unfogged. Core.PostProcessing still runs over the whole desktop
    // afterwards.
    private readonly PostProcessManager _postProcess;
    private RenderTarget2D _postTarget;
    private Effect _depthEffect;
    private float _elapsedSeconds;
    private readonly Vector2[] _depthPixel = new Vector2[1];

    /// <summary>The colour buffer the view draws to screen.</summary>
    public RenderTarget2D Output => _renderTarget;

    /// <summary>Icon quads for non-mesh entities. Shared with the pick buffer, disposed here.</summary>
    public BillboardGizmoRenderer Billboards => _billboards;

    /// <summary>The view's own post-process chain -- add scene-aware effects here.</summary>
    public PostProcessManager PostProcess => _postProcess;

    /// <summary>Exponential fog over this view. Disabled by default; scenes that want it (the
    /// walking sim) turn it on, and the <c>fog</c> console command tunes it live.</summary>
    public ExponentialFogPostProcessEffect Fog { get; }

    /// <summary>Silhouette outlines from the depth pass' entity ids. Disabled by default; tuned
    /// live with the <c>outline</c> console command.</summary>
    public OutlinePostProcessEffect Outline { get; }

    /// <summary>Screen-space ambient occlusion off the same depth pass. Disabled by default; tuned
    /// live with the <c>ssao</c> console command.</summary>
    public SSAOPostProcessEffect Ssao { get; }

    /// <summary>Procedural moon skybox (stars + Earth + Sun). Off by default; enabled per scene.</summary>
    public bool ShowSkybox { get; set; }

    /// <summary>World distance the depth pass' 1.0 encodes. Geometry beyond this reads as
    /// maximally far (same as the background), so keep it comfortably past where fog saturates --
    /// the camera's FarPlane (tens of thousands of units) would work but wastes the useful range.</summary>
    public float DepthFarDistance { get; set; } = 1000f;

    /// <summary>Force the depth pass and sample its centre pixel even when no effect needs it
    /// (the <c>depthview</c> overlay). GetData stalls the GPU, so only while the overlay is up.</summary>
    public bool SampleDepthForDebug { get; set; }

    /// <summary>Last centre-pixel depth sample: (distance / DepthFarDistance, geometry mask).</summary>
    public Vector2 LastCrosshairDepth { get; private set; }

    private List<Entity> _puzzlePanels;

    public Scene3DRenderer(GraphicsDevice device, Scene scene, int width, int height)
    {
        _device = device;
        _scene  = scene;
        _width  = width;
        _height = height;

        _renderTarget = new RenderTarget2D(
            device, width, height,
            false, SurfaceFormat.Color,
            DepthFormat.Depth24, 0,
            RenderTargetUsage.PreserveContents);

        _shadowEffect    = Core.Content.Load<Effect>("shaders/shadow-depth");
        _dirShadowEffect = Core.Content.Load<Effect>("shaders/shadow-depth-dir");
        _billboards      = new BillboardGizmoRenderer(device);

        // Registered but off: a view only pays for the depth pass and the chain once something in
        // it is actually enabled (see ApplyPostProcess). All three effects read the same depth/id
        // buffer, so turning further ones on costs no extra scene pass.
        _postProcess = new PostProcessManager(device, width, height);
        Fog = _postProcess.AddEffect<ExponentialFogPostProcessEffect>();
        Fog.Enabled = false;
        Outline = _postProcess.AddEffect<OutlinePostProcessEffect>();
        Outline.Enabled = false;
        Ssao = _postProcess.AddEffect<SSAOPostProcessEffect>();
        Ssao.Enabled = false;
    }

    /// <summary>Render each visible puzzle panel to its own texture, before the main pass samples it.</summary>
    public void RenderPuzzleSurfaces()
    {
        _puzzlePanels ??= _scene.FindEntitiesWithComponent<PuzzlePanelComponent>();
        foreach (var e in _puzzlePanels)
            if (e.Visible)
                e.GetComponent<PuzzlePanelComponent>().RenderSurface(_device, Core.SpriteBatch);
    }

    /// <summary>
    /// Shadow pass, skybox, scene, post-process. Leaves <see cref="Output"/> bound with the main
    /// pass' depth buffer intact so overlays can draw into it; finish with <see cref="EndScene"/>.
    /// </summary>
    public void BeginScene(Matrix view, Matrix proj, Vector3 cameraPos, float deltaTime)
    {
        using var _ = Profiler.Sample("3d.scene");

        // Shadow pass -- renders all 6 cube faces
        Profiler.Begin("3d.shadow");
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState   = RasterizerState.CullNone;
        _scene.DrawShadowPass(_device, _shadowEffect, _dirShadowEffect);
        Profiler.End();

        // Main scene pass
        _device.SetRenderTarget(_renderTarget);
        _device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1f, 0);
        RestoreSceneStates(_device);

        if (ShowSkybox)
        {
            Profiler.Begin("3d.skybox");
            DrawSkybox(view, proj, cameraPos);
            Profiler.End();
        }

        Profiler.Begin("3d.geometry");
        _scene.Draw3D(_device, view, proj);
        Profiler.End();

        // Fog (and any other depth-aware effect) filters the scene HERE -- after the world is
        // drawn but before the overlays, so gizmos and the navmesh stay readable at any density.
        ApplyPostProcess(view, proj, cameraPos, deltaTime);
    }

    public void EndScene() => _device.SetRenderTarget(null);

    // Procedural skybox: fills the background before the scene draws over it. Sync the sun to the
    // scene's directional light so the sky's sun matches the lighting direction.
    private void DrawSkybox(Matrix view, Matrix proj, Vector3 cameraPos)
    {
        _skybox ??= new SkyboxRenderer(_device);
        var dl = _scene.FindEntitiesWithComponent<DirectionalLightComponent>();
        if (dl.Count > 0)
            _skybox.SunDir = dl[0].GetComponent<DirectionalLightComponent>().Direction;
        _skybox.Draw(_device, view, proj, cameraPos);

        // Skybox disabled depth + set its own states; restore before the scene draws.
        _device.DepthStencilState = DepthStencilState.Default;
        _device.BlendState        = BlendState.Opaque;
        _device.RasterizerState   = RasterizerState.CullNone;
    }

    /// <summary>
    /// Icon quads for the entities with no Mesh3D geometry of their own (lights, PlayerStart), so
    /// they're visible and click-selectable in the editor. Pass the pick buffer's full pickable
    /// list -- entities that do have geometry are skipped.
    /// </summary>
    public void DrawBillboards(IReadOnlyList<Entity> pickables, Entity selected,
                               Vector3 cameraPos, Matrix view, Matrix proj)
    {
        foreach (var e in pickables)
        {
            if (!e.Visible || e.GetComponent<Mesh3DComponent>() != null) continue;
            bool isSelected = ReferenceEquals(e, selected);
            var color = BillboardColorFor(e) * (isSelected ? 1.6f : 1f);
            var pos = e.Position3D * _scene.SceneScale;
            _billboards.Draw(_device, pos, cameraPos,
                BillboardGizmoRenderer.WorldSizeFor(pos, cameraPos, proj), view, proj, color);
        }
    }

    // Icon color for a non-mesh entity's billboard: yellow for point lights, orange for the sun,
    // cyan for the spawn point.
    private static Vector4 BillboardColorFor(Entity e)
    {
        if (e.GetComponent<PointLightComponent>() != null) return new Vector4(1f, 0.9f, 0.3f, 1f);
        if (e.GetComponent<DirectionalLightComponent>() != null) return new Vector4(1f, 0.6f, 0.2f, 1f);
        return new Vector4(0.3f, 0.9f, 1f, 1f); // PlayerStart / other
    }

    /// <summary>Walkable-surface overlay. GPU buffers are rebuilt whenever a different mesh
    /// arrives (a bake), so callers just hand over whatever the walker is bound to now.</summary>
    public void DrawNavMesh(NavMesh mesh, Matrix view, Matrix proj)
    {
        if (mesh == null) return;
        if (_navDebug == null || !ReferenceEquals(_navDebugSource, mesh))
        {
            _navDebug?.Dispose();
            _navDebug = new NavMeshDebugRenderer(_device, mesh);
            _navDebugSource = mesh;
        }
        _navDebug.Draw(_device, Matrix.CreateScale(_scene.SceneScale), view, proj);
    }

    // Render the scene's linear depth, then run this view's post-process chain over the colour
    // buffer and blit the result back into it. The blit-back (rather than just handing the chain's
    // output straight to the view) is what lets the overlays keep drawing into the render target
    // afterwards with the main pass' depth buffer intact -- the target is PreserveContents, so
    // re-binding it doesn't clear colour or depth.
    private void ApplyPostProcess(Matrix view, Matrix proj, Vector3 cameraPos, float deltaTime)
    {
        _elapsedSeconds += deltaTime;

        // Nothing wants depth and nothing is enabled -> skip the extra scene draw entirely.
        bool needsDepth = _postProcess.NeedsSceneDepth || SampleDepthForDebug;
        if (!needsDepth && !_postProcess.HasEnabledEffects) return;

        if (needsDepth)
        {
            Profiler.Begin("3d.depth");
            _depthEffect ??= Core.Content.Load<Effect>("shaders/scene-depth");
            _scene.DrawDepthPass(_device, _depthEffect, _width, _height,
                view, proj, cameraPos, DepthFarDistance);
            Profiler.End();

            if (SampleDepthForDebug && _scene.DepthMap != null)
            {
                // Its own step because it is not like the others: GetData blocks the CPU until the
                // GPU has actually finished the depth pass, so this is a real pipeline stall and the
                // only draw-side number here that reflects GPU time rather than submission cost.
                Profiler.Begin("3d.depth.readback");
                _scene.DepthMap.GetData(0, new Rectangle(_width / 2, _height / 2, 1, 1), _depthPixel, 0, 1);
                LastCrosshairDepth = _depthPixel[0];
                Profiler.End();
            }
        }

        if (!_postProcess.HasEnabledEffects)
        {
            // Depth-only frame (the debug view): put the colour target back and carry on.
            _device.SetRenderTarget(_renderTarget);
            RestoreSceneStates(_device);
            return;
        }

        _postProcess.SetSceneContext(new Scene3DFrameContext
        {
            DepthMap              = needsDepth ? _scene.DepthMap : null,
            CameraPosition        = cameraPos,
            View                  = view,
            Projection            = proj,
            InverseViewProjection = Matrix.Invert(view * proj),
            FarDistance           = DepthFarDistance,
            Time                  = _elapsedSeconds,
        });

        if (_postTarget == null || _postTarget.IsDisposed)
            _postTarget = new RenderTarget2D(_device, _width, _height, false,
                SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);

        // The chain reads the render target as a texture; unbind it first (the depth pass above
        // already does when it runs, but a depth-free chain would still have it bound).
        _device.SetRenderTarget(null);

        var gameTime = new GameTime(TimeSpan.FromSeconds(_elapsedSeconds), TimeSpan.FromSeconds(deltaTime));
        Profiler.Begin("3d.post");
        _postProcess.Process(_renderTarget, _postTarget, gameTime);
        Profiler.End();

        _device.SetRenderTarget(_renderTarget);
        Core.SpriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp);
        Core.SpriteBatch.Draw(_postTarget, Vector2.Zero, Color.White);
        Core.SpriteBatch.End();

        RestoreSceneStates(_device);
    }

    // SpriteBatch leaves the device set up for 2D; the overlays that draw after the post-process
    // are 3D and need the main pass' states back.
    private static void RestoreSceneStates(GraphicsDevice device)
    {
        device.BlendState        = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState   = RasterizerState.CullNone;
        device.SamplerStates[0]  = SamplerState.LinearWrap;
    }

    public void Dispose()
    {
        _postProcess?.Dispose();
        _postTarget?.Dispose();
        _renderTarget?.Dispose();
        _navDebug?.Dispose();
        _billboards?.Dispose();
        _skybox?.Dispose();
    }
}
