using RocoPilot.Input;

namespace RocoPilot.Routing;

public sealed class Route
{
    public Route(
        string name,
        DateTimeOffset recordedAt,
        TimeSpan duration,
        IReadOnlyList<RouteEvent> events,
        IReadOnlyList<RouteKeyframe> keyframes)
    {
        Name = name;
        RecordedAt = recordedAt;
        Duration = duration;
        Events = events;
        Keyframes = keyframes;
    }

    public string Name { get; }

    public DateTimeOffset RecordedAt { get; }

    public TimeSpan Duration { get; }

    public IReadOnlyList<RouteEvent> Events { get; }

    public IReadOnlyList<RouteKeyframe> Keyframes { get; }
}

public sealed record RouteEvent(double OffsetMs, ReceivedStroke Stroke);

public sealed record RouteKeyframe(double OffsetMs, int Width, int Height, byte[] MinimapPng);

public sealed record RouteSummary(string Name, DateTimeOffset RecordedAt, TimeSpan Duration, int EventCount);
