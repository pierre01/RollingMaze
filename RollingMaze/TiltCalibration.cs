namespace RollingMaze;

// Keep offsets in sensor coordinates so display rotation cannot reverse the correction.
internal sealed class TiltCalibration(double offsetX = 0, double offsetY = 0)
{
    private readonly object _sync = new();
    private double _offsetX = offsetX;
    private double _offsetY = offsetY;
    private bool _collecting;
    private int _count;
    private double _sumX, _sumY;
    private double _minX, _maxX, _minY, _maxY, _minZ, _maxZ;

    public (double X, double Y) Offset
    {
        get { lock (_sync) return (_offsetX, _offsetY); }
    }

    public void Begin()
    {
        lock (_sync)
        {
            _count = 0;
            _sumX = _sumY = 0;
            _minX = _minY = _minZ = double.PositiveInfinity;
            _maxX = _maxY = _maxZ = double.NegativeInfinity;
            _collecting = true;
        }
    }

    public (double X, double Y) Process(double x, double y, double z)
    {
        lock (_sync)
        {
            if (_collecting && double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z))
            {
                _count++;
                _sumX += x;
                _sumY += y;
                _minX = Math.Min(_minX, x); _maxX = Math.Max(_maxX, x);
                _minY = Math.Min(_minY, y); _maxY = Math.Max(_maxY, y);
                _minZ = Math.Min(_minZ, z); _maxZ = Math.Max(_maxZ, z);
            }
            return (x - _offsetX, y - _offsetY);
        }
    }

    public bool Finish()
    {
        lock (_sync)
        {
            if (!_collecting)
                return false;
            _collecting = false;
            // Reject missing samples or motion; leave the previous calibration intact.
            if (_count < 10 || _maxX - _minX > 0.03 ||
                _maxY - _minY > 0.03 || _maxZ - _minZ > 0.03)
                return false;

            _offsetX = _sumX / _count;
            _offsetY = _sumY / _count;
            return true;
        }
    }

    public void Cancel()
    {
        lock (_sync) _collecting = false;
    }
}
