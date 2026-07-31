using RocoPilot.Settings;
using RocoPilot.Shell.Services;
using RocoPilot.Tools.AutoThrow;

namespace RocoPilot.Shell.Pages;

public sealed class AutoThrowPage : ToolHostPage
{
    public AutoThrowPage(AutoThrowTool tool, ISettingsStore store, RunningTaskHost taskHost)
        : base(tool, store, taskHost)
    {
    }

}
