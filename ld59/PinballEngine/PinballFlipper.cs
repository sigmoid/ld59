using Microsoft.Xna.Framework;
using Quartz;

public class PinballFlipper : PinballObstacle
{
    public Vector2 HingePosition {get;set;}
    public float Length {get;set;}
    public float RestAngle {get;set;}
    public float ActivatedAngle {get;set;}
    public float ActivationSpeed {get;set;}
    public float ReturnSpeed {get;set;}
    public string ActivationKey {get;set;}
    public float CurrentAngle
    {
        get => Math.Lerp(RestAngle, ActivatedAngle, _currentAngleLerp);
    }
    public float AngularVelocity { get; private set; }

    public override void Update(float deltaTime)
    {
        bool input = Core.InputManager.GetButton(ActivationKey).IsHeld;

        float prevLerp = _currentAngleLerp;
        if (input && _currentAngleLerp < 1f)
            _currentAngleLerp = Math.Min(_currentAngleLerp + ActivationSpeed * deltaTime, 1f);
        else if (!input && _currentAngleLerp > 0f)
            _currentAngleLerp = Math.Max(_currentAngleLerp - ReturnSpeed * deltaTime, 0f);

        // Derive angular velocity from the lerp change that ACTUALLY happened this
        // step (rather than the nominal rate), so the clamp at either end of the
        // swing doesn't report a kick the flipper never delivered.
        // rad/s = (ActivatedAngle - RestAngle) rad * (Δlerp / Δt)
        float dLerpRate = deltaTime > 1e-6f ? (_currentAngleLerp - prevLerp) / deltaTime : 0f;
        AngularVelocity = (ActivatedAngle - RestAngle) * dLerpRate;

        base.Update(deltaTime);
    }

    private float _currentAngleLerp;
}