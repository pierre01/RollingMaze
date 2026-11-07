using MazeDesigner;
using RollingMaze.Mazes;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var maze = new MazeDefinition();
var history = new MazeEditHistory();
history.Reset(maze);
Check(!history.CanUndo && !history.CanRedo && !history.IsDirty(maze), "New document starts clean.");

// One edit adds a wall; multiple intermediate drag updates form just one more edit.
var wall = new MazeWallDefinition(new(.1, .2, 5), new(.6, .2, 10));
history.BeginEdit(maze);
maze.Walls.Add(wall);
history.CommitEdit(maze);
history.BeginEdit(maze);
maze.Walls[0] = wall with { End = new(.7, .2, 10) };
history.BeginEdit(maze);
maze.Walls[0] = wall with { End = new(.8, .2, 10) };
history.CommitEdit(maze);
history.Undo(maze);
Check(maze.Walls.Single() == wall, "Undo collapses a drag and preserves scores.");
history.Undo(maze);
Check(maze.Walls.Count == 0 && !history.CanUndo && !history.IsDirty(maze), "Undo addition returns to saved state.");
history.Redo(maze);
history.Redo(maze);
Check(maze.Walls.Single().End.X == .8 && !history.CanRedo, "Redo restores each completed edit.");

history.MarkSaved(maze);
history.Undo(maze);
Check(history.IsDirty(maze), "Undo past a save marks document dirty.");
history.Redo(maze);
Check(!history.IsDirty(maze), "Redo to the save clears dirty state.");

history.Undo(maze);
history.BeginEdit(maze);
history.CommitEdit(maze);
Check(history.CanRedo, "An unchanged click preserves redo.");
history.BeginEdit(maze);
maze.Holes.Add(new(.3, .4, 25));
history.CommitEdit(maze);
Check(!history.CanRedo, "A new edit after undo discards the redo branch.");

// Captures every marker type and independent collection snapshots for deletion/cut/paste.
var start = maze.Start;
var goal = maze.Goal;
history.BeginEdit(maze);
maze.Start = new(.2, .3, 1);
maze.Goal = new(.7, .8, 2);
maze.Dip = new(.4, .6, 3);
maze.BlackHoles.Add(new(.6, .4, 4));
maze.Walls.Clear();
maze.Holes.Clear();
history.CommitEdit(maze);
maze.Name = "Saved under a new name";
history.Undo(maze);
Check(maze.Start == start && maze.Goal == goal && maze.Dip is null && maze.BlackHoles.Count == 0,
    "Undo restores all marker types.");
Check(maze.Walls.Single() == wall && maze.Holes.Single().Score == 25, "Removed collections and scores are restored.");
Check(maze.Name == "Saved under a new name", "Undo preserves current file metadata.");
history.Redo(maze);
Check(maze.Start.Score == 1 && maze.Goal.Score == 2 && maze.Dip?.Score == 3 && maze.BlackHoles.Single().Score == 4,
    "Redo preserves all marker scores.");
Check(maze.Walls.Count == 0 && maze.Holes.Count == 0, "Redo reapplies deletion.");

history.BeginEdit(maze);
maze.Goal = new(.9, .9);
history.Reset(maze);
history.CommitEdit(maze);
history.Undo(maze);
history.Redo(maze);
Check(!history.CanUndo && !history.CanRedo && !history.IsDirty(maze), "Opening/new resets pending edits and both stacks.");
Console.WriteLine("Maze history checks passed.");
