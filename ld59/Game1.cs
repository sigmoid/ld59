using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using Quartz;
using Quartz.Graphics;

namespace ld59;

public class Game1 : Core
{
    public static Game1 Instance { get; private set; }
    private KeyboardState _prevKeyboard;
    private MouseState _prevMouse;
    private VictoryScreen _victoryScreen;

    public Game1() : base("Glory, Glory, Anastasia", 1920, 1080, false, "fonts/Default")
    {
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Core.ClearColor = new Color(0,0,0,255);
        Instance = this;
    }

    protected override void Initialize()
    {
        base.Initialize();

        PostProcessing.AddEffect<CRTPostProcessEffect>();
        PostProcessing.AddEffect<CRTScanlinePostProcessEffect>();
        PostProcessing.AddEffect<OneBitDitheringPostProcessEffect>();
        // PostProcessing.AddEffect<ChromaticAberrationPostProcessEffect>();
        var noise = PostProcessing.AddEffect<StaticNoisePostProcessEffect>();
        noise.Intensity = 0.1f;
    }

    protected override void LoadContent()
    {
        AudioAtlas.Load(Content);
        WindowManager.OnWindowOpened += () => AudioAtlas.Maximize_003.Play();
        Core.CurrentScene.AddManager(new GameFileDataManager());
        Core.CurrentScene.AddManager(new EmailDataManager());

        var screenBounds = new Rectangle(0, 0, GameplayConstants.ScreenWidth, GameplayConstants.ScreenHeight);
        BootSpinner spinner = null;
        SplashAnimation splash = null;
        splash = new SplashAnimation(screenBounds, () =>
        {
            UISystem.RemoveElement(splash);
            spinner = new BootSpinner(screenBounds, () =>
            {
                UISystem.RemoveElement(spinner);
                UISystem.AddElement(new DesktopUI(screenBounds));
            });
            UISystem.AddElement(spinner);
        });

        UISystem.AddElement(splash);
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

        if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
            AudioAtlas.Mouse_Click_Down.Play();
        
        if(mouse.LeftButton == ButtonState.Released && _prevMouse.LeftButton == ButtonState.Pressed)
            AudioAtlas.Mouse_Click_Up.Play();

        _prevKeyboard = keyboard;
        _prevMouse = mouse;

        base.Update(gameTime);
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
