using Microsoft.Win32;
using RollingMaze.Mazes;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IOPath = System.IO.Path;

namespace MazeDesigner;

public partial class MainWindow : Window
{
    private enum Tool { Select, Wall, Start, Goal, Hole, BlackHole, Dip }
    private enum DragKind { None, NewWall, Wall, WallStart, WallEnd, Start, Goal, Hole, BlackHole, Dip }

    private MazeDefinition _maze = NewMaze();
    private Tool _tool;
    private DragKind _dragKind;
    private Point _dragStart;
    private MazeWallDefinition? _selectedWall;
    private MazePoint? _selectedPoint;
    private DragKind _selectedKind;
    private Line? _preview;
    private ClipboardItem? _clipboardItem;
    private string? _path;
    private bool _dirty;
    private readonly List<string> _recentFiles = [];
    private readonly List<string> _recentFolders = [];
    private static readonly string RecentItemsPath = IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RollingMazeDesigner", "recent-items.json");

    public MainWindow()
    {
        InitializeComponent();
        LoadRecentItems();
        Loaded += (_, _) => Render();
        Closing += (_, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
    }

    private static MazeDefinition NewMaze() => new() { Name = "Untitled maze" };

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse(tag, out Tool tool)) _tool = tool;
    }

    private void ToolMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse(tag, out Tool tool))
        {
            _tool = tool;
            if (tool == Tool.Wall) WallToolButton.IsChecked = true;
            else if (tool == Tool.Hole) HoleToolButton.IsChecked = true;
            else if (tool == Tool.BlackHole) BlackHoleToolButton.IsChecked = true;
            else if (tool == Tool.Dip) DipToolButton.IsChecked = true;
            StatusText.Text = $"{((MenuItem)sender).Header}".Replace("_", "");
        }
    }

    private void Board_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Point p = Clamp(e.GetPosition(Board));
        _dragStart = p;
        Board.CaptureMouse();

        if (_tool == Tool.Wall)
        {
            _dragKind = DragKind.NewWall;
            _preview = MakeWall(p, p, true);
            Board.Children.Add(_preview);
            return;
        }

        if (_tool != Tool.Select)
        {
            MazePoint point = Normalize(p);
            switch (_tool)
            {
                case Tool.Start: _maze.Start = point; break;
                case Tool.Goal: _maze.Goal = point; break;
                case Tool.Hole: _maze.Holes.Add(point); break;
                case Tool.BlackHole: _maze.BlackHoles.Add(point); break;
                case Tool.Dip: _maze.Dip = point; break;
            }
            SetDirty();
            Render();
            Board.ReleaseMouseCapture();
            SelectTool();
            return;
        }

        EditorTag? tagInfo = FindEditorTag(VisualTreeHelper.HitTest(Board, p)?.VisualHit);
        if (tagInfo is not null)
        {
            _selectedWall = tagInfo.Wall;
            _selectedPoint = tagInfo.Point;
            _dragKind = tagInfo.Kind;
            _selectedKind = tagInfo.Kind;
            Render();
        }
        else
        {
            _selectedWall = null;
            _selectedPoint = null;
            _selectedKind = DragKind.None;
            _dragKind = DragKind.None;
            Render();
        }
    }

    private void Board_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragKind == DragKind.None) return;
        Point p = Clamp(e.GetPosition(Board));
        if (_dragKind == DragKind.NewWall && _preview is not null)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                if (Math.Abs(p.X - _dragStart.X) >= Math.Abs(p.Y - _dragStart.Y)) p.Y = _dragStart.Y;
                else p.X = _dragStart.X;
            }
            _preview.X2 = p.X; _preview.Y2 = p.Y;
            return;
        }

        double dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
        _dragStart = p;
        if (_dragKind == DragKind.Wall && _selectedWall is not null)
        {
            _maze.Walls[_maze.Walls.IndexOf(_selectedWall)] = _selectedWall = new(
                Normalize(Clamp(ToPoint(_selectedWall.Start) + new Vector(dx, dy))),
                Normalize(Clamp(ToPoint(_selectedWall.End) + new Vector(dx, dy))));
        }
        else if (_dragKind is DragKind.WallStart or DragKind.WallEnd && _selectedWall is not null)
        {
            MazePoint endpoint = Normalize(p);
            int index = _maze.Walls.IndexOf(_selectedWall);
            _selectedWall = _dragKind == DragKind.WallStart
                ? new MazeWallDefinition(endpoint, _selectedWall.End)
                : new MazeWallDefinition(_selectedWall.Start, endpoint);
            _maze.Walls[index] = _selectedWall;
        }
        else if (_selectedPoint is not null)
        {
            MazePoint moved = Normalize(p);
            if (_dragKind == DragKind.Start) _maze.Start = moved;
            else if (_dragKind == DragKind.Goal) _maze.Goal = moved;
            else if (_dragKind == DragKind.Dip) _maze.Dip = moved;
            else if (_dragKind == DragKind.Hole)
            {
                int index = _maze.Holes.IndexOf(_selectedPoint);
                if (index >= 0) _maze.Holes[index] = moved;
            }
            else if (_dragKind == DragKind.BlackHole)
            {
                int index = _maze.BlackHoles.IndexOf(_selectedPoint);
                if (index >= 0) _maze.BlackHoles[index] = moved;
            }
            _selectedPoint = moved;
        }
        SetDirty();
        Render();
    }

    private void Board_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragKind == DragKind.NewWall)
        {
            Point end = Clamp(e.GetPosition(Board));
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                if (Math.Abs(end.X - _dragStart.X) >= Math.Abs(end.Y - _dragStart.Y)) end.Y = _dragStart.Y;
                else end.X = _dragStart.X;
            }
            if ((end - _dragStart).Length >= 8)
            {
                _maze.Walls.Add(new(Normalize(_dragStart), Normalize(end)));
                SetDirty();
                SelectTool();
            }
        }
        _dragKind = DragKind.None;
        _preview = null;
        Board.ReleaseMouseCapture();
        Render();
    }

    private void Render()
    {
        if (Board.ActualWidth <= 0 || Board.ActualHeight <= 0) return;
        Board.Children.Clear();
        foreach (MazeWallDefinition wall in _maze.Walls)
        {
            Line line = MakeWall(ToPoint(wall.Start), ToPoint(wall.End), wall == _selectedWall);
            line.Tag = new EditorTag(DragKind.Wall, wall, null);
            line.Cursor = Cursors.SizeAll;
            Board.Children.Add(line);
        }
        if (_selectedWall is not null)
        {
            AddWallHandle(_selectedWall.Start, DragKind.WallStart);
            AddWallHandle(_selectedWall.End, DragKind.WallEnd);
        }
        AddMarker(_maze.Start, DragKind.Start, Brushes.DodgerBlue, "S", 18);
        AddMarker(_maze.Goal, DragKind.Goal, Brushes.Gold, "G", 24);
        foreach (MazePoint hole in _maze.Holes) AddMarker(hole, DragKind.Hole, Brushes.Black, "", 20);
        foreach (MazePoint blackHole in _maze.BlackHoles) AddMarker(blackHole, DragKind.BlackHole, Brushes.Black, "BH", 42);
        if (_maze.Dip is not null) AddMarker(_maze.Dip, DragKind.Dip, Brushes.SeaGreen, "D", 35);
    }

    private Line MakeWall(Point a, Point b, bool selected) => new()
    {
        X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = selected ? Brushes.Gold : new SolidColorBrush(Color.FromRgb(135, 95, 50)),
        StrokeThickness = selected ? 13 : 11, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
    };

    private void AddWallHandle(MazePoint endpoint, DragKind kind)
    {
        const double size = 14;
        Point p = ToPoint(endpoint);
        var handle = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = Brushes.White,
            Stroke = Brushes.Goldenrod,
            StrokeThickness = 3,
            Cursor = Cursors.Cross,
            Tag = new EditorTag(kind, _selectedWall, null)
        };
        Canvas.SetLeft(handle, p.X - size / 2);
        Canvas.SetTop(handle, p.Y - size / 2);
        Panel.SetZIndex(handle, 4);
        Board.Children.Add(handle);
    }

    private void AddMarker(MazePoint point, DragKind kind, Brush fill, string text, double radius)
    {
        Point p = ToPoint(point);
        var grid = new Grid { Width = radius * 2, Height = radius * 2, Tag = new EditorTag(kind, null, point) };
        grid.Children.Add(new Ellipse { Fill = fill, Stroke = point == _selectedPoint ? Brushes.White : Brushes.Transparent, StrokeThickness = 3 });
        grid.Children.Add(new TextBlock { Text = text, Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        Canvas.SetLeft(grid, p.X - radius); Canvas.SetTop(grid, p.Y - radius); Panel.SetZIndex(grid, 2);
        Board.Children.Add(grid);
    }

    private static EditorTag? FindEditorTag(DependencyObject? visual)
    {
        while (visual is not null)
        {
            if (visual is FrameworkElement { Tag: EditorTag tag })
                return tag;

            visual = VisualTreeHelper.GetParent(visual);
        }

        return null;
    }

    private void DeleteSelection()
    {
        if (_selectedWall is not null) _maze.Walls.Remove(_selectedWall);
        else if (_selectedPoint is not null && _selectedKind == DragKind.Hole) _maze.Holes.Remove(_selectedPoint);
        else if (_selectedPoint is not null && _selectedKind == DragKind.BlackHole) _maze.BlackHoles.Remove(_selectedPoint);
        else if (_selectedPoint is not null && _selectedKind == DragKind.Dip) _maze.Dip = null;
        else return;
        _selectedWall = null; _selectedPoint = null; _selectedKind = DragKind.None; SetDirty(); Render();
    }

    private void SelectTool()
    {
        _tool = Tool.Select;
        SelectToolButton.IsChecked = true;
        StatusText.Text = "Select / Move";
    }

    private void Selection_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = _selectedWall is not null ||
            (_selectedPoint is not null && _selectedKind is DragKind.Hole or DragKind.BlackHole or DragKind.Dip);

    private void Paste_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _clipboardItem is not null;

    private void Copy_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_selectedWall is not null)
            _clipboardItem = new ClipboardItem(DragKind.Wall, _selectedWall.Start, _selectedWall.End);
        else if (_selectedPoint is not null)
            _clipboardItem = new ClipboardItem(_selectedKind, _selectedPoint, null);
        CommandManager.InvalidateRequerySuggested();
    }

    private void Cut_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        Copy_Executed(sender, e);
        DeleteSelection();
    }

    private void Paste_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_clipboardItem is null) return;
        MazePoint Offset(MazePoint p) => new(Math.Clamp(p.X + 0.02, 0, 1), Math.Clamp(p.Y + 0.02, 0, 1));

        _selectedWall = null;
        _selectedPoint = null;
        _selectedKind = _clipboardItem.Kind;
        if (_clipboardItem.Kind == DragKind.Wall && _clipboardItem.End is not null)
        {
            _selectedWall = new(Offset(_clipboardItem.Start), Offset(_clipboardItem.End));
            _maze.Walls.Add(_selectedWall);
            _clipboardItem = new ClipboardItem(DragKind.Wall, _selectedWall.Start, _selectedWall.End);
        }
        else
        {
            MazePoint point = Offset(_clipboardItem.Start);
            if (_clipboardItem.Kind == DragKind.Hole) _maze.Holes.Add(point);
            else if (_clipboardItem.Kind == DragKind.BlackHole) _maze.BlackHoles.Add(point);
            else if (_clipboardItem.Kind == DragKind.Dip) _maze.Dip = point;
            _selectedPoint = point;
            _clipboardItem = new ClipboardItem(_clipboardItem.Kind, point, null);
        }
        SetDirty();
        SelectTool();
        Render();
    }

    private void Delete_Executed(object sender, ExecutedRoutedEventArgs e) => DeleteSelection();

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        _maze = NewMaze(); _path = null; _dirty = false; ClearSelection(); SelectTool(); Render(); UpdateTitle();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Filter = "Rolling Maze (*.rollingmaze.json)|*.rollingmaze.json|JSON (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        OpenMaze(dialog.FileName);
    }

    private void OpenMaze(string path)
    {
        try
        {
            _maze = MazeFile.Load(path);
            _maze.Name = MazeNameFromPath(path);
            _path = path; _dirty = false; ClearSelection(); SelectTool(); Render(); UpdateTitle();
            AddRecentPath(path);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cannot open maze", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void RecentFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string path }) return;
        if (!File.Exists(path))
        {
            _recentFiles.Remove(path); SaveRecentItems(); RefreshRecentMenus();
            MessageBox.Show(this, "This file no longer exists.", "Recent file", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (ConfirmDiscard()) OpenMaze(path);
    }

    private void RecentFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string folder }) return;
        if (!Directory.Exists(folder))
        {
            _recentFolders.Remove(folder); SaveRecentItems(); RefreshRecentMenus();
            MessageBox.Show(this, "This folder no longer exists.", "Recent folder", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog
        {
            Filter = "Rolling Maze (*.rollingmaze.json)|*.rollingmaze.json|JSON (*.json)|*.json",
            InitialDirectory = folder
        };
        if (dialog.ShowDialog() == true) OpenMaze(dialog.FileName);
    }

    private void Save_Click(object sender, RoutedEventArgs e) { if (_path is null) SaveAs_Click(sender, e); else SaveTo(_path); }

    private void New_Executed(object sender, ExecutedRoutedEventArgs e) => New_Click(sender, e);
    private void Open_Executed(object sender, ExecutedRoutedEventArgs e) => Open_Click(sender, e);
    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => Save_Click(sender, e);
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Rolling Maze (*.rollingmaze.json)|*.rollingmaze.json", DefaultExt = MazeFile.Extension, AddExtension = true, FileName = SafeName(_maze.Name) + MazeFile.Extension };
        if (dialog.ShowDialog() == true) SaveTo(dialog.FileName);
    }

    private void SaveTo(string path)
    {
        try
        {
            _maze.Name = MazeNameFromPath(path);
            MazeFile.Save(path, _maze); _path = path; _dirty = false;
            StatusText.Text = $"Saved {System.IO.Path.GetFileName(path)}"; UpdateTitle();
            AddRecentPath(path);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cannot save maze", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private bool ConfirmDiscard() => !_dirty || new DiscardChangesDialog { Owner = this }.ShowDialog() == true;
    private void ClearSelection() { _selectedWall = null; _selectedPoint = null; _selectedKind = DragKind.None; }
    private void SetDirty() { _dirty = true; StatusText.Text = "Unsaved changes"; UpdateTitle(); }
    private void UpdateTitle() => Title = $"Rolling Maze Designer — {_maze.Name}{(_dirty ? " *" : "")}";
    private void Board_SizeChanged(object sender, SizeChangedEventArgs e) => Render();
    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, Board.ActualWidth), Math.Clamp(p.Y, 0, Board.ActualHeight));
    private MazePoint Normalize(Point p) => new(p.X / Board.ActualWidth, p.Y / Board.ActualHeight);
    private Point ToPoint(MazePoint p) => new(p.X * Board.ActualWidth, p.Y * Board.ActualHeight);
    private static string SafeName(string name) => string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    private static string MazeNameFromPath(string path)
    {
        string fileName = System.IO.Path.GetFileName(path);
        return fileName.EndsWith(MazeFile.Extension, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^MazeFile.Extension.Length]
            : System.IO.Path.GetFileNameWithoutExtension(fileName);
    }

    private void AddRecentPath(string path)
    {
        path = IOPath.GetFullPath(path);
        AddMostRecent(_recentFiles, path);
        string? folder = IOPath.GetDirectoryName(path);
        if (folder is not null) AddMostRecent(_recentFolders, folder);
        SaveRecentItems();
        RefreshRecentMenus();
    }

    private static void AddMostRecent(List<string> items, string value)
    {
        items.RemoveAll(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        items.Insert(0, value);
        if (items.Count > 10) items.RemoveRange(10, items.Count - 10);
    }

    private void LoadRecentItems()
    {
        try
        {
            if (File.Exists(RecentItemsPath))
            {
                RecentItems? items = JsonSerializer.Deserialize<RecentItems>(File.ReadAllText(RecentItemsPath));
                if (items is not null)
                {
                    _recentFiles.AddRange(items.Files.Take(10));
                    _recentFolders.AddRange(items.Folders.Take(10));
                }
            }
        }
        catch { /* A damaged history must not prevent the designer from starting. */ }
        RefreshRecentMenus();
    }

    private void SaveRecentItems()
    {
        try
        {
            Directory.CreateDirectory(IOPath.GetDirectoryName(RecentItemsPath)!);
            File.WriteAllText(RecentItemsPath, JsonSerializer.Serialize(new RecentItems(_recentFiles, _recentFolders)));
        }
        catch { /* Recent history is optional. */ }
    }

    private void RefreshRecentMenus()
    {
        PopulateRecentMenu(RecentFilesMenu, _recentFiles, RecentFile_Click, IOPath.GetFileName);
        PopulateRecentMenu(RecentFoldersMenu, _recentFolders, RecentFolder_Click, path => path);
    }

    private static void PopulateRecentMenu(MenuItem menu, IEnumerable<string> paths, RoutedEventHandler handler, Func<string, string> header)
    {
        menu.Items.Clear();
        foreach (string path in paths)
        {
            var item = new MenuItem { Header = header(path), ToolTip = path, Tag = path };
            item.Click += handler;
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "(Empty)", IsEnabled = false });
    }
    private sealed record EditorTag(DragKind Kind, MazeWallDefinition? Wall, MazePoint? Point);
    private sealed record ClipboardItem(DragKind Kind, MazePoint Start, MazePoint? End);
    private sealed record RecentItems(List<string> Files, List<string> Folders);
}
