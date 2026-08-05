using RocoPilot.Capture;

namespace RocoPilot.Routing;

public enum ScreenChange
{
    Unknown,
    Still,
    Changed,
}

public sealed class FrameStillnessProbe
{
    private readonly ICaptureSource _capture;
    private readonly double _stillMeanDiff;
    private readonly int _maxSamples;
    private readonly object _gate = new();
    private byte[] _previous = [];
    private int _previousColumns;
    private long _lastSequence = -1;

    public FrameStillnessProbe(ICaptureSource capture, double stillMeanDiff = 2.0, int maxSamples = 4096)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (stillMeanDiff < 0) throw new ArgumentOutOfRangeException(nameof(stillMeanDiff));
        if (maxSamples < 16) throw new ArgumentOutOfRangeException(nameof(maxSamples));

        _capture = capture;
        _stillMeanDiff = stillMeanDiff;
        _maxSamples = maxSamples;
    }

    public ScreenChange Sample()
    {
        if (!_capture.TryGrabLatest(out var frame) || frame is null) return ScreenChange.Unknown;

        lock (_gate)
        {
            using (frame)
            {
                if (frame.Pixels.IsEmpty || frame.Sequence == _lastSequence) return ScreenChange.Unknown;
                _lastSequence = frame.Sequence;

                var current = Downsample(frame, out var columns);
                if (current.Length != _previous.Length || columns != _previousColumns)
                {
                    _previous = current;
                    _previousColumns = columns;
                    return ScreenChange.Unknown;
                }

                long total = 0;
                for (var i = 0; i < current.Length; i++)
                {
                    total += Math.Abs(current[i] - _previous[i]);
                }

                _previous = current;
                var meanDiff = (double)total / current.Length;
                return meanDiff < _stillMeanDiff ? ScreenChange.Still : ScreenChange.Changed;
            }
        }
    }

    private byte[] Downsample(CapturedFrame frame, out int columns)
    {
        var stride = Math.Max(1, (int)Math.Sqrt((double)frame.Width * frame.Height / _maxSamples));
        columns = (frame.Width + stride - 1) / stride;
        var rows = (frame.Height + stride - 1) / stride;
        var samples = new byte[columns * rows];
        var pixels = frame.Pixels;

        var index = 0;
        for (var y = 0; y < frame.Height; y += stride)
        {
            var rowStart = y * frame.Stride;
            for (var x = 0; x < frame.Width; x += stride)
            {
                var p = rowStart + x * 4;
                samples[index++] = (byte)((pixels[p] + pixels[p + 1] + pixels[p + 2]) / 3);
            }
        }

        return samples;
    }
}
