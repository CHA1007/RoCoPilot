using RocoPilot.Core;
using RocoPilot.Settings;
using RocoPilot.Shell.Hotkeys;
using RocoPilot.Shell.Services;

namespace RocoPilot.Shell.Tests;

public class TaskStopHotkeyTests
{
    private readonly FakeHotkeyRegistry _registry = new();
    private readonly RunningTaskHost _tasks = new();
    private readonly InMemorySettingsStore _store = new();
    private readonly TaskStopHotkey _hotkey;

    public TaskStopHotkeyTests() => _hotkey = new TaskStopHotkey(_registry, _tasks, _store);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("等待超时");
            }

            await Task.Delay(10);
        }
    }

    private void Configure(string hotkey, HotkeyScope scope = HotkeyScope.Global)
    {
        var shell = _store.GetShellSettings();
        shell.TaskStopHotkey = hotkey;
        shell.TaskStopHotkeyScope = scope;
        _store.SetShellSettings(shell);
    }

    [Fact]
    public void Start_without_a_running_task_registers_nothing()
    {
        Configure("F8");

        _hotkey.Start();

        Assert.Empty(_registry.Bindings);
    }

    [Fact]
    public void A_running_task_registers_the_configured_stop_hotkey_and_the_key_stops_it()
    {
        Configure("F8");
        var task = new StubTask();
        _hotkey.Start();

        _tasks.TryStart(task);

        var binding = Assert.Single(_registry.Bindings);
        Assert.Equal(TaskStopHotkey.Owner, binding.Owner);
        Assert.Equal("F8", binding.Hotkey);
        Assert.Equal(HotkeyScope.Global, binding.Scope);
        Assert.True(binding.Swallow);

        binding.Callback();
        Assert.Equal(1, task.StopRequests);
    }

    [Fact]
    public void The_scope_is_taken_from_the_settings()
    {
        Configure("F8", HotkeyScope.InGame);
        _hotkey.Start();

        _tasks.TryStart(new StubTask());

        Assert.Equal(HotkeyScope.InGame, Assert.Single(_registry.Bindings).Scope);
    }

    [Fact]
    public void A_padded_hotkey_is_trimmed_before_it_is_registered()
    {
        Configure("  Ctrl+Shift+F9  ");
        _hotkey.Start();

        _tasks.TryStart(new StubTask());

        Assert.Equal("Ctrl+Shift+F9", Assert.Single(_registry.Bindings).Hotkey);
    }

    [Fact]
    public async Task A_stopped_task_gives_up_the_hotkey()
    {
        Configure("F8");
        var task = new StubTask();
        _hotkey.Start();
        _tasks.TryStart(task);
        Assert.Single(_registry.Bindings);

        task.Complete();
        await WaitUntilAsync(() => _tasks.Current is null);
        await WaitUntilAsync(() => _registry.Bindings.Count == 0);

        Assert.Contains(TaskStopHotkey.Owner, _registry.Unregistered);
    }

    [Fact]
    public void A_blank_hotkey_leaves_the_key_alone()
    {
        Configure("   ");
        _hotkey.Start();

        _tasks.TryStart(new StubTask());

        Assert.Empty(_registry.Bindings);
    }

    [Fact]
    public void An_unparseable_hotkey_leaves_the_key_alone()
    {
        Configure("不是按键");
        _hotkey.Start();

        _tasks.TryStart(new StubTask());

        Assert.Empty(_registry.Bindings);
    }

    [Fact]
    public void Rebinding_while_a_task_runs_moves_the_key_onto_the_same_task()
    {
        Configure("F8");
        var task = new StubTask();
        _hotkey.Start();
        _tasks.TryStart(task);

        Configure("Ctrl+Shift+F9");
        _hotkey.Apply();

        var binding = Assert.Single(_registry.Bindings);
        Assert.Equal("Ctrl+Shift+F9", binding.Hotkey);

        binding.Callback();
        Assert.Equal(1, task.StopRequests);
    }

    [Fact]
    public void Clearing_the_hotkey_while_a_task_runs_gives_the_key_up()
    {
        Configure("F8");
        _hotkey.Start();
        _tasks.TryStart(new StubTask());
        Assert.Single(_registry.Bindings);

        Configure(string.Empty);
        _hotkey.Apply();

        Assert.Empty(_registry.Bindings);
        Assert.Contains(TaskStopHotkey.Owner, _registry.Unregistered);
    }

    [Fact]
    public void A_task_started_before_the_hotkey_starts_is_picked_up()
    {
        Configure("F8");
        _tasks.TryStart(new StubTask());

        _hotkey.Start();

        Assert.Single(_registry.Bindings);
    }

    private sealed class FakeHotkeyRegistry : IHotkeyRegistry
    {
        private readonly object _gate = new();
        private readonly List<Binding> _bindings = [];
        private readonly List<string> _unregistered = [];

        public IReadOnlyList<Binding> Bindings
        {
            get { lock (_gate) { return _bindings.ToList(); } }
        }

        public IReadOnlyList<string> Unregistered
        {
            get { lock (_gate) { return _unregistered.ToList(); } }
        }

        public bool Register(string owner, string hotkey, HotkeyScope scope, Action callback, bool swallow)
        {
            lock (_gate)
            {
                _bindings.RemoveAll(binding => binding.Owner == owner);
                _bindings.Add(new Binding(owner, hotkey, scope, callback, swallow));
            }

            return true;
        }

        public void Unregister(string owner)
        {
            lock (_gate)
            {
                _bindings.RemoveAll(binding => binding.Owner == owner);
                _unregistered.Add(owner);
            }
        }
    }

    internal sealed record Binding(string Owner, string Hotkey, HotkeyScope Scope, Action Callback, bool Swallow);

    private sealed class StubTask : IRunningTask
    {
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ToolId => "stub";

        public TaskState State { get; private set; } = TaskState.Idle;

        public int StopRequests { get; private set; }

        public Task WhenStopped => _stopped.Task;

        public event EventHandler<TaskState>? StateChanged;

        public event EventHandler<ToolEvent>? EventRaised;

        public void Start()
        {
            State = TaskState.Running;
            StateChanged?.Invoke(this, State);
        }

        public void RequestPause(string source = "manual")
        {
        }

        public void RequestResume(string source = "manual")
        {
        }

        public void RequestStop() => StopRequests++;

        public void Complete()
        {
            State = TaskState.Idle;
            StateChanged?.Invoke(this, State);
            EventRaised?.Invoke(this, new ToolEvent("stub-stopped"));
            _stopped.TrySetResult();
        }
    }
}
