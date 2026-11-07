using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using RollingMaze.Mazes;

namespace RollingMaze;

public partial class MainPage : ContentPage
{
    private readonly IDispatcherTimer _gameTimer;
    private BallProfile? _ball;
    private SKPoint _position;
    private SKPoint _velocity;
    private SKPoint _tilt;
    private DisplayRotation _displayRotation = DisplayRotation.Rotation0;
    private readonly SKSize _boardSize = new(MazeLayout.Width, MazeLayout.Height);
    private float _boardScale;
    private SKPoint _boardOffset;
    private DateTime _lastFrame;
    private DateTime _startedAt;
    private bool _won;
    private bool _dragging;
    private float _ballHeight;
    private float _verticalVelocity;
    private float _landingEffect;
    private readonly List<SKPoint> _hazardHoles = [];
    private readonly List<SKPoint> _blackHoles = [];
    private bool _falling;
    private bool _fallingIntoBlackHole;
    private float _fallProgress;
    private SKPoint _fallingInto;
    private SKPoint _dipCenter;
    private bool _insideDip;
    private float _lastAccelerationMagnitude = 1f;
    private DateTime _lastKnockAt = DateTime.MinValue;
    private MazeDefinition _maze = CreateFirstMaze();
    private bool _usesDesignedObstacles;
    private int _currentLevelIndex;
    private int _currentLevelNumber = LevelOrder.GetLevelNumber(0);
    private bool _initialLevelLoaded;
    private bool _completedMaze;
    private bool _gameOver;
    private int _lives = StartingLives;
    private int _totalScore;

    private const string LevelsFolder = "levels";
    private const int StartingLives = 3;
    private const double LevelTimeLimitSeconds = 120d;

    private const float BallRadius = MazeVisuals.BallRadius;
    private const float WallInset = MazeLayout.WallInset;
    private static readonly SKColor MazeWallColor = new(135, 95, 50);
    private const float GoalRadius = MazeVisuals.GoalRadius;
    private const float HazardRadius = MazeVisuals.HazardRadius;
    private const float BlackHoleRadius = MazeVisuals.BlackHoleRadius;
    private const float BlackHoleAccretionRadius = 66f;
    private const float BlackHoleGraphicSize = MazeVisuals.BlackHoleGraphicSize;
    private const float BlackHoleEventHorizon = 32f;
    private const float BlackHoleInfluenceRadius = MazeVisuals.BlackHoleInfluenceRadius;
    private const float BlackHoleGravity = 2200f;
    private const float DipRadius = 52f;
    private const float DipJumpSpeed = 360f;
    // Amplify both accelerometer axes so short platform movements immediately
    // translate into stronger forward/back and left/right ball acceleration.
    private const float PhoneTiltSensitivity = 2.25f;

    /// <summary>
    /// Global rolling-surface resistance. Values near 0.15 feel very slick,
    /// 1.0 preserves the original floor, and values around 3.0 feel like velvet.
    /// This is separate from each ball material's own drag.
    /// </summary>
    public float SurfaceFriction { get; set; } = 0.25f;

    public MainPage()
    {
        InitializeComponent();
#if DEBUG
        InitializeTiltDiagnostics();
#endif
        _gameTimer = Dispatcher.CreateTimer();
        _gameTimer.Interval = TimeSpan.FromMilliseconds(16);
        _gameTimer.Tick += OnGameTick;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Read the active display immediately. A foldable may already be open
        // when the process starts, so waiting for a display-change event is not enough.
        _displayRotation = DeviceDisplay.Current.MainDisplayInfo.Rotation;
        DeviceDisplay.Current.MainDisplayInfoChanged += OnMainDisplayInfoChanged;

        if (!_initialLevelLoaded)
        {
            _initialLevelLoaded = true;
            try
            {
                await LoadLevelAsync(_currentLevelNumber);
                StatusLabel.Text = $"{_maze.Name}  •  choose a ball";
                UpdateScoreboard(LevelTimeLimitSeconds);
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Could not load level {_currentLevelNumber:000}: {ex.Message}";
            }
        }

        StartAccelerometer();
        _lastFrame = DateTime.UtcNow;
        _gameTimer.Start();
    }

    protected override void OnDisappearing()
    {
        _calibrationCancellation?.Cancel();
        _gameTimer.Stop();
        DeviceDisplay.Current.MainDisplayInfoChanged -= OnMainDisplayInfoChanged;
        if (Accelerometer.Default.IsMonitoring)
        {
            Accelerometer.Default.ReadingChanged -= OnAccelerometerChanged;
            Accelerometer.Default.Stop();
        }
        base.OnDisappearing();
    }

    private void OnMainDisplayInfoChanged(object? sender, DisplayInfoChangedEventArgs e)
    {
        _displayRotation = e.DisplayInfo.Rotation;
    }

    private void StartAccelerometer()
    {
        if (!Accelerometer.Default.IsSupported || Accelerometer.Default.IsMonitoring)
            return;

        try
        {
            Accelerometer.Default.ReadingChanged += OnAccelerometerChanged;
            Accelerometer.Default.Start(SensorSpeed.Game);
        }
        catch (Exception)
        {
            StatusLabel.Text = "Drag the board to tilt";
        }
    }

    private void OnAccelerometerChanged(object? sender, AccelerometerChangedEventArgs e)
    {
        double x = e.Reading.Acceleration.X;
        double y = e.Reading.Acceleration.Y;
        double z = e.Reading.Acceleration.Z;

        // Bounce detection must continue to use the original acceleration magnitude.
        float magnitude = (float)Math.Sqrt((x * x) + (y * y) + (z * z));
        (x, y) = _tiltCalibration.Process(x, y, z);

        // Sensor axes are fixed to the device's natural orientation; they do not
        // follow the active display. Remap them so screen-left/up mean the same
        // thing on cover and inner displays, including when the app starts unfolded.
        (x, y) = _displayRotation switch
        {
            DisplayRotation.Rotation90 => (-y, x),
            DisplayRotation.Rotation180 => (-x, -y),
            DisplayRotation.Rotation270 => (y, -x),
            _ => (x, y)
        };

        // The accelerometer reports the support force, which points opposite the
        // downhill direction. Reverse it so the ball follows the physical tilt.
        // Canvas Y also increases downward, giving Y the sign shown here.
        _tilt = new SKPoint(
            -(float)x * PhoneTiltSensitivity,
            (float)y * PhoneTiltSensitivity);

        // A knock behind the screen creates a brief acceleration spike. Compare
        // consecutive total-force readings so ordinary steady tilting is ignored.
        float impulse = MathF.Abs(magnitude - _lastAccelerationMagnitude);
        _lastAccelerationMagnitude = magnitude;

        DateTime now = DateTime.UtcNow;
        if (!_isCalibrating && impulse >= 0.65f && (now - _lastKnockAt).TotalMilliseconds >= 550)
        {
            _lastKnockAt = now;
            MainThread.BeginInvokeOnMainThread(BounceBall);
        }
    }

    private void SelectBall(BallProfile profile)
    {
        _ball = profile;
        MaterialDot.TextColor = profile.Highlight.ToMauiColor();
        StatusLabel.Text = $"{profile.Name}  •  tilt to roll";
        SelectionOverlay.IsVisible = false;
        ResetGame();
    }

    private void ResetGame()
    {
        _position = ToBoardPoint(_maze.Start);
        _velocity = SKPoint.Empty;
        _ballHeight = 0f;
        _verticalVelocity = 0f;
        _landingEffect = 0f;
        _falling = false;
        _fallingIntoBlackHole = false;
        _fallProgress = 0f;
        _insideDip = false;
        _won = false;
        _completedMaze = false;
        _gameOver = false;
        _startedAt = DateTime.UtcNow;
        _lastFrame = DateTime.UtcNow;
        WinOverlay.IsVisible = false;
        ChangeBallButton.IsVisible = true;
        GenerateObstacles();
        UpdateScoreboard(LevelTimeLimitSeconds);
        GameCanvas.InvalidateSurface();
    }

    /// <summary>Loads a designer-created maze. The built-in first maze remains the default.</summary>
    public void LoadMaze(MazeDefinition maze)
    {
        MazeFile.Validate(maze);
        _maze = maze;
        _usesDesignedObstacles = true;
        ResetGame();
    }

    public async Task LoadMazeFromAppPackageAsync(string fileName)
    {
        await using Stream stream = await FileSystem.Current.OpenAppPackageFileAsync(fileName);
        using var reader = new StreamReader(stream);
        LoadMaze(MazeFile.Parse(await reader.ReadToEndAsync()));
    }

    private Task LoadLevelAsync(int levelNumber) =>
        LoadMazeFromAppPackageAsync($"{LevelsFolder}/level{levelNumber:000}{MazeFile.Extension}");

    private void OnGameTick(object? sender, EventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        float dt = Math.Clamp((float)(now - _lastFrame).TotalSeconds, 0f, 0.033f);
        _lastFrame = now;

        if (_isCalibrating || _ball is null || _won || _boardSize.Width <= 0 || _boardSize.Height <= 0)
            return;

        double remainingSeconds = GetRemainingSeconds(now);
        UpdateScoreboard(remainingSeconds);

        if (_falling)
        {
            UpdateFalling(dt);
            GameCanvas.InvalidateSurface();
            return;
        }

        // Profiles alter how strongly tilt accelerates the ball, how quickly it
        // loses speed, and how much energy remains after touching a wall.
        SKPoint tilt = _tilt;
        _velocity.X += tilt.X * _ball.Acceleration * dt;
        _velocity.Y += tilt.Y * _ball.Acceleration * dt;
        float drag = MathF.Exp(-_ball.Drag * Math.Max(0f, SurfaceFriction) * dt);
        _velocity.X *= drag;
        _velocity.Y *= drag;

        UpdateBounce(dt);
#if DEBUG
        float velocityBeforeMaze = _velocity.Y;
        if (!_ignoreMazePull)
        {
#endif
        ApplyDipPhysics(dt);
        ApplyBlackHolePhysics(dt);
#if DEBUG
        }
        UpdateTiltDiagnostics(now, tilt.Y, dt > 0f ? (_velocity.Y - velocityBeforeMaze) / dt : 0f, dt);
#endif

        if (_falling)
        {
            GameCanvas.InvalidateSurface();
            return;
        }

        float speed = MathF.Sqrt((_velocity.X * _velocity.X) + (_velocity.Y * _velocity.Y));
        if (speed > _ball.MaxSpeed)
        {
            float scale = _ball.MaxSpeed / speed;
            _velocity.X *= scale;
            _velocity.Y *= scale;
        }

        _position.X += _velocity.X * dt;
        _position.Y += _velocity.Y * dt;
        ResolveWallCollisions();
        ResolveMazeCollisions();
        CheckHazardHoles();
        CheckGoal();
        GameCanvas.InvalidateSurface();
    }

    private void UpdateBounce(float dt)
    {
        if (_ballHeight > 0f || _verticalVelocity > 0f)
        {
            _verticalVelocity -= 980f * dt;
            _ballHeight += _verticalVelocity * dt;

            if (_ballHeight <= 0f)
            {
                _ballHeight = 0f;
                _verticalVelocity = 0f;
                _landingEffect = 1f;
            }
        }

        _landingEffect = Math.Max(0f, _landingEffect - (dt * 2.8f));
    }

    private void ApplyDipPhysics(float dt)
    {
        if (_ballHeight > 5f)
        {
            _insideDip = false;
            return;
        }

        float dx = _dipCenter.X - _position.X;
        float dy = _dipCenter.Y - _position.Y;
        float distance = MathF.Sqrt((dx * dx) + (dy * dy));
        bool isInside = distance < DipRadius;

        if (!isInside)
        {
            if (_insideDip)
            {
                float exitSpeed = MathF.Sqrt((_velocity.X * _velocity.X) + (_velocity.Y * _velocity.Y));
                if (exitSpeed >= DipJumpSpeed)
                {
                    // Carrying enough momentum over the far lip launches the ball.
                    _verticalVelocity = Math.Clamp(260f + ((exitSpeed - DipJumpSpeed) * 0.35f), 260f, 430f);
                }
            }

            _insideDip = false;
            return;
        }

        _insideDip = true;
        if (distance > 0.001f)
        {
            // A parabolic bowl is steepest near its rim and flat at its center.
            float slope = distance / DipRadius;
            float inwardAcceleration = 390f * slope;
            _velocity.X += (dx / distance) * inwardAcceleration * dt;
            _velocity.Y += (dy / distance) * inwardAcceleration * dt;
        }

        // The rougher, compressed floor in the dip bleeds energy. A slow ball
        // settles at the center unless device tilt overcomes the bowl's incline.
        float depth = 1f - Math.Clamp(distance / DipRadius, 0f, 1f);
        float dipDrag = MathF.Exp(-(1.4f + (depth * 1.8f)) * dt);
        _velocity.X *= dipDrag;
        _velocity.Y *= dipDrag;
    }

    private void BounceBall()
    {
        if (_ball is null || _won || _falling || _ballHeight > 3f)
            return;

        _verticalVelocity = 440f;
        _landingEffect = 0f;
        GameCanvas.InvalidateSurface();
    }

    private void ApplyBlackHolePhysics(float dt)
    {
        if (_ball is null)
            return;

        foreach (SKPoint center in _blackHoles)
        {
            float dx = center.X - _position.X;
            float dy = center.Y - _position.Y;
            float distanceSquared = (dx * dx) + (dy * dy);
            float distance = MathF.Sqrt(distanceSquared);

            if (distance <= BlackHoleEventHorizon)
            {
                BeginFalling(center, true);
                return;
            }

            if (distance >= BlackHoleInfluenceRadius || distance <= 0.001f)
                continue;

            float proximity = 1f - ((distance - BlackHoleEventHorizon) /
                (BlackHoleInfluenceRadius - BlackHoleEventHorizon));
            float acceleration = BlackHoleGravity * proximity * proximity * _ball.BlackHoleSusceptibility;
            _velocity.X += (dx / distance) * acceleration * dt;
            _velocity.Y += (dy / distance) * acceleration * dt;
        }
    }

    private void ResolveWallCollisions()
    {
        float minX = WallInset + BallRadius;
        float maxX = _boardSize.Width - WallInset - BallRadius;
        float minY = WallInset + BallRadius;
        float maxY = _boardSize.Height - WallInset - BallRadius;

        if (_position.X < minX) { _position.X = minX; _velocity.X = MathF.Abs(_velocity.X) * _ball!.Restitution; }
        if (_position.X > maxX) { _position.X = maxX; _velocity.X = -MathF.Abs(_velocity.X) * _ball!.Restitution; }
        if (_position.Y < minY) { _position.Y = minY; _velocity.Y = MathF.Abs(_velocity.Y) * _ball!.Restitution; }
        if (_position.Y > maxY) { _position.Y = maxY; _velocity.Y = -MathF.Abs(_velocity.Y) * _ball!.Restitution; }
    }

    private void ResolveMazeCollisions()
    {
        // A sufficiently high bounce clears the low maze dividers.
        if (_ballHeight > 12f)
            return;

        const float dividerRadius = 8f;
        float collisionRadius = BallRadius + dividerRadius;

        // Resolve twice so corners where a divider meets the outer wall remain stable.
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (MazeWall wall in GetMazeWalls())
            {
                SKPoint segment = new(wall.End.X - wall.Start.X, wall.End.Y - wall.Start.Y);
                float lengthSquared = (segment.X * segment.X) + (segment.Y * segment.Y);
                float t = lengthSquared <= 0.001f ? 0f : Math.Clamp(
                    (((_position.X - wall.Start.X) * segment.X) + ((_position.Y - wall.Start.Y) * segment.Y)) / lengthSquared, 0f, 1f);
                float closestX = wall.Start.X + (segment.X * t);
                float closestY = wall.Start.Y + (segment.Y * t);
                float dx = _position.X - closestX;
                float dy = _position.Y - closestY;
                float distanceSquared = (dx * dx) + (dy * dy);
                if (distanceSquared >= collisionRadius * collisionRadius)
                    continue;

                float distance = MathF.Sqrt(distanceSquared);
                float normalX;
                float normalY;
                if (distance > 0.001f)
                {
                    normalX = dx / distance;
                    normalY = dy / distance;
                }
                else
                {
                    float segmentLength = MathF.Sqrt(lengthSquared);
                    normalX = segmentLength > 0.001f ? -segment.Y / segmentLength : 0f;
                    normalY = segmentLength > 0.001f ? segment.X / segmentLength : 1f;
                    if ((_velocity.X * normalX) + (_velocity.Y * normalY) > 0f) { normalX = -normalX; normalY = -normalY; }
                    distance = 0f;
                }

                float correction = collisionRadius - distance;
                _position.X += normalX * correction;
                _position.Y += normalY * correction;

                float velocityIntoWall = (_velocity.X * normalX) + (_velocity.Y * normalY);
                if (velocityIntoWall < 0f)
                {
                    float bounce = (1f + _ball!.Restitution) * velocityIntoWall;
                    _velocity.X -= bounce * normalX;
                    _velocity.Y -= bounce * normalY;
                }
            }
        }
    }

    private void CheckGoal()
    {
        SKPoint goal = GetGoalCenter();
        float dx = _position.X - goal.X;
        float dy = _position.Y - goal.Y;
        if (!BallSurfaceContact.IsOverOpening(dx, dy, GoalRadius, _ballHeight))
            return;

        _won = true;
        _completedMaze = true;
        _velocity = SKPoint.Empty;
        double seconds = (DateTime.UtcNow - _startedAt).TotalSeconds;
        int remainingSeconds = (int)Math.Ceiling(GetRemainingSeconds(DateTime.UtcNow));
        int score = remainingSeconds * _ball!.ScoreValue * 100;
        _totalScore += score;
        int previousBest = GetBestScore();
        int bestScore = Math.Max(previousBest, score);
        if (bestScore != previousBest)
            Preferences.Default.Set(GetBestScoreKey(), bestScore);
        UpdateScoreboard(remainingSeconds);
        ResultTitleLabel.Text = "GOAL!";
        ResultTitleLabel.TextColor = Color.FromArgb("#F2CF55");
        WinDetailLabel.Text = $"{_ball.Name} {_ball.ScoreValue}× • {seconds:0.0}s • +{score:N0}\nTotal: {_totalScore:N0}  •  Best: {bestScore:N0}";
        ContinueButton.Text = "NEXT LEVEL";
        WinOverlay.IsVisible = true;
    }

    private void CheckHazardHoles()
    {
        if (_falling)
            return;

        foreach (SKPoint hole in _hazardHoles)
        {
            float dx = _position.X - hole.X;
            float dy = _position.Y - hole.Y;
            if (BallSurfaceContact.IsOverOpening(dx, dy, HazardRadius, _ballHeight))
            {
                BeginFalling(hole, false);
                return;
            }
        }
    }

    private void BeginFalling(SKPoint center, bool isBlackHole)
    {
        _falling = true;
        _fallingIntoBlackHole = isBlackHole;
        _fallProgress = 0f;
        _fallingInto = center;
        _velocity = SKPoint.Empty;
    }

    private void UpdateFalling(float dt)
    {
        _fallProgress = Math.Min(1f, _fallProgress + (dt / 0.65f));
        float pull = Math.Min(1f, dt * 9f);
        _position.X += (_fallingInto.X - _position.X) * pull;
        _position.Y += (_fallingInto.Y - _position.Y) * pull;

        if (_fallProgress < 1f)
            return;

        _falling = false;
        string reason = _fallingIntoBlackHole
            ? "Your ball crossed the event horizon."
            : "Your ball fell into a hole.";
        CompleteFailure(reason);
    }

    private double GetRemainingSeconds(DateTime now) =>
        Math.Max(0d, LevelTimeLimitSeconds - (now - _startedAt).TotalSeconds);

    private string GetBestScoreKey() => $"level_{_currentLevelNumber:000}_best_score";

    private int GetBestScore() => Preferences.Default.Get(GetBestScoreKey(), 0);

    private void UpdateScoreboard(double remainingSeconds)
    {
        TimerLabel.Text = $"TIME {Math.Max(0, (int)Math.Ceiling(remainingSeconds))}";
        LivesLabel.Text = $"LIVES {new string('♥', _lives)}{new string('♡', StartingLives - _lives)}";
        ScoreLabel.Text = $"SCORE {_totalScore:N0}";
        BestScoreLabel.Text = $"BEST {GetBestScore():N0}";
    }

    private void CompleteFailure(string reason)
    {
        if (_won)
            return;

        _won = true;
        _completedMaze = false;
        _velocity = SKPoint.Empty;
        _lives = Math.Max(0, _lives - 1);
        _gameOver = _lives == 0;
        UpdateScoreboard(GetRemainingSeconds(DateTime.UtcNow));

        ResultTitleLabel.Text = _gameOver ? "GAME OVER" : "LOST!";
        ResultTitleLabel.TextColor = Color.FromArgb("#E76D5B");
        WinDetailLabel.Text = _gameOver
            ? $"{reason}\nFinal score: {_totalScore:N0}\nNo lives left — return to level 001."
            : $"{reason}\n{_lives} {( _lives == 1 ? "life" : "lives")} remaining.";
        ContinueButton.Text = _gameOver ? "START OVER" : "TRY AGAIN";
        ChangeBallButton.IsVisible = !_gameOver;
        WinOverlay.IsVisible = true;
    }

    private SKPoint GetGoalCenter() => ToBoardPoint(_maze.Goal);

    private IReadOnlyList<MazeWall> GetMazeWalls()
    {
        return _maze.Walls.Select(w => new MazeWall(ToBoardPoint(w.Start), ToBoardPoint(w.End))).ToArray();
    }

    private void GenerateObstacles()
    {
        _hazardHoles.Clear();
        _blackHoles.Clear();
        if (_boardSize.Width <= 0 || _boardSize.Height <= 0)
            return;

        if (_usesDesignedObstacles)
        {
            _hazardHoles.AddRange(_maze.Holes.Select(ToBoardPoint));
            _blackHoles.AddRange(_maze.BlackHoles.Select(ToBoardPoint));
            _dipCenter = _maze.Dip is null ? new SKPoint(-1000, -1000) : ToBoardPoint(_maze.Dip);
            return;
        }

        float left = WallInset + HazardRadius + 10f;
        float right = _boardSize.Width - WallInset - HazardRadius - 10f;
        float top = WallInset;
        float height = _boardSize.Height - (WallInset * 2f);
        float[] wallLevels = [0.20f, 0.34f, 0.49f, 0.63f, 0.77f, 0.89f];

        // Put the broad dip in the middle corridor where it is easy to recognize
        // and where both approaches leave room to build speed.
        float dipBandCenter = (wallLevels[2] + wallLevels[3]) * 0.5f;
        _dipCenter = new SKPoint(
            left + ((right - left) * 0.52f),
            top + (height * dipBandCenter));

        // Pick three different interior corridor bands. Keeping the top and bottom
        // bands clear protects the goal and starting area.
        // The dip occupies band 3, so holes use other corridors.
        int[] bands = [1, 2, 4, 5];
        Random.Shared.Shuffle(bands);
        for (int i = 0; i < 3; i++)
        {
            int band = bands[i];
            float upper = wallLevels[band - 1];
            float lower = wallLevels[band];
            float centerY = top + (height * ((upper + lower) * 0.5f));
            float safeHalfHeight = Math.Max(0f, ((lower - upper) * height * 0.5f) - HazardRadius - 10f);
            float jitterY = ((float)Random.Shared.NextDouble() * 2f - 1f) * safeHalfHeight;
            SKPoint candidate = default;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                float x = left + ((float)Random.Shared.NextDouble() * Math.Max(1f, right - left));
                candidate = new SKPoint(x, centerY + jitterY);
                float dx = candidate.X - _dipCenter.X;
                float dy = candidate.Y - _dipCenter.Y;
                if ((dx * dx) + (dy * dy) >= MathF.Pow(DipRadius + HazardRadius + 24f, 2f))
                    break;
            }
            _hazardHoles.Add(candidate);
        }
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        var viewport = MazeLayout.Fit(e.Info.Width, e.Info.Height);
        _boardScale = (float)viewport.Scale;
        _boardOffset = new SKPoint((float)viewport.Left, (float)viewport.Top);
        canvas.Clear(new SKColor(16, 23, 19));
        if (_boardScale <= 0)
        {
            GoalFlag.IsVisible = false;
            return;
        }
        UpdateGoalFlag(e.Info.Width, e.Info.Height);
        canvas.Save();
        canvas.Translate(_boardOffset.X, _boardOffset.Y);
        canvas.Scale(_boardScale);
        canvas.ClipRect(new SKRect(0, 0, MazeLayout.Width, MazeLayout.Height));
        using var background = new SKPaint { Color = new SKColor(25, 49, 36) };
        canvas.DrawRect(0, 0, MazeLayout.Width, MazeLayout.Height, background);

        using var floor = new SKPaint { Color = new SKColor(38, 74, 52), IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(WallInset, WallInset, MazeLayout.Width - WallInset, MazeLayout.Height - WallInset), 24, 24, floor);

        using var grain = new SKPaint { Color = new SKColor(255, 255, 255, 10), StrokeWidth = 2 };
        for (float y = WallInset + 24; y < MazeLayout.Height - WallInset; y += 34)
            canvas.DrawLine(WallInset + 18, y, MazeLayout.Width - WallInset - 18, y, grain);

        DrawGoal(canvas);
        DrawDip(canvas);
        DrawBlackHoles(canvas);
        DrawHazardHoles(canvas);
        DrawMazeWalls(canvas);
        DrawWalls(canvas, MazeLayout.Width, MazeLayout.Height);
        if (_ball is not null)
            DrawBall(canvas, _ball);
        canvas.Restore();
    }

    private static void DrawWalls(SKCanvas canvas, int width, int height)
    {
        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 80), Style = SKPaintStyle.Stroke, StrokeWidth = 22, IsAntialias = true };
        using var wall = new SKPaint { Color = MazeWallColor, Style = SKPaintStyle.Stroke, StrokeWidth = 16, IsAntialias = true };
        var rect = new SKRect(WallInset, WallInset, width - WallInset, height - WallInset);
        canvas.DrawRoundRect(rect, 24, 24, shadow);
        canvas.DrawRoundRect(rect, 24, 24, wall);
    }

    private void DrawMazeWalls(SKCanvas canvas)
    {
        using var shadow = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 95),
            StrokeWidth = 20,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };
        using var divider = new SKPaint
        {
            Color = MazeWallColor,
            StrokeWidth = 16,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };
        using var edge = new SKPaint
        {
            Color = new SKColor(207, 157, 82),
            StrokeWidth = 3,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true
        };

        foreach (MazeWall wall in GetMazeWalls())
        {
            canvas.DrawLine(wall.Start.X + 2, wall.Start.Y + 4, wall.End.X + 2, wall.End.Y + 4, shadow);
            canvas.DrawLine(wall.Start, wall.End, divider);
            canvas.DrawLine(wall.Start.X, wall.Start.Y - 4, wall.End.X, wall.End.Y - 4, edge);
        }
    }

    private void DrawGoal(SKCanvas canvas)
    {
        SKPoint goal = GetGoalCenter();
        using var rim = new SKPaint { Color = new SKColor(10, 18, 13), IsAntialias = true };
        using var hole = new SKPaint { Color = new SKColor(1, 5, 3), IsAntialias = true };
        canvas.DrawCircle(goal.X + 3, goal.Y + 5, GoalRadius + MazeVisuals.GoalRimGrowth, rim);
        canvas.DrawCircle(goal, GoalRadius, hole);
    }

    private void UpdateGoalFlag(int pixelWidth, int pixelHeight)
    {
        // The native image uses device-independent units; Skia paints in surface pixels.
        // Apply the same centered board transform, then convert to the layout's units.
        SKPoint goal = GetGoalCenter();
        double unitsX = GameCanvas.Width / pixelWidth;
        double unitsY = GameCanvas.Height / pixelHeight;
        double size = GoalRadius * 1.6 * _boardScale;
        var bounds = new Rect(
            (_boardOffset.X + goal.X * _boardScale - size / 2) * unitsX,
            (_boardOffset.Y + goal.Y * _boardScale - size / 2) * unitsY,
            size * unitsX, size * unitsY);
        if (AbsoluteLayout.GetLayoutBounds(GoalFlag) != bounds)
            AbsoluteLayout.SetLayoutBounds(GoalFlag, bounds);
        GoalFlag.IsVisible = true;
    }

    private void DrawHazardHoles(SKCanvas canvas)
    {
        using var rim = new SKPaint { Color = new SKColor(54, 65, 50), IsAntialias = true };
        using var hole = new SKPaint { Color = new SKColor(1, 4, 3), IsAntialias = true };

        foreach (SKPoint center in _hazardHoles)
        {
            canvas.DrawCircle(center, HazardRadius + MazeVisuals.HazardRimGrowth, rim);
            canvas.DrawCircle(center, HazardRadius, hole);
        }
    }

    private void DrawBlackHoles(SKCanvas canvas)
    {
        using var influence = new SKPaint
        {
            Color = new SKColor(105, 78, 160, 28),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = true
        };
        using var diskGlow = new SKPaint
        {
            Color = new SKColor(177, 104, 225, 150),
            Style = SKPaintStyle.Fill,
            ImageFilter = SKImageFilter.CreateBlur(5f, 5f),
            IsAntialias = true
        };
        using var diskLight = new SKPaint
        {
            Color = new SKColor(232, 208, 255, 230),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var horizon = new SKPaint { Color = new SKColor(0, 0, 0), IsAntialias = true };

        foreach (SKPoint center in _blackHoles)
        {
            canvas.DrawCircle(center, BlackHoleInfluenceRadius, influence);

            // black_hole.svg has a 288-square viewBox. Scale its paths once to the
            // exact gameplay footprint instead of depending on platform image density.
            float scale = BlackHoleGraphicSize / 288f;
            canvas.Save();
            canvas.Translate(center.X - (BlackHoleGraphicSize / 2f), center.Y - (BlackHoleGraphicSize / 2f));
            canvas.Scale(scale);
            foreach (SKPath path in BlackHoleGraphicPaths)
                canvas.DrawPath(path, diskGlow);
            foreach (SKPath path in BlackHoleGraphicPaths)
                canvas.DrawPath(path, diskLight);
            canvas.Restore();

            canvas.DrawCircle(center, BlackHoleRadius, horizon);
        }
    }

    private static readonly SKPath[] BlackHoleGraphicPaths =
        MazeVisuals.BlackHolePaths.Select(SKPath.ParseSvgPathData).ToArray();

    private void DrawDip(SKCanvas canvas)
    {
        using var outerSlope = new SKPaint { Color = new SKColor(58, 91, 67), IsAntialias = true };
        using var middleSlope = new SKPaint { Color = new SKColor(29, 59, 41), IsAntialias = true };
        using var bottom = new SKPaint { Color = new SKColor(18, 39, 27), IsAntialias = true };
        using var farHighlight = new SKPaint
        {
            Color = new SKColor(151, 184, 158, 80),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3f,
            IsAntialias = true
        };

        canvas.DrawCircle(_dipCenter, DipRadius + 5f, outerSlope);
        canvas.DrawCircle(_dipCenter.X + 2f, _dipCenter.Y + 4f, DipRadius - 8f, middleSlope);
        canvas.DrawCircle(_dipCenter.X + 4f, _dipCenter.Y + 7f, DipRadius * 0.48f, bottom);
        canvas.DrawArc(
            new SKRect(_dipCenter.X - DipRadius, _dipCenter.Y - DipRadius,
                _dipCenter.X + DipRadius, _dipCenter.Y + DipRadius),
            195f, 120f, false, farHighlight);
    }

    private void DrawBall(SKCanvas canvas, BallProfile profile)
    {
        float heightRatio = Math.Clamp(_ballHeight / 100f, 0f, 1f);
        float drawnY = _position.Y - (_ballHeight * 0.16f);
        float fallScale = _falling ? Math.Max(0.08f, 1f - (_fallProgress * 0.92f)) : 1f;
        float ballScale = (1f + (heightRatio * 0.13f)) * fallScale;
        float squashX = 1f + (_landingEffect * 0.22f);
        float squashY = 1f - (_landingEffect * 0.16f);

        using var shadow = new SKPaint
        {
            Color = new SKColor(0, 0, 0, (byte)(90 - (heightRatio * 55))),
            IsAntialias = true
        };
        canvas.Save();
        canvas.Scale(1f + (heightRatio * 0.45f), 1f - (heightRatio * 0.28f), _position.X, _position.Y + 8);
        canvas.DrawCircle(_position.X + 5, _position.Y + 8, BallRadius, shadow);
        canvas.Restore();

        if (_landingEffect > 0f)
        {
            using var ring = new SKPaint
            {
                Color = new SKColor(242, 207, 85, (byte)(150 * _landingEffect)),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3f,
                IsAntialias = true
            };
            canvas.DrawCircle(_position.X, _position.Y, BallRadius + ((1f - _landingEffect) * 36f), ring);
        }

        using var ballPaint = new SKPaint { IsAntialias = true };
        ballPaint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(_position.X - 8, drawnY - 10), BallRadius * 1.5f,
            [profile.Highlight, profile.BaseColor, profile.Shadow],
            [0f, 0.58f, 1f], SKShaderTileMode.Clamp);
        canvas.Save();
        canvas.Scale(ballScale * squashX, ballScale * squashY, _position.X, drawnY);
        canvas.DrawCircle(_position.X, drawnY, BallRadius, ballPaint);

        using var shine = new SKPaint { Color = new SKColor(255, 255, 255, profile.Name == "Wood" ? (byte)55 : (byte)135), IsAntialias = true };
        canvas.DrawCircle(_position.X - 8, drawnY - 9, 5, shine);
        canvas.Restore();
    }

    private void OnCanvasTouch(object? sender, SKTouchEventArgs e)
    {
        if (_boardScale <= 0) return;
        var location = new SKPoint(
            (e.Location.X - _boardOffset.X) / _boardScale,
            (e.Location.Y - _boardOffset.Y) / _boardScale);
        if (e.ActionType == SKTouchAction.Pressed)
            _dragging = location.X >= 0 && location.X <= _boardSize.Width &&
                        location.Y >= 0 && location.Y <= _boardSize.Height;

        if (e.ActionType == SKTouchAction.Released || e.ActionType == SKTouchAction.Cancelled)
        {
            _dragging = false;
            _tilt = SKPoint.Empty;
        }
        else if (_dragging && _boardSize.Width > 0 && _boardSize.Height > 0)
        {
            _tilt = new SKPoint(
                Math.Clamp((location.X - (_boardSize.Width / 2)) / (_boardSize.Width / 2), -1f, 1f),
                Math.Clamp((location.Y - (_boardSize.Height / 2)) / (_boardSize.Height / 2), -1f, 1f));
        }
        e.Handled = true;
    }

    private void OnWoodClicked(object? sender, EventArgs e) => SelectBall(BallProfile.Wood);
    private void OnSilverClicked(object? sender, EventArgs e) => SelectBall(BallProfile.Silver);
    private void OnGoldClicked(object? sender, EventArgs e) => SelectBall(BallProfile.Gold);
    private async void OnPlayAgainClicked(object? sender, EventArgs e)
    {
        if (_gameOver)
        {
            _lives = StartingLives;
            _totalScore = 0;
            _currentLevelIndex = 0;
            _currentLevelNumber = LevelOrder.GetLevelNumber(_currentLevelIndex);
            await LoadLevelAsync(_currentLevelNumber);
            StatusLabel.Text = $"Level {_currentLevelNumber:000}: {_maze.Name}  •  tilt to roll";
            return;
        }

        if (_completedMaze)
        {
            int nextLevelNumber = LevelOrder.GetLevelNumber(_currentLevelIndex + 1);
            try
            {
                await LoadLevelAsync(nextLevelNumber);
                _currentLevelIndex++;
                _currentLevelNumber = nextLevelNumber;
                UpdateScoreboard(LevelTimeLimitSeconds);
                StatusLabel.Text = $"Level {_currentLevelNumber:000}: {_maze.Name}  •  tilt to roll";
                return;
            }
            catch (FileNotFoundException)
            {
                StatusLabel.Text = "All levels complete!";
                ContinueButton.Text = "PLAY AGAIN";
                _totalScore = 0;
                _currentLevelIndex = 0;
                _currentLevelNumber = LevelOrder.GetLevelNumber(_currentLevelIndex);
                await LoadLevelAsync(_currentLevelNumber);
                return;
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Could not load level {nextLevelNumber:000}: {ex.Message}";
            }
        }
        ResetGame();
    }

    private void OnChangeBallClicked(object? sender, EventArgs e)
    {
        WinOverlay.IsVisible = false;
        SelectionOverlay.IsVisible = true;
        StatusLabel.Text = "Choose a ball";
    }

    private SKPoint ToBoardPoint(MazePoint point)
    {
        float width = Math.Max(1f, _boardSize.Width - (WallInset * 2f));
        float height = Math.Max(1f, _boardSize.Height - (WallInset * 2f));
        return new SKPoint(WallInset + ((float)point.X * width), WallInset + ((float)point.Y * height));
    }

    private static MazeDefinition CreateFirstMaze() => new()
    {
        Name = "Classic",
        Start = new MazePoint(0.06, 0.94),
        Goal = new MazePoint(0.94, 0.06),
        Walls =
        [
            new(new(0.18, 0.20), new(1.00, 0.20)),
            new(new(0.00, 0.34), new(0.80, 0.34)),
            new(new(0.18, 0.49), new(1.00, 0.49)),
            new(new(0.00, 0.63), new(0.82, 0.63)),
            new(new(0.18, 0.77), new(1.00, 0.77)),
            new(new(0.00, 0.89), new(0.82, 0.89))
        ]
    };
}

internal sealed record BallProfile(
    string Name, int ScoreValue, float Acceleration, float Drag, float Restitution, float MaxSpeed, float BlackHoleSusceptibility,
    SKColor Highlight, SKColor BaseColor, SKColor Shadow)
{
    // Higher acceleration makes short, quick tilts register immediately. The
    // increased drag sheds old momentum sooner while retaining the material
    // progression: wood grips most, silver is balanced, and gold carries furthest.
    public static BallProfile Wood { get; } = new("Wood", 1, 1080f, 7.0f, 0.28f, 650f, 1.25f,
        new SKColor(225, 169, 91), new SKColor(145, 82, 40), new SKColor(70, 36, 21));

    public static BallProfile Silver { get; } = new("Silver", 2, 1280f, 4.0f, 0.52f, 850f, 0.75f,
        new SKColor(250, 253, 255), new SKColor(150, 164, 170), new SKColor(56, 65, 70));

    public static BallProfile Gold { get; } = new("Gold", 3, 980f, 2.2f, 0.38f, 950f, 0.42f,
        new SKColor(255, 240, 142), new SKColor(211, 158, 34), new SKColor(100, 64, 8));
}

internal readonly record struct MazeWall(SKPoint Start, SKPoint End);

internal static class ColorExtensions
{
    public static Color ToMauiColor(this SKColor color) =>
        Color.FromRgba(color.Red, color.Green, color.Blue, color.Alpha);
}
