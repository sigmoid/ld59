using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Quartz;
using Quartz.UI;

public class Minefield : UIPanel
{
    private Rectangle _bounds;
    private Window _rootWindow;
    private int _cellsWide = 16;
    private int _cellsHigh = 16;
    private int _numMines = 40;
    private Label _statusLabel;
    private Dictionary<(int,int), MinefieldCell> _cells = new Dictionary<(int,int), MinefieldCell>();

    public Minefield(Rectangle bounds)
    {
        _bounds = bounds;
        CreateUI();
    }

    public override void SetBounds(Rectangle bounds)
    {
        _bounds = bounds;
        base.SetBounds(bounds);
    }

    public override Rectangle GetBoundingBox()
    {
        return _bounds;
    }


    private void CreateUI()
    {
        _rootWindow = new Window(_bounds, "Minefield", Core.DefaultFont);
        _rootWindow.SetColors(ColorPalette.DarkCream, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.DarkGreen);
        
        _statusLabel = new Label(new Rectangle(_bounds.X + 10, _rootWindow.GetContentBounds().Y + 10, _bounds.Width - 20, 20), "Click a cell to start!", Core.DefaultFont, ColorPalette.ActualWhite);
        _rootWindow.AddChild(_statusLabel);
        var resetButton = new Button(new Rectangle(_rootWindow.GetContentBounds().Right - 90, _rootWindow.GetContentBounds().Y + 10, 80, 30), "Reset", Core.DefaultFont,ColorPalette.DarkGreen, ColorPalette.LightGreen, ColorPalette.ActualWhite, () => CreateGame());
        _rootWindow.AddChild(resetButton);

        var gridBounds = new Rectangle(_bounds.X + 10, _bounds.Y + 100, _bounds.Width - 20, _bounds.Height - 110);
        var grid = new GridLayoutGroup(gridBounds, _cellsWide, _cellsHigh, 1, 1);

        var cellWidth = gridBounds.Width / _cellsWide;
        var cellHeight = gridBounds.Height / _cellsHigh;

        for(int y = 0; y < _cellsHigh; y++)
        {
            for(int x = 0; x < _cellsWide; x++)
            {
                var cell = new MinefieldCell(new Rectangle(gridBounds.X + x * cellWidth, gridBounds.Y + y * cellHeight, cellWidth, cellHeight), (x,y));
                cell.OnClick += OnCellClicked;
                _cells[(x, y)] = cell;
                grid.AddChild(cell);
            }
        }

        CreateGame();

        _rootWindow.AddChild(grid);
        AddChild(_rootWindow);

        _rootWindow.OnFocus();
    }

    private void CreateGame()
    {
        foreach(var cell in _cells.Values)
        {
            cell.HasMine = false;
            cell.IsRevealed = false;
            cell.NeighborCount = 0;
        }

        for(int i = 0; i < _numMines; i++)
        {
            int x, y;
            do
            {
                x = Random.Next(0, _cellsWide);
                y = Random.Next(0, _cellsHigh);
            } while (_cells[(x, y)].HasMine);

            _cells[(x, y)].HasMine = true;
        }

        for(int y = 0; y < _cellsHigh; y++)
        {
            for(int x = 0; x < _cellsWide; x++)
            {
                if(_cells[(x, y)].HasMine) continue;

                var neighbors = new List<(int,int)>()
                {
                    (x - 1, y - 1),
                    (x, y - 1),
                    (x + 1, y - 1),
                    (x - 1, y),
                    (x + 1, y),
                    (x - 1, y + 1),
                    (x, y + 1),
                    (x + 1, y + 1)
                };

                int mineCount = 0;
                foreach(var neighbor in neighbors)
                {
                    if(_cells.ContainsKey(neighbor) && _cells[neighbor].HasMine)
                    {
                        mineCount++;
                    }
                }

                _cells[(x, y)].NeighborCount = mineCount;
            }
        }
    }

    private void OnCellClicked((int,int) position)
    {
        _statusLabel.Text = "";
        if(_cells[position].HasMine)
        {
            GameOver();
            return;
        }

        dfs(position);

        CheckVictory();
    }

    private void CheckVictory()
    {
        foreach(var cell in _cells.Values)
        {
            if(!cell.HasMine && !cell.IsRevealed)
            {
                return;
            }
        }

        _statusLabel.Text = "Congratulations! You've cleared the minefield!";
    }

    private void dfs((int,int) startCell)
    {
        var stack = new Stack<(int,int)>();
        stack.Push(startCell);

        while(stack.Count > 0)
        {
            var cellPos = stack.Pop();
            var cell = _cells[cellPos];
            if(cell.IsRevealed) continue;

            cell.IsRevealed = true;

            if(cell.NeighborCount == 0 && !cell.HasMine)
            {
                var neighbors = new List<(int,int)>()
                {
                    (cellPos.Item1 - 1, cellPos.Item2 - 1),
                    (cellPos.Item1, cellPos.Item2 - 1),
                    (cellPos.Item1 + 1, cellPos.Item2 - 1),
                    (cellPos.Item1 - 1, cellPos.Item2),
                    (cellPos.Item1 + 1, cellPos.Item2),
                    (cellPos.Item1 - 1, cellPos.Item2 + 1),
                    (cellPos.Item1, cellPos.Item2 + 1),
                    (cellPos.Item1 + 1, cellPos.Item2 + 1)
                };

                foreach(var neighbor in neighbors)
                {
                    if(_cells.ContainsKey(neighbor) && !_cells[neighbor].IsRevealed)
                    {
                        stack.Push(neighbor);
                    }
                }
            }
        }
    }

    private void GameOver()
    {
        foreach(var cell in _cells.Values)
        {
            cell.IsRevealed = true;
        }

        _statusLabel.Text = "Game Over! Click Reset to try again.";
    }
}