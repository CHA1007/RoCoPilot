using RocoPilot.Detection;

namespace RocoPilot.Loop;

public sealed record RecognitionFlip(int TrackId, string PreviousClass, StableTarget Current);
