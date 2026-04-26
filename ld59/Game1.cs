using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using Quartz;

namespace ld59;

public class Game1 : Core
{
    public static Game1 Instance { get; private set; }
    private KeyboardState _prevKeyboard;
    private VictoryScreen _victoryScreen;

    public Game1() : base("Glory, Glory, Anastasia", 1920, 1080, false, "fonts/Default")
    {
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Core.ClearColor = ColorPalette.LightGreen;
        Instance = this;
    }

    protected override void Initialize()
    {

        base.Initialize();
    }

    protected override void LoadContent()
    {
        Core.CurrentScene.AddManager(new GameFileDataManager());

        var screenBounds = new Rectangle(0, 0, GameplayConstants.ScreenWidth, GameplayConstants.ScreenHeight);
        SplashAnimation splash = null;
        splash = new SplashAnimation(screenBounds, () =>
        {
            UISystem.RemoveElement(splash);
            UISystem.AddElement(new DesktopUI(screenBounds));
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
        _prevKeyboard = keyboard;

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
