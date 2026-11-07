namespace RollingMaze.Mazes;

/// <summary>Shared logical board geometry, independent of window size and display density.</summary>
public static class MazeLayout
{
    public const int Width = 1000;
    public const int Height = 800;
    public const int WallInset = 24;

    public static (double Scale, double Left, double Top) Fit(double width, double height)
    {
        double scale = Math.Max(0, Math.Min(width / Width, height / Height));
        return (scale, (width - Width * scale) / 2, (height - Height * scale) / 2);
    }
}
