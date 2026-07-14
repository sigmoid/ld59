using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

/// <summary>
/// Window adapter for the TCG minigame (tcg.md), mirroring SolitaireUI: owns the Window,
/// registers with the taskbar, and hosts a TcgBoardPanel sized to the window's content bounds.
/// </summary>
public class TcgUI : UIPanel
{
    private Rectangle _bounds;
    private Window _rootWindow;
    private TcgBoardPanel _board;
    private static readonly System.Random _seeds = new();   // System-qualified: the game defines its own static Random

    public TcgUI(Rectangle bounds)
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

    private void CreateUI()
    {
        _rootWindow = new Window(_bounds, "TCG", Core.DefaultFont);
        _rootWindow.SetColors(ColorPalette.ActualWhite, ColorPalette.Black, ColorPalette.ActualWhite, ColorPalette.Black);
        _rootWindow.SetCloseButtonColors(ColorPalette.Black, Color.DarkGray);
        // Placeholder icon and name per tcg.md.
        TaskbarRegistry.Register("TCG", Core.Content.Load<Texture2D>("images/file_icon"), _rootWindow);

        NewGame();
        Core.UISystem.AddElement(_rootWindow);
    }

    // Deals a fresh match. Also the board's reset/play-again action.
    private void NewGame()
    {
        if (_board != null)
            _rootWindow.RemoveChild(_board);

        _board = new TcgBoardPanel(_rootWindow.GetContentBounds(), new TcgGame(_seeds.Next()), NewGame);
        _rootWindow.AddChild(_board);
    }
}
