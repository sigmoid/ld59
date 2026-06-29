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
    public static Symbol Phi = new Symbol { Name = "Phi", Tier = 2, TexturePath = SymbolTextureBasePath + "phi.png", RowOrder = 1 };
    public static Symbol Axe = new Symbol { Name = "Axe", Tier = 2, TexturePath = SymbolTextureBasePath + "axe.png", RowOrder = 2 };

    // Tier 3 
    public static Symbol Omam = new Symbol { Name = "Omam", Tier = 3, TexturePath = SymbolTextureBasePath + "omam.png", RowOrder = 1 };
    public static Symbol Squid = new Symbol { Name = "Squid", Tier = 3, TexturePath = SymbolTextureBasePath + "squid.png", RowOrder = 2 };
    public static Symbol Medal = new Symbol { Name = "Medal", Tier = 3, TexturePath = SymbolTextureBasePath + "medal.png", RowOrder = 3 };

    // Tier 4
    public static Symbol Moon = new Symbol { Name = "Left Moon", Tier = 4, TexturePath = SymbolTextureBasePath + "moon.png", RowOrder = 1 };
    public static Symbol Horns = new Symbol { Name = "Left Gibbous", Tier = 4, TexturePath = SymbolTextureBasePath + "left-gibbous.png", RowOrder = 2 };
    public static Symbol Kurt = new Symbol { Name = "Right Gibbous", Tier = 4, TexturePath = SymbolTextureBasePath + "right-gibbous.png", RowOrder = 3 };
    public static Symbol Kite = new Symbol { Name = "Right Moon", Tier = 4, TexturePath = SymbolTextureBasePath + "right-moon.png", RowOrder = 4 };


    // Tier 5
    public static Symbol Mouth = new Symbol { Name = "Left L", Tier = 5, TexturePath = SymbolTextureBasePath + "left-l.png", RowOrder = 1 };
    public static Symbol D = new Symbol { Name = "Left Lambda", Tier = 5, TexturePath = SymbolTextureBasePath + "left-lambda.png", RowOrder = 2 };
    public static Symbol Chi = new Symbol { Name = "Kurt", Tier = 5, TexturePath = SymbolTextureBasePath + "kurt.png", RowOrder = 3 };
    public static Symbol Target = new Symbol { Name = "Right Lambda", Tier = 5, TexturePath = SymbolTextureBasePath + "right-lambda.png", RowOrder = 4 };
    public static Symbol Eye = new Symbol { Name = "Right L", Tier = 5, TexturePath = SymbolTextureBasePath + "right-l.png", RowOrder = 5 };

    public static readonly System.Collections.Generic.List<Symbol> All = new()
    {
        Lith,
        Axe, Phi,
        Omam, Squid, Medal,
        Moon, Horns, Kurt, Kite,
        Mouth, D, Chi, Target, Eye,
    };
}