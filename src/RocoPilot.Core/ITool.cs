namespace RocoPilot.Core;

public interface ITool
{
    string Id { get; }

    string DisplayName { get; }

    Type SettingsType { get; }

    object CreateDefaultSettings();

    IRunningTask Run(object settings);
}
