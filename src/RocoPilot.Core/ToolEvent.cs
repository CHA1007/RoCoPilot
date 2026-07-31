namespace RocoPilot.Core;

public sealed record ToolEvent(string Name, IReadOnlyDictionary<string, object?>? Data = null)
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}
