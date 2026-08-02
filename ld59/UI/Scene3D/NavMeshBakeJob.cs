using System;
using System.Threading.Tasks;
using Quartz;
using ld59.WalkingSim;

namespace ld59.UI.Scene3D;

/// <summary>
/// In-process navmesh bake. Geometry is gathered on the main thread (GPU vertex-buffer readback
/// isn't thread-safe), then Recast -- the slow part -- runs on a background thread so the game
/// keeps rendering. <see cref="Poll"/> is called every frame from the main thread and, once the
/// background work completes, raises <see cref="Succeeded"/> so the view can rebind the walker and
/// rebuild the overlay, then persists the OBJ.
/// </summary>
public sealed class NavMeshBakeJob
{
    /// <summary>Absolute path the baked navmesh OBJ is written to (Content source dir). If null,
    /// a bake still updates the live mesh + overlay but doesn't persist to disk.</summary>
    public string SavePath { get; set; }

    // Last bake outcome, for the navmesh panel to display.
    public int LastSourceTris { get; private set; }
    public int LastNavTris { get; private set; }
    public string LastError { get; private set; }
    public bool IsRunning => _task != null;

    public event Action Started;
    public event Action Completed;
    /// <summary>Raised on the main thread with a finished bake, before it is persisted.</summary>
    public event Action<SceneNavBaker.Result> Succeeded;

    private Task<SceneNavBaker.Result> _task;

    /// <summary>Gather the scene's geometry and kick off Recast. No-op while a bake is running.</summary>
    public void Start(Scene scene, float sceneScale)
    {
        if (IsRunning) return;

        NavMeshBaker.TriangleSoup soup;
        int sourceTris;
        try
        {
            soup = SceneNavBaker.GatherSoup(scene, sceneScale, out sourceTris);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Console.WriteLine($"[bake] failed (gather): {ex.Message}");
            Completed?.Invoke();
            return;
        }

        var p = NavMeshBaker.BakeParams.Default;
        Console.WriteLine("[bake] running Recast on a background thread...");
        Started?.Invoke();
        _task = Task.Run(() => SceneNavBaker.BakeFromSoup(soup, p, sourceTris));
    }

    /// <summary>Finalize a finished bake on the main thread. Cheap no-op otherwise.</summary>
    public void Poll()
    {
        if (_task == null || !_task.IsCompleted) return;

        var task = _task;
        _task = null;   // clears IsRunning

        if (task.IsFaulted)
        {
            LastError = task.Exception?.GetBaseException().Message ?? "unknown error";
            Console.WriteLine($"[bake] failed: {LastError}");
            Completed?.Invoke();
            return;
        }

        var result = task.Result;
        Succeeded?.Invoke(result);

        LastSourceTris = result.SourceTris;
        LastNavTris    = result.NavMesh.Triangles.Length;
        LastError      = null;

        if (!string.IsNullOrEmpty(SavePath))
        {
            SceneNavBaker.WriteObj(result.NavSoup, SavePath);
            Console.WriteLine($"[bake] {result.SourceTris} src tris -> {LastNavTris} nav tris; wrote {SavePath}");
        }
        else
        {
            Console.WriteLine($"[bake] {result.SourceTris} src tris -> {LastNavTris} nav tris (not persisted)");
        }
        Completed?.Invoke();
    }
}
