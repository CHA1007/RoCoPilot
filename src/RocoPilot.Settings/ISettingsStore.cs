namespace RocoPilot.Settings;

public interface ISettingsStore
{
    string FilePath { get; }

    void Load();

    void Save();

    ShellSettings GetShellSettings();

    void SetShellSettings(ShellSettings value);

    object GetToolSettings(string toolId, Type settingsType, Func<object> defaultsFactory);

    void SetToolSettings(string toolId, object settings);
}
