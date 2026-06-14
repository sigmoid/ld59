using Quartz;

public static class InputButtons
{
    public const string LeftFlipper = "LeftFlipper";
    public const string RightFlipper = "RightFlipper";

    public static void RegisterDefaultButtons()
    {
        Core.InputManager.AddButton(LeftFlipper, Microsoft.Xna.Framework.Input.Keys.Z);
        Core.InputManager.AddButton(RightFlipper, Microsoft.Xna.Framework.Input.Keys.OemPeriod);
    }
}