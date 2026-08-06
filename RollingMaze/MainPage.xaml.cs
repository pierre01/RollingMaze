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
    private SKSize _boardSize;
    private DateTime _lastFrame;
    private DateTime _startedAt;
    private bool _won;
    private bool _dragging;
    private float _ballHeight;
    private float _verticalVelocity;
    private float _landingEffect;
    private readonly List<SKPoint> _hazardHoles = [];
    private bool _falling;
    private float _fallProgress;
    private SKPoint _fallingInto;
    private SKPoint _dipCenter;
    private bool _insideDip;
    private float _lastAccelerationMagnitude = 1f;
    private DateTime _lastKnockAt = DateTime.MinValue;
    private MazeDefinition _maze = CreateFirstMaze();
    private bool _usesDesignedObstacles;
    private int _nextMazeFileIndex;
    private bool _completedMaze;
    private static readonly string[] MazeFiles = ["maze2.rollingmaze.json"];

    private const float BallRadius = 24f;
    private const float WallInset = 24f;
    private const float GoalRadius = 34f;
    private const float HazardRadius = 27f;
    private const float HazardCaptureRadius = 15f;
    private const float DipRadius = 52f;
    private const float DipJumpSpeed = 360f;

    /// <summary>
    /// Global rolling-surface resistance. Values near 0.15 feel very slick,
    /// 1.0 preserves the original floor, and values around 3.0 feel like velvet.
    /// This is separate from each ball material's own drag.
    /// </summary>
    public float SurfaceFriction { get; set; } = 0.5f;

    public MainPage()
    {
        InitializeComponent();
        _gameTimer = Dispatcher.CreateTimer();
        _gameTimer.Interval = TimeSpan.FromMilliseconds(16);
        _gameTimer.Tick += OnGameTick;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StartAccelerometer();
        _lastFrame = DateTime.UtcNow;
        _gameTimer.Start();
    }

    protected override void OnDisappearing()
    {
        _gameTimer.Stop();
        if (Accelerometer.Default.IsMonitoring)
        {
            Accelerometer.Default.ReadingChanged -= OnAccelerometerChanged;
            Accelerometer.Default.Stop();
        }
        base.OnDisappearing();
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

        // The accelerometer reports the support force, which points opposite the
        // downhill direction. Reverse it so the ball follows the physical tilt.
        // Canvas Y also increases downward, giving Y the sign shown here.
        _tilt = new SKPoint(-(float)x, (float)y);

        // A knock behind the screen creates a brief acceleration spike. Compare
        // consecutive total-force readings so ordinary steady tilting is ignored.
        float magnitude = (float)Math.Sqrt((x * x) + (y * y) + (z * z));
        float impulse = MathF.Abs(magnitude - _lastAccelerationMagnitude);
        _lastAccelerationMagnitude = magnitude;

        DateTime now = DateTime.UtcNow;
        if (impulse >= 0.65f && (now - _lastKnockAt).TotalMilliseconds >= 550)
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
        _fallProgress = 0f;
        _insideDip = false;
        _won = false;
        _completedMaze = false;
        _startedAt = DateTime.UtcNow;
        _lastFrame = DateTime.UtcNow;
        WinOverlay.IsVisible = false;
        GenerateObstacles();
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

    private void OnGameTick(object? sender, EventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        float dt = Math.Clamp((float)(now - _lastFrame).TotalSeconds, 0f, 0.033f);
        _lastFrame = now;

        if (_ball is null || _won || _boardSize.Width <= 0 || _boardSize.Height <= 0)
            return;

        if (_falling)
        {
            UpdateFalling(dt);
            GameCanvas.InvalidateSurface();
            return;
        }

        // Profiles alter how strongly tilt accelerates the ball, how quickly it
        // loses speed, and how much energy remains after touching a wall.
        _velocity.X += _tilt.X * _ball.Acceleration * dt;
        _velocity.Y += _tilt.Y * _ball.Acceleration * dt;
        float drag = MathF.Exp(-_ball.Drag * Math.Max(0f, SurfaceFriction) * dt);
        _velocity.X *= drag;
        _velocity.Y *= drag;

        UpdateBounce(dt);
        ApplyDipPhysics(dt);

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
        // An airborne ball passes over holes; it can only drop into the goal
        // after returning close to the board surface.
        if (_ballHeight > 5f)
            return;

        SKPoint goal = GetGoalCenter();
        float dx = _position.X - goal.X;
        float dy = _position.Y - goal.Y;
        if ((dx * dx) + (dy * dy) > (GoalRadius - 3) * (GoalRadius - 3))
            return;

        _won = true;
        _completedMaze = true;
        _velocity = SKPoint.Empty;
        double seconds = (DateTime.UtcNow - _startedAt).TotalSeconds;
        ResultTitleLabel.Text = "GOAL!";
        ResultTitleLabel.TextColor = Color.FromArgb("#F2CF55");
        WinDetailLabel.Text = $"{_ball!.Name} ball • {seconds:0.0} seconds";
        ContinueButton.Text = _nextMazeFileIndex < MazeFiles.Length ? "NEXT MAZE" : "PLAY AGAIN";
        WinOverlay.IsVisible = true;
    }

    private void CheckHazardHoles()
    {
        if (_falling || _ballHeight > 5f)
            return;

        foreach (SKPoint hole in _hazardHoles)
        {
            float dx = _position.X - hole.X;
            float dy = _position.Y - hole.Y;
            if ((dx * dx) + (dy * dy) <= HazardCaptureRadius * HazardCaptureRadius)
            {
                _falling = true;
                _fallProgress = 0f;
                _fallingInto = hole;
                _velocity = SKPoint.Empty;
                return;
            }
        }
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
        _won = true;
        _completedMaze = false;
        ResultTitleLabel.Text = "LOST!";
        ResultTitleLabel.TextColor = Color.FromArgb("#E76D5B");
        WinDetailLabel.Text = "Your ball fell into a hole.";
        ContinueButton.Text = "TRY AGAIN";
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
        if (_boardSize.Width <= 0 || _boardSize.Height <= 0)
            return;

        if (_usesDesignedObstacles)
        {
            _hazardHoles.AddRange(_maze.Holes.Select(ToBoardPoint));
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
        _boardSize = new SKSize(e.Info.Width, e.Info.Height);
        canvas.Clear(new SKColor(25, 49, 36));

        using var floor = new SKPaint { Color = new SKColor(38, 74, 52), IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(WallInset, WallInset, e.Info.Width - WallInset, e.Info.Height - WallInset), 24, 24, floor);

        using var grain = new SKPaint { Color = new SKColor(255, 255, 255, 10), StrokeWidth = 2 };
        for (float y = WallInset + 24; y < e.Info.Height - WallInset; y += 34)
            canvas.DrawLine(WallInset + 18, y, e.Info.Width - WallInset - 18, y, grain);

        DrawGoal(canvas);
        DrawDip(canvas);
        DrawHazardHoles(canvas);
        DrawMazeWalls(canvas);
        DrawWalls(canvas, e.Info.Width, e.Info.Height);
        if (_ball is not null)
            DrawBall(canvas, _ball);
    }

    private static void DrawWalls(SKCanvas canvas, int width, int height)
    {
        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 80), Style = SKPaintStyle.Stroke, StrokeWidth = 22, IsAntialias = true };
        using var wall = new SKPaint { Color = new SKColor(160, 126, 73), Style = SKPaintStyle.Stroke, StrokeWidth = 16, IsAntialias = true };
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
            Color = new SKColor(135, 95, 50),
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
        canvas.DrawCircle(goal.X + 3, goal.Y + 5, GoalRadius + 5, rim);
        canvas.DrawCircle(goal, GoalRadius, hole);
    }

    private void DrawHazardHoles(SKCanvas canvas)
    {
        using var rim = new SKPaint { Color = new SKColor(91, 42, 34), IsAntialias = true };
        using var hole = new SKPaint { Color = new SKColor(1, 4, 3), IsAntialias = true };
        using var glint = new SKPaint
        {
            Color = new SKColor(226, 102, 76, 105),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f,
            IsAntialias = true
        };

        foreach (SKPoint center in _hazardHoles)
        {
            canvas.DrawCircle(center.X + 2, center.Y + 4, HazardRadius + 4, rim);
            canvas.DrawCircle(center, HazardRadius, hole);
            canvas.DrawArc(new SKRect(center.X - 20, center.Y - 20, center.X + 20, center.Y + 20), 205, 105, false, glint);
        }
    }

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
        if (e.ActionType == SKTouchAction.Pressed)
            _dragging = true;

        if (e.ActionType == SKTouchAction.Released || e.ActionType == SKTouchAction.Cancelled)
        {
            _dragging = false;
            _tilt = SKPoint.Empty;
        }
        else if (_dragging && _boardSize.Width > 0 && _boardSize.Height > 0)
        {
            _tilt = new SKPoint(
                Math.Clamp((e.Location.X - (_boardSize.Width / 2)) / (_boardSize.Width / 2), -1f, 1f),
                Math.Clamp((e.Location.Y - (_boardSize.Height / 2)) / (_boardSize.Height / 2), -1f, 1f));
        }
        e.Handled = true;
    }

    private void OnWoodClicked(object? sender, EventArgs e) => SelectBall(BallProfile.Wood);
    private void OnSilverClicked(object? sender, EventArgs e) => SelectBall(BallProfile.Silver);
    private void OnGoldClicked(object? sender, EventArgs e) => SelectBall(BallProfile.Gold);
    private async void OnPlayAgainClicked(object? sender, EventArgs e)
    {
        if (_completedMaze && _nextMazeFileIndex < MazeFiles.Length)
        {
            string fileName = MazeFiles[_nextMazeFileIndex++];
            try
            {
                await LoadMazeFromAppPackageAsync(fileName);
                StatusLabel.Text = $"{_maze.Name}  •  tilt to roll";
                return;
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Could not load {fileName}: {ex.Message}";
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
    string Name, float Acceleration, float Drag, float Restitution, float MaxSpeed,
    SKColor Highlight, SKColor BaseColor, SKColor Shadow)
{
    public static BallProfile Wood { get; } = new("Wood", 680f, 2.8f, 0.28f, 520f,
        new SKColor(225, 169, 91), new SKColor(145, 82, 40), new SKColor(70, 36, 21));

    public static BallProfile Silver { get; } = new("Silver", 820f, 1.55f, 0.52f, 680f,
        new SKColor(250, 253, 255), new SKColor(150, 164, 170), new SKColor(56, 65, 70));

    public static BallProfile Gold { get; } = new("Gold", 620f, 0.72f, 0.38f, 760f,
        new SKColor(255, 240, 142), new SKColor(211, 158, 34), new SKColor(100, 64, 8));
}

internal readonly record struct MazeWall(SKPoint Start, SKPoint End);

internal static class ColorExtensions
{
    public static Color ToMauiColor(this SKColor color) =>
        Color.FromRgba(color.Red, color.Green, color.Blue, color.Alpha);
}
