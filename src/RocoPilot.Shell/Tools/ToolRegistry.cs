using RocoPilot.Core;
using RocoPilot.Shell.Pages;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.AutoThrow;

namespace RocoPilot.Shell.Tools;

internal static class ToolRegistry
{
    public static IReadOnlyList<ITool> CreateTools(CaptureHost captureHost) =>
    [
        new AutoThrowTool(() => captureHost.CurrentSource),
    ];

    public static Type PageTypeOf(ITool tool) => tool switch
    {
        AutoThrowTool => typeof(AutoThrowPage),
        _ => throw new NotSupportedException($"工具 {tool.Id} 未登记承载页（{nameof(ToolRegistry)}）"),
    };
}
