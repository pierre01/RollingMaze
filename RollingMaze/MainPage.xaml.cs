using Microsoft.Maui.Devices.Sensors;
using SkiaSharp;
using SkiaSharp.Views.Maui;

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
    private float _lastAccelerationMagnitude = 1f;
    private DateTime _lastKnockAt = DateTime.MinValue;

    private const float BallRadius = 24f;
    private const float WallInset = 24f;
    private const float GoalRadius = 34f;

    /// <summary>
    /// Global rolling-surface resistance. Values near 0.15 feel very slick,
    /// 1.0 preserves the original floor, and values around 3.0 feel like velvet.
    /// This is separate from each ball material's own drag.
    /// </summary>
    public float SurfaceFriction { get; set; } = 1.0f;

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
        float left = WallInset + BallRadius + 18;
        float top = WallInset + BallRadius + 18;
        _position = new SKPoint(left, top);
        _velocity = SKPoint.Empty;
        _ballHeight = 0f;
        _verticalVelocity = 0f;
        _landingEffect = 0f;
        _won = false;
        _startedAt = DateTime.UtcNow;
        _lastFrame = DateTime.UtcNow;
        WinOverlay.IsVisible = false;
        GameCanvas.InvalidateSurface();
    }

    private void OnGameTick(object? sender, EventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        float dt = Math.Clamp((float)(now - _lastFrame).TotalSeconds, 0f, 0.033f);
        _lastFrame = now;

        if (_ball is null || _won || _boardSize.Width <= 0 || _boardSize.Height <= 0)
            return;

        // Profiles alter how strongly tilt accelerates the ball, how quickly it
        // loses speed, and how much energy remains after touching a wall.
        _velocity.X += _tilt.X * _ball.Acceleration * dt;
        _velocity.Y += _tilt.Y * _ball.Acceleration * dt;
        float drag = MathF.Exp(-_ball.Drag * Math.Max(0f, SurfaceFriction) * dt);
        _velocity.X *= drag;
        _velocity.Y *= drag;

        UpdateBounce(dt);

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

    private void BounceBall()
    {
        if (_ball is null || _won || _ballHeight > 3f)
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
        _velocity = SKPoint.Empty;
        double seconds = (DateTime.UtcNow - _startedAt).TotalSeconds;
        WinDetailLabel.Text = $"{_ball!.Name} ball • {seconds:0.0} seconds";
        WinOverlay.IsVisible = true;
    }

    private SKPoint GetGoalCenter() => new(
        _boardSize.Width - WallInset - GoalRadius - 18,
        _boardSize.Height - WallInset - GoalRadius - 18);

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

    private void DrawGoal(SKCanvas canvas)
    {
        SKPoint goal = GetGoalCenter();
        using var rim = new SKPaint { Color = new SKColor(10, 18, 13), IsAntialias = true };
        using var hole = new SKPaint { Color = new SKColor(1, 5, 3), IsAntialias = true };
        canvas.DrawCircle(goal.X + 3, goal.Y + 5, GoalRadius + 5, rim);
        canvas.DrawCircle(goal, GoalRadius, hole);
    }

    private void DrawBall(SKCanvas canvas, BallProfile profile)
    {
        float heightRatio = Math.Clamp(_ballHeight / 100f, 0f, 1f);
        float drawnY = _position.Y - (_ballHeight * 0.16f);
        float ballScale = 1f + (heightRatio * 0.13f);
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
    private void OnPlayAgainClicked(object? sender, EventArgs e) => ResetGame();

    private void OnChangeBallClicked(object? sender, EventArgs e)
    {
        WinOverlay.IsVisible = false;
        SelectionOverlay.IsVisible = true;
        StatusLabel.Text = "Choose a ball";
    }
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

internal static class ColorExtensions
{
    public static Color ToMauiColor(this SKColor color) =>
        Color.FromRgba(color.Red, color.Green, color.Blue, color.Alpha);
}
