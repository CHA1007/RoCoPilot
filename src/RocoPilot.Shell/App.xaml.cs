using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RocoPilot.Settings;
using RocoPilot.Shell.Appearance;
using RocoPilot.Shell.Overlay;
using RocoPilot.Shell.Pages;
using RocoPilot.Shell.Services;
using RocoPilot.Shell.Tools;
using Wpf.Ui.DependencyInjection;

namespace RocoPilot.Shell;

public partial class App : Application
{
    private static IServiceProvider? _services;

    internal static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("应用尚未完成启动装配");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Trace 写入文件，方便排查
        var tracePath = Path.Combine(Path.GetTempPath(), "RocoPilot-trace.log");
        Trace.Listeners.Add(new TextWriterTraceListener(tracePath) { TraceOutputOptions = System.Diagnostics.TraceOptions.DateTime });
        Trace.AutoFlush = true;

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        var store = _services.GetRequiredService<ISettingsStore>();
        store.Load();
        SeedDefaults(store);
        store.Save();

        ShellTheme.Apply(store.GetShellSettings().Theme);

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddNavigationViewPageProvider();

        var settingsStore = new JsonSettingsStore(RocoPaths.SettingsFilePath);
        services.AddSingleton<ISettingsStore>(settingsStore);
        services.AddSingleton<RunningTaskHost>();
        var captureHost = new CaptureHost();
        services.AddSingleton(captureHost);
        services.AddSingleton<OverlayController>();

        foreach (var tool in ToolRegistry.CreateTools(captureHost, settingsStore))
        {
            services.AddSingleton(tool.GetType(), tool);
            services.AddTransient(ToolRegistry.PageTypeOf(tool));
        }

        // 调度器宿主：截图器启动时自动拉起
        var throwTool = (RocoPilot.Tools.AutoThrow.AutoThrowTool)ToolRegistry.CreateTools(captureHost, settingsStore)[0];
        services.AddSingleton(new DispatcherHost(captureHost, settingsStore, throwTool));

        services.AddTransient<LaunchPage>();
        services.AddSingleton<RealtimePage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<InputProbePage>();
        services.AddTransient<CaptureDebugPage>();
        services.AddTransient<DetectionDebugPage>();
        services.AddTransient<CenteringDebugPage>();
        services.AddTransient<CatchLoopDebugPage>();
        services.AddTransient<ScenesPage>();
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var host = _services?.GetService<RunningTaskHost>();
        var current = host?.Current;
        host?.RequestStop();
        current?.WhenStopped.Wait(TimeSpan.FromSeconds(3));
        base.OnExit(e);
    }

    private static void SeedDefaults(ISettingsStore store)
    {
        store.SetShellSettings(store.GetShellSettings());
        var captureHost = _services!.GetRequiredService<CaptureHost>();
        foreach (var tool in ToolRegistry.CreateTools(captureHost, store))
        {
            var settings = store.GetToolSettings(tool.Id, tool.SettingsType, tool.CreateDefaultSettings);
            store.SetToolSettings(tool.Id, settings);
        }
    }
}
