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

    // Reserved pick ids, far above any realistic entity count (the entity pick path caps at
    // 0xFFFFFF entities but scenes here have at most a few thousand).
    private const int IdX = 0x00F00001;
    private const int IdY = 0x00F00002;
    private const int IdZ = 0x00F00003;
    private const int IdAll = 0x00F00004;

    private const float HandleScaleFactor = 0.09f; // world units of handle length per unit of camera distance
    private const float MinHandleLength = 0.3f;
    private const float MaxHandleLength = 1.5f;      // cap so a far camera doesn't balloon the gizmo
    private const float RotateSensitivity = 0.01f;  // radians per pixel of horizontal mouse delta
    private const float ScaleSensitivity = 0.1f;    // scale units per world unit of drag
    private const float UniformScaleSensitivity = 0.01f; // scale units per pixel of horizontal drag
    private const float ScaleMin = 0.01f;

    public bool IsDragging => _dragAxis != GizmoAxis.None;

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

    // Scale with camera distance for a roughly constant on-screen size, but clamp so it neither
    // vanishes up close nor balloons into a scene-spanning object when the camera is far away.
    private static float HandleLength(Vector3 origin, Vector3 cameraPos) =>
        MathHelper.Clamp(Vector3.Distance(origin, cameraPos) * HandleScaleFactor, MinHandleLength, MaxHandleLength);

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

    public static GizmoAxis DecodeAxis(int id) => id switch
    {
        IdX => GizmoAxis.X,
        IdY => GizmoAxis.Y,
        IdZ => GizmoAxis.Z,
        IdAll => GizmoAxis.All,
        _ => GizmoAxis.None,
    };

    private static int EncodeAxisId(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => IdX,
        GizmoAxis.Y => IdY,
        GizmoAxis.Z => IdZ,
        GizmoAxis.All => IdAll,
        _ => 0,
    };

    private static Vector4 EncodeIdColor(int id) => new Vector4(
        (id & 0xFF) / 255f, ((id >> 8) & 0xFF) / 255f, ((id >> 16) & 0xFF) / 255f, 1f);

    // ── drawing ──────────────────────────────────────────────────────────────────
    public void Draw(GraphicsDevice device, Entity entity, Vector3 cameraPos, Matrix view, Matrix proj)
    {
        var (_, _, value) = ResolveTarget(entity);
        Vector3 origin = Mode == GizmoMode.Translate ? value : entity.Position3D;
        float len = HandleLength(origin, cameraPos);
        var model = _renderer.ModelFor(Mode);

        // Clear the depth buffer so the gizmo sits on top of the scene, but draw depth-tested so
        // a solid handle model resolves its own front/back faces correctly.
        device.Clear(ClearOptions.DepthBuffer, Color.Black, 1f, 0);

        foreach (var axis in Axes)
        {
            var color = AxisColor(axis);
            if (_dragAxis == axis) color *= 1.5f; // brighten the axis being dragged
            DrawHandle(device, model, axis, origin, len, view, proj, color);
        }

        // Scale gizmo: a center box that scales all three axes at once.
        if (Mode == GizmoMode.Scale)
        {
            var color = AxisColor(GizmoAxis.All);
            if (_dragAxis == GizmoAxis.All) color *= 1.4f;
            DrawUniformBox(device, origin, len, view, proj, color);
        }
    }

    // Renders the same handles into whatever render target is currently bound (the caller sets it
    // up as an ID buffer with a cleared depth buffer), using reserved pick ids instead of colors.
    public void DrawForPicking(GraphicsDevice device, Entity entity, Vector3 cameraPos, Matrix view, Matrix proj)
    {
        var (_, _, value) = ResolveTarget(entity);
        Vector3 origin = Mode == GizmoMode.Translate ? value : entity.Position3D;
        float len = HandleLength(origin, cameraPos);
        var model = _renderer.ModelFor(Mode);

        foreach (var axis in Axes)
            DrawHandle(device, model, axis, origin, len, view, proj, EncodeIdColor(EncodeAxisId(axis)));

        if (Mode == GizmoMode.Scale)
            DrawUniformBox(device, origin, len, view, proj, EncodeIdColor(IdAll));
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
