using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;

public static class PinballTableLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private record WallData(string Name, float[][] Vertices);
    private record CircleData(string Name, float[] Center, float Radius);
    private record FlipperData(
        string Name, float[] Hinge, float[] Tip,
        float ActivatedAngleOffset, float ActivationSpeed, float ReturnSpeed, string ActivationKey);
    private record RampData(string Name, float[][] Path, float EntryRadius);
    private record TableData(
        int Version,
        WallData[]    Walls,
        CircleData[]  Circles,
        FlipperData[] Flippers,
        RampData[]    Ramps);

    /// <summary>
    /// Loads a pinball table from a JSON file exported by the Blender script.
    /// All UV coordinates in the file are scaled by tableSize to produce game-space positions.
    /// Radius is scaled by tableSize.X (table width).
    /// </summary>
    public static PinballTable Load(
        string contentRelativePath,
        Vector2 tableSize,
        float acceleration = 500f,
        float damping = 0.4f)
    {
        using var stream = TitleContainer.OpenStream(
            Path.Combine(Quartz.Core.Content.RootDirectory, contentRelativePath));
        using var reader = new StreamReader(stream);
        var data = JsonSerializer.Deserialize<TableData>(reader.ReadToEnd(), JsonOptions);

        var table = new PinballTable
        {
            Obstacles           = new List<PinballObstacle>(),
            DefaultAcceleration = acceleration,
            Damping             = damping,
        };

        // The Blender plane's local X runs along the table's long axis (top→flippers),
        // so UV[0] (U) maps to the vertical game axis and UV[1] (V) maps to horizontal.
        static Vector2 ToGame(float u, float v, Vector2 size) =>
            new(v * size.X, u * size.Y);

        foreach (var w in data.Walls ?? Array.Empty<WallData>())
        {
            var verts = new List<Vector2>(w.Vertices.Length);
            foreach (var v in w.Vertices)
                verts.Add(ToGame(v[0], v[1], tableSize));
            table.Obstacles.Add(new PinballWall { Vertices = verts, IsOpen = true });
        }

        foreach (var c in data.Circles ?? Array.Empty<CircleData>())
        {
            table.Obstacles.Add(new PinballCircleCollider
            {
                Center = ToGame(c.Center[0], c.Center[1], tableSize),
                Radius = c.Radius * tableSize.X,
            });
        }

        foreach (var f in data.Flippers ?? Array.Empty<FlipperData>())
        {
            var hinge = ToGame(f.Hinge[0], f.Hinge[1], tableSize);
            var tip   = ToGame(f.Tip[0],   f.Tip[1],   tableSize);
            var diff  = tip - hinge;

            float restAngle      = MathF.Atan2(diff.Y, diff.X);
            float activatedAngle = restAngle + f.ActivatedAngleOffset * MathF.PI / 180f;

            table.Obstacles.Add(new PinballFlipper
            {
                HingePosition   = hinge,
                Length          = diff.Length(),
                RestAngle       = restAngle,
                ActivatedAngle  = activatedAngle,
                ActivationSpeed = f.ActivationSpeed,
                ReturnSpeed     = f.ReturnSpeed,
                ActivationKey   = f.ActivationKey,
            });
        }

        foreach (var r in data.Ramps ?? Array.Empty<RampData>())
        {
            var path = new List<Vector3>(r.Path.Length); // capacity hint
            foreach (var p in r.Path)
            {
                var pos    = ToGame(p[0], p[1], tableSize);
                // Height uses the same normalisation as U (long axis) → scale by tableSize.Y
                float height = p.Length > 2 ? p[2] * tableSize.Y : 0f;
                path.Add(new Vector3(pos.X, pos.Y, height));
            }
            table.Obstacles.Add(new PinballRamp
            {
                Path        = path,
                EntryRadius = r.EntryRadius * tableSize.X,
            });
        }

        return table;
    }
}
