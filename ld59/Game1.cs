using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using Quartz;

namespace ld59;

public class Game1 : Core
{
    public Game1() : base("Signal", 1920, 1080, false, "fonts/Default")
    {
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Core.ClearColor = ColorPalette.LightGreen;
    }

    protected override void Initialize()
    {

        base.Initialize();
    }

    protected override void LoadContent()
    {
        DesktopUI desktopUI = new DesktopUI(new Rectangle(0, 0, GameplayConstants.ScreenWidth, GameplayConstants.ScreenHeight));

        Core.CurrentScene.AddManager(new GameFileDataManager());

        UISystem.AddElement(desktopUI);
    }

    protected override void Update(GameTime gameTime)
    {
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);
    }
}
