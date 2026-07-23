using System;
using System.Collections.Generic;
using System.IO;

namespace ld59.UI.Editor;

// Lists prefab XML files under Content/files/prefabs (content-relative "files/prefabs/name.xml"
// paths, loadable via Entity.FromContentFile). Seeds a couple of example prefabs the first time
// the folder doesn't exist, so the content browser's Prefabs tab isn't empty on a fresh checkout.
public static class PrefabCatalog
{
    private const string RelDir = "files/prefabs";

    public static List<string> ListPrefabs(string contentSourceDir)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(contentSourceDir)) return result;

        string dir = Path.Combine(contentSourceDir, "files", "prefabs");
        EnsureSeeded(dir);
        if (!Directory.Exists(dir)) return result;

        foreach (var f in Directory.GetFiles(dir, "*.xml"))
            result.Add($"{RelDir}/{Path.GetFileName(f)}");

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static void EnsureSeeded(string dir)
    {
        if (Directory.Exists(dir)) return;
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "point_light.xml"), """
            <Entity Name="PointLight">
              <Position3D><X>0</X><Y>0</Y><Z>0</Z></Position3D>
              <Component Type="PointLight">
                <Property Name="Color" Value="1,1,1,1" Type="Color"/>
                <Property Name="Intensity" Value="2" Type="Single"/>
                <Property Name="Range" Value="15" Type="Single"/>
              </Component>
            </Entity>
            """);

        File.WriteAllText(Path.Combine(dir, "warm_point_light.xml"), """
            <Entity Name="WarmPointLight">
              <Position3D><X>0</X><Y>0</Y><Z>0</Z></Position3D>
              <Component Type="PointLight">
                <Property Name="Color" Value="1,0.85,0.6,1" Type="Color"/>
                <Property Name="Intensity" Value="2.5" Type="Single"/>
                <Property Name="Range" Value="12" Type="Single"/>
              </Component>
            </Entity>
            """);
    }
}
