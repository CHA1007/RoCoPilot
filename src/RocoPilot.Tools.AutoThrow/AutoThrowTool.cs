using System.Windows;
using RocoPilot.Capture;
using RocoPilot.Core;
using RocoPilot.Settings;
using RocoPilot.ToolUi;
using Wpf.Ui.Controls;

namespace RocoPilot.Tools.AutoThrow;

public sealed class AutoThrowTool : IToolUi
{
    public const string ToolId = "auto-throw";

    private readonly Func<ICaptureSource?> _captureSourceProvider;
    private readonly ISettingsStore _store;

    public AutoThrowTool(Func<ICaptureSource?> captureSourceProvider, ISettingsStore store)
    {
        _captureSourceProvider = captureSourceProvider ?? throw new ArgumentNullException(nameof(captureSourceProvider));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public string Id => ToolId;

    public string DisplayName => "自动丢球";

    public SymbolRegular Icon => SymbolRegular.TargetArrow24;

    public Type SettingsType => typeof(AutoThrowSettings);

    public object CreateDefaultSettings() => new AutoThrowSettings();

    public IRunningTask Run(object settings)
    {
        var source = _captureSourceProvider()
            ?? throw new InvalidOperationException("请先启动截图器（实时识别页 → 开始截图），再启动自动丢球");
        return new AutoThrowRunningTask(CastSettings(settings), source, _store);
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
