using System.Text;
using Quartz.Components;

namespace ld59.UI.Powergrid;

/// <summary>Horizontal anchoring of a label relative to its entity position.</summary>
public enum TextAlign { Left, Center, Right }

/// <summary>
/// A free-standing text label placed in the puzzle world — flavour text, a hint, a section title.
/// Purely decorative: it takes no part in colouring, sequencing or region counting, and is drawn in
/// both edit and play mode at the entity's world position, scaling with the camera.
///
/// Authors add labels via the Text editor tool (click to place) and type the content in the
/// inspector. The body rides in the component Data blob rather than a property attribute so it can
/// hold commas and line breaks; newlines are escaped as "\n" (and backslashes as "\\").
/// </summary>
public class PowergridTextComponent : Component
{
    /// <summary>Size multiplier applied on top of the camera zoom (1 = the node-label baseline).</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>Which edge of the text box sits on the entity position.</summary>
    public TextAlign Align { get; set; } = TextAlign.Center;

    /// <summary>The label body. May contain line breaks.</summary>
    public string Text = string.Empty;

    public override string SerializeData() => Escape(Text);

    public override void DeserializeData(string data) => Text = Unescape(data);

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static string Unescape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i++;
                sb.Append(s[i] == 'n' ? '\n' : s[i]);
            }
            else
            {
                sb.Append(s[i]);
            }
        }
        return sb.ToString();
    }
}
