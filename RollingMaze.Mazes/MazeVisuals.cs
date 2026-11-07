namespace RollingMaze.Mazes;

/// <summary>Logical sizes and artwork shared by the game and designer.</summary>
/// <remarks>
/// Dimensions use the fixed board's logical units, independent of screen pixels and
/// display density. Both applications scale these dimensions uniformly with the board.
/// </remarks>
public static class MazeVisuals
{
    /// <summary>Radius of the visible ball and its sphere used for wall collisions.</summary>
    /// <remarks>Surface support uses the smaller patch defined by <see cref="BallSurfaceContact"/>.</remarks>
    public const int BallRadius = 21;

    /// <summary>Radius of the goal opening, excluding its surrounding rim.</summary>
    public const int GoalRadius = 34;

    /// <summary>Radius of a regular hazard opening, excluding its surrounding rim.</summary>
    /// <remarks>The ball's diameter is approximately four-fifths of this opening's diameter.</remarks>
    public const int HazardRadius = 27;

    /// <summary>Radius of the black central disk drawn over the black-hole artwork.</summary>
    public const int BlackHoleRadius = 32;

    /// <summary>Side length of the square viewport used to draw the black-hole artwork.</summary>
    public const int BlackHoleGraphicSize = 156;

    /// <summary>Radius of the black hole's gravitational influence and its faint boundary ring.</summary>
    public const int BlackHoleInfluenceRadius = 240;

    /// <summary>Amount added to the goal opening's radius to size its offset outer rim.</summary>
    public const int GoalRimGrowth = 5;

    /// <summary>Amount added to a hazard opening's radius to draw its thin, centered rim.</summary>
    public const int HazardRimGrowth = 1;

    /// <summary>Read-only SVG path data for the black hole's luminous disk.</summary>
    /// <remarks>
    /// Paths come from Resources/Images/black_hole.svg and retain its 288-by-288
    /// coordinate system. Scale by <see cref="BlackHoleGraphicSize"/> divided by 288
    /// when drawing them on the logical board; the central disk is drawn separately.
    /// </remarks>
    public static IReadOnlyList<string> BlackHolePaths { get; } = Array.AsReadOnly(new[]
    {
        "M14 142 L91.2 145 A52.6 52.6 0 0 1 191.6 122.5 L214 130.3 A71.6 71.6 0 0 0 80 111.5 C76.4 117.1 75.2 124.4 70 129 C66.3 133 61.4 136.1 56 137.2 C42.3 140.7 28 140.6 14 142 Z",
        "M271 142.3 L196.4 143.2 A52.6 52.6 0 0 1 96 166.5 L73.6 158.7 A71.6 71.6 0 0 0 206.5 181 C209.3 173.7 214.7 167.7 220 162 C225.6 158 230.8 151.7 238 151 C248.4 145.8 259.6 144.7 271 142.3 Z",
        "M114 146.2 C142 145 172 143.9 202 142.9 L202 158.8 C176 154.2 146 149.6 114 146.2 Z"
    });
}
