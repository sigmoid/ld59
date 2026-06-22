using System;
using Microsoft.Xna.Framework;
using Quartz;

// A flipper modelled as a rigid bat pivoting about its hinge — inspired by how Visual
// Pinball treats the flipper as a driven rotating body rather than a kinematic lerp.
//
// A "coil" applies a constant angular acceleration toward the activated stop while the
// button is held, and a weaker return acceleration toward rest when released. Because the
// bat now carries real angular momentum (and a moment of inertia), the ball↔flipper
// collision can be resolved as a genuine two-body impulse (see PinballEngine): tip hits
// transfer more energy than base hits, and a fast/heavy ball can momentarily stall the bat.
public class PinballFlipper : PinballObstacle
{
    public Vector2 HingePosition { get; set; }
    public float   Length        { get; set; }
    public float   RestAngle     { get; set; }
    public float   ActivatedAngle{ get; set; }
    public string  ActivationKey { get; set; }

    // Retained for table-data compatibility (PinballTableLoader / testbed still set these).
    // Reinterpreted as the *intended full-swing rate* (swings per second): the coil and
    // return angular accelerations are derived from them, so existing tables keep their
    // timing while now behaving as a driven rigid body.
    public float ActivationSpeed { get; set; } = 15f;
    public float ReturnSpeed     { get; set; } = 10f;

    // Flipper mass relative to the ball (ball mass = 1). Moment of inertia about the hinge
    // is that of a uniform rod, I = m·L²/3. Heavier ⇒ the ball gives way more than the bat
    // on contact; lighter ⇒ the ball can deflect/stall the bat.
    public float Mass             { get; set; } = 1.5f;
    public float MomentOfInertia => Mass * Length * Length / 3f;

    // The tip is the fastest-moving thing in the sim; cap its speed so a single physics
    // step can't tunnel a ball past the (thin) bat. tipSpeed = |ω|·Length.
    public float MaxTipSpeed { get; set; } = 2000f;

    public float CurrentAngle    => _angle;
    public float AngularVelocity => _angularVelocity;

    private float _angle;
    private float _angularVelocity;
    private bool  _initialized;

    // Lets the collision resolver feed the reaction impulse back into the bat.
    public void ApplyAngularImpulse(float deltaOmega)
    {
        _angularVelocity += deltaOmega;
        ClampAngularVelocity();
    }

    public override void Update(float deltaTime)
    {
        if (!_initialized)
        {
            _angle       = RestAngle;
            _initialized = true;
        }

        float span = MathF.Abs(ActivatedAngle - RestAngle);
        float dir  = MathF.Sign(ActivatedAngle - RestAngle);
        if (dir == 0f) dir = 1f;

        bool input = Core.InputManager.GetButton(ActivationKey).IsHeld;

        // Constant-acceleration coil. Derived so a free (uncontacted) swing covers `span`
        // in ~1/Speed seconds — matching the old lerp timing — but now with real
        // acceleration and terminal angular velocity (θ = ½·a·t²  ⇒  a = 2·span/t²).
        float tUp       = ActivationSpeed > 1e-3f ? 1f / ActivationSpeed : 0.0667f;
        float tDown     = ReturnSpeed     > 1e-3f ? 1f / ReturnSpeed     : 0.1f;
        float accelUp   = span > 1e-4f ? 2f * span / (tUp   * tUp)   : 0f;
        float accelDown = span > 1e-4f ? 2f * span / (tDown * tDown) : 0f;

        float accel = input ? dir * accelUp : -dir * accelDown;
        _angularVelocity += accel * deltaTime;
        ClampAngularVelocity();

        _angle += _angularVelocity * deltaTime;

        // End stops: clamp to the [rest, activated] arc and kill velocity driving into a stop.
        float lo = MathF.Min(RestAngle, ActivatedAngle);
        float hi = MathF.Max(RestAngle, ActivatedAngle);
        if (_angle <= lo)
        {
            _angle = lo;
            if (_angularVelocity < 0f) _angularVelocity = 0f;
        }
        else if (_angle >= hi)
        {
            _angle = hi;
            if (_angularVelocity > 0f) _angularVelocity = 0f;
        }

        base.Update(deltaTime);
    }

    private void ClampAngularVelocity()
    {
        float maxOmega = Length > 1e-3f ? MaxTipSpeed / Length : float.MaxValue;
        if (_angularVelocity >  maxOmega) _angularVelocity =  maxOmega;
        if (_angularVelocity < -maxOmega) _angularVelocity = -maxOmega;
    }
}
