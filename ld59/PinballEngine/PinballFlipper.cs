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

        float dLerp = 0f;
        if (input && _currentAngleLerp < 1f)
        {
            _currentAngleLerp = Math.Min(_currentAngleLerp + ActivationSpeed * deltaTime, 1f);
            dLerp = ActivationSpeed;
        }
        else if (!input && _currentAngleLerp > 0f)
        {
            _currentAngleLerp = Math.Max(_currentAngleLerp - ReturnSpeed * deltaTime, 0f);
            dLerp = -ReturnSpeed;
        }

        // rad/s = (ActivatedAngle - RestAngle) rad * lerp_rate (1/s)
        AngularVelocity = (ActivatedAngle - RestAngle) * dLerp;

        base.Update(deltaTime);
    }

    private float _currentAngleLerp;
}