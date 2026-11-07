# Rolling Maze file format (version 1)

Maze files use UTF-8 JSON and the extension `.rollingmaze.json`. Coordinates are normalized to the playable board: `(0, 0)` is its top-left and `(1, 1)` is its bottom-right. This keeps a maze independent of screen size.

Both the game and designer use the same fixed 1000×800 logical board (5:4 width:height), scaled uniformly to fit the screen. Normalized coordinates span the area inside the outer wall centerlines, inset 24 logical units from each edge. Existing version 1 files need no conversion.

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
  "blackHoles": [ { "x": 0.70, "y": 0.50 } ],
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
| `blackHoles` | no | Black-hole centers. Their gravity affects nearby balls and their event horizon captures balls. Omit or use `[]` for none. |
| `dip` | no | Dip center; use `null` or omit it for none. |

Every coordinate must be a finite number from `0` through `1`. Walls must have two different endpoints. The shared `RollingMaze.Mazes` library is the authoritative reader, writer, and validator used by both applications.

Any point may include an optional integer `score`, for example `{ "x": 0.50, "y": 0.50, "score": 10 }`. It defaults to `0` when omitted and is omitted when saving a zero score. Scores are reserved for future use and do not affect gameplay. Existing version 1 files remain compatible.

Game levels are bundled under `RollingMaze/Resources/Raw/levels` and named sequentially as `level001.rollingmaze.json`, `level002.rollingmaze.json`, and so on. The game loads level 001 at startup and advances to the next numbered file after each completed maze. Files can be created and edited with MazeDesigner.
