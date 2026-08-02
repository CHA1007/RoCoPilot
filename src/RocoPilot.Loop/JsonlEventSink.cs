using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using RocoPilot.Core;

namespace RocoPilot.Loop;

public sealed class JsonlEventSink : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    public JsonlEventSink(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = false,
        };
    }

    public string FilePath { get; }

    public void Write(ToolEvent toolEvent)
    {
        ArgumentNullException.ThrowIfNull(toolEvent);
        var line = new Dictionary<string, object?>
        {
            ["t"] = toolEvent.Timestamp.ToUnixTimeMilliseconds() / 1000.0,
            ["event"] = toolEvent.Name,
        };
        if (toolEvent.Data is { } data)
        {
            foreach (var (key, value) in data)
            {
                line[key] = value;
            }
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _writer.Write(JsonSerializer.Serialize(line, JsonOptions));
            _writer.Write('\n');
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _writer.Dispose();
    }
}
