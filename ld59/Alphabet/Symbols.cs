public class Symbol
{
    public string Name;
    public int Tier;
    public string TexturePath;
    public int RowOrder;
}

public static class SymbolDictionary
{
    private static string SymbolTextureBasePath = "images/symbols/";

    // Tier 1
    public static Symbol Lith = new Symbol { Name = "Lith", Tier = 1, TexturePath = SymbolTextureBasePath + "lith.png", RowOrder = 1 };

    // Tier 2
    public static Symbol Axe = new Symbol { Name = "Axe", Tier = 2, TexturePath = SymbolTextureBasePath + "axe.png", RowOrder = 1 };
    public static Symbol Phi = new Symbol { Name = "Phi", Tier = 2, TexturePath = SymbolTextureBasePath + "phi.png", RowOrder = 2 };

    // Tier 3 
    public static Symbol Omam = new Symbol { Name = "Omam", Tier = 3, TexturePath = SymbolTextureBasePath + "omam.png", RowOrder = 1 };
    public static Symbol Squid = new Symbol { Name = "Squid", Tier = 3, TexturePath = SymbolTextureBasePath + "squid.png", RowOrder = 2 };
    public static Symbol Medal = new Symbol { Name = "Medal", Tier = 3, TexturePath = SymbolTextureBasePath + "medal.png", RowOrder = 3 };

    // Tier 4
    public static Symbol Moon = new Symbol { Name = "Moon", Tier = 4, TexturePath = SymbolTextureBasePath + "moon.png", RowOrder = 1 };
    public static Symbol Horns = new Symbol { Name = "Horns", Tier = 4, TexturePath = SymbolTextureBasePath + "horns.png", RowOrder = 2 };
    public static Symbol Kurt = new Symbol { Name = "Kurt", Tier = 4, TexturePath = SymbolTextureBasePath + "kurt.png", RowOrder = 3 };
    public static Symbol Kite = new Symbol { Name = "Kite", Tier = 4, TexturePath = SymbolTextureBasePath + "kite.png", RowOrder = 4 };


    // Tier 5
    public static Symbol Mouth = new Symbol { Name = "Mouth", Tier = 5, TexturePath = SymbolTextureBasePath + "mouth.png", RowOrder = 1 };
    public static Symbol D = new Symbol { Name = "D", Tier = 5, TexturePath = SymbolTextureBasePath + "d.png", RowOrder = 2 };
    public static Symbol Chi = new Symbol { Name = "Chi", Tier = 5, TexturePath = SymbolTextureBasePath + "chi.png", RowOrder = 3 };
    public static Symbol Target = new Symbol { Name = "Target", Tier = 5, TexturePath = SymbolTextureBasePath + "target.png", RowOrder = 4 };
    public static Symbol Eye = new Symbol { Name = "Eye", Tier = 5, TexturePath = SymbolTextureBasePath + "eye.png", RowOrder = 5 };

    public static readonly System.Collections.Generic.List<Symbol> All = new()
    {
        Lith,
        Axe, Phi,
        Omam, Squid, Medal,
        Moon, Horns, Kurt, Kite,
        Mouth, D, Chi, Target, Eye,
    };
}