using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RocoPilot.Settings;

public sealed class JsonSettingsStore : ISettingsStore
{
    private const string ShellSection = "shell";
    private const string ToolsSection = "tools";

    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly object _gate = new();
    private JsonObject _root = new();

    public JsonSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
    }

    public string FilePath { get; }

    public void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath))
            {
                _root = NewRoot();
                return;
            }

            try
            {
                _root = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? throw new JsonException("根节点不是 JSON 对象");
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                QuarantineCorruptFile();
                _root = NewRoot();
                return;
            }

            EnsureSections();
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            EnsureSections();
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tmp = FilePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(tmp, _root.ToJsonString(s_options));
                if (File.Exists(FilePath))
                {
                    File.Replace(tmp, FilePath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(tmp, FilePath);
                }
            }
            catch
            {
                TryDelete(tmp);
                throw;
            }
        }
    }

    public ShellSettings GetShellSettings()
    {
        lock (_gate)
        {
            return _root[ShellSection].Deserialize<ShellSettings>(s_options) ?? new ShellSettings();
        }
    }

    public void SetShellSettings(ShellSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            MergeIntoSection(ShellSection, value);
        }
    }

    public object GetToolSettings(string toolId, Type settingsType, Func<object> defaultsFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        ArgumentNullException.ThrowIfNull(settingsType);
        ArgumentNullException.ThrowIfNull(defaultsFactory);

        lock (_gate)
        {
            var section = ToolsNode()[toolId];
            if (section is null)
            {
                return defaultsFactory();
            }

            try
            {
                return section.Deserialize(settingsType, s_options) ?? defaultsFactory();
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
            {
                return defaultsFactory();
            }
        }
    }

    public void SetToolSettings(string toolId, object settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            var tools = ToolsNode();
            if (tools[toolId] is not JsonObject section)
            {
                section = new JsonObject();
                tools[toolId] = section;
            }

            MergeFrom(section, settings);
        }
    }

    private JsonObject ToolsNode()
    {
        if (_root[ToolsSection] is not JsonObject tools)
        {
            tools = new JsonObject();
            _root[ToolsSection] = tools;
        }

        return tools;
    }

    private void MergeIntoSection(string sectionName, object value)
    {
        if (_root[sectionName] is not JsonObject section)
        {
            section = new JsonObject();
            _root[sectionName] = section;
        }

        MergeFrom(section, value);
    }

    private static void MergeFrom(JsonObject section, object value)
    {
        if (JsonSerializer.SerializeToNode(value, s_options) is not JsonObject projection)
        {
            throw new ArgumentException($"配置对象须序列化为 JSON 对象：{value.GetType()}", nameof(value));
        }

        foreach (var (key, node) in projection.ToList())
        {
            section[key] = node?.DeepClone();
        }
    }

    private void EnsureSections()
    {
        if (_root[ShellSection] is not JsonObject)
        {
            _root[ShellSection] = new JsonObject();
        }

        _ = ToolsNode();
    }

    private static JsonObject NewRoot() => new() { [ShellSection] = new JsonObject(), [ToolsSection] = new JsonObject() };

    private void QuarantineCorruptFile()
    {
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
        var quarantine = FilePath + ".corrupt-" + stamp;
        try
        {
            File.Move(FilePath, quarantine);
        }
        catch (IOException)
        {
            TryDelete(FilePath);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) {  }
        catch (UnauthorizedAccessException) {  }
    }
}
