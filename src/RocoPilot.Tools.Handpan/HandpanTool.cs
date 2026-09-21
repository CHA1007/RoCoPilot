using System.Windows;
using RocoPilot.Core;
using RocoPilot.Input;
using RocoPilot.ToolUi;
using Wpf.Ui.Controls;

namespace RocoPilot.Tools.Handpan;

public sealed class HandpanTool : IToolUi
{
    public const string ToolId = "handpan";

    private readonly Func<IInputDriver> _driverFactory;
    private readonly HandpanScoreStore _scores;

    public HandpanTool(Func<IInputDriver>? driverFactory = null, HandpanScoreStore? scores = null)
    {
        _driverFactory = driverFactory ?? InputDriverFactory.Create;
        _scores = scores ?? new HandpanScoreStore();
    }

    public string Id => ToolId;

    public SymbolRegular Icon => SymbolRegular.MusicNote224;

    public HandpanScoreStore Scores => _scores;

    public Type SettingsType => typeof(HandpanSettings);

    public object CreateDefaultSettings() => new HandpanSettings();

    public IRunningTask Run(object settings) => new HandpanRunningTask(
        CastSettings(settings), _driverFactory, scores: _scores);

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
