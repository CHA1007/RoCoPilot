using System.Diagnostics;
using RocoPilot.Settings;
using RocoPilot.Shell.Services;

namespace RocoPilot.Shell.Hotkeys;

public sealed class TaskStopHotkey
{
    public const string Owner = "TaskStop";

    private readonly IHotkeyRegistry _registry;
    private readonly RunningTaskHost _tasks;
    private readonly ISettingsStore _store;

    public TaskStopHotkey(IHotkeyRegistry registry, RunningTaskHost tasks, ISettingsStore store)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public void Start()
    {
        _tasks.Changed += Apply;
        Apply();
    }

    public void Apply()
    {
        var task = _tasks.Current;
        var shell = _store.GetShellSettings();
        var hotkey = task is null ? null : StopHotkeyOf(shell.TaskStopHotkey);
        if (task is null || hotkey is null)
        {
            _registry.Unregister(Owner);
            return;
        }

        _registry.Register(Owner, hotkey, shell.TaskStopHotkeyScope, task.RequestStop, swallow: true);
    }

    private static string? StopHotkeyOf(string? configured)
    {
        var hotkey = configured?.Trim();
        if (string.IsNullOrEmpty(hotkey))
        {
            return null;
        }

        if (!HotkeyBinding.TryParse(hotkey, out _))
        {
            Trace.TraceWarning($"[TaskStopHotkey] 无法解析的停止热键 '{hotkey}'，本次运行不注册");
            return null;
        }

        return hotkey;
    }
}
