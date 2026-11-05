using Microsoft.Maui.Devices.Sensors;
using SkiaSharp;

namespace RollingMaze;

public partial class MainPage
{
    private const string TiltOffsetXKey = "tilt-offset-x-v1";
    private const string TiltOffsetYKey = "tilt-offset-y-v1";
    private readonly TiltCalibration _tiltCalibration = new(
        Preferences.Default.Get(TiltOffsetXKey, 0d),
        Preferences.Default.Get(TiltOffsetYKey, 0d));
    private CancellationTokenSource? _calibrationCancellation;
    private bool _isCalibrating;

    private async void OnCalibrateTiltClicked(object? sender, EventArgs e)
    {
        if (_isCalibrating)
            return;

        if (!Accelerometer.Default.IsMonitoring)
        {
            CalibrationStatusLabel.Text = "Tilt sensor is unavailable.";
            StatusLabel.Text = CalibrationStatusLabel.Text;
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _calibrationCancellation = cancellation;
        _isCalibrating = true;
        DateTime pausedAt = DateTime.UtcNow;
        BallChoices.IsEnabled = false;
        CalibrateTiltButton.IsEnabled = false;
        CalibrationStatusLabel.Text = "Keep the phone flat and still…";
        StatusLabel.Text = CalibrationStatusLabel.Text;
        try
        {
            // Let the tap settle before averaging fresh sensor events for 1.5 seconds.
            await Task.Delay(700, cancellation.Token);
            _tiltCalibration.Begin();
            await Task.Delay(1500, cancellation.Token);
            if (_tiltCalibration.Finish())
            {
                var offset = _tiltCalibration.Offset;
                Preferences.Default.Set(TiltOffsetXKey, offset.X);
                Preferences.Default.Set(TiltOffsetYKey, offset.Y);
                _tilt = SKPoint.Empty;
                _velocity = SKPoint.Empty;
                CalibrationStatusLabel.Text = "Tilt calibrated and saved on this device.";
            }
            else
            {
                CalibrationStatusLabel.Text = "Could not get a steady reading. Keep the phone still and try again.";
            }
            StatusLabel.Text = CalibrationStatusLabel.Text;
        }
        catch (OperationCanceledException)
        {
            CalibrationStatusLabel.Text = "Calibration cancelled. Tap Calibrate tilt to retry.";
            StatusLabel.Text = CalibrationStatusLabel.Text;
        }
        finally
        {
            _tiltCalibration.Cancel();
            _startedAt += DateTime.UtcNow - pausedAt;
            _lastFrame = DateTime.UtcNow;
            _isCalibrating = false;
            _calibrationCancellation = null;
            BallChoices.IsEnabled = true;
            CalibrateTiltButton.IsEnabled = true;
        }
    }
}
