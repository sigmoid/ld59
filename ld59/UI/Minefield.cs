using System.Collections.Generic;
using System.IO;
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
    private int _cellsWide = 12;
    private int _cellsHigh = 12;
    private int _numMines = 22;

    // Player-selectable mine count for the standard random board (Easy is the original 22).
    private enum Difficulty { Easy, Medium, Hard }
    private Difficulty _difficulty = Difficulty.Easy;
    private Button _difficultyButton;
    private static int MinesFor(Difficulty d) => d switch
    {
        Difficulty.Easy   => 22,
        Difficulty.Medium => 35,
        Difficulty.Hard   => 50,
        _ => 22,
    };

    private Label _statusLabel;
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
    private float _timer;
    private bool _hasGameStarted = false;

    // When set (during first-click regeneration), these cells are kept mine-free so the opening click
    // is safe and lands on a zero that floods a region open. Null the rest of the time.
    private HashSet<(int,int)> _firstClickSafeZone;

    // Authored board(s) loaded from a text file instead of a random board. A file may hold several
    // grids (separated by blank/comment lines) that form a progression: clear one to unlock the next.
    // _mineLayout/_exploredLayout/_flagLayout are the *current* board's layout (see ApplyBoard).
    private const string LevelsDir = "Content/files/minefield";
    private bool _authored = false;
    private HashSet<(int,int)> _mineLayout;
    private HashSet<(int,int)> _exploredLayout;
    private HashSet<(int,int)> _flagLayout;

    private List<AuthoredBoard> _progression;
    private int _progIndex;
    private bool _solved;
    private GridLayoutGroup _gridGroup;
    private Rectangle _gridArea;
    private Button _nextButton;

    private class AuthoredBoard
    {
        public int Width, Height;
        public HashSet<(int,int)> Mines = new();
        public HashSet<(int,int)> Explored = new();
        public HashSet<(int,int)> Flags = new();
    }

    public Minefield(Rectangle bounds)
    {
        _bounds = bounds;
        CreateUI();
    }

    /// <summary>Opens an authored board from <c>Content/files/minefield/&lt;levelName&gt;.txt</c>. Falls
    /// back to a random board if the file is missing or empty.</summary>
    public Minefield(Rectangle bounds, string levelName)
    {
        _bounds = bounds;
        LoadLevel(levelName);
        CreateUI();
    }

    /// <summary>
    /// Parses an authored file into a progression of one or more boards. A board is a run of grid
    /// rows; a blank or comment line ends the current board (so grids are separated by blank lines,
    /// and a '#' label between them works too). Boards are shown in order — clear one to unlock the
    /// next. Characters in a row:
    ///   '*' (or 'x'/'m') = mine, 'o' = explored (auto-revealed on open), 'f'/'F' = flag (which is
    ///   also a mine by default), anything else = hidden empty.
    /// Short rows are padded with empty cells. On any problem it logs and leaves the board in its
    /// default (random) configuration.
    /// </summary>
    private void LoadLevel(string levelName)
    {
        var path = $"{LevelsDir}/{levelName}.txt";
        if (!File.Exists(path))
        {
            Core.DeveloperConsole.PrintLine($"minefield: level '{levelName}' not found ({path}); using a random board");
            return;
        }

        // Group lines into grid blocks; a blank or comment line breaks the current block.
        var blocks = new List<List<string>>();
        var current = new List<string>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.TrimEnd();
            bool boundary = line.Trim().Length == 0 || line.TrimStart().StartsWith("#");
            if (boundary)
            {
                if (current.Count > 0) { blocks.Add(current); current = new List<string>(); }
                continue;
            }
            current.Add(line);
        }
        if (current.Count > 0) blocks.Add(current);

        _progression = blocks.Select(ParseBoard).ToList();
        if (_progression.Count == 0)
        {
            Core.DeveloperConsole.PrintLine($"minefield: level '{levelName}' has no grids; using a random board");
            _progression = null;
            return;
        }

        _progIndex = 0;
        _authored = true;
        ApplyBoard(0);
    }

    /// <summary>Turns one block of grid rows into a board layout.</summary>
    private AuthoredBoard ParseBoard(List<string> rows)
    {
        var board = new AuthoredBoard { Width = rows.Max(r => r.Length), Height = rows.Count };
        for (int y = 0; y < board.Height; y++)
        {
            var row = rows[y];
            for (int x = 0; x < board.Width; x++)
            {
                char c = x < row.Length ? row[x] : '.';
                switch (c)
                {
                    case '*': case 'x': case 'X': case 'm': case 'M':
                        board.Mines.Add((x, y));
                        break;
                    case 'o': case 'O':
                        board.Explored.Add((x, y));
                        break;
                    case 'f': case 'F': // a flag — also a mine by default
                        board.Mines.Add((x, y));
                        board.Flags.Add((x, y));
                        break;
                    // anything else ('.', ' ', '-', …) = hidden empty cell
                }
            }
        }
        return board;
    }

    /// <summary>Makes the board at <paramref name="index"/> the current one.</summary>
    private void ApplyBoard(int index)
    {
        var b = _progression[index];
        _cellsWide = b.Width;
        _cellsHigh = b.Height;
        _mineLayout = b.Mines;
        _exploredLayout = b.Explored;
        _flagLayout = b.Flags;
        _numMines = b.Mines.Count;
    }

    /// <summary>Advances to the next board in the progression and rebuilds the grid for it.</summary>
    private void AdvanceBoard()
    {
        if (_progression == null || _progIndex >= _progression.Count - 1) return;
        _progIndex++;
        ApplyBoard(_progIndex);
        BuildGrid();
        CreateGame();
        _nextButton?.SetVisibility(false);
        _statusLabel.Text = "Click a cell to start!";
        UpdateTitle();
    }

    private void UpdateTitle()
        => _rootWindow.SetTitle(_progression != null && _progression.Count > 1
            ? $"Minefield ({_progIndex + 1}/{_progression.Count})"
            : "Minefield");

    /// <summary>Help button: (re)start the demo progression in this window and open the alphabet image.</summary>
    private void OnHelpClicked()
    {
        StartLevel("demo");
        OpenAlphabetImage();
    }

    /// <summary>Loads an authored level by name into this window and shows its first board.</summary>
    private void StartLevel(string levelName)
    {
        LoadLevel(levelName); // sets _progression / _progIndex / current board, or logs on failure
        BuildGrid();
        CreateGame();
        UpdateTitle();
        _nextButton?.SetVisibility(false);
        _statusLabel.Text = "Click a cell to start!";
    }

    private void OpenAlphabetImage()
    {
        var file = Core.CurrentScene.GetManager<GameFileDataManager>().GetFileByPath("alphabet.img");
        if (file != null)
            Core.UISystem.AddElement(new ImageViewerUI(file));
        else
            Core.DeveloperConsole.PrintLine("minefield: alphabet.img not found");
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
        if(!_hasGameStarted && _inputSequence.Count > 0)
        {
            _hasGameStarted = true;
            _timer = 0;
        }

        if(_hasGameStarted)
        {
            _timer += deltaTime;
            _statusLabel.Text = $"Time: {_timer.ToString("0.0")}s";
        }

        // Offer the next board once the current one is cleared and another follows it.
        _nextButton?.SetVisibility(_solved && _progression != null && _progIndex < _progression.Count - 1);

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
        _rootWindow.SetColors(ColorPalette.ActualWhite, ColorPalette.Black, ColorPalette.ActualWhite, ColorPalette.Black);
        _rootWindow.SetCloseButtonColors(ColorPalette.Black, Color.DarkGray);
        TaskbarRegistry.Register("Minefield", Core.Content.Load<Microsoft.Xna.Framework.Graphics.Texture2D>("images/minefield_icon"), _rootWindow);

        var cb = _rootWindow.GetContentBounds();
        int headerHeight = 70;

        var headerPanel = new Label(new Rectangle(cb.X, cb.Y, cb.Width, headerHeight), "", Core.DefaultFont, ColorPalette.ActualWhite, ColorPalette.Green);
        _rootWindow.AddChild(headerPanel);

        _statusLabel = new Label(new Rectangle(cb.X + 10, cb.Y, cb.Width - 110, headerHeight), "Click a cell to start!", Core.DefaultFont, ColorPalette.Black);
        _rootWindow.AddChild(_statusLabel);

        var resetButton = new Button(new Rectangle(cb.Right - 90, cb.Y + (headerHeight - 30) / 2, 80, 30), "Reset", Core.DefaultFont, ColorPalette.Black, ColorPalette.DarkGreen, ColorPalette.ActualWhite, CreateGame);
        _rootWindow.AddChild(resetButton);

        // Shown only once the current board is cleared and another board follows it.
        _nextButton = new Button(new Rectangle(cb.Right - 180, cb.Y + (headerHeight - 30) / 2, 80, 30), "Next >", Core.DefaultFont, ColorPalette.Black, ColorPalette.DarkGreen, ColorPalette.ActualWhite, AdvanceBoard);
        _nextButton.SetVisibility(false);
        _rootWindow.AddChild(_nextButton);

        // Help: start the demo progression and open the rune alphabet reference image.
        var helpButton = new Button(new Rectangle(cb.Right - 220, cb.Y + (headerHeight - 30) / 2, 30, 30), "?", Core.DefaultFont, ColorPalette.Black, ColorPalette.DarkGreen, ColorPalette.ActualWhite, OnHelpClicked);
        _rootWindow.AddChild(helpButton);

        // Difficulty: cycles Easy -> Medium -> Hard, each starting a fresh random board.
        _difficultyButton = new Button(new Rectangle(cb.Right - 340, cb.Y + (headerHeight - 30) / 2, 110, 30), _difficulty.ToString(), Core.DefaultFont, ColorPalette.Black, ColorPalette.DarkGreen, ColorPalette.ActualWhite, CycleDifficulty);
        _rootWindow.AddChild(_difficultyButton);

        _gridArea = GridArea();

        _coordinatesLabel = new Label(new Rectangle(cb.X + 10, _gridArea.Bottom + 5, cb.Width - 20, 20), "", Core.DefaultFont, ColorPalette.Black);
        _rootWindow.AddChild(_coordinatesLabel);

        BuildGrid();
        CreateGame();
        UpdateTitle();

        Core.UISystem.AddElement(_rootWindow);

        _rootWindow.OnFocus();
    }

    /// <summary>The area available for the cell grid, in the window's CURRENT screen coordinates
    /// (below the header, inset from the edges). Recomputed each call so the grid lands correctly even
    /// if the window has been dragged since it was opened.</summary>
    private Rectangle GridArea()
    {
        const int headerHeight = 70;
        var cb = _rootWindow.GetContentBounds();
        return new Rectangle(cb.X + 10, cb.Y + headerHeight + 25, cb.Width - 20, cb.Height - headerHeight - 60);
    }

    /// <summary>(Re)creates the cell grid for the current board dimensions, replacing any existing one.
    /// Cells are square and centred within <see cref="GridArea"/> so any board shape stays undistorted.</summary>
    private void BuildGrid()
    {
        if (_gridGroup != null) { _rootWindow.DestroyChild(_gridGroup); _gridGroup = null; }
        _cells.Clear();

        var area = GridArea();
        int cellSize = System.Math.Max(1, System.Math.Min((area.Width - (_cellsWide - 1)) / _cellsWide,
                                                          (area.Height - (_cellsHigh - 1)) / _cellsHigh));
        int gridW = cellSize * _cellsWide + (_cellsWide - 1);
        int gridH = cellSize * _cellsHigh + (_cellsHigh - 1);
        var gridBounds = new Rectangle(area.X + (area.Width - gridW) / 2, area.Y + (area.Height - gridH) / 2, gridW, gridH);
        _gridGroup = new GridLayoutGroup(gridBounds, _cellsWide, _cellsHigh, 1, 1);

        for(int y = 0; y < _cellsHigh; y++)
        {
            for(int x = 0; x < _cellsWide; x++)
            {
                var cell = new MinefieldCell(new Rectangle(gridBounds.X + x * (cellSize + 1), gridBounds.Y + y * (cellSize + 1), cellSize, cellSize), (x,y));
                cell.OnClick += OnCellClicked;
                _cells[(x, y)] = cell;
                _gridGroup.AddChild(cell);
            }
        }

        _rootWindow.AddChild(_gridGroup);

        // Cells only accept input once focused. The window focuses its descendants on a focus change,
        // which won't fire for a grid rebuilt while the window is already active (e.g. clicking Next),
        // so focus the new cells now if this window is the active one.
        if (Core.UISystem.WindowManager.FocusedWindow == _rootWindow)
            _gridGroup.GainFocus();
    }

    // Advances Easy -> Medium -> Hard and starts a fresh random board at the new mine count. Difficulty
    // only applies to the standard random game, so this also drops out of any authored progression.
    private void CycleDifficulty()
    {
        _difficulty = (Difficulty)(((int)_difficulty + 1) % 3);
        _numMines = MinesFor(_difficulty);
        _difficultyButton.SetText(_difficulty.ToString());

        _authored = false;
        _progression = null;
        _nextButton?.SetVisibility(false);
        _cellsWide = 12;
        _cellsHigh = 12;

        BuildGrid();
        CreateGame();
        UpdateTitle();
    }

    private void CreateGame()
    {
        _timer = 0;
        _inputSequence.Clear();
        foreach(var cell in _cells.Values)
        {
            cell.HasMine = false;
            cell.IsRevealed = false;
            cell.IsFlagged = false;
            cell.NeighborCount = 0;
        }

        if(_authored)
        {
            foreach(var mine in _mineLayout)
                if(_cells.ContainsKey(mine)) _cells[mine].HasMine = true;
        }
        else
        {
            for(int i = 0; i < _numMines; i++)
            {
                int x, y;
                do
                {
                    x = Random.Next(0, _cellsWide);
                    y = Random.Next(0, _cellsHigh);
                } while (_cells[(x, y)].HasMine || _winningSequence.Contains((x, y))
                         || (_firstClickSafeZone != null && _firstClickSafeZone.Contains((x, y))));

                _cells[(x, y)].HasMine = true;
            }
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

                int mineCount = 0, leftMines = 0, rightMines = 0;
                foreach(var neighbor in neighbors)
                {
                    if(_cells.ContainsKey(neighbor) && _cells[neighbor].HasMine)
                    {
                        mineCount++;
                        if(neighbor.Item1 < x) leftMines++;
                        else if(neighbor.Item1 > x) rightMines++;
                    }
                }

                _cells[(x, y)].NeighborCount = mineCount;
                // Which side the mines lean to: only-right (+1), only-left (-1), or both/vertical (0).
                _cells[(x, y)].MineLean =
                    (rightMines > 0 && leftMines == 0) ? 1 :
                    (leftMines > 0 && rightMines == 0) ? -1 : 0;
            }
        }

        // Authored boards: auto-reveal the tiles the author marked as explored, exactly as if clicked
        // (a revealed empty/0 tile floods open its region, mirroring normal play).
        if(_authored && _exploredLayout != null)
        {
            foreach(var pos in _exploredLayout)
                if(_cells.TryGetValue(pos, out var cell) && !cell.HasMine)
                    dfs(pos);
        }

        // Flags last: a flag sits on a hidden tile, so keep it unrevealed even if a flood reached it.
        if(_authored && _flagLayout != null)
        {
            foreach(var pos in _flagLayout)
                if(_cells.TryGetValue(pos, out var cell))
                {
                    cell.IsRevealed = false;
                    cell.IsFlagged = true;
                }
        }

        // A fully-explored authored board counts as already solved (so Next appears immediately).
        _solved = AllNonMineRevealed();
    }

    /// <summary>True once every non-mine cell is revealed (the win condition).</summary>
    private bool AllNonMineRevealed()
    {
        foreach(var cell in _cells.Values)
            if(!cell.HasMine && !cell.IsRevealed) return false;
        return true;
    }

    private void OnCellClicked((int,int) position)
    {
        if(_solved) return;   // board already cleared — ignore further clicks (no replayed win sound)

        _statusLabel.Text = "";

        // First move on a random board: re-roll the mines with a mine-free zone around the click so it
        // can never hit a mine and lands on a zero, flooding several cells open. Authored puzzles keep
        // their fixed layout.
        if(_inputSequence.Count == 0 && !_authored)
        {
            _firstClickSafeZone = NeighborhoodOf(position);
            CreateGame();
            _firstClickSafeZone = null;
        }

        if(_cells[position].HasMine)
        {
            GameOver();
            return;
        }

        _inputSequence.Add(position);

        dfs(position);

        CheckVictory();
    }

    /// <summary>A cell plus its in-bounds 8 neighbours — the zone kept mine-free for a safe first click.</summary>
    private HashSet<(int,int)> NeighborhoodOf((int,int) position)
    {
        var zone = new HashSet<(int,int)>();
        for(int dy = -1; dy <= 1; dy++)
            for(int dx = -1; dx <= 1; dx++)
            {
                var neighbor = (position.Item1 + dx, position.Item2 + dy);
                if(_cells.ContainsKey(neighbor)) zone.Add(neighbor);
            }
        return zone;
    }

    private void CheckVictory()
    {
        if(!AllNonMineRevealed()) return;

        _inputSequence.Clear();
        AudioAtlas.Confirmation_003.Play();
        _statusLabel.Text = "Cleared! Time: " + _timer.ToString("0.0") + "s";
        _hasGameStarted = false;
        _solved = true;
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

        _hasGameStarted = false;
        _timer = 0;

        _statusLabel.Text = "Game Over!";
    }
}