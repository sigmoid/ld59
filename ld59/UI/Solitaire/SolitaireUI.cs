using System;
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
    };

    private Rectangle _bounds;
    private Window    _rootWindow;
    private UIElement _activePanel;

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

        var engine = new SolitaireEngine(modeFactory());
        _activePanel = new SolitaireContentPanel(_rootWindow.GetContentBounds(), engine);
        _rootWindow.AddChild(_activePanel);
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
        private Rectangle            _bounds;
        private readonly SolitaireEngine _engine;
        private readonly SpriteFont  _font;
        private readonly Texture2D   _bg;

        public SolitaireContentPanel(Rectangle bounds, SolitaireEngine engine)
        {
            _bounds = bounds;
            _engine = engine;
            _font   = Core.Content.Load<SpriteFont>("fonts/PlayingCard");
            _bg     = new Texture2D(Core.GraphicsDevice, 1, 1);
            _bg.SetData(new[] { new Color(35, 120, 35) });
        }

        public override Rectangle GetBoundingBox() => _bounds;
        public override void SetBounds(Rectangle bounds) => _bounds = bounds;

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
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
        }
    }
}
