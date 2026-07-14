using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Quartz;
using Quartz.UI;

/// <summary>
/// The TCG's content panel: board layout, the click-select → click-target state machine,
/// paced AI turns, and the win/lose overlay. Follows SolitaireUI's content-panel pattern —
/// scissor-clipped drawing, direct mouse polling with edge detection, engine owned by the
/// panel and replaced wholesale on reset.
///
/// Player 0 (bottom, light cards) is the human; player 1 (top, dark cards) is the AI.
/// </summary>
public class TcgBoardPanel : UIElement
{
    private const int Margin = 10;
    private const int Gap = 8;
    private const int SlotGap = 12;
    private const int CenterH = 46;
    private const int OppHandH = 40;
    private const float AiDelay = 0.7f;    // seconds between AI actions, for watchability

    private Rectangle _bounds;
    private readonly TcgGame _game;
    private readonly Action _onReset;
    private readonly TcgCardRenderer _cards = new();
    private readonly SpriteFont _uiFont;
    private readonly Texture2D _felt;
    private readonly Texture2D _white;
    private readonly TcgDictionaryPanel _dictionary;

    private TcgGameState S => _game.State;
    private TcgPlayerState Me => S.Players[0];
    private TcgPlayerState Foe => S.Players[1];

    private int _cardW, _cardH;
    private Rectangle[] _oppLife, _oppFront, _myFront, _myLife;
    private Rectangle _oppHandStrip, _centerStrip, _handStrip;
    private Rectangle _dictBtn, _helpBtn, _resetBtn, _endTurnBtn;

    private ButtonState _prevLeft;
    private (TcgZone Zone, int Index)? _selected;
    private float _aiTimer;
    private float _endFade;    // 0..1 alpha of the win/lose overlay
    private bool _dictOpen;
    private bool _helpOpen;

    public TcgBoardPanel(Rectangle bounds, TcgGame game, Action onReset)
    {
        _bounds = bounds;
        _game = game;
        _onReset = onReset;
        _uiFont = Core.DefaultFont;
        _felt = new Texture2D(Core.GraphicsDevice, 1, 1);
        _felt.SetData(new[] { new Color(30, 95, 55) });
        _white = new Texture2D(Core.GraphicsDevice, 1, 1);
        _white.SetData(new[] { Color.White });
        _dictionary = new TcgDictionaryPanel(DictionaryBounds());
        Layout();
    }

    public override Rectangle GetBoundingBox() => _bounds;

    public override void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        Layout();
        _dictionary.SetBounds(DictionaryBounds());
    }

    // ── Layout ─────────────────────────────────────────────────────────────────

    private void Layout()
    {
        int innerH = _bounds.Height - 2 * Margin;
        _cardH = (innerH - OppHandH - CenterH - 6 * Gap) / 5;
        _cardW = _cardH * 2 / 3;

        int y = _bounds.Y + Margin;
        _oppHandStrip = new Rectangle(_bounds.X + Margin, y, _bounds.Width - 2 * Margin, OppHandH);
        y += OppHandH + Gap;
        _oppLife = RowRects(y, TcgGameState.LifeSlots);
        y += _cardH + Gap;
        _oppFront = RowRects(y, TcgGameState.FrontSlots);
        y += _cardH + Gap;
        _centerStrip = new Rectangle(_bounds.X + Margin, y, _bounds.Width - 2 * Margin, CenterH);
        y += CenterH + Gap;
        _myFront = RowRects(y, TcgGameState.FrontSlots);
        y += _cardH + Gap;
        _myLife = RowRects(y, TcgGameState.LifeSlots);
        y += _cardH + Gap;
        _handStrip = new Rectangle(_bounds.X + Margin, y, _bounds.Width - 2 * Margin, _cardH);

        int btnH = CenterH - 12;
        _dictBtn = new Rectangle(_centerStrip.X, _centerStrip.Y + 6, 110, btnH);
        _resetBtn = new Rectangle(_dictBtn.Right + 8, _centerStrip.Y + 6, 80, btnH);
        _helpBtn = new Rectangle(_resetBtn.Right + 8, _centerStrip.Y + 6, btnH, btnH);   // square "?"
        _endTurnBtn = new Rectangle(_centerStrip.Right - 130, _centerStrip.Y + 6, 130, btnH);
    }

    private Rectangle[] RowRects(int y, int count)
    {
        int total = count * _cardW + (count - 1) * SlotGap;
        int x0 = _bounds.X + (_bounds.Width - total) / 2;
        var rects = new Rectangle[count];
        for (int i = 0; i < count; i++)
            rects[i] = new Rectangle(x0 + i * (_cardW + SlotGap), y, _cardW, _cardH);
        return rects;
    }

    // Hand size changes constantly, so hand rects are computed on demand. Cards compress into
    // an overlapping fan when the hand outgrows the strip; the deck read-out keeps the right edge.
    private List<Rectangle> HandRects()
    {
        var rects = new List<Rectangle>();
        int n = Me.Hand.Count;
        if (n == 0) return rects;

        int availW = _handStrip.Width - 150;
        int spacing = n == 1 ? 0 : Math.Min(_cardW + SlotGap, (availW - _cardW) / (n - 1));
        int total = _cardW + (n - 1) * spacing;
        int x0 = _handStrip.X + (availW - total) / 2;
        for (int i = 0; i < n; i++)
            rects.Add(new Rectangle(x0 + i * spacing, _handStrip.Y, _cardW, _cardH));
        return rects;
    }

    private Rectangle DictionaryBounds()
    {
        int w = (int)(_bounds.Width * 0.42f);
        return new Rectangle(_bounds.Right - w - Margin, _bounds.Y + Margin, w, _bounds.Height - 2 * Margin);
    }

    // ── Input ──────────────────────────────────────────────────────────────────

    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);
        var mouse = Mouse.GetState();
        var pt = Core.GetTransformedMousePoint();
        bool clicked = mouse.LeftButton == ButtonState.Pressed && _prevLeft == ButtonState.Released;
        _prevLeft = mouse.LeftButton;

        float target = S.Winner >= 0 ? 1f : 0f;
        _endFade = MathHelper.Clamp(_endFade + (target - _endFade) * Math.Min(1f, deltaTime * 3f), 0f, 1f);

        // The help overlay pauses everything (including the AI) and closes on any click.
        if (_helpOpen)
        {
            if (clicked) { _helpOpen = false; AudioAtlas.PlayRandomClick(); }
            return;
        }
        if (clicked && _helpBtn.Contains(pt)) { _helpOpen = true; AudioAtlas.PlayRandomClick(); return; }

        if (S.Winner >= 0)
        {
            if (clicked && _endFade > 0.5f) _onReset?.Invoke();
            return;
        }

        if (_dictOpen)
        {
            _dictionary.Update(deltaTime);
            if (clicked && (_dictBtn.Contains(pt) || !_dictionary.GetBoundingBox().Contains(pt)))
            {
                _dictOpen = false;
                AudioAtlas.PlayRandomClick();
            }
            return;   // the dictionary swallows board input while open
        }

        if (clicked && _dictBtn.Contains(pt)) { _dictOpen = true; AudioAtlas.PlayRandomClick(); return; }
        if (clicked && _resetBtn.Contains(pt)) { AudioAtlas.PlayRandomClick(); _onReset?.Invoke(); return; }

        if (S.CurrentPlayer == 1)
        {
            _selected = null;
            _aiTimer += deltaTime;
            if (_aiTimer >= AiDelay)
            {
                _aiTimer = 0f;
                var action = TcgAi.NextAction(S);
                if (action.Kind == TcgActionKind.Attack) AudioAtlas.PlayRandomGlass();
                else if (action.Kind != TcgActionKind.EndTurn) AudioAtlas.PlayRandomClick();
                _game.Apply(action);
            }
            return;
        }

        if (clicked && _endTurnBtn.Contains(pt))
        {
            _selected = null;
            _game.EndTurn();
            _aiTimer = -0.3f;   // small beat before the AI's first action
            AudioAtlas.PlayRandomClick();
            return;
        }

        if (clicked) HandleBoardClick(pt);
    }

    private void HandleBoardClick(Point pt)
    {
        var handRects = HandRects();
        int handHit = LastIndexContaining(handRects, pt);   // fanned cards overlap; topmost wins
        int frontHit = IndexContaining(_myFront, pt);
        int oppFrontHit = IndexContaining(_oppFront, pt);
        int oppLifeHit = IndexContaining(_oppLife, pt);

        if (_selected == null)
        {
            if (handHit >= 0) Select(TcgZone.Hand, handHit);
            else if (frontHit >= 0 && Me.FrontRow[frontHit] != null) Select(TcgZone.Front, frontHit);
            return;
        }

        var sel = _selected.Value;

        // Clicking the selection again cancels it.
        if ((sel.Zone == TcgZone.Hand && sel.Index == handHit) ||
            (sel.Zone == TcgZone.Front && sel.Index == frontHit))
        {
            _selected = null;
            return;
        }

        // A hand card can only be summoned onto an empty own slot; it cannot fuse.
        if (sel.Zone == TcgZone.Hand)
        {
            if (frontHit >= 0 && Me.FrontRow[frontHit] == null)
            {
                if (_game.Summon(TcgAction.Summon(sel.Index, frontHit))) AudioAtlas.PlayRandomClick();
                else AudioAtlas.Error_004.Play();
                _selected = null;
            }
            else if (handHit >= 0) Select(TcgZone.Hand, handHit);                       // switch hand card
            else if (frontHit >= 0 && Me.FrontRow[frontHit] != null) Select(TcgZone.Front, frontHit);
            else _selected = null;
            return;
        }

        // A summoned (front-row) card: fuse onto another own summoned card, or attack an enemy.
        if (frontHit >= 0 && frontHit != sel.Index && Me.FrontRow[frontHit] != null)
        {
            if (_game.Fuse(TcgAction.Fuse(TcgZone.Front, sel.Index, TcgZone.Front, frontHit)))
            {
                AudioAtlas.Confirmation_001.Play();
                _selected = null;
            }
            else Select(TcgZone.Front, frontHit);   // incompatible sidedness → reselect the clicked card
            return;
        }

        bool hitEnemy = (oppFrontHit >= 0 && Foe.FrontRow[oppFrontHit] != null) ||
                        (oppLifeHit >= 0 && Foe.LifeRow[oppLifeHit] != null);
        if (hitEnemy)
        {
            var (zone, index) = oppFrontHit >= 0 && Foe.FrontRow[oppFrontHit] != null
                ? (TcgZone.Front, oppFrontHit)
                : (TcgZone.Life, oppLifeHit);
            if (_game.Attack(TcgAction.Attack(sel.Index, zone, index))) AudioAtlas.PlayRandomGlass();
            else AudioAtlas.Error_004.Play();
            _selected = null;
            return;
        }

        if (handHit >= 0) Select(TcgZone.Hand, handHit);
        else _selected = null;   // clicked felt or something inert
    }

    private void Select(TcgZone zone, int index)
    {
        _selected = (zone, index);
        AudioAtlas.PlayRandomClick();
    }

    private static int IndexContaining(Rectangle[] rects, Point pt)
    {
        for (int i = 0; i < rects.Length; i++)
            if (rects[i].Contains(pt)) return i;
        return -1;
    }

    private static int LastIndexContaining(List<Rectangle> rects, Point pt)
    {
        for (int i = rects.Count - 1; i >= 0; i--)
            if (rects[i].Contains(pt)) return i;
        return -1;
    }

    // ── Drawing ────────────────────────────────────────────────────────────────

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.End();

        var rasterizer = new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None };
        var origScissor = Core.GraphicsDevice.ScissorRectangle;
        Core.GraphicsDevice.ScissorRectangle = _bounds;
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, rasterizer);

        spriteBatch.Draw(_felt, _bounds, Color.White);

        DrawOpponentHand(spriteBatch);
        DrawRow(spriteBatch, _oppLife, Foe.LifeRow, dark: true, "LIFE");
        DrawRow(spriteBatch, _oppFront, Foe.FrontRow, dark: true, null);
        DrawRow(spriteBatch, _myFront, Me.FrontRow, dark: false, null);
        DrawRow(spriteBatch, _myLife, Me.LifeRow, dark: false, "LIFE");
        DrawHand(spriteBatch);
        DrawHighlights(spriteBatch);

        spriteBatch.End();
        Core.GraphicsDevice.ScissorRectangle = origScissor;
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);

        DrawCenterStrip(spriteBatch);
        if (_dictOpen) _dictionary.Draw(spriteBatch);
        if (_helpOpen) DrawHelpOverlay(spriteBatch);
        DrawEndOverlay(spriteBatch);
    }

    private void DrawRow(SpriteBatch sb, Rectangle[] rects, TcgCard[] cards, bool dark, string label)
    {
        if (label != null)
        {
            var size = _uiFont.MeasureString(label) * 0.8f;
            sb.DrawString(_uiFont, label,
                new Vector2(rects[0].X - size.X - 10, rects[0].Y + (_cardH - size.Y) / 2),
                new Color(210, 230, 210), 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 0f);
        }
        for (int i = 0; i < rects.Length; i++)
        {
            if (cards[i] != null) _cards.DrawFace(cards[i], rects[i], dark, sb, _uiFont, 0f);
            else _cards.DrawEmptySlot(rects[i], sb, 0f);
        }
    }

    private void DrawOpponentHand(SpriteBatch sb)
    {
        int n = Foe.Hand.Count;
        int backW = (int)(OppHandH * 0.66f);
        for (int i = 0; i < n && i < 12; i++)
            _cards.DrawBack(new Rectangle(_oppHandStrip.X + i * (backW - 6), _oppHandStrip.Y, backW, OppHandH), sb, 0f);
        var text = $"x{n}";
        sb.DrawString(_uiFont, text, new Vector2(_oppHandStrip.X + Math.Min(n, 12) * (backW - 6) + backW + 8, _oppHandStrip.Y + 8),
            ColorPalette.ActualWhite);

        var deckText = $"Deck {Foe.Deck.Count} - Lost {Foe.CardsLost}";
        var size = _uiFont.MeasureString(deckText);
        sb.DrawString(_uiFont, deckText, new Vector2(_oppHandStrip.Right - size.X, _oppHandStrip.Y + 8), new Color(210, 230, 210));
    }

    private void DrawHand(SpriteBatch sb)
    {
        var rects = HandRects();
        for (int i = 0; i < rects.Count; i++)
            _cards.DrawFace(Me.Hand[i], rects[i], dark: false, sb, _uiFont, 0f);

        var deckText = $"Deck {Me.Deck.Count} - Lost {Me.CardsLost}";
        var size = _uiFont.MeasureString(deckText);
        sb.DrawString(_uiFont, deckText,
            new Vector2(_handStrip.Right - size.X, _handStrip.Y + (_cardH - size.Y) / 2), new Color(210, 230, 210));
    }

    // Selection in orange, legal destinations in white — the highlights double as rule teaching.
    private void DrawHighlights(SpriteBatch sb)
    {
        if (_selected == null || S.CurrentPlayer != 0 || S.Winner >= 0 || _dictOpen) return;
        var sel = _selected.Value;
        var handRects = HandRects();

        DrawBorder(sb, RectFor(sel.Zone, sel.Index, own: true, handRects), ColorPalette.Orange);

        if (sel.Zone == TcgZone.Hand)
        {
            // A hand card can only be summoned.
            foreach (int slot in TcgRules.LegalSummonSlots(S, sel.Index))
                DrawBorder(sb, _myFront[slot], ColorPalette.ActualWhite);
        }
        else
        {
            // A summoned card can fuse with a compatible own card or attack a legal enemy.
            foreach (int partner in TcgRules.LegalFusionPartners(S, sel.Index))
                DrawBorder(sb, _myFront[partner], ColorPalette.ActualWhite);
            foreach (var (zone, index) in TcgRules.LegalAttackTargets(S, sel.Index))
                DrawBorder(sb, RectFor(zone, index, own: false, handRects), ColorPalette.ActualWhite);
        }
    }

    private Rectangle RectFor(TcgZone zone, int index, bool own, List<Rectangle> handRects)
    {
        if (own)
            return zone switch
            {
                TcgZone.Hand => index < handRects.Count ? handRects[index] : Rectangle.Empty,
                TcgZone.Front => _myFront[index],
                _ => _myLife[index],
            };
        return zone == TcgZone.Front ? _oppFront[index] : _oppLife[index];
    }

    private void DrawBorder(SpriteBatch sb, Rectangle r, Color color)
    {
        if (r == Rectangle.Empty) return;
        const int t = 3;
        sb.Draw(_white, new Rectangle(r.X - t, r.Y - t, r.Width + 2 * t, t), color);
        sb.Draw(_white, new Rectangle(r.X - t, r.Bottom, r.Width + 2 * t, t), color);
        sb.Draw(_white, new Rectangle(r.X - t, r.Y, t, r.Height), color);
        sb.Draw(_white, new Rectangle(r.Right, r.Y, t, r.Height), color);
    }

    private void DrawCenterStrip(SpriteBatch sb)
    {
        DrawButton(sb, _dictBtn, _dictOpen ? "Close" : "Dictionary");
        DrawButton(sb, _resetBtn, "Reset");
        DrawButton(sb, _helpBtn, "?");
        if (S.CurrentPlayer == 0 && S.Winner < 0)
            DrawButton(sb, _endTurnBtn, "End Turn");

        string status;
        if (S.Winner >= 0) status = "";
        else if (S.CurrentPlayer == 1) status = "Opponent's turn...";
        else
        {
            status = $"{(S.Phase == TcgPhase.Main ? "Main" : "Attack")} - Summons {S.SummonsUsed}/{TcgGameState.SummonLimit} - Fusion {(S.FusionUsed ? "used" : "ready")}";
            if (S.TurnNumber == 1) status += " - No attacks on turn 1";
        }
        var size = _uiFont.MeasureString(status);
        sb.DrawString(_uiFont, status,
            new Vector2(_bounds.X + (_bounds.Width - size.X) / 2, _centerStrip.Y + (_centerStrip.Height - size.Y) / 2),
            ColorPalette.ActualWhite);
    }

    private void DrawButton(SpriteBatch sb, Rectangle r, string label)
    {
        bool hovered = r.Contains(Core.GetTransformedMousePoint());
        sb.Draw(_white, r, hovered ? new Color(210, 235, 210) : Color.White);
        var size = _uiFont.MeasureString(label);
        sb.DrawString(_uiFont, label,
            new Vector2(r.X + (r.Width - size.X) / 2, r.Y + (r.Height - size.Y) / 2), Color.Black);
    }

    private void DrawEndOverlay(SpriteBatch sb)
    {
        if (_endFade <= 0f) return;

        sb.Draw(_white, _bounds, Color.Black * (_endFade * 0.8f));

        string text = S.Winner == 0 ? "YOU WIN!" : "YOU LOSE";
        const float scale = 4f;
        var size = _uiFont.MeasureString(text) * scale;
        var pos = new Vector2(_bounds.X + (_bounds.Width - size.X) / 2, _bounds.Y + (_bounds.Height - size.Y) / 2);
        sb.DrawString(_uiFont, text, pos, Color.White * _endFade, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

        const string hint = "Click to play again";
        var hintSize = _uiFont.MeasureString(hint);
        sb.DrawString(_uiFont, hint,
            new Vector2(_bounds.X + (_bounds.Width - hintSize.X) / 2, pos.Y + size.Y + 24),
            Color.White * (_endFade * 0.85f));
    }

    // Concise how-to-play tutorial (the "?" button). ASCII only — the UI font lacks glyphs for
    // typographic dashes/dots. Section headers are drawn brighter; the title is centred.
    private static readonly string[] HelpLines =
    {
        "HOW TO PLAY",
        "",
        "GOAL",
        "Destroy all 3 of your opponent's LIFE cards.",
        "",
        "CARDS  (each card is a stack of alien symbols)",
        "Length (how many symbols) = the card's health.",
        "Tier (the number) = its power.",
        "Side (L / R / C) = what it can fuse with.",
        "",
        "ON YOUR TURN",
        "SUMMON:  click a hand card, then an empty slot.",
        "FUSE:    click one of your cards, then another.",
        "         Same side (C is wild), tiers 1 apart.",
        "         Their symbols stack into a bigger card.",
        "ATTACK:  click your card, then an enemy card.",
        "Then press End Turn.",
        "",
        "COMBAT  (you can only attack a card you outrank)",
        "Higher tier than them: knock off their top symbol.",
        "Same tier: both cards are destroyed.",
        "Lower tier: you cannot hurt them at all.",
        "Knock off all of a card's symbols to destroy it.",
        "",
        "REACHING LIFE CARDS",
        "You can only attack a life card once the enemy's",
        "front row is completely empty. Clear it first.",
        "",
        "Click anywhere to close.",
    };

    private static bool IsHelpHeader(string line) =>
        line == "GOAL" || line == "ON YOUR TURN" || line == "REACHING LIFE CARDS" ||
        line.StartsWith("CARDS") || line.StartsWith("COMBAT");

    private void DrawHelpOverlay(SpriteBatch sb)
    {
        sb.Draw(_white, _bounds, Color.Black * 0.85f);

        float lineH = _uiFont.LineSpacing;
        float maxW = 1f;
        foreach (var line in HelpLines)
            maxW = Math.Max(maxW, _uiFont.MeasureString(line).X);

        // Auto-fit to the window so it stays readable at any size.
        float scale = Math.Min(1f, Math.Min(
            (_bounds.Width - 60) / maxW,
            (_bounds.Height - 60) / (HelpLines.Length * lineH)));

        float step = lineH * scale;
        float blockLeft = _bounds.X + (_bounds.Width - maxW * scale) / 2f;
        float startY = _bounds.Y + (_bounds.Height - HelpLines.Length * step) / 2f;

        for (int i = 0; i < HelpLines.Length; i++)
        {
            var line = HelpLines[i];
            if (line.Length == 0) continue;

            float lineW = _uiFont.MeasureString(line).X * scale;
            float x = i == 0 ? _bounds.X + (_bounds.Width - lineW) / 2f : blockLeft;
            var color = (i == 0 || IsHelpHeader(line)) ? ColorPalette.ActualWhite : new Color(205, 225, 205);
            sb.DrawString(_uiFont, line, new Vector2(x, startY + i * step),
                color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }
}
