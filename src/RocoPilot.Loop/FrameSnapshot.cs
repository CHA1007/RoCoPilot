using RocoPilot.Detection;

namespace RocoPilot.Loop;

public sealed record FrameSnapshot(
    byte[] Pixels,
    int Width,
    int Height,
    long Sequence,
    DateTimeOffset CapturedAt,
    IReadOnlyList<DetectedBox> Detections);
