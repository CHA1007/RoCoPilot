using RocoPilot.Core;
using RocoPilot.Settings;

namespace RocoPilot.Shell.Tests;

internal sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, object> _tools = new();
    private ShellSettings _shell = new();

    public string FilePath => "(memory)";

    public int SaveCount { get; private set; }

    public void Load()
    {
    }

    public void Save() => SaveCount++;

    public ShellSettings GetShellSettings() => _shell;

    public void SetShellSettings(ShellSettings value) => _shell = value;

    public object GetToolSettings(string toolId, Type settingsType, Func<object> defaultsFactory)
        => _tools.TryGetValue(toolId, out var existing) && existing.GetType() == settingsType
            ? existing
            : defaultsFactory();

    public void SetToolSettings(string toolId, object settings) => _tools[toolId] = settings;
}

internal sealed class NoopTool : ITool
{
    public string Id => "test-tool";

    public Type SettingsType => typeof(object);

    public IRunningTask Run(object settings) => throw new System.NotSupportedException();

    public object CreateDefaultSettings() => new object();
}