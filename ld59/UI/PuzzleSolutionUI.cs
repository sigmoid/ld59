
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.VisualBasic;
using Microsoft.Xna.Framework;
using Quartz;
using Quartz.UI;

public class PuzzleSolutionPiece
{
    public string ProblemDescription {get;set;}
    public string PlayerSolution {get;set;}
    public string CorrectSolution {get;set;}
    public InfoType RelatedInfoType {get;set;}
}

public class PuzzleSolutionUI : UIPanel
{
    private Rectangle _bounds;
    private Window _rootContainer;
    private Label _solutionLabel;
    private InfoSelectionWindow _infoSelectionWindow;
    private Label _resultLabel;
    private List<PuzzleSolutionPiece> _solutionPieces = new List<PuzzleSolutionPiece>()
    {
        new PuzzleSolutionPiece() { ProblemDescription = "Who leaked the information to the press", PlayerSolution = "", CorrectSolution = "jim golden", RelatedInfoType = InfoType.Name},
        new PuzzleSolutionPiece() { ProblemDescription = "Which employee oversees the surveillance program", PlayerSolution = "", CorrectSolution = "yarden imam", RelatedInfoType = InfoType.Name},
        new PuzzleSolutionPiece() { ProblemDescription = "What is the codename of the surveillance program", PlayerSolution = "", CorrectSolution = "jim golden", RelatedInfoType = InfoType.Name},
    };

    private List<Button> _solutionButtons = new List<Button>();

    public PuzzleSolutionUI(Rectangle bounds, string solutionText)
    {
        _bounds = bounds;

        CreateUI(solutionText);
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

    private void CreateUI(string solutionText)
    {
        _rootContainer = new Window(_bounds, "Puzzle Solution", Core.DefaultFont, ColorPalette.ActualWhite, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.DarkGreen, 2);
        Core.UISystem.AddElement(_rootContainer);

        var contentBounds = _rootContainer.GetContentBounds();

        var problemLayoutGroup = new VerticalLayoutGroup(new Rectangle(contentBounds.X + 20, contentBounds.Y + 10, contentBounds.Width - 40, contentBounds.Height - 100), 10);

        foreach(var piece in _solutionPieces)
        {
            var horizontalGroup = new HorizontalLayoutGroup(new Rectangle(problemLayoutGroup.GetBoundingBox().X, problemLayoutGroup.GetBoundingBox().Y, problemLayoutGroup.GetBoundingBox().Width, 100), 5);
            var pieceLabel = new TextArea(new Rectangle(problemLayoutGroup.GetBoundingBox().X, problemLayoutGroup.GetBoundingBox().Y, (int)(problemLayoutGroup.GetBoundingBox().Width * 0.5f), 100), Core.DefaultFont, true, true, Color.White, Color.Black);
            pieceLabel.Text = piece.ProblemDescription + "\n";
            Button problemButton = null;
            problemButton = new Button(new Rectangle(horizontalGroup.GetBoundingBox().X + (int)(problemLayoutGroup.GetBoundingBox().Width * 0.5f) + 5, horizontalGroup.GetBoundingBox().Y, (int)(problemLayoutGroup.GetBoundingBox().Width * 0.5f) - 5, 100), "<SELECT>", Core.DefaultFont, ColorPalette.Green, ColorPalette.DarkGreen, ColorPalette.ActualWhite, () => OpenInfoSelection(piece.RelatedInfoType, (info) => SelectInfoForButton(info, problemButton, piece)), ColorPalette.Green);
            _solutionButtons.Add(problemButton);
            horizontalGroup.AddChild(pieceLabel);
            horizontalGroup.AddChild(problemButton);
            problemLayoutGroup.AddChild(horizontalGroup);
        }

        _rootContainer.AddChild(problemLayoutGroup);

        _solutionLabel = new Label(new Rectangle(contentBounds.X + 20, contentBounds.Bottom - 80, contentBounds.Width - 40, 30), solutionText, Core.DefaultFont, ColorPalette.Black);
        _rootContainer.AddChild(_solutionLabel);
    }

    private void OpenInfoSelection(InfoType infoType, Action<GameInfo> onInfoSelected = null)
    {
        _infoSelectionWindow = new InfoSelectionWindow(new Rectangle(_bounds.X + 50, _bounds.Y + 50, _bounds.Width - 100, _bounds.Height - 100), infoType, (selectedInfo) => {
            if (selectedInfo != null)
            {
                onInfoSelected?.Invoke(selectedInfo);
            }
            _infoSelectionWindow.CloseWindow();
            Core.UISystem.RemoveElement(_infoSelectionWindow);
        });
        Core.UISystem.AddElement(_infoSelectionWindow);
    }

    private void SelectInfoForButton(GameInfo info, Button button, PuzzleSolutionPiece piece)
    {
        piece.PlayerSolution = info.Value;
        button.SetText(info.Value);
        TestSolution();
    }

    private void TestSolution()
    {
        int correctCount = 0;
        int completeCount = 0;
        for(int i = 0; i < _solutionPieces.Count; i++)
        {
            var piece = _solutionPieces[i];
            if(piece.PlayerSolution.Trim().ToLower() == piece.CorrectSolution.Trim().ToLower())
            {
                correctCount++;
            }
            if(!string.IsNullOrEmpty(piece.PlayerSolution))
            {
                completeCount++;
            }
        }

        if(completeCount < _solutionPieces.Count)
        {
            _solutionLabel.Text = $"Solution incomplete.";
            _solutionLabel.TextColor = ColorPalette.Red;
        }
        else if(correctCount == _solutionPieces.Count)
        {
            _solutionLabel.Text = "Solution verified";
            _solutionLabel.TextColor = ColorPalette.LightGreen;
        }
        else if(correctCount == _solutionPieces.Count - 1)
        {
            _solutionLabel.Text = $"One piece of information is incorrect.";
            _solutionLabel.TextColor = ColorPalette.Orange;
        }
        else
        {
            _solutionLabel.Text = $"Solution is not correct.";
            _solutionLabel.TextColor = ColorPalette.Red;
        }
    }
}