using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;

namespace ld59.UI.Scene3D;

/// <summary>
/// Rising-edge detection for hotkeys, replacing a field-per-key pile of <c>_prevXKey</c> bools.
/// <para>
/// Call <see cref="Pressed"/> once per frame for every key you care about, unconditionally, and
/// apply gating (text focus, editor mode, ...) to the RESULT -- the tracker only sees a key as
/// held once it has been polled, so skipping a poll would make the key re-fire on the next one.
/// </para>
/// </summary>
public sealed class KeyEdgeTracker
{
    private readonly HashSet<Keys> _down = new();

    /// <summary>True on the frame <paramref name="key"/> transitions from up to down.</summary>
    public bool Pressed(KeyboardState keyboard, Keys key) => Edge(key, keyboard.IsKeyDown(key));

    /// <summary>
    /// Edge detection for a chord or any other computed condition, tracked under
    /// <paramref name="key"/>'s slot (e.g. Ctrl+S -> <c>Edge(Keys.S, ctrl &amp;&amp; sDown)</c>).
    /// </summary>
    public bool Edge(Keys key, bool isDown)
    {
        bool was = isDown ? !_down.Add(key) : _down.Remove(key);
        return isDown && !was;
    }
}
