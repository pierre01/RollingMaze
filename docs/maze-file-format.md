# Rolling Maze file format (version 1)

Maze files use UTF-8 JSON and the extension `.rollingmaze.json`. Coordinates are normalized to the playable board: `(0, 0)` is its top-left and `(1, 1)` is its bottom-right. This keeps a maze independent of screen size.

```json
{
  "formatVersion": 1,
  "name": "Example",
  "start": { "x": 0.08, "y": 0.92 },
  "goal": { "x": 0.92, "y": 0.08 },
  "walls": [
    {
      "start": { "x": 0.10, "y": 0.70 },
      "end": { "x": 0.80, "y": 0.70 }
    }
  ],
  "holes": [ { "x": 0.50, "y": 0.50 } ],
  "dip": { "x": 0.30, "y": 0.30 }
}
```

## Fields

| Field | Required | Meaning |
|---|---:|---|
| `formatVersion` | yes | Must be `1`; permits future evolution. |
| `name` | yes | Non-empty display name. |
| `start` | yes | Ball starting center. |
| `goal` | yes | Goal-hole center. |
| `walls` | yes | Straight wall segments. Segments may use any angle. |
| `holes` | yes | Hazard-hole centers; use `[]` for none. |
| `dip` | no | Dip center; use `null` or omit it for none. |

Every coordinate must be a finite number from `0` through `1`. Walls must have two different endpoints. The shared `RollingMaze.Mazes` library is the authoritative reader, writer, and validator used by both applications.

The first maze remains defined in `MainPage.CreateFirstMaze()`. Later files can be bundled as MAUI raw assets and loaded with `LoadMazeFromAppPackageAsync`, or parsed with `MazeFile.Load`/`MazeFile.Parse` when obtained from another source.
