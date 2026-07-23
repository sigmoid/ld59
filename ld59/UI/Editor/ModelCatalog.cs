using System;
using System.Collections.Generic;
using System.IO;

namespace ld59.UI.Editor;

// Lists models actually built into the content pipeline (so the browser only offers models that
// will successfully Content.Load<Model>), by reading Content.mgcb's /build:models/*.fbx entries.
// Falls back to raw *.fbx files on disk (less precise -- could include unbuilt models) if the
// .mgcb can't be found, e.g. a published build with no project source tree alongside it.
public static class ModelCatalog
{
    public static List<string> ListBuiltModels(string contentSourceDir)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(contentSourceDir)) return result;

        string mgcb = Path.Combine(contentSourceDir, "Content.mgcb");
        if (File.Exists(mgcb))
        {
            foreach (var raw in File.ReadLines(mgcb))
            {
                var line = raw.Trim();
                if (!line.StartsWith("/build:models/", StringComparison.OrdinalIgnoreCase)) continue;
                if (!line.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;

                string rel = line.Substring("/build:".Length);
                result.Add(rel.Substring(0, rel.Length - ".fbx".Length));
            }
        }
        else
        {
            string modelsDir = Path.Combine(contentSourceDir, "models");
            if (Directory.Exists(modelsDir))
                foreach (var f in Directory.GetFiles(modelsDir, "*.fbx"))
                    result.Add("models/" + Path.GetFileNameWithoutExtension(f));
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }
}
