using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.Components;
using ld59.UI.Editor.Commands;

namespace ld59.UI.Editor.Gizmos;

public enum GizmoMode { None, Translate, Rotate, Scale }
public enum GizmoAxis { None, X, Y, Z, All }   // All = uniform (scale gizmo's center box)

// On-screen move/rotate/scale handles for the selected entity. One handle box per world axis,
// reused across all three modes (only color/behavior differs) to keep the geometry and picking
// code in one place. Handles are picked through the same ID-buffer mechanism as entities, using
// three reserved ids far above any realistic entity count so there's no collision risk.
//
// Translate drags project the mouse ray onto a plane containing the axis and facing the camera
// (standard technique -- this is ray-vs-plane math, not scene raycasting, so it doesn't need any
// mesh/navmesh intersection infrastructure). Scale reuses the same projection, mapping world-unit
// drag distance onto a Scale component. Rotate is intentionally simpler: horizontal mouse-pixel
// delta while the handle is held maps to a rotation delta around that axis -- a common simplified
// fallback where a full rotation-ring drag isn't implemented.
public sealed class TransformGizmo : IDisposable
{
    private readonly GizmoRenderer _renderer;

    public GizmoMode Mode { get; set; } = GizmoMode.Translate;

    // Fraction of the viewport HEIGHT the gizmo's reach occupies. Because the handle length is
    // derived from this and the camera distance, the gizmo covers the same number of screen pixels
    // whether the selection is at arm's length or across a level -- so a distant object's handles
    // stay just as grabbable as a near one's. (0.11 reproduces how the gizmo used to look at close
    // range, when its length was a flat 0.09 per unit of distance under the default 45 FOV.)
    private const float ScreenHeightFraction = 0.11f;
    private const float MinHandleLength = 1e-3f;    // only guards a camera sitting exactly on the origin
    private const float RotateSensitivity = 0.01f;  // radians per pixel of horizontal mouse delta
    private const float ScaleSensitivity = 0.1f;    // scale units per world unit of drag
    private const float UniformScaleSensitivity = 0.01f; // scale units per pixel of horizontal drag
    private const float ScaleMin = 0.01f;

    public bool IsDragging => _dragAxis != GizmoAxis.None;

    /// <summary>
    /// The handle the cursor is currently over, pushed in by the view each frame (it owns the
    /// cursor and the camera matrices). Drawn brighter so you can tell which axis a click will
    /// grab before committing to the drag -- the pick tolerances are generous, so which handle
    /// wins isn't always obvious from the cursor position alone.
    /// </summary>
    public GizmoAxis HoverAxis { get; set; } = GizmoAxis.None;

    // How far a hovered handle is washed toward white. Lerping rather than multiplying keeps the
    // already-bright axis colours from just clipping to the same white.
    private const float HoverLerp = 0.45f;

    private GizmoAxis _dragAxis = GizmoAxis.None;
    private object _dragTarget;
    private PropertyInfo _dragProperty;
    private Vector3 _dragOldValue;
    private Vector3 _dragStartValue;
    private Vector3 _dragOrigin;      // entity position at drag start (anchor for the plane)
    private Vector3 _dragPlaneNormal;
    private Vector3 _dragStartHit;     // world hit point on the drag plane at drag start
    private Point _dragLastMouse;
    private Point _dragStartMouse;     // mouse at drag start (uniform scale is measured from here)

    public TransformGizmo(GraphicsDevice device)
    {
        _renderer = new GizmoRenderer(device);
    }

    // Rotate/Scale need a Mesh3DComponent (RotationEuler/Scale live there); Translate only needs
    // the entity itself (Position3D). Used to decide whether to draw/interact with the gizmo at all.
    public bool HasValidTarget(Entity entity) => Mode switch
    {
        GizmoMode.None      => false,
        GizmoMode.Translate => entity != null,
        GizmoMode.Rotate    => entity?.GetComponent<Mesh3DComponent>() != null,
        GizmoMode.Scale     => entity?.GetComponent<Mesh3DComponent>() != null,
        _ => false,
    };

    private (object target, PropertyInfo prop, Vector3 value) ResolveTarget(Entity entity) => Mode switch
    {
        GizmoMode.Translate => (entity, typeof(Entity).GetProperty(nameof(Entity.Position3D)), entity.Position3D),
        GizmoMode.Rotate    => ResolveMeshProp(entity, nameof(Mesh3DComponent.RotationEuler)),
        GizmoMode.Scale     => ResolveMeshProp(entity, nameof(Mesh3DComponent.Scale)),
        _ => (null, null, Vector3.Zero),
    };

    private static (object, PropertyInfo, Vector3) ResolveMeshProp(Entity entity, string propName)
    {
        var mesh = entity.GetComponent<Mesh3DComponent>();
        var prop = typeof(Mesh3DComponent).GetProperty(propName);
        return (mesh, prop, (Vector3)prop.GetValue(mesh));
    }

    // ── geometry ─────────────────────────────────────────────────────────────────
    private static Vector3 AxisVector(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => Vector3.UnitX,
        GizmoAxis.Y => Vector3.UnitY,
        GizmoAxis.Z => Vector3.UnitZ,
        _ => Vector3.Zero,
    };

    private static Vector4 AxisColor(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => new Vector4(1f, 0.2f, 0.2f, 1f),
        GizmoAxis.Y => new Vector4(0.2f, 1f, 0.2f, 1f),
        GizmoAxis.Z => new Vector4(0.3f, 0.5f, 1f, 1f),
        GizmoAxis.All => new Vector4(0.9f, 0.9f, 0.9f, 1f),   // uniform = light gray
        _ => Vector4.One,
    };

    private static readonly GizmoAxis[] Axes = { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z };

    // Draw colour for one handle: its axis colour, brightened while dragged and washed toward white
    // while hovered (drag wins, so the held axis doesn't dim if the cursor wanders onto another).
    private Vector4 HandleColor(GizmoAxis axis)
    {
        var color = AxisColor(axis);
        if (_dragAxis == axis) return color * (axis == GizmoAxis.All ? 1.4f : 1.5f);
        if (!IsDragging && HoverAxis == axis) return Vector4.Lerp(color, Vector4.One, HoverLerp);
        return color;
    }

    // Constant on-screen size: the world height a perspective camera sees at distance d is
    // 2*d*tan(fovY/2), so making the handle that height times a fixed fraction pins its projected
    // length to the same share of the viewport at every distance. tan(fovY/2) comes straight out of
    // the projection matrix (M22 = 1/tan(fovY/2)), so this tracks the caller's FOV automatically.
    private static float HandleLength(Vector3 origin, Vector3 cameraPos, Matrix proj)
    {
        float dist = Vector3.Distance(origin, cameraPos);
        float tanHalfFov = proj.M22 > 1e-6f ? 1f / proj.M22 : 0.41421f;   // fall back to 45 vertical FOV
        return MathF.Max(dist * 2f * tanHalfFov * ScreenHeightFraction, MinHandleLength);
    }

    // Rotation that points the models' native forward axis onto the target world axis. The FBX
    // geometry points +Z, but MonoGame's importer converts Z-up -> Y-up, so in the BUILT model the
    // arrow/scale handles point +Y and the rotate ring's normal is +Y. The same rotation orients
    // all three gizmo types for a given axis.
    private static Matrix AxisRotation(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => Matrix.CreateRotationZ(-MathHelper.PiOver2),  // +Y -> +X
        GizmoAxis.Z => Matrix.CreateRotationX(MathHelper.PiOver2),   // +Y -> +Z
        _           => Matrix.Identity,                             // +Y (native)
    };

    // World transform for one axis' handle model: normalize the model to a consistent size, rotate
    // it onto the axis, optionally spin it about that axis (used to draw the rotate half-ring model
    // twice -- 0 and 180 -- so it reads and picks as a full ring), then place it at the origin.
    private Matrix HandleModelWorld(GizmoAxis axis, Vector3 origin, float len, float spin)
    {
        float s = len / _renderer.ReachFor(Mode);
        Matrix spinM = spin != 0f ? Matrix.CreateFromAxisAngle(AxisVector(axis), spin) : Matrix.Identity;
        return Matrix.CreateScale(s) * AxisRotation(axis) * spinM * Matrix.CreateTranslation(origin);
    }

    // Fallback thin box (used only if a gizmo model failed to load).
    private static Matrix HandleBoxWorld(GizmoAxis axis, Vector3 origin, float len)
    {
        float thick = len * 0.12f;
        Vector3 scale = axis switch
        {
            GizmoAxis.X => new Vector3(len, thick, thick),
            GizmoAxis.Y => new Vector3(thick, len, thick),
            GizmoAxis.Z => new Vector3(thick, thick, len),
            _ => Vector3.One,
        };
        return Matrix.CreateScale(scale) * Matrix.CreateTranslation(origin + AxisVector(axis) * (len * 0.5f));
    }

    // ── screen-space ray picking (CPU) ────────────────────────────────────────────
    // This is a raycast in the practical sense: pure math against the projected handle geometry (a
    // screen-space capsule test), evaluated in Update at the exact click position. No GPU readback -> no
    // frame-lag and no render-target timing hazards (what made the id-buffer attempts select behind).
    //
    // Grab tolerance FLOOR in render-target pixels -- used for small/far handles.
    public const float GrabPixels = 18f;

    // The tolerance also scales with the handle's on-screen length: a handle drawn large (camera close)
    // must be grabbable across its full visible width, and its arrow is fat. A fixed pixel tolerance is
    // too thin next to a big arrow, which let clicks on the clearly-visible handle fall through and
    // select the object behind. The grab tolerance is max(GrabPixels, onScreenLength * ThicknessFrac).
    // Deliberately generous: the selected entity's gizmo should win over selecting whatever is behind it.
    private const float ThicknessFrac = 0.22f;

    private static float ToleranceFor(float projectedLenPx) => MathF.Max(GrabPixels, projectedLenPx * ThicknessFrac);

    // Return the axis whose handle the cursor is closest to (relative to that handle's tolerance), or
    // None. `bestPixels` is the winner's pixel miss distance (diagnostics). Picking is screen-space;
    // dragging uses the world ray (BeginDrag/UpdateDrag) built from the SAME cursor, so they agree.
    public GizmoAxis PickAxis(Entity entity, Vector3 cameraPos, Vector2 cursor,
        Matrix view, Matrix proj, Viewport viewport, out float bestPixels)
    {
        var (_, _, value) = ResolveTarget(entity);
        Vector3 origin = Mode == GizmoMode.Translate ? value : entity.Position3D;
        float len = HandleLength(origin, cameraPos, proj);
        float pickLen = len * _renderer.TipFor(Mode) / _renderer.ReachFor(Mode);
        Matrix vp = view * proj;

        GizmoAxis best = GizmoAxis.None;
        float bestScore = 1f;   // normalised miss = distance / tolerance; a hit is < 1, smaller is closer
        bestPixels = GrabPixels;

        // Uniform-scale center box: distance from the cursor to the projected origin.
        if (Mode == GizmoMode.Scale && Project(origin, vp, viewport, out var oc))
        {
            float d = Vector2.Distance(cursor, oc);
            float score = d / GrabPixels;
            if (score < bestScore) { bestScore = score; best = GizmoAxis.All; bestPixels = d; }
        }

        foreach (var axis in Axes)
        {
            Vector3 dir = AxisVector(axis);
            float d, tol;
            if (Mode == GizmoMode.Rotate)
            {
                d = RingPixelDistance(origin, dir, len, cursor, vp, viewport, out float ringRadiusPx);
                tol = ToleranceFor(ringRadiusPx);
            }
            else
            {
                if (!Project(origin, vp, viewport, out var o2) ||
                    !Project(origin + dir * pickLen, vp, viewport, out var t2)) continue;
                d = PointSegmentDistance2D(cursor, o2, t2);
                tol = ToleranceFor(Vector2.Distance(o2, t2));
            }
            float score = d / tol;
            if (score < bestScore) { bestScore = score; best = axis; bestPixels = d; }
        }
        return best;
    }

    // Project a world point to render-target pixels. False if on/behind the camera plane (w <= 0).
    private static bool Project(Vector3 world, Matrix viewProj, Viewport vp, out Vector2 screen)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
        if (clip.W <= 1e-4f) { screen = Vector2.Zero; return false; }
        float nx = clip.X / clip.W, ny = clip.Y / clip.W;
        screen = new Vector2(vp.X + (nx * 0.5f + 0.5f) * vp.Width,
                             vp.Y + (1f - (ny * 0.5f + 0.5f)) * vp.Height);
        return true;
    }

    // Pixel distance from the cursor to the projected rotate ring (radius `len`, plane normal `axis`),
    // measured against the sampled screen polyline so foreshortening is handled. Also outputs the ring's
    // on-screen radius (projected origin -> first ring point) so the tolerance can scale with it.
    private static float RingPixelDistance(Vector3 origin, Vector3 axis, float len, Vector2 cursor,
        Matrix vp, Viewport view, out float ringRadiusPx)
    {
        Vector3 u = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Y) > 0.99f ? Vector3.UnitX : Vector3.UnitY));
        Vector3 v = Vector3.Cross(axis, u);
        const int N = 48;
        float best = float.PositiveInfinity;
        bool ocOk = Project(origin, vp, view, out var oc);
        Vector3 prevW = origin + u * len;
        bool prevOk = Project(prevW, vp, view, out var prev);
        ringRadiusPx = ocOk && prevOk ? Vector2.Distance(oc, prev) : len;
        for (int i = 1; i <= N; i++)
        {
            float a = MathHelper.TwoPi * i / N;
            Vector3 curW = origin + (u * MathF.Cos(a) + v * MathF.Sin(a)) * len;
            bool curOk = Project(curW, vp, view, out var cur);
            if (prevOk && curOk) best = MathF.Min(best, PointSegmentDistance2D(cursor, prev, cur));
            prev = cur; prevOk = curOk;
        }
        return best;
    }

    private static float PointSegmentDistance2D(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.LengthSquared();
        float t = len2 < 1e-6f ? 0f : MathHelper.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    // ── drawing ──────────────────────────────────────────────────────────────────
    public void Draw(GraphicsDevice device, Entity entity, Vector3 cameraPos, Matrix view, Matrix proj)
    {
        var (_, _, value) = ResolveTarget(entity);
        Vector3 origin = Mode == GizmoMode.Translate ? value : entity.Position3D;
        float len = HandleLength(origin, cameraPos, proj);
        var model = _renderer.ModelFor(Mode);

        // Clear the depth buffer so the gizmo sits on top of the scene, but draw depth-tested so
        // a solid handle model resolves its own front/back faces correctly.
        device.Clear(ClearOptions.DepthBuffer, Color.Black, 1f, 0);

        foreach (var axis in Axes)
            DrawHandle(device, model, axis, origin, len, view, proj, HandleColor(axis));

        // Scale gizmo: a center box that scales all three axes at once.
        if (Mode == GizmoMode.Scale)
            DrawUniformBox(device, origin, len, view, proj, HandleColor(GizmoAxis.All));
    }

    // The uniform-scale handle: a small cube at the gizmo origin.
    private void DrawUniformBox(GraphicsDevice device, Vector3 origin, float len, Matrix view, Matrix proj, Vector4 color)
    {
        var world = Matrix.CreateScale(len * 0.28f) * Matrix.CreateTranslation(origin);
        _renderer.DrawBox(device, world, view, proj, color, depthTest: true);
    }

    private void DrawHandle(GraphicsDevice device, Model model, GizmoAxis axis,
        Vector3 origin, float len, Matrix view, Matrix proj, Vector4 color)
    {
        if (model == null)
        {
            _renderer.DrawBox(device, HandleBoxWorld(axis, origin, len), view, proj, color, depthTest: false);
            return;
        }

        _renderer.DrawModel(device, model, HandleModelWorld(axis, origin, len, 0f), view, proj, color, depthTest: true);
        // The rotate model is a half-ring; draw a 180°-spun copy so it forms (and picks as) a full ring.
        if (Mode == GizmoMode.Rotate)
            _renderer.DrawModel(device, model, HandleModelWorld(axis, origin, len, MathHelper.Pi), view, proj, color, depthTest: true);
    }

    // ── pick debug ────────────────────────────────────────────────────────────────
    // When on (toggled with F4 in the editor), UI3DScene shows a text readout of what PickAxis reports
    // under the cursor. No 3D overlay -- just the HUD text.
    public bool ShowPickDebug;

    // ── dragging ─────────────────────────────────────────────────────────────────
    public void BeginDrag(GizmoAxis axis, Entity entity, Vector3 cameraPos, Ray mouseRay, Point mousePos)
    {
        var (target, prop, value) = ResolveTarget(entity);
        if (target == null) return;

        _dragAxis = axis;
        _dragTarget = target;
        _dragProperty = prop;
        _dragOldValue = value;
        _dragStartValue = value;
        _dragOrigin = entity.Position3D;
        _dragLastMouse = mousePos;
        _dragStartMouse = mousePos;

        // Uniform scale is measured from mouse delta, not a drag plane -- nothing more to set up.
        if (axis == GizmoAxis.All)
        {
            _dragPlaneNormal = Vector3.UnitY;
            _dragStartHit = _dragOrigin;
            return;
        }

        Vector3 axisDir = AxisVector(axis);
        Vector3 toCam = cameraPos - _dragOrigin;
        if (toCam.LengthSquared() < 1e-6f) toCam = Vector3.UnitZ;
        Vector3 planeNormal = Vector3.Cross(Vector3.Cross(axisDir, toCam), axisDir);
        if (planeNormal.LengthSquared() < 1e-6f) planeNormal = Vector3.Cross(axisDir, Vector3.UnitY);
        _dragPlaneNormal = Vector3.Normalize(planeNormal);

        _dragStartHit = RayPlaneHit(mouseRay, _dragOrigin, _dragPlaneNormal) ?? _dragOrigin;
    }

    public void UpdateDrag(Ray mouseRay, Point mousePos)
    {
        if (_dragAxis == GizmoAxis.None) return;
        Vector3 axisDir = AxisVector(_dragAxis);
        int axisIndex = _dragAxis == GizmoAxis.X ? 0 : _dragAxis == GizmoAxis.Y ? 1 : 2;

        if (Mode == GizmoMode.Rotate)
        {
            float dx = mousePos.X - _dragLastMouse.X;
            var current = (Vector3)_dragProperty.GetValue(_dragTarget);
            SetComponent(ref current, axisIndex, GetComponent(current, axisIndex) + dx * RotateSensitivity);
            _dragProperty.SetValue(_dragTarget, current);
            _dragLastMouse = mousePos;
            return;
        }

        // Uniform scale (center box): horizontal drag from the grab point scales all axes equally.
        if (Mode == GizmoMode.Scale && _dragAxis == GizmoAxis.All)
        {
            float d = (mousePos.X - _dragStartMouse.X) * UniformScaleSensitivity;
            var nv = new Vector3(
                MathF.Max(ScaleMin, _dragStartValue.X + d),
                MathF.Max(ScaleMin, _dragStartValue.Y + d),
                MathF.Max(ScaleMin, _dragStartValue.Z + d));
            _dragProperty.SetValue(_dragTarget, nv);
            return;
        }

        var hit = RayPlaneHit(mouseRay, _dragOrigin, _dragPlaneNormal);
        if (hit == null) return;
        float delta = Vector3.Dot(hit.Value - _dragStartHit, axisDir);

        Vector3 newValue = _dragStartValue;
        if (Mode == GizmoMode.Translate)
        {
            newValue = _dragStartValue + axisDir * delta;
        }
        else if (Mode == GizmoMode.Scale)
        {
            float nv = MathF.Max(ScaleMin, GetComponent(_dragStartValue, axisIndex) + delta * ScaleSensitivity);
            SetComponent(ref newValue, axisIndex, nv);
        }
        _dragProperty.SetValue(_dragTarget, newValue);
    }

    public void EndDrag(EditorHistory history)
    {
        if (_dragAxis == GizmoAxis.None) return;
        var finalValue = _dragProperty.GetValue(_dragTarget);
        history.Execute(new SetPropertyCommand(_dragTarget, _dragProperty, _dragOldValue, finalValue,
            $"{Mode} {_dragAxis}"));
        _dragAxis = GizmoAxis.None;
        _dragTarget = null;
        _dragProperty = null;
    }

    public void CancelDrag()
    {
        if (_dragAxis == GizmoAxis.None) return;
        _dragProperty?.SetValue(_dragTarget, _dragOldValue);
        _dragAxis = GizmoAxis.None;
        _dragTarget = null;
        _dragProperty = null;
    }

    private static float GetComponent(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;
    private static void SetComponent(ref Vector3 v, int i, float value)
    {
        if (i == 0) v.X = value; else if (i == 1) v.Y = value; else v.Z = value;
    }

    private static Vector3? RayPlaneHit(Ray ray, Vector3 planePoint, Vector3 planeNormal)
    {
        float denom = Vector3.Dot(ray.Direction, planeNormal);
        if (MathF.Abs(denom) < 1e-6f) return null;
        float t = Vector3.Dot(planePoint - ray.Position, planeNormal) / denom;
        if (t < 0f) return null;
        return ray.Position + ray.Direction * t;
    }

    public void Dispose() => _renderer.Dispose();
}
