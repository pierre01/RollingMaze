namespace RollingMaze.Mazes;

/// <summary>The small support patch directly below the ball's center of gravity.</summary>
public static class BallSurfaceContact
{
    public const double Radius = 1.5;

    public static bool IsOverOpening(double dx, double dy, double openingRadius, double height)
    {
        // An airborne ball has no surface contact. At rest, the ball loses support
        // when this tiny patch is fully inside the opening, regardless of its silhouette.
        if (height > 0 || openingRadius <= Radius)
            return false;

        double unsupportedRadius = openingRadius - Radius;
        return dx * dx + dy * dy <= unsupportedRadius * unsupportedRadius;
    }
}
