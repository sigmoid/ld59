using System.Collections.Generic;
using System.Linq;
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
    private UIImage _splashImage;
    private Dictionary<(int,int), MinefieldCell> _cells = new Dictionary<(int,int), MinefieldCell>();
    private Label _coordinatesLabel;
    private List<(int, int)> _inputSequence = new List<(int, int)>();
    private List<(int, int)> _winningSequence = new List<(int, int)>()
    {
        (2,4),
        (8,3),
        (7,3),
        (8,7),
        (5,4)
    };
    private float _splashTimer = 0;
    private float _splashDuration = 0.8f;

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

    public override void Update(float deltaTime)
    {
        if(_splashImage.IsVisible())
        {
            _splashTimer += deltaTime;
            if(_splashTimer >= _splashDuration)
            {
                _splashImage.SetVisibility(false);
            }
        }

        _coordinatesLabel.Text = "";
        var mousePoint = Core.GetTransformedMousePoint();

        foreach(var cell in _cells)
        {
            var key = cell.Key;
            var value = cell.Value;

            if(value.GetBoundingBox().Contains(mousePoint))
            {
                _coordinatesLabel.Text = $"({key.Item1}, {key.Item2})";
                break;
            }
        }

        if(_inputSequence.SequenceEqual(_winningSequence))
        {
            _inputSequence.Clear();
            var modal = new NotificationPopup(new Rectangle(Core.GraphicsDevice.Viewport.Width / 2 - 150, Core.GraphicsDevice.Viewport.Height / 2 - 75, 700, 300), "Secret Sequence Activated: To decrypt reward use keys: x.okafor@anastasia.net, o.nightingale@anastasia.net, umiyamoto@anastasia.net\n");
            Core.UISystem.AddElement(modal);
        }

        base.Update(deltaTime);
    }

    public override void LateUpdate(float deltaTime)
    {
        base.LateUpdate(deltaTime);
    }


    private void CreateUI()
    {
        _rootWindow = new Window(_bounds, "Minefield", Core.DefaultFont);
        _rootWindow.SetColors(ColorPalette.DarkCream, ColorPalette.DarkGreen, ColorPalette.ActualWhite, ColorPalette.DarkGreen);
        TaskbarRegistry.Register("Minefield", Core.Content.Load<Microsoft.Xna.Framework.Graphics.Texture2D>("images/minefield_icon"), _rootWindow);
        
        _statusLabel = new Label(new Rectangle(_rootWindow.GetContentBounds().X + 10, _rootWindow.GetContentBounds().Y + 10, _rootWindow.GetContentBounds().Width - 20, 20), "Click a cell to start!", Core.DefaultFont, ColorPalette.ActualWhite);
        _rootWindow.AddChild(_statusLabel);
        var resetButton = new Button(new Rectangle(_rootWindow.GetContentBounds().Right - 90, _rootWindow.GetContentBounds().Y + 10, 80, 30), "Reset", Core.DefaultFont,ColorPalette.DarkGreen, ColorPalette.LightGreen, ColorPalette.ActualWhite, () => CreateGame());
        _rootWindow.AddChild(resetButton);

        var gridBounds = new Rectangle(_rootWindow.GetContentBounds().X + 10, _rootWindow.GetContentBounds().Y + 100, _rootWindow.GetContentBounds().Width - 20, _rootWindow.GetContentBounds().Height - 130);
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

        _coordinatesLabel = new Label(new Rectangle(_rootWindow.GetContentBounds().X + 10, gridBounds.Bottom + 5, _rootWindow.GetContentBounds().Width - 20, 20), "", Core.DefaultFont, ColorPalette.ActualWhite);
        _rootWindow.AddChild(_coordinatesLabel);

        CreateGame();

        _rootWindow.AddChild(grid);
        Core.UISystem.AddElement(_rootWindow);

        _splashImage = new UIImage( Core.Content.Load<Texture2D>("images/scramlogo"), new Rectangle(_rootWindow.GetContentBounds().X, _rootWindow.GetContentBounds().Y + 70, _rootWindow.GetContentBounds().Width, _rootWindow.GetContentBounds().Width ));
        _rootWindow.AddChild(_splashImage);
        _rootWindow.OnFocus();
    }

    private void CreateGame()
    {
        _inputSequence.Clear();
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
            } while (_cells[(x, y)].HasMine || _winningSequence.Contains((x, y)));

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

        _inputSequence.Add(position);

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

        _inputSequence.Clear();
        AudioAtlas.Confirmation_003.Play();
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

        AudioAtlas.Error_004.Play();
        _inputSequence.Clear();

        _statusLabel.Text = "Game Over! Click Reset to try again.";
    }
}