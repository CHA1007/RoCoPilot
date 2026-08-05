namespace RocoPilot.Core;

public interface ITool
{
    string Id { get; }

    Type SettingsType { get; }

    object CreateDefaultSettings();

    IRunningTask Run(object settings);
}
