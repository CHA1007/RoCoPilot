using System.Text.Json.Serialization;
using RocoPilot.Input;

namespace RocoPilot.Scripting;

public sealed record TimedStroke(
    ReceivedDeviceKind Kind,
    ushort Code,
    ushort State,
    ushort Flags,
    short Rolling,
    int X,
    int Y,
    long OffsetMs)
{
    public ReceivedStroke ToRaw() => Kind == ReceivedDeviceKind.Keyboard
        ? ReceivedStroke.Key(Code, State)
        : ReceivedStroke.Mouse(State, Flags, Rolling, X, Y);
}

public sealed class RecordedScript
{
    public RecordedScript(string name, IReadOnlyList<TimedStroke> strokes, DateTimeOffset? createdAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        CreatedAt = createdAt ?? DateTimeOffset.Now;
        Strokes = [.. strokes];
    }

    [JsonConstructor]
    private RecordedScript() { }

    [JsonInclude]
    public string Name { get; private set; } = string.Empty;

    [JsonInclude]
    public DateTimeOffset CreatedAt { get; private set; }

    [JsonInclude]
    public IReadOnlyList<TimedStroke> Strokes { get; private set; } = [];

    public TimeSpan Duration => Strokes.Count == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Strokes[^1].OffsetMs);
}