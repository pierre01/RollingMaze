using System.Text.Json;
using System.Text.Json.Serialization;

namespace RollingMaze.Mazes;

public sealed class MazeDefinition
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string Name { get; set; } = "Untitled maze";
    public MazePoint Start { get; set; } = new(0.08, 0.92);
    public MazePoint Goal { get; set; } = new(0.92, 0.08);
    public List<MazeWallDefinition> Walls { get; set; } = [];
    public List<MazePoint> Holes { get; set; } = [];
    public MazePoint? Dip { get; set; }
}

public sealed record MazePoint(double X, double Y);

public sealed record MazeWallDefinition(MazePoint Start, MazePoint End);

public static class MazeFile
{
    public const string Extension = ".rollingmaze.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static MazeDefinition Load(string path) => Parse(File.ReadAllText(path));

    public static MazeDefinition Parse(string json)
    {
        MazeDefinition maze = JsonSerializer.Deserialize<MazeDefinition>(json, Options)
            ?? throw new InvalidDataException("The file does not contain a maze.");
        Validate(maze);
        return maze;
    }

    public static void Save(string path, MazeDefinition maze)
    {
        Validate(maze);
        File.WriteAllText(path, JsonSerializer.Serialize(maze, Options));
    }

    public static void Validate(MazeDefinition maze)
    {
        if (maze.FormatVersion != MazeDefinition.CurrentFormatVersion)
            throw new InvalidDataException($"Unsupported maze format version {maze.FormatVersion}.");
        if (string.IsNullOrWhiteSpace(maze.Name))
            throw new InvalidDataException("A maze name is required.");

        ValidatePoint(maze.Start, "start");
        ValidatePoint(maze.Goal, "goal");
        if (maze.Dip is not null) ValidatePoint(maze.Dip, "dip");
        for (int i = 0; i < maze.Holes.Count; i++) ValidatePoint(maze.Holes[i], $"holes[{i}]");
        for (int i = 0; i < maze.Walls.Count; i++)
        {
            MazeWallDefinition wall = maze.Walls[i];
            ValidatePoint(wall.Start, $"walls[{i}].start");
            ValidatePoint(wall.End, $"walls[{i}].end");
            if (wall.Start == wall.End) throw new InvalidDataException($"walls[{i}] has zero length.");
        }
    }

    private static void ValidatePoint(MazePoint point, string name)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
            point.X is < 0 or > 1 || point.Y is < 0 or > 1)
            throw new InvalidDataException($"{name} must use coordinates between 0 and 1.");
    }
}
