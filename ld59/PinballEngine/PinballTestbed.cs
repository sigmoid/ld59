using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

public class PinballTestbed
{
    public static PinballTable GetTestbedTable()
    {
        float W = 500;
        float H = 700;

        var table = new PinballTable();
        table.Obstacles = new List<PinballObstacle>();
        table.DefaultAcceleration = 500f;
        table.Damping             = 0.4f;

        // --- Boundary with explicit guide junction vertices so guides connect cleanly ---
        table.Obstacles.Add(Wall(
            new Vector2(0,          0),
            new Vector2(W,          0),
            new Vector2(W,          0.70f * H),
            new Vector2(W,          H),
            new Vector2(0,          H),
            new Vector2(0,          0.70f * H)
        ));

        // --- Angled guides — start at the explicit boundary junction, end at flipper hinge ---
        table.Obstacles.Add(Wall(new Vector2(0,   0.70f * H), new Vector2(0.35f * W, 0.80f * H)));
        table.Obstacles.Add(Wall(new Vector2(W,   0.70f * H), new Vector2(0.65f * W, 0.80f * H)));

        // --- Slingshot triangles (mid-lower sides) ---
        table.Obstacles.Add(Wall(
            new Vector2(0.08f * W, 0.50f * H),
            new Vector2(0.26f * W, 0.63f * H),
            new Vector2(0.08f * W, 0.63f * H)
        ));
        table.Obstacles.Add(Wall(
            new Vector2(0.92f * W, 0.50f * H),
            new Vector2(0.74f * W, 0.63f * H),
            new Vector2(0.92f * W, 0.63f * H)
        ));

        // --- Centre box (upper playfield) ---
        table.Obstacles.Add(Wall(
            new Vector2(0.37f * W, 0.17f * H),
            new Vector2(0.63f * W, 0.17f * H),
            new Vector2(0.63f * W, 0.30f * H),
            new Vector2(0.37f * W, 0.30f * H)
        ));

        // --- Scatter boxes (mid playfield) ---
        table.Obstacles.Add(Wall(
            new Vector2(0.10f * W, 0.37f * H),
            new Vector2(0.22f * W, 0.37f * H),
            new Vector2(0.22f * W, 0.47f * H),
            new Vector2(0.10f * W, 0.47f * H)
        ));
        table.Obstacles.Add(Wall(
            new Vector2(0.78f * W, 0.37f * H),
            new Vector2(0.90f * W, 0.37f * H),
            new Vector2(0.90f * W, 0.47f * H),
            new Vector2(0.78f * W, 0.47f * H)
        ));

        // --- Flippers ---
        var leftFlipper = new PinballFlipper();
        leftFlipper.HingePosition  = new Vector2(0.35f * W, 0.80f * H);
        leftFlipper.Length         = W * 0.125f;
        leftFlipper.RestAngle      =  30f / 180f * MathF.PI;
        leftFlipper.ActivatedAngle = -30f / 180f * MathF.PI;
        leftFlipper.ActivationSpeed = 15f;
        leftFlipper.ReturnSpeed     = 10f;
        leftFlipper.ActivationKey   = InputButtons.LeftFlipper;

        var rightFlipper = new PinballFlipper();
        rightFlipper.HingePosition  = new Vector2(0.65f * W, 0.80f * H);
        rightFlipper.Length         = W * 0.125f;
        rightFlipper.RestAngle      = (180f - 30f) / 180f * MathF.PI;
        rightFlipper.ActivatedAngle = (180f + 30f) / 180f * MathF.PI;
        rightFlipper.ActivationSpeed = 15f;
        rightFlipper.ReturnSpeed     = 10f;
        rightFlipper.ActivationKey   = InputButtons.RightFlipper;

        table.Obstacles.Add(leftFlipper);
        table.Obstacles.Add(rightFlipper);

        // --- Ball ---
        table.AddBall(new PinballBall(10, new Vector2(0.55f * W, 0.10f * H)));

        return table;
    }

    private static PinballWall Wall(params Vector2[] vertices) =>
        new PinballWall { Vertices = new List<Vector2>(vertices) };
}
