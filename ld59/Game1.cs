using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using Quartz;
using Quartz.Graphics;
using Quartz.Input;
using ld59.UI;

namespace ld59;

public class Game1 : Core
{
    public static Game1 Instance { get; private set; }
    private VictoryScreen _victoryScreen;

    private SplashAnimation _splash;
    private FullscreenPrompt _fullscreenPrompt;
    private BootSpinner _spinner;
    private UIFluidSimulation _fluidSim;
    private FluidPrewarmer _fluidPrewarmer;
    private double _fpsAccumulator;
    private int _fpsFrameCount;
    private double _fpsReportInterval = 5.0;

    public Game1() : base("Legacy System", 1920, 1080, false, "fonts/Default")
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
        DeveloperConsole.RegisterCommandHandler(new BrowserCommandHandler());
        DeveloperConsole.RegisterCommandHandler(new OneBitCommandHandler());
        DeveloperConsole.RegisterCommandHandler(new PosterizeCommandHandler());
        DeveloperConsole.RegisterCommandHandler(new IdViewCommandHandler());
        DeveloperConsole.RegisterCommandHandler(new DepthViewCommandHandler());
        DeveloperConsole.RegisterCommandHandler(new FogCommandHandler());
        DeveloperConsole.RegisterCommandHandler(new OutlineCommandHandler());
        DeveloperConsole.RegisterCommandHandler(new SSAOCommandHandler());
        DeveloperConsole.RegisterCommandHandler(new WalkingSimCommandHandler());
        WebPages.RegisterAll();
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
        // Runs before base.Update, so sample here; GameInput ignores the second call Core makes.
        GameInput.Update(gameTime);

        // Quitting is the most destructive thing input can do here, so it honours the block
        // outright: Escape must not take the game down when it was aimed at the developer console,
        // and neither key nor pad should reach it while the window is unfocused.
        if (!GameInput.Blocked &&
            (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || GameInput.Down(Keys.Escape)))
            Exit();

        if (GameInput.Pressed(Keys.F1))
            SkipIntro();

        // Click feedback follows the UI, not the game, so it stays on the raw view: the console's
        // own buttons should still click.
        if (GameInput.RawLeftJustPressed)
            AudioAtlas.Mouse_Click_Down.Play();

        if (GameInput.RawLeftJustReleased)
            AudioAtlas.Mouse_Click_Up.Play();

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
