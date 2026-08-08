using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RocoPilot.Settings;

namespace RocoPilot.Scripting;

public sealed record ScriptSummary(string Name, DateTimeOffset CreatedAt, int StrokeCount, TimeSpan Duration);

public sealed class ScriptStore
{
    private const string ScriptExtension = ".json";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _scriptsRoot;

    public ScriptStore(string? scriptsRoot = null) => _scriptsRoot = scriptsRoot ?? RocoPaths.ScriptsRoot;

    public string ScriptsRoot => _scriptsRoot;

    public IReadOnlyList<ScriptSummary> List()
    {
        Directory.CreateDirectory(_scriptsRoot);
        var summaries = new List<ScriptSummary>();
        foreach (var file in Directory.EnumerateFiles(_scriptsRoot, "*" + ScriptExtension))
        {
            try
            {
                var json = File.ReadAllText(file);
                var script = JsonSerializer.Deserialize<RecordedScript>(json, JsonOptions);
                if (script is null) continue;
                summaries.Add(new ScriptSummary(script.Name, script.CreatedAt, script.Strokes.Count, script.Duration));
            }
            catch (JsonException)
            {
            }
        }

        return summaries.OrderByDescending(summary => summary.CreatedAt).ToList();
    }

    public async Task<RecordedScript?> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = PathFor(name);
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<RecordedScript>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(RecordedScript script, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);

        Directory.CreateDirectory(_scriptsRoot);
        var path = PathFor(script.Name);
        var json = JsonSerializer.SerializeToUtf8Bytes(script, JsonOptions);
        await File.WriteAllBytesAsync(path, json, cancellationToken);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task ExportAsync(string name, string targetPath, CancellationToken cancellationToken = default)
    {
        var script = await LoadAsync(name, cancellationToken);
        if (script is null) throw new FileNotFoundException("脚本不存在。", name);

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var json = JsonSerializer.SerializeToUtf8Bytes(script, JsonOptions);
        await File.WriteAllBytesAsync(targetPath, json, cancellationToken);
    }

    public async Task<string> ImportAsync(string sourcePath, string? nameOverride = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("导入文件不存在。", sourcePath);

        var json = await File.ReadAllTextAsync(sourcePath, cancellationToken);
        var script = JsonSerializer.Deserialize<RecordedScript>(json, JsonOptions)
            ?? throw new InvalidDataException("脚本文件损坏。");

        var finalName = string.IsNullOrWhiteSpace(nameOverride) ? script.Name : nameOverride.Trim();
        var imported = new RecordedScript(finalName, script.Strokes, script.CreatedAt);
        await SaveAsync(imported, cancellationToken);
        return imported.Name;
    }

    private string PathFor(string name) => Path.Combine(_scriptsRoot, FileNameFor(name));

    private static string FileNameFor(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "script" : safe + ScriptExtension;
    }

    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}