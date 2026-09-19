using System.Windows;
using RocoPilot.Core;
using RocoPilot.Handpan;
using RocoPilot.Input;
using RocoPilot.Settings;
using RocoPilot.ToolUi;
using Wpf.Ui.Controls;

namespace RocoPilot.Tools.Handpan;

public enum HandpanTaskMode
{
    Chart,
    Play,
    Probe,
}

public sealed class HandpanTool : IToolUi
{
    public const string ToolId = "handpan";

    private readonly Func<IInputDriver> _driverFactory;

    public HandpanTool(Func<IInputDriver>? driverFactory = null)
    {
        _driverFactory = driverFactory ?? InputDriverFactory.Create;
    }

    public string Id => ToolId;

    public SymbolRegular Icon => SymbolRegular.MusicNote224;

    public Type SettingsType => typeof(HandpanSettings);

    public object CreateDefaultSettings() => new HandpanSettings();

    public IRunningTask Run(object settings)
    {
        var typed = CastSettings(settings);
        return new HandpanRunningTask(typed, HandpanTaskMode.Play, _driverFactory);
    }

    public IRunningTask RunChart(object settings)
    {
        var typed = CastSettings(settings);
        return new HandpanRunningTask(typed, HandpanTaskMode.Chart, _driverFactory);
    }

    public IRunningTask RunProbe(object settings)
    {
        var typed = CastSettings(settings);
        return new HandpanRunningTask(typed, HandpanTaskMode.Probe, _driverFactory);
    }

    public FrameworkElement CreateConfigPanel(object settings, Action persist)
    {
        ArgumentNullException.ThrowIfNull(persist);
        return new HandpanConfigPanel(CastSettings(settings), persist, this);
    }

    private static HandpanSettings CastSettings(object settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings is not HandpanSettings typed)
        {
            throw new ArgumentException($"配置须为 {nameof(HandpanSettings)}，实得 {settings.GetType().Name}", nameof(settings));
        }

        return typed;
    }
}
