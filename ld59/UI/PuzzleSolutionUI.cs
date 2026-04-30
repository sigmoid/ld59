
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ld59;
using Microsoft.VisualBasic;
using Microsoft.Xna.Framework;
using Quartz;
using Quartz.UI;

public class PuzzleSolutionPiece
{
    public string ProblemDescription {get;set;}
    public string PlayerSolution {get;set;}
    public string CorrectSolution {get;set;}
    public string[] AcceptableSolutions {get;set;}
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
    private static int _currentPuzzleIndex = 0;

    private List<PuzzleSolutionPiece> _solutionPieces = new List<PuzzleSolutionPiece>()
    {
        new PuzzleSolutionPiece() { ProblemDescription = "Who is the CEO of Eutropia", PlayerSolution = "", CorrectSolution = "jack pilgrim", RelatedInfoType = InfoType.Name},
        new PuzzleSolutionPiece() { ProblemDescription = "Who is the CEO of The Anastasia Corporation", PlayerSolution = "", CorrectSolution = "lance nightingale", RelatedInfoType = InfoType.Name},
        new PuzzleSolutionPiece() { ProblemDescription = "Who is the CEO of Lithos Software", PlayerSolution = "", CorrectSolution = "ed sled", RelatedInfoType = InfoType.Name},
        new PuzzleSolutionPiece() { ProblemDescription = "Who is the CEO of Scram! Games", PlayerSolution = "", CorrectSolution = "rose cantrell", RelatedInfoType = InfoType.Name},
    };

    private static readonly string[] _workerNames = new[] { "Miguel Angel Soto", "Sebastian Araya" };

    private List<PuzzleSolutionPiece> _solutionPieces2 = new List<PuzzleSolutionPiece>()
    {
        new PuzzleSolutionPiece() { ProblemDescription = "Name of worker who died", PlayerSolution = "", AcceptableSolutions = _workerNames, RelatedInfoType = InfoType.Name},
        new PuzzleSolutionPiece() { ProblemDescription = "Name of worker who died", PlayerSolution = "", AcceptableSolutions = _workerNames, RelatedInfoType = InfoType.Name},
        new PuzzleSolutionPiece() { ProblemDescription = "Cause of death", PlayerSolution = "", CorrectSolution = "suffocation", RelatedInfoType = InfoType.CauseOfDeath},
    };

    private List<PuzzleSolutionPiece> _solutionPieces3 = new List<PuzzleSolutionPiece>()
    {
        new PuzzleSolutionPiece() { ProblemDescription = "What is the name of the assassin who killed Rose Cantrell", PlayerSolution = "", CorrectSolution = "mike poisson", RelatedInfoType = InfoType.Name},
    };

    private List<List<PuzzleSolutionPiece>> _puzzleSequence = new List<List<PuzzleSolutionPiece>>();

    private List<PuzzleSolutionPiece> _currentSolutionPieces {get {return _puzzleSequence[_currentPuzzleIndex];}}

    private List<Button> _solutionButtons = new List<Button>();

    public PuzzleSolutionUI(Rectangle bounds, string solutionText) : base()
    {
        _bounds = bounds;

        _puzzleSequence.Add(_solutionPieces);
        _puzzleSequence.Add(_solutionPieces2);
        _puzzleSequence.Add(_solutionPieces3);
        
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
        _rootContainer = new Window(_bounds, "Looking Glass", Core.DefaultFont, ColorPalette.ActualWhite, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.DarkGreen, 2);
        Core.UISystem.AddElement(_rootContainer);
        TaskbarRegistry.Register("Looking Glass", Core.Content.Load<Microsoft.Xna.Framework.Graphics.Texture2D>("images/puzzle_icon"), _rootContainer);

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
            var horizontalGroup = new HorizontalLayoutGroup(new Rectangle(_problemsLayoutGroup.GetBoundingBox().X, _problemsLayoutGroup.GetBoundingBox().Y, _problemsLayoutGroup.GetBoundingBox().Width, 150), 5);
            var pieceLabel = new TextArea(new Rectangle(_problemsLayoutGroup.GetBoundingBox().X, _problemsLayoutGroup.GetBoundingBox().Y, (int)(_problemsLayoutGroup.GetBoundingBox().Width * 0.5f), 150), Core.DefaultFont, true, true, Color.White, Color.Black);
            pieceLabel.Text = piece.ProblemDescription + "\n";
            Button problemButton = null;
            problemButton = new Button(new Rectangle(horizontalGroup.GetBoundingBox().X + (int)(_problemsLayoutGroup.GetBoundingBox().Width * 0.5f) + 5, horizontalGroup.GetBoundingBox().Y, (int)(_problemsLayoutGroup.GetBoundingBox().Width * 0.5f) - 5, 60), "<SELECT>", Core.DefaultFont, ColorPalette.Green, ColorPalette.DarkGreen, ColorPalette.ActualWhite, () => OpenInfoSelection(piece.RelatedInfoType, (info) => SelectInfoForButton(info, problemButton, piece)), ColorPalette.Green);
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
        AudioAtlas.Confirmation_001.Play();
        var modal = new NotificationPopup(new Rectangle(_rootContainer.GetBoundingBox().Center.X - 300, _rootContainer.GetBoundingBox().Center.Y - 50, 600, 250), "Solution Correct: A new mission has been started.\n");
        DesktopUI.ToastManager.ShowSuccess("Mystery Solved", 5, Toast.ToastPosition.TopRight);
        Core.UISystem.AddElement(modal);
    }

    private bool IsPieceCorrect(PuzzleSolutionPiece piece)
    {
        var playerVal = piece.PlayerSolution.Trim().ToLower();
        if (piece.AcceptableSolutions == null || piece.AcceptableSolutions.Length == 0)
            return playerVal == piece.CorrectSolution.Trim().ToLower();

        if (!System.Array.Exists(piece.AcceptableSolutions, s => s.Trim().ToLower() == playerVal))
            return false;

        // Reject duplicate answers among pieces sharing the same AcceptableSolutions pool
        foreach (var sibling in _currentSolutionPieces)
        {
            if (sibling != piece && sibling.AcceptableSolutions == piece.AcceptableSolutions
                && sibling.PlayerSolution.Trim().ToLower() == playerVal)
                return false;
        }
        return true;
    }

    private void TestSolution()
    {
        int correctCount = 0;
        int completeCount = 0;
        for(int i = 0; i < _currentSolutionPieces.Count; i++)
        {
            var piece = _currentSolutionPieces[i];
            if(IsPieceCorrect(piece))
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
            int completedIndex = _currentPuzzleIndex;
            _currentPuzzleIndex++;
            if(_currentPuzzleIndex < _puzzleSequence.Count)
            {
                PopulateCurrentMystery();
            }

            if (completedIndex == 0)
            {
                var emailManager = Core.CurrentScene.GetManager<EmailDataManager>();
                emailManager?.DeliverEmail("case2_start.eml");
            }
            else
            {
                Game1.Instance.EndGame();
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