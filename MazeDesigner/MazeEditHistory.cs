using RollingMaze.Mazes;

namespace MazeDesigner;

internal sealed class MazeEditHistory
{
    private readonly Stack<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();
    private Snapshot? _pending;
    private Snapshot? _saved;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsDirty(MazeDefinition maze) => _saved is null || !_saved.Matches(maze);

    public void Reset(MazeDefinition maze)
    {
        _undo.Clear();
        _redo.Clear();
        _pending = null;
        MarkSaved(maze);
    }

    public void MarkSaved(MazeDefinition maze) => _saved = new Snapshot(maze);
    public void BeginEdit(MazeDefinition maze) => _pending ??= new Snapshot(maze);

    public void CommitEdit(MazeDefinition maze)
    {
        if (_pending is null) return;
        if (!_pending.Matches(maze))
        {
            _undo.Push(_pending);
            _redo.Clear();
        }
        _pending = null;
    }

    public void Undo(MazeDefinition maze)
    {
        if (!_undo.TryPop(out Snapshot? snapshot)) return;
        _redo.Push(new Snapshot(maze));
        snapshot.Restore(maze);
    }

    public void Redo(MazeDefinition maze)
    {
        if (!_redo.TryPop(out Snapshot? snapshot)) return;
        _undo.Push(new Snapshot(maze));
        snapshot.Restore(maze);
    }

    // Immutable points and walls only need their collections copied for an isolated snapshot.
    // File names and paths are metadata and stay unchanged when undoing an edit.
    private sealed class Snapshot(MazeDefinition maze)
    {
        private readonly MazePoint _start = maze.Start;
        private readonly MazePoint _goal = maze.Goal;
        private readonly MazePoint? _dip = maze.Dip;
        private readonly MazeWallDefinition[] _walls = maze.Walls.ToArray();
        private readonly MazePoint[] _holes = maze.Holes.ToArray();
        private readonly MazePoint[] _blackHoles = maze.BlackHoles.ToArray();

        public bool Matches(MazeDefinition current) =>
            _start == current.Start && _goal == current.Goal && _dip == current.Dip &&
            _walls.SequenceEqual(current.Walls) && _holes.SequenceEqual(current.Holes) &&
            _blackHoles.SequenceEqual(current.BlackHoles);

        public void Restore(MazeDefinition target)
        {
            target.Start = _start;
            target.Goal = _goal;
            target.Dip = _dip;
            target.Walls = _walls.ToList();
            target.Holes = _holes.ToList();
            target.BlackHoles = _blackHoles.ToList();
        }
    }
}
