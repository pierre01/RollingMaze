namespace RollingMaze.Mazes;

/// <summary>Logical sizes and artwork shared by the game and designer.</summary>
public static class MazeVisuals
{
    public const int BallRadius = 21;
    public const int GoalRadius = 34;
    // The ball is about four-fifths of a regular hole's diameter.
    public const int HazardRadius = 27;
    public const int BlackHoleRadius = 32;
    public const int BlackHoleGraphicSize = 156;
    public const int BlackHoleInfluenceRadius = 240;
    public const int GoalRimGrowth = 5;
    public const int HazardRimGrowth = 1;

    // Resources/Images/black_hole.svg, in its original 288-square viewBox.
    public static IReadOnlyList<string> BlackHolePaths { get; } = Array.AsReadOnly(new[]
    {
        "M14 142 L91.2 145 A52.6 52.6 0 0 1 191.6 122.5 L214 130.3 A71.6 71.6 0 0 0 80 111.5 C76.4 117.1 75.2 124.4 70 129 C66.3 133 61.4 136.1 56 137.2 C42.3 140.7 28 140.6 14 142 Z",
        "M271 142.3 L196.4 143.2 A52.6 52.6 0 0 1 96 166.5 L73.6 158.7 A71.6 71.6 0 0 0 206.5 181 C209.3 173.7 214.7 167.7 220 162 C225.6 158 230.8 151.7 238 151 C248.4 145.8 259.6 144.7 271 142.3 Z",
        "M114 146.2 C142 145 172 143.9 202 142.9 L202 158.8 C176 154.2 146 149.6 114 146.2 Z"
    });
}
