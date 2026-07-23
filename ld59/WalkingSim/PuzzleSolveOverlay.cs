using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;
using ld59.UI;
using ld59.UI.Powergrid;

namespace ld59.WalkingSim;

// Focused modal for solving a puzzle panel. Hosts the puzzle's PowergridView at a large centered
// rect with a live cursor (the walk camera capture is suspended while it's open). Polls the
// controller for completion; on solve it matches the solution to an Outcome and fires the effect,
// then closes. Tab closes without solving. (Escape can't be used — it quits the game.)
public class PuzzleSolveOverlay : UIElement
{
    private readonly PuzzlePanelComponent _panel;
    private readonly UI3DScene _walkView;
    private readonly Scene _walkScene;
    private readonly PowergridView _view;
    private readonly Texture2D _pixel;
    private Rectangle _viewBounds;

    private bool _wasSolved;
    private bool _prevTab;
    private bool _closed;

    // Puzzle rect = the walk window's content bounds inset by a margin, recomputed each frame so
    // the puzzle follows the window when it is dragged/resized.
    private Rectangle ComputeViewBounds()
    {
        var wb = _walkView.GetBoundingBox();
        int mx = System.Math.Max(24, (int)(wb.Width  * 0.06f));
        int my = System.Math.Max(24, (int)(wb.Height * 0.06f));
        return new Rectangle(wb.X + mx, wb.Y + my, wb.Width - 2 * mx, wb.Height - 2 * my);
    }

    public PuzzleSolveOverlay(PuzzlePanelComponent panel, UI3DScene walkView, Scene walkScene)
    {
        _panel = panel;
        _walkView = walkView;
        _walkScene = walkScene;
        Order = 0.99f; // draw on top of the walk window

        _viewBounds = ComputeViewBounds();
        _view = _panel.GetOrCreateView(_viewBounds);
        _wasSolved = _panel.IsSolved;

        _pixel = new Texture2D(Core.GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });

        _panel.SurfaceSuspended = true;   // freeze the on-object render while solving on-screen
        _walkView.SuspendCapture();        // free the cursor for the puzzle
        _walkView.Removed += Close;        // close with the walking-sim window
    }

    public override void Update(float deltaTime)
    {
        if (_closed) return;

        _viewBounds = ComputeViewBounds();   // follow the window if it moved
        _view.SetBounds(_viewBounds);
        _view.Update(deltaTime);

        // Tab leaves the puzzle without solving.
        bool tab = Keyboard.GetState().IsKeyDown(Keys.Tab);
        if (tab && !_prevTab) { Close(); return; }
        _prevTab = tab;

        // Rising edge of solved: match the solution to an outcome and fire it.
        bool solved = _panel.IsSolved;
        if (solved && !_wasSolved)
        {
            var outcome = _panel.Match(_panel.ReadSolution());
            if (outcome.HasValue)
            {
                var o = outcome.Value;
                InteractionDispatcher.Dispatch(
                    new Interactable3DComponent { Action = o.Action, Target = o.Target, Message = o.Message },
                    _walkScene);
            }
            Close();
        }
        _wasSolved = solved;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (_closed) return;

        // Lightly dim the rest of the walk window so the puzzle stands out, but keep the margin
        // readable (the environment still shows through). Then an opaque panel behind the puzzle.
        _viewBounds = ComputeViewBounds();
        var wb = _walkView.GetBoundingBox();
        spriteBatch.Draw(_pixel, wb, Color.Black * 0.35f);
        var frame = _viewBounds; frame.Inflate(3, 3);
        spriteBatch.Draw(_pixel, frame, new Color(60, 66, 82));         // thin border
        spriteBatch.Draw(_pixel, _viewBounds, new Color(16, 18, 26));   // puzzle backdrop
        _view.Draw(spriteBatch);

        var font = Core.DefaultFont;
        const string hint = "Press Tab to leave";
        var size = font.MeasureString(hint);
        spriteBatch.DrawString(font, hint,
            new Vector2(_viewBounds.Center.X - size.X / 2, _viewBounds.Bottom + 4), Color.White * 0.85f);
    }

    // Close from outside (e.g. the walking-sim window is closing).
    public void ForceClose() => Close();

    private void Close()
    {
        if (_closed) return;
        _closed = true;
        _walkView.Removed -= Close;
        _panel.SurfaceSuspended = false;
        _walkView.ResumeCapture();
        Core.UISystem.RemoveElement(this);
    }

    public override void OnRemovedFromUI()
    {
        _pixel?.Dispose();
    }

    public override Rectangle GetBoundingBox() => _viewBounds;
}
