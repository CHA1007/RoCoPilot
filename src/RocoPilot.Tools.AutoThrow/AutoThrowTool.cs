using System.Windows;
using RocoPilot.Core;
using Wpf.Ui.Controls;

namespace RocoPilot.Tools.AutoThrow;

public sealed class AutoThrowTool : ITool
{
    public const string ToolId = "auto-throw";

    public string Id => ToolId;

    public string DisplayName => "自动丢球";

    public SymbolRegular Icon => SymbolRegular.TargetArrow24;

    public Type SettingsType => typeof(AutoThrowSettings);

    public object CreateDefaultSettings() => new AutoThrowSettings();

    public IRunningTask Run(object settings)
    {
        return new AutoThrowRunningTask(CastSettings(settings));
    }

    public FrameworkElement CreateConfigPanel(object settings, Action persist)
    {
        ArgumentNullException.ThrowIfNull(persist);
        return new AutoThrowConfigPanel(CastSettings(settings), persist);
    }

    private static AutoThrowSettings CastSettings(object settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings is not AutoThrowSettings typed)
        {
            throw new ArgumentException($"配置须为 {nameof(AutoThrowSettings)}，实得 {settings.GetType().Name}", nameof(settings));
        }

        return typed;
    }
}
