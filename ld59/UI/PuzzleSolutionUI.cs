
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
    private LayoutGroup _problemsLayoutGroup;
    private int _currentPuzzleIndex = 0;

    private List<PuzzleSolutionPiece> _solutionPieces = new List<PuzzleSolutionPiece>()
    {
        new PuzzleSolutionPiece() { ProblemDescription = "Who is the head engineer", PlayerSolution = "", CorrectSolution = "ed sled", RelatedInfoType = InfoType.Name},
        new PuzzleSolutionPiece() { ProblemDescription = "What project were they assigned to before 3/20", PlayerSolution = "", CorrectSolution = "orpheus", RelatedInfoType = InfoType.Codename},
        new PuzzleSolutionPiece() { ProblemDescription = "What is their rank in the company", PlayerSolution = "", CorrectSolution = "tier iii", RelatedInfoType = InfoType.Rank},
        new PuzzleSolutionPiece() { ProblemDescription = "Who takes minutes at executive meetings", PlayerSolution = "", CorrectSolution = "leif haynes", RelatedInfoType = InfoType.Name},
    };
    private List<PuzzleSolutionPiece> _solutionPieces2 = new List<PuzzleSolutionPiece>()
    {
        new PuzzleSolutionPiece() { ProblemDescription = "Who lead project Agamemnon", PlayerSolution = "", CorrectSolution = "yarden imam", RelatedInfoType = InfoType.Name},
        new PuzzleSolutionPiece() { ProblemDescription = "Who lead project Achilles", PlayerSolution = "", CorrectSolution = "mortimer nightingale", RelatedInfoType = InfoType.Name},
        new PuzzleSolutionPiece() { ProblemDescription = "What is the codename of the assassination plot", PlayerSolution = "", CorrectSolution = "agamemnon", RelatedInfoType = InfoType.Codename},
    };

    private List<PuzzleSolutionPiece> _solutionPieces3 = new List<PuzzleSolutionPiece>()
    {
        new PuzzleSolutionPiece() { ProblemDescription = "What is the name of the assassin who killed Rose Cantrell", PlayerSolution = "", CorrectSolution = "mike poisson", RelatedInfoType = InfoType.Name},
    };

    private List<List<PuzzleSolutionPiece>> _puzzleSequence = new List<List<PuzzleSolutionPiece>>();

    private List<PuzzleSolutionPiece> _currentSolutionPieces {get;set;}

    private List<Button> _solutionButtons = new List<Button>();

    public PuzzleSolutionUI(Rectangle bounds, string solutionText) : base()
    {
        _bounds = bounds;

        _currentSolutionPieces = _solutionPieces;
        CreateUI(solutionText);


        _puzzleSequence.Add(_solutionPieces);
        _puzzleSequence.Add(_solutionPieces2);
        _puzzleSequence.Add(_solutionPieces3);
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

        _problemsLayoutGroup = new VerticalLayoutGroup(new Rectangle(contentBounds.X + 20, contentBounds.Y + 10, contentBounds.Width - 40, contentBounds.Height - 100), 10);

        PopulateCurrentMystery();

        _rootContainer.AddChild(_problemsLayoutGroup);

        _solutionLabel = new Label(new Rectangle(contentBounds.X + 20, contentBounds.Bottom - 80, contentBounds.Width - 40, 30), solutionText, Core.DefaultFont, ColorPalette.Black);
        _rootContainer.AddChild(_solutionLabel);
    }

    private void PopulateCurrentMystery()
    {
        _problemsLayoutGroup.ClearChildren();

        foreach(var piece in _currentSolutionPieces)
        {
            var horizontalGroup = new HorizontalLayoutGroup(new Rectangle(_problemsLayoutGroup.GetBoundingBox().X, _problemsLayoutGroup.GetBoundingBox().Y, _problemsLayoutGroup.GetBoundingBox().Width, 100), 5);
            var pieceLabel = new TextArea(new Rectangle(_problemsLayoutGroup.GetBoundingBox().X, _problemsLayoutGroup.GetBoundingBox().Y, (int)(_problemsLayoutGroup.GetBoundingBox().Width * 0.5f), 100), Core.DefaultFont, true, true, Color.White, Color.Black);
            pieceLabel.Text = piece.ProblemDescription + "\n";
            Button problemButton = null;
            problemButton = new Button(new Rectangle(horizontalGroup.GetBoundingBox().X + (int)(_problemsLayoutGroup.GetBoundingBox().Width * 0.5f) + 5, horizontalGroup.GetBoundingBox().Y, (int)(_problemsLayoutGroup.GetBoundingBox().Width * 0.5f) - 5, 100), "<SELECT>", Core.DefaultFont, ColorPalette.Green, ColorPalette.DarkGreen, ColorPalette.ActualWhite, () => OpenInfoSelection(piece.RelatedInfoType, (info) => SelectInfoForButton(info, problemButton, piece)), ColorPalette.Green);
            _solutionButtons.Add(problemButton);
            horizontalGroup.AddChild(pieceLabel);
            horizontalGroup.AddChild(problemButton);
            _problemsLayoutGroup.AddChild(horizontalGroup);
        }
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
    
    private void ShowSuccess()
    {
        var modal = new NotificationPopup(new Rectangle(_rootContainer.GetBoundingBox().Center.X - 300, _rootContainer.GetBoundingBox().Center.Y - 50, 600, 250), "Solution Correct: A new mission has been started.\n");
        DesktopUI.ToastManager.ShowSuccess("Mystery Solved", 5, Toast.ToastPosition.TopRight);
        Core.UISystem.AddElement(modal);
    }

    private void TestSolution()
    {
        int correctCount = 0;
        int completeCount = 0;
        for(int i = 0; i < _currentSolutionPieces.Count; i++)
        {
            var piece = _currentSolutionPieces[i];
            if(piece.PlayerSolution.Trim().ToLower() == piece.CorrectSolution.Trim().ToLower())
            {
                correctCount++;
            }
            if(!string.IsNullOrEmpty(piece.PlayerSolution))
            {
                completeCount++;
            }
        }

        if(completeCount < _currentSolutionPieces.Count)
        {
            _solutionLabel.Text = $"Solution incomplete.";
            _solutionLabel.TextColor = ColorPalette.Red;
        }
        else if(correctCount == _currentSolutionPieces.Count)
        {
            ShowSuccess();
            _currentPuzzleIndex++;
            if(_currentPuzzleIndex < _puzzleSequence.Count)
            {
                _currentSolutionPieces = _puzzleSequence[_currentPuzzleIndex];
                PopulateCurrentMystery();
            }
            else
            {
                // TODO you beat the game
            }
        
            _solutionLabel.Text = "";
            _solutionLabel.TextColor = ColorPalette.LightGreen;
        }
        else if(correctCount == _currentSolutionPieces.Count - 1)
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