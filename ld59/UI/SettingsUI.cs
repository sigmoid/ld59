using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

public class SettingsUI : UIContainer
{
    public Action OnClose;

    private Window _rootContainer;
    private readonly Rectangle _bounds;

    public SettingsUI(Rectangle bounds)
    {
        _bounds = bounds;
        CreateUI();
    }

    public void CloseWindow() => _rootContainer.Close();

    public void Close() => OnClose?.Invoke();

    private void CreateUI()
    {
        _rootContainer = new Window(_bounds, "Settings", Core.DefaultFont,
            ColorPalette.ActualWhite, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.DarkGreen, 2);
        _rootContainer.SetCloseButtonColors(ColorPalette.DarkGreen, ColorPalette.LightGreen);
        Core.UISystem.AddElement(_rootContainer);
        _rootContainer.OnWindowClosed += _ => Close();
        TaskbarRegistry.Register("Settings",
            Core.Content.Load<Texture2D>("images/settings_icon"), _rootContainer);

        var content = _rootContainer.GetContentBounds();
        int y = content.Y + 20;
        int labelW = 180;
        int rowH = 50;
        int x = content.X + 20;
        int innerW = content.Width - 40;

        // ── Display ─────────────────────────────────────────────────────
        AddSectionHeader("Display", x, ref y, innerW, content);

        var fsLabel = new Label(new Rectangle(x, y + 10, labelW, 30),
            "Fullscreen", Core.DefaultFont, ColorPalette.Black, Color.Transparent);
        _rootContainer.AddChild(fsLabel);

        var fsButtonW = 160;
        Button fsButton = null;
        fsButton = new Button(
            new Rectangle(x + labelW + 20, y + 5, fsButtonW, 36),
            GetFullscreenLabel(),
            Core.DefaultFont,
            ColorPalette.DarkGreen, ColorPalette.LightGreen, ColorPalette.ActualWhite,
            () =>
            {
                Core.Graphics.ToggleFullScreen();
                fsButton.SetText(GetFullscreenLabel());
            });
        _rootContainer.AddChild(fsButton);
        y += rowH + 10;

        // ── Audio ────────────────────────────────────────────────────────
        AddSectionHeader("Audio", x, ref y, innerW, content);

        var volLabel = new Label(new Rectangle(x, y + 10, labelW, 30),
            "Volume", Core.DefaultFont, ColorPalette.Black, Color.Transparent);
        _rootContainer.AddChild(volLabel);

        int sliderX = x + labelW + 20;
        int sliderW = innerW - labelW - 20;
        var volSlider = new Slider(
            new Rectangle(sliderX, y + 14, sliderW, 30),
            minValue: 0f, maxValue: 1f,
            initialValue: SoundEffect.MasterVolume,
            step: 0f, isHorizontal: true,
            trackColor: ColorPalette.DarkGreen,
            fillColor: ColorPalette.LightGreen,
            handleColor: ColorPalette.DarkGreen,
            handleHoverColor: ColorPalette.LightGreen,
            handlePressedColor: ColorPalette.Green,
            trackHeight: 8, handleSize: 24, handleBorderSize: 2);
        volSlider.OnValueChanged += v => SoundEffect.MasterVolume = v;
        _rootContainer.AddChild(volSlider);
        y += rowH;
    }

    private void AddSectionHeader(string title, int x, ref int y, int w, Rectangle content)
    {
        var header = new Label(new Rectangle(x, y, w, 28),
            title, Core.DefaultFont, ColorPalette.DarkGreen, Color.Transparent);
        _rootContainer.AddChild(header);

        // Divider
        var pixel = new Canvas(new Rectangle(x, y + 30, w, 2), ColorPalette.DarkGreen);
        _rootContainer.AddChild(pixel);
        y += 42;
    }

    private static string GetFullscreenLabel() =>
        Core.Graphics.IsFullScreen ? "Windowed" : "Fullscreen";
}
