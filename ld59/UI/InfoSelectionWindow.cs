using System;
using Microsoft.Xna.Framework;
using Quartz;
using Quartz.UI;

public class InfoSelectionWindow : UIPanel
{
    private Rectangle _bounds;
    private Window _rootContainer;
    private InfoType _infoType;
    private Action<GameInfo> _onSelectInfo;

    public InfoSelectionWindow(Rectangle bounds, InfoType infoType, Action<GameInfo> onSelectInfo)
    {
        _bounds = bounds;
        _infoType = infoType;
        _onSelectInfo = onSelectInfo;
        CreateUI();
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        _bounds = bounds;
    }

    public override Rectangle GetBoundingBox()
    {
        return _bounds;
    }

    public void CloseWindow()
    {
        _rootContainer.Close();
        Core.UISystem.RemoveElement(_rootContainer);
    }

    private void CreateUI()
    {
        _rootContainer = new Window(_bounds, "Information", Core.DefaultFont, ColorPalette.ActualWhite, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.DarkGreen, 2);
        Core.UISystem.AddElement(_rootContainer);

        var contentBounds = _rootContainer.GetContentBounds();
        var scrollArea = new ScrollArea(new Rectangle(contentBounds.X + 10, contentBounds.Y + 10, contentBounds.Width - 20, contentBounds.Height - 20));
        _rootContainer.AddChild(scrollArea);

        var layoutGroup  = new VerticalLayoutGroup(new Rectangle(contentBounds.X + 10, contentBounds.Y + 10, contentBounds.Width - 20, contentBounds.Height - 20), 5);
        scrollArea.AddChild(layoutGroup);

        Core.UISystem.AddElement(_rootContainer);
        
        var dataManager = Core.CurrentScene.GetManager<GameFileDataManager>();
        var infoItems = dataManager.GetAllInfoOfType(_infoType);

        foreach(var item in infoItems)
        {
            var label = new Button(new Rectangle(layoutGroup.GetBoundingBox().X, layoutGroup.GetBoundingBox().Y, layoutGroup.GetBoundingBox().Width - 20, 30), item.Value, Core.DefaultFont, ColorPalette.Green, ColorPalette.DarkGreen, ColorPalette.ActualWhite, () => { _onSelectInfo?.Invoke(item); });
            layoutGroup.AddChild(label);
            scrollArea.RefreshContentBounds();
        }
    }

}