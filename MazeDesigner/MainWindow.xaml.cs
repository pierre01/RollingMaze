using Microsoft.Win32;
using RollingMaze.Mazes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MazeDesigner;

public partial class MainWindow : Window
{
    private enum Tool { Select, Wall, Start, Goal, Hole, Dip }
    private enum DragKind { None, NewWall, Wall, Start, Goal, Hole, Dip }

    private MazeDefinition _maze = NewMaze();
    private Tool _tool;
    private DragKind _dragKind;
    private Point _dragStart;
    private MazeWallDefinition? _selectedWall;
    private MazePoint? _selectedPoint;
    private DragKind _selectedKind;
    private Line? _preview;
    private string? _path;
    private bool _dirty;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Render();
        Closing += (_, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
    }

    private static MazeDefinition NewMaze() => new() { Name = "Untitled maze" };

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse(tag, out Tool tool)) _tool = tool;
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
                case Tool.Dip: _maze.Dip = point; break;
            }
            SetDirty();
            Render();
            Board.ReleaseMouseCapture();
            return;
        }

        HitTestResult? hit = VisualTreeHelper.HitTest(Board, p);
        if (hit?.VisualHit is FrameworkElement { Tag: EditorTag tagInfo })
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
            Board.Children.Add(line);
        }
        AddMarker(_maze.Start, DragKind.Start, Brushes.DodgerBlue, "S", 18);
        AddMarker(_maze.Goal, DragKind.Goal, Brushes.Gold, "G", 24);
        foreach (MazePoint hole in _maze.Holes) AddMarker(hole, DragKind.Hole, Brushes.Black, "", 20);
        if (_maze.Dip is not null) AddMarker(_maze.Dip, DragKind.Dip, Brushes.SeaGreen, "D", 35);
    }

    private Line MakeWall(Point a, Point b, bool selected) => new()
    {
        X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = selected ? Brushes.Gold : new SolidColorBrush(Color.FromRgb(135, 95, 50)),
        StrokeThickness = selected ? 13 : 11, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
    };

    private void AddMarker(MazePoint point, DragKind kind, Brush fill, string text, double radius)
    {
        Point p = ToPoint(point);
        var grid = new Grid { Width = radius * 2, Height = radius * 2, Tag = new EditorTag(kind, null, point) };
        grid.Children.Add(new Ellipse { Fill = fill, Stroke = point == _selectedPoint ? Brushes.White : Brushes.Transparent, StrokeThickness = 3 });
        grid.Children.Add(new TextBlock { Text = text, Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        Canvas.SetLeft(grid, p.X - radius); Canvas.SetTop(grid, p.Y - radius); Panel.SetZIndex(grid, 2);
        Board.Children.Add(grid);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWall is not null) _maze.Walls.Remove(_selectedWall);
        else if (_selectedPoint is not null && _selectedKind == DragKind.Hole) _maze.Holes.Remove(_selectedPoint);
        else if (_selectedPoint is not null && _selectedKind == DragKind.Dip) _maze.Dip = null;
        else return;
        _selectedWall = null; _selectedPoint = null; _selectedKind = DragKind.None; SetDirty(); Render();
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        _maze = NewMaze(); _path = null; _dirty = false; NameBox.Text = _maze.Name; Render(); UpdateTitle();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Filter = "Rolling Maze (*.rollingmaze.json)|*.rollingmaze.json|JSON (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        try { _maze = MazeFile.Load(dialog.FileName); _path = dialog.FileName; _dirty = false; NameBox.Text = _maze.Name; Render(); UpdateTitle(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cannot open maze", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Save_Click(object sender, RoutedEventArgs e) { if (_path is null) SaveAs_Click(sender, e); else SaveTo(_path); }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Rolling Maze (*.rollingmaze.json)|*.rollingmaze.json", DefaultExt = MazeFile.Extension, AddExtension = true, FileName = SafeName(_maze.Name) + MazeFile.Extension };
        if (dialog.ShowDialog() == true) SaveTo(dialog.FileName);
    }

    private void SaveTo(string path)
    {
        try { MazeFile.Save(path, _maze); _path = path; _dirty = false; StatusText.Text = $"Saved {System.IO.Path.GetFileName(path)}"; UpdateTitle(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cannot save maze", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_maze.Name == NameBox.Text) return;
        _maze.Name = NameBox.Text; SetDirty();
    }

    private bool ConfirmDiscard() => !_dirty || MessageBox.Show(this, "Discard unsaved changes?", "Rolling Maze Designer", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    private void SetDirty() { _dirty = true; StatusText.Text = "Unsaved changes"; UpdateTitle(); }
    private void UpdateTitle() => Title = $"Rolling Maze Designer — {_maze.Name}{(_dirty ? " *" : "")}";
    private void Board_SizeChanged(object sender, SizeChangedEventArgs e) => Render();
    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, Board.ActualWidth), Math.Clamp(p.Y, 0, Board.ActualHeight));
    private MazePoint Normalize(Point p) => new(p.X / Board.ActualWidth, p.Y / Board.ActualHeight);
    private Point ToPoint(MazePoint p) => new(p.X * Board.ActualWidth, p.Y * Board.ActualHeight);
    private static string SafeName(string name) => string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    private sealed record EditorTag(DragKind Kind, MazeWallDefinition? Wall, MazePoint? Point);
}
