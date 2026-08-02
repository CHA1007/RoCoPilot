using RocoPilot.Core;
using RocoPilot.Settings;
using RocoPilot.Shell.Pages;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.AutoThrow;

namespace RocoPilot.Shell.Tools;

internal static class ToolRegistry
{
    private static IReadOnlyList<ITool>? _cached;

    public static IReadOnlyList<ITool> CreateTools(CaptureHost captureHost, ISettingsStore store)
    {
        if (_cached is not null) return _cached;
        _cached =
        [
            new AutoThrowTool(() => captureHost.CurrentSource, store),
        ];
        return _cached;
    }

    public static Type PageTypeOf(ITool tool) => tool switch
    {
        AutoThrowTool => typeof(AutoThrowPage),
        _ => throw new NotSupportedException($"工具 {tool.Id} 未登记承载页（{nameof(ToolRegistry)}）"),
    };
}
