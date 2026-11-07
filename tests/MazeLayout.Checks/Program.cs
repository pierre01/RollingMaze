using RollingMaze.Mazes;

static void Near(double actual, double expected, string message)
{
    if (Math.Abs(actual - expected) > 0.000001)
        throw new Exception($"{message}: expected {expected}, got {actual}.");
}

// Portrait phone, landscape tablet, exact fit, and high-density display.
foreach (var (width, height) in new[] { (400d, 900d), (1600d, 700d), (1000d, 800d), (3000d, 2400d) })
{
    var (scale, left, top) = MazeLayout.Fit(width, height);
    double boardWidth = MazeLayout.Width * scale;
    double boardHeight = MazeLayout.Height * scale;
    Near(boardWidth / boardHeight, 1.25, "Board stays 5:4");
    Near(left * 2 + boardWidth, width, "Board is horizontally centered");
    Near(top * 2 + boardHeight, height, "Board is vertically centered");
    if (left < 0 || top < 0 || (left > 0.000001 && top > 0.000001))
        throw new Exception("Board must fit completely and fill at least one axis.");

    // The rendered ball and corridor must keep the same proportions.
    Near(MazeVisuals.BallRadius * scale / boardWidth,
        (double)MazeVisuals.BallRadius / MazeLayout.Width, "Ball size stays proportional");
    // Inverse rendering transform maps touch coordinates back into physics space.
    double touchX = left + 730 * scale;
    double touchY = top + 260 * scale;
    Near((touchX - left) / scale, 730, "Touch X maps back to board");
    Near((touchY - top) / scale, 260, "Touch Y maps back to board");
}

Near(MazeLayout.Fit(0, 800).Scale, 0, "Zero-width surface does not render");
Near(MazeLayout.Fit(1000, 0).Scale, 0, "Zero-height surface does not render");

// Surface support depends on the projected center, not the visible sphere's edge.
foreach (double radius in new[] { (double)MazeVisuals.HazardRadius, MazeVisuals.GoalRadius })
{
    void Contact(bool expected, double x, double y, double height, string scenario)
    {
        if (BallSurfaceContact.IsOverOpening(x, y, radius, height) != expected)
            throw new Exception($"Incorrect surface contact: {scenario}.");
    }
    Contact(false, radius + MazeVisuals.BallRadius / 2d, 0, 0, "sphere overlaps rim but center is supported");
    Contact(false, radius - BallSurfaceContact.Radius / 2, 0, 0, "tiny contact patch still touches rim");
    Contact(true, radius - BallSurfaceContact.Radius - 0.1, 0, 0, "projected contact patch is unsupported");
    Contact(true, 0, radius - BallSurfaceContact.Radius - 0.1, 0, "same support rule on vertical axis");
    Contact(false, 0, 0, 0.1, "airborne ball passes over opening");
    Contact(true, 0, 0, 0, "landing over opening loses support");
}
Console.WriteLine("Maze layout checks passed.");
