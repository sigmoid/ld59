using System;
using System.Collections.Generic;
using ld59;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

public class PuzzleSolutionUI : UIPanel
{
    private Rectangle _bounds;
    private Window _rootContainer;
    private RichTextArea _richText;
    private Label _statusLabel;
    private InfoSelectionWindow _infoSelectionWindow;
    private static int _currentPuzzleIndex = 0;

    private static readonly string[] _workerNames = new[] { "Miguel Angel Soto", "Sebastian Araya" };

    private static readonly (string Template, List<RichTextArea.Slot> Slots)[] _puzzles = new (string, List<RichTextArea.Slot>)[]
    {
        (
            "Who is the CEO of Eutropia: {0}\n\nWho is the CEO of The Anastasia Corporation: {1}\n\nWho is the CEO of LithOS Software: {2}\n\nWho is the CEO of Scram! Games: {3}",
            new List<RichTextArea.Slot>
            {
                new() { InfoType = InfoType.Name, CorrectSolution = "jack pilgrim" },
                new() { InfoType = InfoType.Name, CorrectSolution = "lance nightingale" },
                new() { InfoType = InfoType.Name, CorrectSolution = "ed sled" },
                new() { InfoType = InfoType.Name, CorrectSolution = "rose cantrell" },
            }
        ),
        (
            "Project {0} is an Anastasia Corporation project led by {1} to mine {2} in {3}.\nWorkers used {4} to mine, but an accident caused {5} and {6} to die by {7}",
            new List<RichTextArea.Slot>
            {
                new() { InfoType = InfoType.Codename, CorrectSolution = "orpheus"},
                new() { InfoType = InfoType.Name, CorrectSolution = "yarden imam"},
                new() { InfoType = InfoType.Resource, CorrectSolution = "lithium"},
                new() { InfoType = InfoType.Location, CorrectSolution = "chile"},
                new() { InfoType = InfoType.Tool, CorrectSolution = ""},
                new() { InfoType = InfoType.Name, CorrectSolution = _workerNames[0]},
                new() { InfoType = InfoType.Name, CorrectSolution = _workerNames[1]},
                new() { InfoType = InfoType.CauseOfDeath, CorrectSolution = "atomization"},
                
            }
        ),
    };

    public PuzzleSolutionUI(Rectangle bounds, string solutionText) : base()
    {
        _bounds = bounds;
        CreateUI();
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        _bounds = bounds;
    }

    public override Rectangle GetBoundingBox() => _bounds;

    private void CreateUI()
    {
        _rootContainer = new Window(_bounds, "Looking Glass", Core.DefaultFont,
            ColorPalette.ActualWhite, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.DarkGreen, 2);
        _rootContainer.SetCloseButtonColors(ColorPalette.DarkGreen, ColorPalette.LightGreen);
        Core.UISystem.AddElement(_rootContainer);
        TaskbarRegistry.Register("Looking Glass", Core.Content.Load<Texture2D>("images/puzzle_icon"), _rootContainer);

        var cb = _rootContainer.GetContentBounds();
        int statusH = 36;

        _richText = new RichTextArea(
            new Rectangle(cb.X + 10, cb.Y + 10, cb.Width - 20, cb.Height - statusH - 30),
            Core.DefaultFont,
            ColorPalette.ActualWhite, ColorPalette.Black);
        _richText.OnSlotClicked = (idx, infoType, onSelected) => OpenInfoSelection(infoType, onSelected);
        _rootContainer.AddChild(_richText);

        _statusLabel = new Label(
            new Rectangle(cb.X + 10, cb.Bottom - statusH - 10, cb.Width - 20, statusH),
            "", Core.DefaultFont, ColorPalette.Black, Color.Transparent);
        _rootContainer.AddChild(_statusLabel);

        LoadCurrentPuzzle();
    }

    private void LoadCurrentPuzzle()
    {
        if (_currentPuzzleIndex >= _puzzles.Length) return;
        var (template, slots) = _puzzles[_currentPuzzleIndex];
        foreach (var s in slots) s.Value = null;
        _richText.SetContent(template, slots);
        _statusLabel.Text = "";
    }

    private void OpenInfoSelection(InfoType infoType, Action<string> onValueSelected)
    {
        if (_infoSelectionWindow != null) return;
        var bounds = new Rectangle(_bounds.X + 50, _bounds.Y + 50, _bounds.Width - 100, _bounds.Height - 100);
        _infoSelectionWindow = new InfoSelectionWindow(bounds, infoType, selectedInfo =>
        {
            if (selectedInfo != null)
                onValueSelected(selectedInfo.Value);
            _infoSelectionWindow.CloseWindow();
            Core.UISystem.RemoveElement(_infoSelectionWindow);
            _infoSelectionWindow = null;
            TestSolution();
        });
        Core.UISystem.AddElement(_infoSelectionWindow);
    }

    private bool IsSlotCorrect(RichTextArea.Slot slot, List<RichTextArea.Slot> allSlots)
    {
        if (string.IsNullOrEmpty(slot.Value)) return false;
        var playerVal = slot.Value.Trim().ToLower();

        if (slot.AcceptableSolutions == null || slot.AcceptableSolutions.Length == 0)
            return playerVal == slot.CorrectSolution?.Trim().ToLower();

        if (!System.Array.Exists(slot.AcceptableSolutions, s => s.Trim().ToLower() == playerVal))
            return false;

        foreach (var sibling in allSlots)
        {
            if (sibling != slot && sibling.AcceptableSolutions == slot.AcceptableSolutions
                && sibling.Value?.Trim().ToLower() == playerVal)
                return false;
        }
        return true;
    }

    private void TestSolution()
    {
        var slots = _richText.GetSlots();
        int filled = 0, correct = 0;
        foreach (var slot in slots)
        {
            if (!string.IsNullOrEmpty(slot.Value)) filled++;
            if (IsSlotCorrect(slot, slots)) correct++;
        }

        if (filled < slots.Count)
        {
            _statusLabel.Text = "Solution incomplete.";
            _statusLabel.TextColor = ColorPalette.Red;
            return;
        }

        if (correct == slots.Count)
        {
            ShowSuccess();
            int completedIndex = _currentPuzzleIndex;
            _currentPuzzleIndex++;
            if (_currentPuzzleIndex < _puzzles.Length)
                LoadCurrentPuzzle();

            if (completedIndex == 0)
                Core.CurrentScene.GetManager<EmailDataManager>()?.DeliverEmail("case2_start.eml");
            else
                Game1.Instance.EndGame();

            _statusLabel.Text = "";
            return;
        }

        if (correct == slots.Count - 1)
        {
            _statusLabel.Text = "One piece of information is incorrect.";
            _statusLabel.TextColor = ColorPalette.Orange;
        }
        else
        {
            _statusLabel.Text = "Solution is not correct.";
            _statusLabel.TextColor = ColorPalette.Red;
        }
    }

    private void ShowSuccess()
    {
        AudioAtlas.Confirmation_001.Play();
        var modal = new NotificationPopup(
            new Rectangle(_rootContainer.GetBoundingBox().Center.X - 300, _rootContainer.GetBoundingBox().Center.Y - 50, 600, 250),
            "Solution Correct: A new mission has been started.\n");
        DesktopUI.ToastManager.ShowSuccess("Mystery Solved", 5, Toast.ToastPosition.TopRight);
        Core.UISystem.AddElement(modal);
    }
}
