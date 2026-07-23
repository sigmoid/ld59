using System;
using System.IO;

namespace ld59.UI.Editor;

// Resolves the Content SOURCE directory (the one under version control, e.g. F:\Dev\LD59\ld59\
// Content) as opposed to Core.Content.RootDirectory, which points at the runtime copy under
// bin\...\Content. The editor must write to the source dir so edits survive a rebuild. Walking
// up from the running executable to find ld59.csproj works regardless of build configuration
// or whether the game was launched via `dotnet run` or a built exe.
public static class EditorPaths
{
    // Null if the source tree can't be located (e.g. a packaged/published build with no project
    // file alongside it). Callers should fall back to the runtime content dir and warn.
    public static string FindContentSourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ld59.csproj")))
                return Path.Combine(dir.FullName, "Content");
            dir = dir.Parent;
        }
        return null;
    }
}
