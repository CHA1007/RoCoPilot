using System.Threading;
using RocoPilot.Core;
using RocoPilot.Dispatch;
using RocoPilot.Input;
using RocoPilot.Tools.AutoBattle.Battle;

namespace RocoPilot.Tools.AutoBattle;

public sealed class AutoBattleHandler : ISceneHandler, IDisposable
{
    private static readonly TimeSpan SettingsRefreshInterval = TimeSpan.FromMilliseconds(500);

    private readonly Func<AutoBattleSettings>? _settingsProvider;
    private readonly IBattleSensor _sensor;
    private volatile AutoBattleSettings _settings;

    private IBattleAction? _action;
    private SceneContext? _context;
    private CancellationTokenSource? _cts;
    private Thread? _settingsRefresher;

    public AutoBattleHandler(AutoBattleSettings settings, IBattleSensor sensor, Func<AutoBattleSettings>? settingsProvider = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _sensor = sensor ?? throw new ArgumentNullException(nameof(sensor));
        _settingsProvider = settingsProvider;
    }

    public GameScene Scene => GameScene.Battle;

    public bool IsEnabled { get; set; } = true;

    public void Activate(SceneContext context)
    {
        _context = context;
        _settings.SanitizeInPlace();
        _action = new SkillAction(() => _settings);
        StartSettingsRefresher();

        context.EmitEvent(new ToolEvent("battle_started", new Dictionary<string, object?>
        {
            ["skill_slot"] = _settings.SkillSlot,
        }));
    }

    public bool Handle(ReadOnlySpan<byte> bgraPixels, int width, int height)
    {
        if (_action is null || _context is null)
            return false;

        return _action.Execute(_context.InputDriver, _sensor, bgraPixels, width, height);
    }

    public void Deactivate()
    {
        StopSettingsRefresher();
        _context?.EmitEvent(new ToolEvent("battle_stopped"));
        _action = null;
        _context = null;
    }

    public void Dispose()
    {
        StopSettingsRefresher();
        (_sensor as IDisposable)?.Dispose();
    }

    private void StartSettingsRefresher()
    {
        StopSettingsRefresher();
        if (_settingsProvider is null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _settingsRefresher = new Thread(() =>
        {
            while (!ct.IsCancellationRequested)
            {
                ct.WaitHandle.WaitOne(SettingsRefreshInterval);
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                var latest = _settingsProvider();
                if (latest is not null)
                {
                    latest.SanitizeInPlace();
                    _settings = latest;
                }
            }
        })
        {
            IsBackground = true,
            Name = "自动战斗配置热刷新",
        };
        _settingsRefresher.Start();
    }

    private void StopSettingsRefresher()
    {
        _cts?.Cancel();
        var refresher = _settingsRefresher;
        _settingsRefresher = null;
        if (refresher is not null && refresher.IsAlive)
        {
            refresher.Join(TimeSpan.FromSeconds(1));
        }

        _cts?.Dispose();
        _cts = null;
    }
}