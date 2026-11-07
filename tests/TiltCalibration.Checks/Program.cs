using RollingMaze;

static void Near(double expected, double actual)
{
    if (Math.Abs(expected - actual) > 1e-9)
        throw new Exception($"Expected {expected}, got {actual}.");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

// Regression: the user's flat-phone capture must become neutral, not downward force.
double[] flatReadings = [0.0496, 0.0491, 0.0499, 0.0501, 0.0501, 0.0493, 0.0494,
    0.0498, 0.0489, 0.0501, 0.0491, 0.0498, 0.0491, 0.0494, 0.0498, 0.0495,
    0.0496, 0.0495, 0.0498, 0.0499, 0.0499];
var calibration = new TiltCalibration();
calibration.Begin();
foreach (double y in flatReadings) calibration.Process(-0.01, y, 1);
Check(calibration.Finish(), "Steady flat readings should calibrate.");
Near(0, flatReadings.Average(y => calibration.Process(-0.01, y, 1).Y));
Near(0, calibration.Process(-0.01, flatReadings.Average(), 1).X);

// Opposite departures from the measured neutral retain equal magnitude.
double neutral = calibration.Offset.Y;
Near(0.2, calibration.Process(-0.01, neutral + 0.2, 0.98).Y);
Near(-0.2, calibration.Process(-0.01, neutral - 0.2, 0.98).Y);
var restored = new TiltCalibration(calibration.Offset.X, calibration.Offset.Y);
Near(0, restored.Process(-0.01, neutral, 1).Y);

// Missing samples, movement in any axis, and cancellation preserve the old offset.
calibration.Begin();
Check(!calibration.Finish(), "No samples must not overwrite calibration.");
Near(neutral, calibration.Offset.Y);
for (int axis = 0; axis < 3; axis++)
{
    calibration.Begin();
    for (int i = 0; i < 20; i++)
    {
        double movement = i % 2 == 0 ? 0.1 : -0.1;
        calibration.Process(axis == 0 ? movement : 0,
            axis == 1 ? movement : 0, axis == 2 ? 1 + movement : 1);
    }
    Check(!calibration.Finish(), "Moving samples must be rejected.");
    Near(neutral, calibration.Offset.Y);
}
calibration.Begin();
for (int i = 0; i < 20; i++) calibration.Process(0, 0, 1);
calibration.Cancel();
for (int i = 0; i < 20; i++) calibration.Process(0, 0, 1);
Check(!calibration.Finish(), "Cancelled sampling must not collect more readings.");
Near(neutral, calibration.Offset.Y);
Console.WriteLine("Passed: captured neutral, symmetric tilt, restored offsets, missing samples, motion rejection, cancellation.");
