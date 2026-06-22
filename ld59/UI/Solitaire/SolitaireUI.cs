using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;
using ld59.UI;

public class SolitaireUI : UIPanel
{
    // Register new game modes here — each entry is a factory so every game gets a fresh instance
    private static readonly (string Name, Func<SolitaireGameMode> Create)[] AvailableModes =
    {
        ("Klondike", () => new KlondikeSolitaire()),
        ("FreeCell", () => new FreeCellSolitaire(columnCount: 8, freeCellCount: 4)),
        ("Symbols",  () => new SymbolsSolitaire(columnCount: 7, freeCellCount: 1)),
    };

    private Rectangle _bounds;
    private Window    _rootWindow;
    private UIElement _activePanel;
    private volatile string _pendingTitle;   // set by the background solver, applied on the main thread
    private volatile Action _pendingAction;  // queued work (e.g. applying a solved move) for the main thread
    private volatile bool   _solving;        // guards against overlapping solves

    // Cached solution for click-by-click stepping (main-thread only). Valid while the player hasn't
    // moved (tracked via the engine's MoveVersion).
    private SymbolsSolitaire _stepMode;
    private List<SymbolsSolver.Move> _stepMoves;
    private IReadOnlyList<SolitaireStack> _stepColumns;   // fixed snapshot of active columns at solve time
    private IReadOnlyList<SolitaireStack> _stepCells;
    private int _stepIndex;
    private int _stepVersion;

    public SolitaireUI(Rectangle bounds)
    {
        _bounds = bounds;
        CreateUI();
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        _bounds = bounds;
    }

    public override Rectangle GetBoundingBox() => _bounds;
    public override void Update(float deltaTime) => base.Update(deltaTime);

    // Applies work produced by the background solver (title text, queued move). Called on the main
    // thread from the content panel's Update (this manager itself is never added to the update loop).
    private void ApplyPendingWork()
    {
        var pending = _pendingTitle;
        if (pending != null)
        {
            _rootWindow.SetTitle(pending);
            _pendingTitle = null;
        }

        var action = _pendingAction;
        if (action != null)
        {
            _pendingAction = null;
            action();
        }
    }

    private void CreateUI()
    {
        _rootWindow = new Window(_bounds, "Solitaire", Core.DefaultFont);
        _rootWindow.SetColors(ColorPalette.ActualWhite, ColorPalette.Black, ColorPalette.ActualWhite, ColorPalette.Black);
        _rootWindow.SetCloseButtonColors(ColorPalette.Black, Color.DarkGray);
        TaskbarRegistry.Register("Solitaire", Core.Content.Load<Texture2D>("images/file_icon"), _rootWindow);

        ShowModeSelect();
        Core.UISystem.AddElement(_rootWindow);
    }

    private void ShowModeSelect()
    {
        if (_activePanel != null)
            _rootWindow.RemoveChild(_activePanel);

        _activePanel = new ModeSelectPanel(_rootWindow.GetContentBounds(), StartGame);
        _rootWindow.AddChild(_activePanel);
    }

    private void StartGame(Func<SolitaireGameMode> modeFactory)
    {
        if (_activePanel != null)
            _rootWindow.RemoveChild(_activePanel);

        var contentBounds = _rootWindow.GetContentBounds();
        var mode = modeFactory();
        var engine = new SolitaireEngine(mode, contentBounds.Width);

        // For the Symbols experiment, offer solver-driven buttons and run an initial check.
        var symbols = mode as SymbolsSolitaire;
        var buttons = new List<(string Label, Action OnClick)>();
        if (symbols != null)
        {
            buttons.Add(("Check solvable", () => CheckSolvable(symbols)));
            buttons.Add(("Play next move", () => StepOrSolve(symbols, engine)));
        }

        // The panel is updated every frame (this manager is not), so let it apply pending solver work.
        _activePanel = new SolitaireContentPanel(contentBounds, engine, ApplyPendingWork, buttons);
        _rootWindow.AddChild(_activePanel);

        ClearStepCache();
        if (symbols != null)
            CheckSolvable(symbols);
    }

    // Solves the current Symbols board in the background and reports the verdict in the title. Does
    // not touch the step cache (checking solvability doesn't change the board).
    private void CheckSolvable(SymbolsSolitaire symbols)
    {
        if (_solving) return;
        _solving = true;

        var problem = symbols.BuildSolverProblem();
        _rootWindow.SetTitle("Solitaire - solving...");
        System.Threading.Tasks.Task.Run(() =>
        {
            var result = SymbolsSolver.Solve(problem);
            _pendingTitle = $"Solitaire - {result}";
            _solving = false;
        });
    }

    // Plays the next move. If a cached solution from a previous solve is still valid (the player
    // hasn't moved since), step it instantly; otherwise solve once, cache the solution, and play its
    // first move. Subsequent presses then just walk the cache until the player makes their own move.
    private void StepOrSolve(SymbolsSolitaire symbols, SolitaireEngine engine)
    {
        bool cacheValid = _stepMode == symbols && _stepMoves != null
                       && _stepIndex < _stepMoves.Count && _stepVersion == engine.MoveVersion;

        if (cacheValid && TryApplyCachedMove(engine, _stepMoves[_stepIndex]))
        {
            _stepIndex++;
            _stepVersion = engine.MoveVersion;   // ApplyMove doesn't bump it; keep them equal
            _rootWindow.SetTitle($"Solitaire - move {_stepIndex} of {_stepMoves.Count}");
            return;
        }

        // No usable cache: solve, cache, and play the first move.
        if (_solving) return;
        _solving = true;
        ClearStepCache();

        int requestVersion = engine.MoveVersion;
        var problem = symbols.BuildSolverProblem();
        _rootWindow.SetTitle("Solitaire - finding solution...");
        System.Threading.Tasks.Task.Run(() =>
        {
            var result = SymbolsSolver.Solve(problem);
            _pendingAction = () => BeginStepping(symbols, engine, result, requestVersion);
            _solving = false;
        });
    }

    // Main-thread: caches a freshly solved solution and plays its first move (unless the player moved
    // while we were solving, in which case the result is stale and discarded).
    private void BeginStepping(SymbolsSolitaire symbols, SolitaireEngine engine, SymbolsSolver.Result result, int requestVersion)
    {
        if (result.Outcome != SymbolsSolver.Outcome.Winnable || result.Moves.Count == 0)
        {
            _rootWindow.SetTitle($"Solitaire - {result}");
            return;
        }
        if (engine.MoveVersion != requestVersion)
        {
            _rootWindow.SetTitle("Solitaire - board changed, press again");
            return;
        }

        _stepMode    = symbols;
        _stepMoves   = result.Moves;
        _stepColumns = symbols.ActiveColumns;   // fixed snapshot so indices stay valid as columns complete
        _stepCells   = symbols.FreeCells;
        _stepIndex   = 0;

        if (TryApplyCachedMove(engine, _stepMoves[0]))
            _stepIndex = 1;
        _stepVersion = engine.MoveVersion;
        _rootWindow.SetTitle($"Solitaire - move {_stepIndex} of {_stepMoves.Count}");
    }

    private void ClearStepCache()
    {
        _stepMode    = null;
        _stepMoves   = null;
        _stepColumns = null;
        _stepCells   = null;
        _stepIndex   = 0;
    }

    // Applies one cached solver move against the fixed column snapshot, validating it still matches
    // the live board. Returns false (so the caller re-solves) if anything has drifted.
    private bool TryApplyCachedMove(SolitaireEngine engine, SymbolsSolver.Move move)
    {
        SolitaireStack from = move.FromColumn >= 0
            ? (move.FromColumn < _stepColumns.Count ? _stepColumns[move.FromColumn] : null)
            : _stepCells.FirstOrDefault(c => c.Cards.Count > 0 && SymbolsSolitaire.Encode(c.Cards[^1]) == move.Card);

        SolitaireStack to = move.ToColumn >= 0
            ? (move.ToColumn < _stepColumns.Count ? _stepColumns[move.ToColumn] : null)
            : _stepCells.FirstOrDefault(c => c.Cards.Count == 0 && !c.IsCompleted);

        if (from == null || from.Cards.Count == 0 || SymbolsSolitaire.Encode(from.Cards[^1]) != move.Card) return false;
        if (to == null || to.IsCompleted) return false;

        engine.ApplyMove(from, from.Cards.Count - 1, to);
        return true;
    }

    // ── Mode selection ────────────────────────────────────────────────────────

    private sealed class ModeSelectPanel : UIPanel
    {
        private Rectangle      _bounds;
        private readonly Texture2D   _bg;
        private readonly SpriteFont  _font;

        private const int ButtonWidth  = 200;
        private const int ButtonHeight = 180;
        private const int ButtonGap    = 24;

        public ModeSelectPanel(Rectangle bounds, Action<Func<SolitaireGameMode>> onSelected)
        {
            _bounds = bounds;
            _font   = Core.DefaultFont;
            _bg     = new Texture2D(Core.GraphicsDevice, 1, 1);
            _bg.SetData(new[] { new Color(35, 120, 35) });

            var totalW = AvailableModes.Length * ButtonWidth + (AvailableModes.Length - 1) * ButtonGap;
            var startX = bounds.X + (bounds.Width - totalW) / 2;
            var startY = bounds.Y + (int)(bounds.Height * 0.48f);

            for (int i = 0; i < AvailableModes.Length; i++)
            {
                var (name, factory) = AvailableModes[i];
                var capturedFactory = factory;
                var btnBounds = new Rectangle(startX + i * (ButtonWidth + ButtonGap), startY, ButtonWidth, ButtonHeight);
                AddChild(new ModeButton(btnBounds, name, () => onSelected(capturedFactory)));
            }
        }

        public override Rectangle GetBoundingBox() => _bounds;
        public override void SetBounds(Rectangle bounds) => _bounds = bounds;

        public override void Draw(SpriteBatch spriteBatch)
        {
            // Felt background
            spriteBatch.Draw(_bg, _bounds, null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, GetActualOrder());

            // Title — scaled up
            const string title = "SOLITAIRE";
            const float  titleScale = 2.5f;
            var titleSize = _font.MeasureString(title) * titleScale;
            var titlePos  = new Vector2(
                _bounds.X + (_bounds.Width - titleSize.X) * 0.5f,
                _bounds.Y + (int)(_bounds.Height * 0.12f));
            spriteBatch.DrawString(_font, title, titlePos, Color.White,
                0f, Vector2.Zero, titleScale, SpriteEffects.None, GetActualOrder() + 0.001f);

            // Subtitle
            const string sub = "Select a game mode";
            var subSize = _font.MeasureString(sub);
            var subPos  = new Vector2(
                _bounds.X + (_bounds.Width - subSize.X) * 0.5f,
                titlePos.Y + titleSize.Y + 12);
            spriteBatch.DrawString(_font, sub, subPos, new Color(180, 220, 180),
                0f, Vector2.Zero, 1f, SpriteEffects.None, GetActualOrder() + 0.001f);

            // Children (mode buttons)
            base.Draw(spriteBatch);
        }
    }

    private sealed class ModeButton : UIElement
    {
        private Rectangle      _bounds;
        private readonly string      _name;
        private readonly Action      _onClick;
        private readonly SpriteFont  _font;
        private readonly Texture2D   _bg;
        private bool         _hovered;
        private ButtonState  _prevLeft;

        public ModeButton(Rectangle bounds, string name, Action onClick)
        {
            _bounds  = bounds;
            _name    = name;
            _onClick = onClick;
            _font    = Core.DefaultFont;
            _bg      = new Texture2D(Core.GraphicsDevice, 1, 1);
            _bg.SetData(new[] { Color.White });
        }

        public override Rectangle GetBoundingBox() => _bounds;
        public override void SetBounds(Rectangle bounds) => _bounds = bounds;

        public override void Update(float deltaTime)
        {
            var mouse = Mouse.GetState();
            var pt    = Core.GetTransformedMousePoint();
            _hovered  = _bounds.Contains(pt);

            if (_hovered && mouse.LeftButton == ButtonState.Pressed && _prevLeft == ButtonState.Released)
                _onClick();

            _prevLeft = mouse.LeftButton;
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            var bgColor = _hovered ? new Color(210, 235, 210) : Color.White;
            spriteBatch.Draw(_bg, _bounds, null, bgColor, 0f, Vector2.Zero, SpriteEffects.None, GetActualOrder());

            // Mode name centered on the button
            var nameSize = _font.MeasureString(_name);
            var namePos  = new Vector2(
                _bounds.X + (_bounds.Width  - nameSize.X) * 0.5f,
                _bounds.Y + (_bounds.Height - nameSize.Y) * 0.5f);
            spriteBatch.DrawString(_font, _name, namePos, Color.Black,
                0f, Vector2.Zero, 1f, SpriteEffects.None, GetActualOrder() + 0.001f);

            // Bottom label
            const string prompt = "click to play";
            var promptSize = _font.MeasureString(prompt);
            var promptPos  = new Vector2(
                _bounds.X + (_bounds.Width - promptSize.X) * 0.5f,
                _bounds.Bottom - (int)promptSize.Y - 14);
            spriteBatch.DrawString(_font, prompt, promptPos, Color.Gray,
                0f, Vector2.Zero, 1f, SpriteEffects.None, GetActualOrder() + 0.001f);
        }
    }

    // ── Game panel ────────────────────────────────────────────────────────────

    private sealed class SolitaireContentPanel : UIElement
    {
        private const int ButtonWidth  = 210;
        private const int ButtonHeight = 34;
        private const int ButtonMargin = 12;

        private const int ButtonGap    = 8;

        private Rectangle            _bounds;
        private readonly SolitaireEngine _engine;
        private readonly SpriteFont  _font;
        private readonly SpriteFont  _uiFont;
        private readonly Texture2D   _bg;
        private readonly Texture2D   _white;
        private readonly Action      _onUpdate;
        private readonly List<(string Label, Action OnClick)> _buttons;

        private readonly Rectangle[] _buttonBounds;
        private int         _hoveredButton = -1;
        private ButtonState _prevLeft;

        public SolitaireContentPanel(Rectangle bounds, SolitaireEngine engine, Action onUpdate = null,
            List<(string Label, Action OnClick)> buttons = null)
        {
            _bounds      = bounds;
            _engine      = engine;
            _onUpdate    = onUpdate;
            _buttons     = buttons ?? new List<(string, Action)>();
            _buttonBounds = new Rectangle[_buttons.Count];
            _font        = Core.Content.Load<SpriteFont>("fonts/PlayingCard");
            _uiFont      = Core.DefaultFont;
            _bg          = new Texture2D(Core.GraphicsDevice, 1, 1);
            _bg.SetData(new[] { new Color(35, 120, 35) });
            _white       = new Texture2D(Core.GraphicsDevice, 1, 1);
            _white.SetData(new[] { Color.White });
            LayoutButtons();
        }

        public override Rectangle GetBoundingBox() => _bounds;
        public override void SetBounds(Rectangle bounds) { _bounds = bounds; LayoutButtons(); }

        // Stacked at the top-right of the content area — empty space in the Symbols layout, so clicks
        // there won't be confused with card picks.
        private void LayoutButtons()
        {
            for (int i = 0; i < _buttonBounds.Length; i++)
                _buttonBounds[i] = new Rectangle(
                    _bounds.Right - ButtonWidth - ButtonMargin,
                    _bounds.Y + ButtonMargin + i * (ButtonHeight + ButtonGap),
                    ButtonWidth, ButtonHeight);
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            _onUpdate?.Invoke();

            var mouse = Mouse.GetState();
            var pt    = Core.GetTransformedMousePoint();
            _hoveredButton = -1;
            for (int i = 0; i < _buttonBounds.Length; i++)
                if (_buttonBounds[i].Contains(pt)) { _hoveredButton = i; break; }

            if (_hoveredButton >= 0 && mouse.LeftButton == ButtonState.Pressed && _prevLeft == ButtonState.Released)
                _buttons[_hoveredButton].OnClick();
            _prevLeft = mouse.LeftButton;

            // Suppress card interaction this frame when the cursor is over a button.
            if (_hoveredButton < 0)
                _engine.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(deltaTime)), new Vector2(_bounds.X, _bounds.Y));
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

            DrawButtons(spriteBatch);
        }

        private void DrawButtons(SpriteBatch spriteBatch)
        {
            for (int i = 0; i < _buttonBounds.Length; i++)
            {
                var bgColor = i == _hoveredButton ? new Color(210, 235, 210) : Color.White;
                spriteBatch.Draw(_white, _buttonBounds[i], null, bgColor, 0f, Vector2.Zero, SpriteEffects.None, 0.9f);

                var label     = _buttons[i].Label;
                var labelSize = _uiFont.MeasureString(label);
                var labelPos  = new Vector2(
                    _buttonBounds[i].X + (_buttonBounds[i].Width  - labelSize.X) * 0.5f,
                    _buttonBounds[i].Y + (_buttonBounds[i].Height - labelSize.Y) * 0.5f);
                spriteBatch.DrawString(_uiFont, label, labelPos, Color.Black, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0.91f);
            }
        }
    }
}
