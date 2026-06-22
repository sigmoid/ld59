using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Xna.Framework;
using Quartz;

// ─────────────────────────────────────────────────────────────────────────────
// Flipper launch-cone visualizer + tuning loop.
//
// Exposes the flipper dials (rest angle, activated-angle offset, length,
// activation/return speed, speed cap) in an editable params file. For each
// flipper it rests a ball at points along the bat, flips, and traces where the
// ball goes — writing an HTML page (with inline SVG) plus a console table.
//
//   dotnet run                 one-shot: regenerate from the params file
//   dotnet run -- watch        re-render whenever the params file changes
//
// The first run seeds the params file from the live pinball_table.json, so you
// start from the real geometry and just nudge numbers. Keep the .html open in a
// browser (it auto-refreshes) and edit the params file to see changes live.
// ─────────────────────────────────────────────────────────────────────────────

internal static class Program
{
    // Matches StartMenu.cs: PinballUI window is 700x800; the ball is radius 10.
    private const float W = 700f;
    private const float H = 800f;
    private const float BallRadius = 10f;

    private static readonly float[] Fractions =
        { 0.30f, 0.40f, 0.50f, 0.60f, 0.70f, 0.80f, 0.90f, 1.00f };

    // Flip-delay sweep (seconds): a falling ball is flipped at each delay, so we
    // see the real catch-and-shoot cone — where the ball goes for each flip timing.
    private static readonly float[] Delays =
        { 0.00f, 0.03f, 0.06f, 0.09f, 0.12f, 0.15f, 0.18f, 0.21f, 0.24f, 0.27f };

    private static string _contentDir;
    private static string _paramsPath;
    private static string _htmlPath;
    private static string _svgPath;

    // ── editable params ────────────────────────────────────────────────────────
    private sealed class FlipperParams
    {
        public string Label { get; set; }
        public float RestAngleDeg { get; set; }
        public float ActivatedOffsetDeg { get; set; }
        public float LengthPx { get; set; }
        public float ActivationSpeed { get; set; }
        public float ReturnSpeed { get; set; }
        public float LaunchFanDeg { get; set; }
    }

    private sealed class SimParams
    {
        public string _comment { get; set; } =
            "Edit and save; the .html auto-refreshes. RestAngleDeg points the bat (screen, +Y down); " +
            "ActivatedOffsetDeg is how far it swings; LengthPx is bat length; speeds are 1/sec lerp rates.";
        public float MaxBallSpeed { get; set; } = 2000f;
        public List<FlipperParams> Flippers { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private sealed class LaunchResult
    {
        public int FlipperIndex;
        public float Key;       // the swept variable (contact fraction OR flip delay)
        public float ColorT;    // 0..1 for the colour gradient
        public Vector2 LaunchPoint;
        public Vector2 LaunchVel;
        public float LaunchSpeed;
        public float LaunchAngleDeg;
        public Vector2 Apex;
        public List<Vector2> Path = new();
    }

    private static int Main(string[] args)
    {
        bool watch = Array.IndexOf(args, "watch") >= 0;

        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        _contentDir = Path.Combine(root, "ld59", "Content");
        _paramsPath = Path.Combine(root, "flipper_params.json");
        _htmlPath   = Path.Combine(root, "pinball_flipper_cone.html");
        _svgPath    = Path.Combine(root, "pinball_flipper_cone.svg");

        if (!File.Exists(Path.Combine(_contentDir, "scenes", "pinball_table.json")))
        {
            Console.Error.WriteLine($"Could not find pinball_table.json under: {_contentDir}");
            return 1;
        }

        EnsureParamsFile();

        if (!watch)
        {
            Generate();
            Console.WriteLine($"\nEdit {_paramsPath} and re-run, or use `dotnet run -- watch` for a live loop.");
            return 0;
        }

        Console.WriteLine($"Watching {_paramsPath}\nOpen {_htmlPath} in a browser (it auto-refreshes). Ctrl+C to stop.\n");
        long last = -1;
        while (true)
        {
            long stamp = File.GetLastWriteTimeUtc(_paramsPath).Ticks;
            if (stamp != last)
            {
                last = stamp;
                try { Generate(); Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] re-rendered"); }
                catch (Exception ex) { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] error: {ex.Message}"); }
            }
            Thread.Sleep(300);
        }
    }

    // ── params file ─────────────────────────────────────────────────────────────

    private static void EnsureParamsFile()
    {
        if (File.Exists(_paramsPath)) return;

        // Seed from the live table so the user starts at the real geometry.
        var table = LoadTable();
        var cfg = new SimParams();
        int idx = 0;
        foreach (var f in Flippers(table))
        {
            cfg.Flippers.Add(new FlipperParams
            {
                Label              = idx == 0 ? "L" : (idx == 1 ? "R" : $"F{idx}"),
                RestAngleDeg       = f.RestAngle * 180f / MathF.PI,
                ActivatedOffsetDeg = (f.ActivatedAngle - f.RestAngle) * 180f / MathF.PI,
                LengthPx           = f.Length,
                ActivationSpeed    = f.ActivationSpeed,
                ReturnSpeed        = f.ReturnSpeed,
                LaunchFanDeg       = f.LaunchFanDeg,
            });
            idx++;
        }
        File.WriteAllText(_paramsPath, JsonSerializer.Serialize(cfg, JsonOpts));
        Console.WriteLine($"Seeded {_paramsPath} from the current table.");
    }

    private static SimParams ReadParams() =>
        JsonSerializer.Deserialize<SimParams>(File.ReadAllText(_paramsPath), JsonOpts) ?? new SimParams();

    // ── table / overrides ─────────────────────────────────────────────────────

    private static PinballTable LoadTable()
    {
        Core.Content.RootDirectory = _contentDir;
        return PinballTableLoader.Load("scenes/pinball_table.json", new Vector2(W, H));
    }

    private static List<PinballFlipper> Flippers(PinballTable t)
    {
        var list = new List<PinballFlipper>();
        foreach (var o in t.Obstacles) if (o is PinballFlipper f) list.Add(f);
        return list;
    }

    private static void ApplyParams(PinballTable table, SimParams cfg)
    {
        var flippers = Flippers(table);
        for (int i = 0; i < flippers.Count && i < cfg.Flippers.Count; i++)
        {
            var p = cfg.Flippers[i];
            var f = flippers[i];
            f.RestAngle      = p.RestAngleDeg * MathF.PI / 180f;
            f.ActivatedAngle = f.RestAngle + p.ActivatedOffsetDeg * MathF.PI / 180f;
            f.Length         = p.LengthPx;
            f.ActivationSpeed = p.ActivationSpeed;
            f.ReturnSpeed     = p.ReturnSpeed;
            f.LaunchFanDeg    = p.LaunchFanDeg;
        }
    }

    // ── one render pass ─────────────────────────────────────────────────────────

    private static void Generate()
    {
        var cfg = ReadParams();

        var geom = LoadTable();
        ApplyParams(geom, cfg);
        int flipperCount = Flippers(geom).Count;

        // Two cones: contact-position (cradle & flick at each spot) and flip-timing
        // (the real catch-and-shoot — a falling ball flipped at each delay).
        var contact = new List<LaunchResult>();
        var timing  = new List<LaunchResult>();
        for (int fi = 0; fi < flipperCount; fi++)
        {
            for (int i = 0; i < Fractions.Length; i++)
                contact.Add(ContactLaunch(fi, Fractions[i], i / (float)(Fractions.Length - 1), cfg));
            for (int i = 0; i < Delays.Length; i++)
                timing.Add(TimingLaunch(fi, Delays[i], i / (float)(Delays.Length - 1), cfg));
        }

        PrintTable("contact-position", contact, "frac");
        PrintTable("flip-timing",      timing,  "delay");

        string svgContact = BuildSvg(geom, contact, "contact point (hinge→tip)", Fractions, keysAreMs: false);
        string svgTiming  = BuildSvg(geom, timing,  "flip delay (early→late)",   Delays,    keysAreMs: true);
        File.WriteAllText(_svgPath, svgTiming);
        File.WriteAllText(_htmlPath, BuildHtml(cfg, svgContact, svgTiming));
    }

    // Resting ball at a fixed point on the bat, flipped immediately ("cradle & flick").
    private static LaunchResult ContactLaunch(int flipperIndex, float fraction, float colorT, SimParams cfg)
    {
        var flipper = Flippers(WithParams(cfg, out _))[flipperIndex];
        var dir = new Vector2(MathF.Cos(flipper.RestAngle), MathF.Sin(flipper.RestAngle));
        var contact = flipper.HingePosition + dir * (fraction * flipper.Length);
        var n = new Vector2(-dir.Y, dir.X);
        if (n.Y > 0f) n = -n;                 // upward-facing normal (ball rests on top)
        var start = contact + n * (BallRadius + 0.5f);

        var r = RunLaunch(flipperIndex, start, Vector2.Zero, flipDelay: 0f, cfg);
        r.Key = fraction; r.ColorT = colorT;
        return r;
    }

    // Falling ball flipped after `delay` seconds ("catch & shoot" at a given timing).
    private static LaunchResult TimingLaunch(int flipperIndex, float delay, float colorT, SimParams cfg)
    {
        var flipper = Flippers(WithParams(cfg, out _))[flipperIndex];
        var dir = new Vector2(MathF.Cos(flipper.RestAngle), MathF.Sin(flipper.RestAngle));
        var n = new Vector2(-dir.Y, dir.X);
        if (n.Y > 0f) n = -n;
        // Drop from above the mid-bat, heading down onto the surface.
        var origin = flipper.HingePosition + dir * (0.55f * flipper.Length) + n * 80f;
        var vel0   = -n * 160f;               // toward the bat (gravity adds the rest)

        var r = RunLaunch(flipperIndex, origin, vel0, delay, cfg);
        r.Key = delay; r.ColorT = colorT;
        return r;
    }

    // Builds a fresh table with the params applied (handy one-liner for geometry reads).
    private static PinballTable WithParams(SimParams cfg, out PinballTable t)
    {
        t = LoadTable(); ApplyParams(t, cfg); return t;
    }

    private static LaunchResult RunLaunch(int flipperIndex, Vector2 start, Vector2 vel0, float flipDelay, SimParams cfg)
    {
        var table = LoadTable();
        ApplyParams(table, cfg);
        var flipper = Flippers(table)[flipperIndex];

        var ball = new PinballBall(BallRadius, start) { Velocity = vel0 };
        table.AddBall(ball);
        var engine = new PinballEngine(table) { MaxBallSpeed = cfg.MaxBallSpeed };

        var r = new LaunchResult { FlipperIndex = flipperIndex };
        const float dt = 1f / 120f;
        const float maxTime = 4f;
        float bestSpeed = -1f;
        var apex = start;
        bool flipping = false;

        for (float t = 0f; t < maxTime; t += dt)
        {
            if (t >= flipDelay)
            {
                if (!flipping) { Core.InputManager.Held.Clear(); Core.InputManager.Held.Add(flipper.ActivationKey); flipping = true; }
            }
            else Core.InputManager.Held.Clear();

            engine.Update(dt);
            var p = ball.Center;
            if (t >= flipDelay) r.Path.Add(p);

            float speed = ball.Velocity.Length();
            // Capture the launch in the window just after the flip starts.
            if (t >= flipDelay && t < flipDelay + 0.45f && speed > bestSpeed)
            {
                bestSpeed = speed; r.LaunchPoint = p; r.LaunchVel = ball.Velocity;
            }
            if (t >= flipDelay && p.Y < apex.Y) apex = p;

            if (p.Y > 0.93f * H || p.Y < -20f || p.X < -20f || p.X > W + 20f) break;
            if (t > flipDelay + 0.25f && speed < 4f) break;
        }

        r.LaunchSpeed = bestSpeed > 0f ? bestSpeed : 0f;
        r.LaunchAngleDeg = MathF.Atan2(-r.LaunchVel.Y, r.LaunchVel.X) * 180f / MathF.PI;
        r.Apex = apex;
        return r;
    }

    private static void PrintTable(string title, List<LaunchResult> results, string keyName)
    {
        Console.WriteLine($"\n  [{title}]   flip  {keyName,5}   speed   angle°");
        foreach (var r in results)
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "             {0,-5}  {1,5:0.00}  {2,6:0}  {3,7:0.0}",
                r.FlipperIndex == 0 ? "L" : "R", r.Key, r.LaunchSpeed, r.LaunchAngleDeg));
    }

    // ── rendering ─────────────────────────────────────────────────────────────

    private static string BuildSvg(PinballTable table, List<LaunchResult> results,
                                   string legendTitle, float[] keys, bool keysAreMs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {W} {H}' width='{W}' height='{H}' font-family='sans-serif'>");
        sb.AppendLine($"<rect x='0' y='0' width='{W}' height='{H}' fill='#f7f7fa'/>");

        foreach (var o in table.Obstacles)
        {
            if (o is not PinballWall wall || wall.Vertices == null || wall.Vertices.Count < 2) continue;
            var pts = new StringBuilder();
            foreach (var v in wall.Vertices) pts.Append(F(v.X)).Append(',').Append(F(v.Y)).Append(' ');
            string tag = wall.IsOpen ? "polyline" : "polygon";
            sb.AppendLine($"<{tag} points='{pts}' fill='none' stroke='#333' stroke-width='2'/>");
        }

        foreach (var f in Flippers(table))
        {
            DrawFlipper(sb, f, f.RestAngle, "#666", "4");
            DrawFlipper(sb, f, f.ActivatedAngle, "#bbb", "2", dashed: true);
            sb.AppendLine($"<circle cx='{F(f.HingePosition.X)}' cy='{F(f.HingePosition.Y)}' r='4' fill='#000'/>");
        }

        foreach (var r in results)
        {
            string col = HueColor(r.ColorT);
            if (r.Path.Count > 1)
            {
                var pts = new StringBuilder();
                foreach (var p in r.Path) pts.Append(F(p.X)).Append(',').Append(F(p.Y)).Append(' ');
                sb.AppendLine($"<polyline points='{pts}' fill='none' stroke='{col}' stroke-width='1.3' opacity='0.45'/>");
            }
            if (r.LaunchSpeed > 1f)
            {
                var d = Microsoft.Xna.Framework.Vector2.Normalize(r.LaunchVel);
                var tip = r.LaunchPoint + d * 150f;
                sb.AppendLine($"<line x1='{F(r.LaunchPoint.X)}' y1='{F(r.LaunchPoint.Y)}' x2='{F(tip.X)}' y2='{F(tip.Y)}' stroke='{col}' stroke-width='3'/>");
                sb.AppendLine($"<circle cx='{F(r.LaunchPoint.X)}' cy='{F(r.LaunchPoint.Y)}' r='3.5' fill='{col}'/>");
            }
        }

        float lx = 16f, ly = 24f;
        sb.AppendLine($"<text x='{F(lx)}' y='{F(ly)}' font-size='14' fill='#111'>colour = {legendTitle}</text>");
        for (int i = 0; i < keys.Length; i++)
        {
            float yy = ly + 18f + i * 16f;
            string label = keysAreMs ? $"{keys[i] * 1000f:0} ms" : keys[i].ToString("0.00", CultureInfo.InvariantCulture);
            sb.AppendLine($"<rect x='{F(lx)}' y='{F(yy - 10f)}' width='20' height='10' fill='{HueColor(i / (float)(keys.Length - 1))}'/>");
            sb.AppendLine($"<text x='{F(lx + 26f)}' y='{F(yy)}' font-size='12' fill='#111'>{label}</text>");
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static string BuildHtml(SimParams cfg, string svgContact, string svgTiming)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><head><meta charset='utf-8'>");
        sb.AppendLine("<meta http-equiv='refresh' content='1'>");
        sb.AppendLine("<title>Flipper launch cone</title>");
        sb.AppendLine("<style>body{font-family:sans-serif;margin:16px;background:#fff;color:#111}" +
                      "table{border-collapse:collapse;font-size:13px;margin-bottom:12px}td,th{border:1px solid #ccc;padding:3px 8px;text-align:right}" +
                      ".wrap{display:flex;gap:20px;align-items:flex-start;flex-wrap:wrap}" +
                      ".col h3{margin:0 0 4px}svg{width:460px;height:auto;border:1px solid #ddd}</style></head><body>");
        sb.AppendLine($"<h2>Flipper cones <span style='font-weight:normal;font-size:13px;color:#888'>· updated {DateTime.Now:HH:mm:ss} · edit flipper_params.json (auto-refresh)</span></h2>");

        sb.AppendLine($"<p><b>maxBallSpeed</b> = {cfg.MaxBallSpeed:0} &nbsp;|&nbsp; ");
        foreach (var p in cfg.Flippers)
            sb.Append($"<b>{p.Label}</b>: rest {p.RestAngleDeg:0.#}° swing {p.ActivatedOffsetDeg:0.#}° len {p.LengthPx:0} actSpd {p.ActivationSpeed:0} fan {p.LaunchFanDeg:0.#}° &nbsp; ");
        sb.AppendLine("</p>");

        sb.AppendLine("<div class='wrap'>");
        sb.AppendLine($"<div class='col'><h3>Flip-timing (catch &amp; shoot — the real cone)</h3>{svgTiming}</div>");
        sb.AppendLine($"<div class='col'><h3>Contact-position (cradle &amp; flick)</h3>{svgContact}</div>");
        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }

    private static void DrawFlipper(StringBuilder sb, PinballFlipper f, float angle, string col, string width, bool dashed = false)
    {
        var tip = f.HingePosition + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * f.Length;
        string dash = dashed ? " stroke-dasharray='5,4'" : "";
        sb.AppendLine($"<line x1='{F(f.HingePosition.X)}' y1='{F(f.HingePosition.Y)}' x2='{F(tip.X)}' y2='{F(tip.Y)}' stroke='{col}' stroke-width='{width}'{dash} stroke-linecap='round'/>");
    }

    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string HueColor(float t)
    {
        float hue = 240f * (1f - t);
        HsvToRgb(hue, 0.85f, 0.9f, out int r, out int g, out int b);
        return $"rgb({r},{g},{b})";
    }

    private static void HsvToRgb(float h, float s, float v, out int r, out int g, out int b)
    {
        float c = v * s;
        float x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f));
        float m = v - c;
        float rf, gf, bf;
        if      (h < 60f)  { rf = c; gf = x; bf = 0; }
        else if (h < 120f) { rf = x; gf = c; bf = 0; }
        else if (h < 180f) { rf = 0; gf = c; bf = x; }
        else if (h < 240f) { rf = 0; gf = x; bf = c; }
        else if (h < 300f) { rf = x; gf = 0; bf = c; }
        else               { rf = c; gf = 0; bf = x; }
        r = (int)((rf + m) * 255f);
        g = (int)((gf + m) * 255f);
        b = (int)((bf + m) * 255f);
    }
}
