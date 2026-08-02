using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.Input;
using ld59.WalkingSim;

namespace ld59.UI.Scene3D;

/// <summary>
/// The camera behind a <see cref="UI3DScene"/>, and the mouse capture that drives it.
/// Fly = free 6-DOF (the 3D scene viewer / editor camera). Walk = first-person walker constrained
/// to a navmesh: look is free, movement is flattened to the horizontal plane, and height comes
/// from the <see cref="WalkController"/>.
/// </summary>
public sealed class CameraRig
{
    public Vector3 Position { get; set; } = new Vector3(0, 0, 5);
    public Vector3 Target   { get; set; } = Vector3.Zero;
    public float FieldOfView { get; set; } = MathHelper.PiOver4;
    public float NearPlane   { get; set; } = 1.0f;
    public float FarPlane    { get; set; } = 70000f;
    /// <summary>Free-fly speed, in world units per second. Sized for crossing a level quickly from
    /// a detached camera, so it is deliberately much faster than <see cref="WalkSpeed"/>.</summary>
    public float FlySpeed { get; set; } = 30f;

    /// <summary>On-foot speed, in world units per second. Pushed into the
    /// <see cref="WalkController"/> each frame, so this is the authority while walking -- setting
    /// <c>WalkController.MoveSpeed</c> directly on a camera-driven walker has no effect.</summary>
    public float WalkSpeed { get; set; } = 10f;

    /// <summary>Shift-to-boost multiplier for the free-fly (editor) camera. Walking has no boost --
    /// the walker's speed is a gameplay value, not a navigation convenience.</summary>
    public float FlyBoostMultiplier { get; set; } = 6f;
    public float LookSensitivity    { get; set; } = 0.002f;

    /// <summary>Toggle (F3) for diagnosing the "mouse look goes dead" glitch: logs the raw cursor
    /// position, per-frame look delta, accumulated yaw, recentre target and bounds a few times a
    /// second.</summary>
    public bool DebugLook { get; set; }

    public CameraMode Mode      { get; set; } = CameraMode.Fly;
    public WalkController Walker { get; set; }

    /// <summary>True while the cursor is captured and the mouse is steering the view.</summary>
    public bool IsActive => _cameraActive;

    public Matrix View => Matrix.CreateLookAt(Position, Target, Vector3.Up);
    public Matrix Projection(float aspect) =>
        Matrix.CreatePerspectiveFieldOfView(FieldOfView, aspect, NearPlane, FarPlane);

    private bool _cameraActive;
    private float _yaw;
    private float _pitch;
    private bool _anglesInitialized;
    private Point _lockCenter;
    private Point _prevRawMouse;      // previous raw (back-buffer) cursor pos for frame-to-frame look deltas
    private bool _recenterPending;    // set the frame we reposition the cursor; swallow the next delta
    private bool _prevLookPressed;
    private bool _prevTabPressed;
    private bool _captureSuspended;
    private bool _inputBlocked;              // last frame's GameInput.Blocked, for edge detection
    private bool _restoreCaptureOnUnblock;   // walk-mode capture we took away for the console
    private int  _debugLookFrame;

    /// <summary>
    /// Release the mouse and stop re-capturing on click, so a modal (e.g. the puzzle solve view)
    /// can use the cursor. Restore with <see cref="ResumeCapture"/>.
    /// </summary>
    public void SuspendCapture()
    {
        _captureSuspended = true;
        _cameraActive = false;
        Core.Instance.IsMouseVisible = true;
    }

    public void ResumeCapture()
    {
        _captureSuspended = false;

        // Re-lock the mouse into the walk view so leaving a puzzle drops you straight back into
        // walking-look, instead of landing with a freed cursor that needs an extra click.
        _cameraActive = true;
        Core.Instance.IsMouseVisible = false;
        _recenterPending = true;   // zero the first look delta so the view doesn't snap on resume

        // A puzzle is usually closed with Tab, which is also the walk view's "release the mouse"
        // key. Left alone, that same Tab press registers as a fresh release edge here next frame
        // and immediately frees the cursor we just recaptured. Mask the edge so one Tab press only
        // closes the puzzle.
        _prevTabPressed = true;
    }

    /// <summary>Give the cursor back, without the re-capture suppression of SuspendCapture. Used
    /// when the view itself goes away.</summary>
    public void ReleaseCapture()
    {
        if (!_cameraActive) return;
        Core.Instance.IsMouseVisible = true;
        _cameraActive = false;
    }

    /// <summary>
    /// Pull the camera back along its current view direction far enough that a sphere of
    /// <paramref name="radius"/> at <paramref name="center"/> fits the vertical FOV. Keeps the
    /// current orientation, so framing never spins the view -- it only closes the distance.
    /// </summary>
    /// <returns>The chosen camera distance (for diagnostics).</returns>
    public float Frame(Vector3 center, float radius, float margin)
    {
        // Vertical FOV is the binding constraint on any aspect ratio wider than tall.
        float dist = radius / MathF.Max(MathF.Sin(FieldOfView * 0.5f), 1e-3f) * margin;

        var dir = Target - Position;
        dir = dir.LengthSquared() > 1e-6f ? Vector3.Normalize(dir) : Vector3.Forward;

        Position = center - dir * dist;
        Target   = center;
        return dist;
    }

    /// <summary>
    /// Capture/release the mouse and, while captured, apply look and movement.
    /// <paramref name="lookPressed"/> is the button that means "look" in the caller's current mode
    /// (right button in the editor so left stays free for selection; left otherwise).
    /// </summary>
    public void Update(float deltaTime, KeyboardState keyboard, in RtViewport vp, Point cursor,
                       bool lookPressed, bool tabPressed, float sceneScale)
    {
        if (!_anglesInitialized) InitializeAngles();

        // While the game isn't listening (developer console), the cursor has to go back to the
        // user or the console can't be clicked -- and the view must not keep turning with it.
        if (UpdateInputBlock())
        {
            _prevLookPressed = lookPressed;
            _prevTabPressed  = tabPressed;
            return;
        }

        bool justCaptured = false;
        if (!_captureSuspended && lookPressed && !_prevLookPressed && vp.Contains(cursor))
        {
            _cameraActive = true;
            Core.Instance.IsMouseVisible = false;
            justCaptured  = true;   // recentre this frame without treating the click position as a look delta
        }

        // Fly releases capture on look-button-up (hold-to-look); Walk keeps capture until released.
        // Tab releases in both modes (Escape can't be used -- it quits the game globally).
        bool flyRelease = Mode == CameraMode.Fly && !lookPressed && _prevLookPressed && _cameraActive;
        bool tabRelease = tabPressed && !_prevTabPressed && _cameraActive;
        if (flyRelease || tabRelease)
        {
            _cameraActive = false;
            Core.Instance.IsMouseVisible = true;
        }

        if (_cameraActive)
            UpdateLookAndMove(deltaTime, keyboard, vp, justCaptured, sceneScale);

        _prevLookPressed = lookPressed;
        _prevTabPressed  = tabPressed;
    }

    /// <summary>
    /// Tracks <see cref="GameInput.Blocked"/> and takes/returns the mouse across the transition.
    /// Kept separate from <see cref="SuspendCapture"/>: a modal owns that flag for as long as it is
    /// up, and the console closing must not steal it back. Only walk-mode capture is restored --
    /// fly-mode look is hold-to-look, so it re-captures on its own at the next button press, and
    /// force-restoring it would leave the cursor hidden if the button was let go while blocked.
    /// </summary>
    /// <returns>True while input is blocked, i.e. the caller should do nothing this frame.</returns>
    private bool UpdateInputBlock()
    {
        bool blocked = GameInput.Blocked;
        if (blocked == _inputBlocked) return blocked;
        _inputBlocked = blocked;

        if (blocked)
        {
            _restoreCaptureOnUnblock = _cameraActive && Mode == CameraMode.Walk;
            ReleaseCapture();
        }
        else if (_restoreCaptureOnUnblock && !_captureSuspended)
        {
            _restoreCaptureOnUnblock = false;
            _cameraActive = true;
            Core.Instance.IsMouseVisible = false;
            _recenterPending = true;   // zero the first look delta so the view doesn't snap on resume
        }
        else
        {
            _restoreCaptureOnUnblock = false;
        }

        return blocked;
    }

    private void InitializeAngles()
    {
        var dir = Vector3.Normalize(Target - Position);
        _pitch = MathF.Asin(MathHelper.Clamp(dir.Y, -1f, 1f));
        _yaw   = MathF.Atan2(dir.X, dir.Z);
        _anglesInitialized = true;
    }

    private void UpdateLookAndMove(float deltaTime, KeyboardState keyboard, in RtViewport vp,
                                   bool justCaptured, float sceneScale)
    {
        // Recompute the recentre target every frame. When it was only set at capture-start, a
        // mid-capture window move/resize/fullscreen-toggle left it stale, so the cursor parked
        // off-centre and could drift into a screen edge where the OS clamps it -- at which point
        // the delta reads ~0 forever and the look appears to die.
        _lockCenter = new Point(vp.Bounds.X + vp.Bounds.Width / 2, vp.Bounds.Y + vp.Bounds.Height / 2);

        // Measure the look delta in raw back-buffer pixels rather than logical pixels.
        // Core.GetMouseState() scales pointer motion down into logical space, so in borderless
        // fullscreen (back buffer larger than the logical resolution) a given physical mouse move
        // yields a smaller logical delta -- which reads as much weaker look sensitivity than in
        // windowed mode. Recentre on, and diff against, the viewport centre expressed in
        // back-buffer space so sensitivity (and precision) stay identical across window modes.
        var pp = Core.GraphicsDevice.PresentationParameters;
        int centerX = (int)(_lockCenter.X * (float)pp.BackBufferWidth  / Core.ScreenWidth);
        int centerY = (int)(_lockCenter.Y * (float)pp.BackBufferHeight / Core.ScreenHeight);

        // Look delta is measured frame-to-frame from the previous raw position -- NOT by
        // recentring every frame and diffing against the centre. Mouse.SetPosition is an async
        // OS call whose result may not be visible to the next Mouse.GetState(), so recentring
        // every frame made deltas double-count/drop on alternating frames -> choppy look.
        var raw = GameInput.RawDeviceMouse;
        Vector2 delta;
        if (justCaptured || _recenterPending)
        {
            // Capture frame (cursor is at the click point) or the frame after a recenter (a
            // lagged SetPosition may not have landed yet): just re-anchor and skip the delta so
            // neither registers as a snap.
            delta = Vector2.Zero;
            _prevRawMouse = new Point(raw.X, raw.Y);
            _recenterPending = false;
        }
        else
        {
            delta = new Vector2(raw.X - _prevRawMouse.X, raw.Y - _prevRawMouse.Y);
            _prevRawMouse = new Point(raw.X, raw.Y);

            // Recentre only when the cursor nears the window edge (outside the central half), so
            // it never reaches the OS clamp (where the delta would read ~0 and the look die),
            // while keeping SetPosition rare enough that its latency doesn't cause jitter.
            int marginX = pp.BackBufferWidth  / 4;
            int marginY = pp.BackBufferHeight / 4;
            if (raw.X < marginX || raw.X > pp.BackBufferWidth  - marginX ||
                raw.Y < marginY || raw.Y > pp.BackBufferHeight - marginY)
            {
                Mouse.SetPosition(centerX, centerY);
                _recenterPending = true;
            }
        }

        _yaw   -= delta.X * LookSensitivity;
        _pitch -= delta.Y * LookSensitivity;
        _pitch  = MathHelper.Clamp(_pitch, -MathHelper.PiOver2 + 0.01f, MathHelper.PiOver2 - 0.01f);
        _yaw    = MathHelper.WrapAngle(_yaw);   // keep yaw in [-pi,pi] so float precision never erodes slow looks

        if (DebugLook && (_debugLookFrame++ % 15) == 0)
            Console.WriteLine($"[look] raw=({raw.X},{raw.Y}) delta=({delta.X},{delta.Y}) " +
                $"yaw={_yaw:F3} center=({centerX},{centerY}) lockCenter=({_lockCenter.X},{_lockCenter.Y}) " +
                $"bounds={vp.Bounds} suspended={_captureSuspended}");

        var forward = new Vector3(
            MathF.Cos(_pitch) * MathF.Sin(_yaw),
            MathF.Sin(_pitch),
            MathF.Cos(_pitch) * MathF.Cos(_yaw));

        if (Mode == CameraMode.Walk && Walker != null)
            MoveWalk(deltaTime, keyboard, forward, sceneScale);
        else
            MoveFly(deltaTime, keyboard, forward);
    }

    // Look is free (full pitch); movement is flattened to the ground plane and driven through the
    // navmesh walker. Height comes from the walker's eye position.
    private void MoveWalk(float deltaTime, KeyboardState keyboard, Vector3 forward, float sceneScale)
    {
        var flatForward = new Vector3(MathF.Sin(_yaw), 0f, MathF.Cos(_yaw));
        var flatRight   = Vector3.Normalize(Vector3.Cross(flatForward, Vector3.Up));
        var move = Vector3.Zero;
        if (keyboard.IsKeyDown(Keys.W)) move += flatForward;
        if (keyboard.IsKeyDown(Keys.S)) move -= flatForward;
        if (keyboard.IsKeyDown(Keys.A)) move -= flatRight;
        if (keyboard.IsKeyDown(Keys.D)) move += flatRight;

        Walker.MoveSpeed = WalkSpeed;
        Walker.Move(new Vector2(move.X, move.Z), deltaTime);
        Position = Walker.EyePosition * sceneScale;
        Target   = Position + forward;
    }

    private void MoveFly(float deltaTime, KeyboardState keyboard, Vector3 forward)
    {
        var right   = Vector3.Normalize(Vector3.Cross(forward, Vector3.Up));
        var moveDir = Vector3.Zero;
        if (keyboard.IsKeyDown(Keys.W)) moveDir += forward;
        if (keyboard.IsKeyDown(Keys.S)) moveDir -= forward;
        if (keyboard.IsKeyDown(Keys.A)) moveDir -= right;
        if (keyboard.IsKeyDown(Keys.D)) moveDir += right;

        if (moveDir.LengthSquared() > 0)
            moveDir = Vector3.Normalize(moveDir);

        // Hold Shift to fly much faster (handy for crossing a large level in the editor).
        bool boost = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        float speed = FlySpeed * (boost ? FlyBoostMultiplier : 1f);
        Position += moveDir * speed * deltaTime;
        Target    = Position + forward;
    }
}
