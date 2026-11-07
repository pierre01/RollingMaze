#if DEBUG
using Microsoft.Maui.Devices;
using SkiaSharp;

namespace RollingMaze;

public partial class MainPage
{
    private bool _ignoreMazePull;
    private bool _tiltDiagnosticsEnabled;
    private Label _tiltDiagnosticsLabel = null!;
    private DateTime _nextTiltDiagnosticsUpdate;

    private void InitializeTiltDiagnostics()
    {
        _tiltDiagnosticsLabel = new Label
        {
            Text = "Choose a ball to see tilt readings. Positive values mean DOWN.",
            TextColor = Colors.White,
            FontSize = 12,
            InputTransparent = true
        };
        var ignorePull = new Switch();
        var calibrate = new Button { Text = "Calibrate tilt (lay phone flat)", FontSize = 12 };
        calibrate.Clicked += OnCalibrateTiltClicked;
        ignorePull.Toggled += (_, e) =>
        {
            _ignoreMazePull = e.Value;
            // Compare directions from rest, without momentum from the previous trial.
            _velocity = SKPoint.Empty;
        };
        var details = new VerticalStackLayout
        {
            IsVisible = false,
            Spacing = 4,
            Children =
            {
                _tiltDiagnosticsLabel,
                calibrate,
                new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        ignorePull,
                        new Label
                        {
                            Text = "Ignore black-hole and dip forces",
                            TextColor = Colors.White,
                            FontSize = 12,
                            VerticalOptions = LayoutOptions.Center
                        }
                    }
                }
            }
        };
        var toggle = new Button
        {
            Text = "Tilt diagnostics (Debug)",
            FontSize = 12,
            Padding = new Thickness(8, 2),
            HorizontalOptions = LayoutOptions.Start
        };
        toggle.Clicked += (_, _) =>
        {
            details.IsVisible = !details.IsVisible;
            _tiltDiagnosticsEnabled = details.IsVisible;
            if (!details.IsVisible)
                ignorePull.IsToggled = false;
        };

        // Reserve space below the board so diagnostic controls do not cover the maze.
        FooterPanel.Children.Insert(0, new VerticalStackLayout
        {
            BackgroundColor = Color.FromArgb("#E6101713"),
            Padding = 8,
            Children = { toggle, details }
        });
    }

    private void UpdateTiltDiagnostics(DateTime now, float verticalTilt, float mazeAcceleration, float dt)
    {
        if (!_tiltDiagnosticsEnabled || now < _nextTiltDiagnosticsUpdate)
            return;

        _nextTiltDiagnosticsUpdate = now.AddMilliseconds(200);
        var offset = _tiltCalibration.Offset;
        double verticalOffset = _displayRotation switch
        {
            DisplayRotation.Rotation90 => offset.X,
            DisplayRotation.Rotation180 => -offset.Y,
            DisplayRotation.Rotation270 => -offset.X,
            _ => offset.Y
        };
        double correctedY = verticalTilt / PhoneTiltSensitivity;
        double rawY = correctedY + verticalOffset;
        _tiltDiagnosticsLabel.Text =
            $"Positive = DOWN; negative = UP\n" +
            $"Raw Y: {rawY:+0.000;-0.000;0.000} g   Corrected: {correctedY:+0.000;-0.000;0.000} g\n" +
            $"Tilt acceleration: {verticalTilt * _ball!.Acceleration:+0;-0;0} px/s²   " +
            $"Maze: {mazeAcceleration:+0;-0;0} px/s²\n" +
            $"Vertical speed: {_velocity.Y:+0;-0;0} px/s (before collisions)";

        // Keep phone captures searchable and independent of the debugger's output routing.
        // Values match the on-screen sample, before speed limiting and collisions.
        string sample = FormattableString.Invariant($"inputY_g={correctedY:F4} rawY_g={rawY:F4} offsetY_g={verticalOffset:F4} tiltAy={verticalTilt * _ball.Acceleration:F2} mazeAy={mazeAcceleration:F2} vx={_velocity.X:F2} vy={_velocity.Y:F2} x={_position.X:F1} y={_position.Y:F1} dt={dt:F4} rotation={_displayRotation} ignoreMazePull={_ignoreMazePull} dragging={_dragging} ball={_ball.Name} level={_currentLevelNumber}");
#if ANDROID
        Android.Util.Log.Info("RollingMazeTilt", sample);
#else
        System.Diagnostics.Debug.WriteLine($"RollingMazeTilt: {sample}");
#endif
    }
}
#endif
