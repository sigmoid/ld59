using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;
using ld59.UI;

public class SolitaireUI : UIPanel
{
    private Rectangle _bounds;
    private Window    _rootWindow;
    private UIElement _activePanel;
    private volatile bool _solving;   // guards against overlapping solves

    // Live winnability read-out, re-evaluated after every move and drawn as text in the content panel.
    // Held behind an immutable snapshot so the background solver can publish it without locking.
    private volatile StatusInfo _status = new("Solving...", StatusSolving);

    // The engine + mode of the active game, and the StateVersion the current _status corresponds to.
    // Comparing against the live StateVersion each frame tells us when to re-solve.
    private SolitaireEngine  _engine;
    private SymbolsSolitaire _symbols;
    private int  _evaluatedVersion = -1;
    private bool _won;              // one-shot latch so a win is only counted once
    private int  _winCount;         // persisted tally, shown in the corner

    // Status colours, tuned to read as text on the dark-green felt.
    private static readonly Color StatusWinnable = new(150, 245, 150);
    private static readonly Color StatusNo       = new(255, 140, 140);
    private static readonly Color StatusUnknown  = new(220, 220, 220);
    private static readonly Color StatusSolving  = new(245, 215, 130);

    private sealed record StatusInfo(string Text, Color Color);

    public SolitaireUI(Rectangle bounds)
    {
        _bounds = bounds;
        _winCount = SolitaireStats.LoadWins();
        CreateUI();
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        _bounds = bounds;
    }

    public override Rectangle GetBoundingBox() => _bounds;
    public override void Update(float deltaTime) => base.Update(deltaTime);

    // Runs every frame on the main thread from the content panel's Update (this manager itself is never
    // added to the update loop): counts a fresh win once, then re-checks winnability.
    private void Tick()
    {
        if (_engine == null) return;

        if (_engine.IsWon && !_won)
        {
            _won = true;
            _winCount++;
            SolitaireStats.SaveWins(_winCount);
        }

        EvaluateWinnabilityIfChanged();
    }

    // Re-solves the current board whenever it has changed since the last evaluation, publishing the
    // verdict to _status for the content panel to draw. One solve at a time; a move made mid-solve is
    // picked up on a later frame once the in-flight solve finishes.
    private void EvaluateWinnabilityIfChanged()
    {
        if (_symbols == null || _solving) return;
        if (_engine.StateVersion == _evaluatedVersion) return;
        _evaluatedVersion = _engine.StateVersion;

        if (_engine.IsWon)
        {
            _status = new StatusInfo("Solved!", StatusWinnable);
            return;
        }

        _solving = true;
        _status  = new StatusInfo("Solving...", StatusSolving);
        var problem = _symbols.BuildSolverProblem();
        System.Threading.Tasks.Task.Run(() =>
        {
            var result = SymbolsSolver.Solve(problem);
            _status  = ToStatus(result);
            _solving = false;
        });
    }

    // Only reports whether the board is winnable — deliberately not how (no move hints for now).
    private static StatusInfo ToStatus(SymbolsSolver.Result result) => result.Outcome switch
    {
        SymbolsSolver.Outcome.Winnable   => new StatusInfo("Winnable", StatusWinnable),
        SymbolsSolver.Outcome.Unwinnable => new StatusInfo("Not winnable", StatusNo),
        _                                => new StatusInfo("Unknown", StatusUnknown),
    };

    private void CreateUI()
    {
        _rootWindow = new Window(_bounds, "Solitaire", Core.DefaultFont);
        _rootWindow.SetColors(ColorPalette.ActualWhite, ColorPalette.Black, ColorPalette.ActualWhite, ColorPalette.Black);
        _rootWindow.SetCloseButtonColors(ColorPalette.Black, Color.DarkGray);
        TaskbarRegistry.Register("Solitaire", Core.Content.Load<Texture2D>("images/file_icon"), _rootWindow);

        // Demo build: open straight into the Symbols game, skipping mode selection.
        NewGame();
        Core.UISystem.AddElement(_rootWindow);
    }

    // Deals a fresh Symbols game, replacing the active board. Also the Reset button's action.
    private void NewGame()
    {
        if (_activePanel != null)
            _rootWindow.RemoveChild(_activePanel);

        var contentBounds = _rootWindow.GetContentBounds();
        var mode   = new SymbolsSolitaire(columnCount: 6, freeCellCount: 2);
        var engine = new SolitaireEngine(mode, contentBounds.Width);

        _engine  = engine;
        _symbols = mode;
        _evaluatedVersion = -1;   // force an initial evaluation on the first frame
        _won     = false;
        _status  = new StatusInfo("Solving...", StatusSolving);

        // The panel is updated every frame (this manager is not); it drives our per-frame tick and draws
        // the read-out, win count, reset button, and win overlay.
        _activePanel = new SolitaireContentPanel(contentBounds, engine, Tick, NewGame,
            () => _status, () => _winCount);
        _rootWindow.AddChild(_activePanel);
    }

    // ── Game panel ────────────────────────────────────────────────────────────

    private sealed class SolitaireContentPanel : UIElement
    {
        private const int Margin       = 14;
        private const int ButtonWidth  = 130;
        private const int ButtonHeight = 34;
        private const float AutoReturnSeconds = 4f;   // deal a new game this long after a win

        private Rectangle            _bounds;
        private readonly SolitaireEngine _engine;
        private readonly SpriteFont  _font;
        private readonly SpriteFont  _uiFont;
        private readonly Texture2D   _bg;
        private readonly Texture2D   _white;
        private readonly Action      _onUpdate;
        private readonly Action      _onReset;
        private readonly Func<StatusInfo> _statusProvider;   // null when the mode has no solver read-out
        private readonly Func<int>   _winCountProvider;

        private Rectangle   _resetBounds;
        private bool        _resetHovered;
        private ButtonState _prevLeft;
        private float       _winFade;   // 0..1 alpha of the victory overlay
        private float       _winHold;   // seconds the victory screen has been shown

        public SolitaireContentPanel(Rectangle bounds, SolitaireEngine engine, Action onUpdate,
            Action onReset, Func<StatusInfo> statusProvider, Func<int> winCountProvider)
        {
            _bounds      = bounds;
            _engine      = engine;
            _onUpdate    = onUpdate;
            _onReset     = onReset;
            _statusProvider   = statusProvider;
            _winCountProvider = winCountProvider;
            _font        = Core.Content.Load<SpriteFont>("fonts/PlayingCard");
            _uiFont      = Core.DefaultFont;
            _bg          = new Texture2D(Core.GraphicsDevice, 1, 1);
            _bg.SetData(new[] { new Color(35, 120, 35) });
            _white       = new Texture2D(Core.GraphicsDevice, 1, 1);
            _white.SetData(new[] { Color.White });
            LayoutWidgets();
        }

        public override Rectangle GetBoundingBox() => _bounds;
        public override void SetBounds(Rectangle bounds) { _bounds = bounds; LayoutWidgets(); }

        private void LayoutWidgets()
        {
            _resetBounds = new Rectangle(
                _bounds.Right - ButtonWidth - Margin, _bounds.Y + Margin, ButtonWidth, ButtonHeight);
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _onUpdate?.Invoke();

            var mouse = Mouse.GetState();
            var pt    = Core.GetTransformedMousePoint();
            bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevLeft == ButtonState.Released;

            if (_engine.IsWon)
            {
                // Victory screen: deal a new game on a click, or automatically after a short pause.
                _winHold += deltaTime;
                if (clicked || _winHold >= AutoReturnSeconds)
                    _onReset?.Invoke();
            }
            else
            {
                _resetHovered = _resetBounds.Contains(pt);
                if (_resetHovered && clicked)
                    _onReset?.Invoke();

                // Suppress card interaction while the cursor is over the reset button.
                if (!_resetHovered)
                    _engine.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(deltaTime)), new Vector2(_bounds.X, _bounds.Y));
            }
            _prevLeft = mouse.LeftButton;

            // Ease the victory overlay in while won; snap back out on a fresh deal.
            float target = _engine.IsWon ? 1f : 0f;
            _winFade = MathHelper.Clamp(_winFade + (target - _winFade) * Math.Min(1f, deltaTime * 3f), 0f, 1f);
            if (target == 0f && _winFade < 0.01f) { _winFade = 0f; _winHold = 0f; }
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            spriteBatch.End();

            var rasterizer  = new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None };
            var origScissor = Core.GraphicsDevice.ScissorRectangle;
            Core.GraphicsDevice.ScissorRectangle = _bounds;

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, rasterizer);

            spriteBatch.Draw(_bg, _bounds, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
            _engine.Draw(spriteBatch, _font, 0f, new Vector2(_bounds.X, _bounds.Y));

            spriteBatch.End();

            Core.GraphicsDevice.ScissorRectangle = origScissor;
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);

            DrawWinCount(spriteBatch);
            DrawStatus(spriteBatch);
            DrawResetButton(spriteBatch);
            DrawWinOverlay(spriteBatch);
        }

        // Persisted win tally, bottom-left corner — clear of the free-cell row at the top.
        private void DrawWinCount(SpriteBatch spriteBatch)
        {
            if (_winCountProvider == null) return;
            var text = $"Wins: {_winCountProvider()}";
            var size = _uiFont.MeasureString(text);
            spriteBatch.DrawString(_uiFont, text, new Vector2(_bounds.X + Margin, _bounds.Bottom - Margin - size.Y),
                new Color(230, 240, 230), 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.91f);
        }

        // The live winnability read-out — plain text under the reset button, right-aligned: a muted
        // "Winnable?" label above the verdict, coloured by outcome. Refreshed after every move.
        private void DrawStatus(SpriteBatch spriteBatch)
        {
            if (_statusProvider == null) return;
            var status = _statusProvider();

            const string label = "Winnable?";
            float lineH = _uiFont.LineSpacing;
            float right = _bounds.Right - Margin;
            float top   = _resetBounds.Bottom + 10;

            var labelSize = _uiFont.MeasureString(label);
            spriteBatch.DrawString(_uiFont, label, new Vector2(right - labelSize.X, top),
                new Color(200, 220, 200), 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.91f);

            var verdictSize = _uiFont.MeasureString(status.Text);
            spriteBatch.DrawString(_uiFont, status.Text, new Vector2(right - verdictSize.X, top + lineH + 2),
                status.Color, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.91f);
        }

        private void DrawResetButton(SpriteBatch spriteBatch)
        {
            var bgColor = _resetHovered ? new Color(210, 235, 210) : Color.White;
            spriteBatch.Draw(_white, _resetBounds, null, bgColor, 0f, Vector2.Zero, SpriteEffects.None, 0.9f);

            const string label = "Reset";
            var size = _uiFont.MeasureString(label);
            var pos  = new Vector2(
                _resetBounds.X + (_resetBounds.Width  - size.X) * 0.5f,
                _resetBounds.Y + (_resetBounds.Height - size.Y) * 0.5f);
            spriteBatch.DrawString(_uiFont, label, pos, Color.Black, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.91f);
        }

        // Victory: fade a black veil over the board with big centred "YOU WIN!" text.
        private void DrawWinOverlay(SpriteBatch spriteBatch)
        {
            if (_winFade <= 0f) return;

            spriteBatch.Draw(_white, _bounds, null, Color.Black * (_winFade * 0.75f), 0f, Vector2.Zero, SpriteEffects.None, 0.95f);

            const string text  = "YOU WIN!";
            const float  scale = 4f;
            var size = _uiFont.MeasureString(text) * scale;
            var pos  = new Vector2(
                _bounds.X + (_bounds.Width  - size.X) * 0.5f,
                _bounds.Y + (_bounds.Height - size.Y) * 0.5f);
            spriteBatch.DrawString(_uiFont, text, pos, Color.White * _winFade, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.96f);

            const string hint = "Click to play again";
            var hintSize = _uiFont.MeasureString(hint);
            var hintPos  = new Vector2(
                _bounds.X + (_bounds.Width - hintSize.X) * 0.5f,
                pos.Y + size.Y + 24);
            spriteBatch.DrawString(_uiFont, hint, hintPos, Color.White * (_winFade * 0.85f), 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.96f);
        }
    }
}
