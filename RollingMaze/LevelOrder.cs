namespace RollingMaze;

internal static class LevelOrder
{
    public static int GetLevelNumber(int index)
    {
#if DEBUG
        // Promote the latest maze and skip its old position in the numeric order.
        int first = DebugMaze.FirstLevelNumber;
        if (index == 0)
            return first;

        return index < first ? index : index + 1;
#else
        return index + 1;
#endif
    }
}
