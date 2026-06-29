using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using Quartz;
using Quartz.Graphics;
using ld59.UI;

namespace ld59;

public class Game1 : Core
{
    public static Game1 Instance { get; private set; }
    private KeyboardState _prevKeyboard;
    private MouseState _prevMouse;
    private VictoryScreen _victoryScreen;

    private SplashAnimation _splash;
    private FullscreenPrompt _fullscreenPrompt;
    private BootSpinner _spinner;
    private UIFluidSimulation _fluidSim;
    private FluidPrewarmer _fluidPrewarmer;
    private double _fpsAccumulator;
    private int _fpsFrameCount;
    private double _fpsReportInterval = 5.0;

    public Game1() : base("Glory, Glory, Anastasia", 1920, 1080, false, "fonts/Default")
    {
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Core.ClearColor = new Color(255,255,255,255);
        Instance = this;
    }

    protected override void Initialize()
    {
        base.Initialize();

        InputButtons.RegisterDefaultButtons();

        // PostProcessing.AddEffect<CRTPostProcessEffect>();
        PostProcessing.AddEffect<CRTScanlinePostProcessEffect>();
        PostProcessing.AddEffect<OneBitDitheringPostProcessEffect>();
        // PostProcessing.AddEffect<ChromaticAberrationPostProcessEffect>();
        // var noise = PostProcessing.AddEffect<StaticNoisePostProcessEffect>();
        // noise.Intensity = 0.1f;
}

    protected override void LoadContent()
    {
        AudioAtlas.Load(Content);
        WindowManager.OnWindowOpened += () => AudioAtlas.Maximize_003.Play();
        DeveloperConsole.RegisterCommandHandler(new PowergridCommandHandler());
        DeveloperConsole.RegisterCommandHandler(new PowergridSaveCommandHandler());
        DeveloperConsole.RegisterCommandHandler(new MinefieldCommandHandler());
        Core.CurrentScene.AddManager(new GameFileDataManager());
        Core.CurrentScene.AddManager(new EmailDataManager());

        var screenBounds = new Rectangle(0, 0, GameplayConstants.ScreenWidth, GameplayConstants.ScreenHeight);

        _fluidSim = new UIFluidSimulation(screenBounds);
        _fluidPrewarmer = new FluidPrewarmer(_fluidSim, new Vector2(0.23f, 0.95f));
        UISystem.AddElement(_fluidPrewarmer);

        _splash = new SplashAnimation(screenBounds, () =>
        {
            UISystem.RemoveElement(_splash);
            _splash = null;
            _fullscreenPrompt = new FullscreenPrompt(screenBounds, () =>
            {
                UISystem.RemoveElement(_fullscreenPrompt);
                _fullscreenPrompt = null;
                _spinner = new BootSpinner(screenBounds, () =>
                {
                    UISystem.RemoveElement(_spinner);
                    _spinner = null;
                    UISystem.RemoveElement(_fluidPrewarmer);
                    _fluidPrewarmer = null;
                    UISystem.AddElement(new DesktopUI(screenBounds, _fluidSim));
                });
                UISystem.AddElement(_spinner);
            });
            UISystem.AddElement(_fullscreenPrompt);
        });

        UISystem.AddElement(_splash);
    }


    public void ShowVictoryScreen()
    {
        if (_victoryScreen != null)
            UISystem.RemoveElement(_victoryScreen);

        var screenBounds = new Rectangle(0, 0, GameplayConstants.ScreenWidth, GameplayConstants.ScreenHeight);
        _victoryScreen = new VictoryScreen(screenBounds, () =>
        {
            UISystem.RemoveElement(_victoryScreen);
            _victoryScreen = null;
        });
        UISystem.AddElement(_victoryScreen);
    }

    protected override void Update(GameTime gameTime)
    {
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        var keyboard = Keyboard.GetState();
        var mouse = Mouse.GetState();

        if (keyboard.IsKeyDown(Keys.F1) && !_prevKeyboard.IsKeyDown(Keys.F1))
            SkipIntro();

        if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
            AudioAtlas.Mouse_Click_Down.Play();
        
        if(mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed)
            AudioAtlas.Mouse_Click_Up.Play();

        _prevKeyboard = keyboard;
        _prevMouse = mouse;

        _fpsAccumulator += gameTime.ElapsedGameTime.TotalSeconds;
        _fpsFrameCount++;
        if (_fpsAccumulator >= _fpsReportInterval)
        {
            Console.WriteLine($"Avg FPS (last {_fpsReportInterval}s): {_fpsFrameCount / _fpsAccumulator:F1}");
            _fpsAccumulator = 0;
            _fpsFrameCount = 0;
        }

        base.Update(gameTime);
    }

    private void SkipIntro()
    {
        if (_splash != null)         { UISystem.RemoveElement(_splash);          _splash = null; }
        if (_fullscreenPrompt != null){ UISystem.RemoveElement(_fullscreenPrompt); _fullscreenPrompt = null; }
        if (_spinner != null)        { UISystem.RemoveElement(_spinner);          _spinner = null; }

        if (_fluidPrewarmer == null) return; // already past intro

        var screenBounds = new Rectangle(0, 0, GameplayConstants.ScreenWidth, GameplayConstants.ScreenHeight);
        UISystem.RemoveElement(_fluidPrewarmer);
        _fluidPrewarmer = null;
        UISystem.AddElement(new DesktopUI(screenBounds, _fluidSim));
    }

    public void EndGame()
    {
        ShowVictoryScreen();
    }   

    protected override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);
    }
}
