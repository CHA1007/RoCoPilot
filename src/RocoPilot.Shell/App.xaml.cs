using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using RocoPilot.Breeding;
using RocoPilot.Routing;
using RocoPilot.Scripting;
using RocoPilot.Settings;
using RocoPilot.Shell.Appearance;
using RocoPilot.Shell.Hotkeys;
using RocoPilot.Shell.Overlay;
using RocoPilot.Shell.Pages;
using RocoPilot.Shell.Services;
using RocoPilot.Shell.Tools;
using Velopack;
using Wpf.Ui.DependencyInjection;

namespace RocoPilot.Shell;

public partial class App : Application
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private static IServiceProvider? _services;

    internal static IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("应用尚未完成启动装配");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            AppUpdater.ApplyPendingUpdate();
        }
        catch
        {
        }

        var tracePath = Path.Combine(Path.GetTempPath(), "RocoPilot-trace.log");
        Trace.Listeners.Add(new TextWriterTraceListener(tracePath) { TraceOutputOptions = System.Diagnostics.TraceOptions.DateTime });
        Trace.AutoFlush = true;

        var settingsStore = new JsonSettingsStore(RocoPaths.SettingsFilePath);
        settingsStore.Load();

        var services = new ServiceCollection();
        ConfigureServices(services, settingsStore);
        _services = services.BuildServiceProvider();

        var store = _services.GetRequiredService<ISettingsStore>();
        SeedDefaults(store);
        store.Save();

        ShellTheme.Apply(store.GetShellSettings().Theme);

        var window = _services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    private static void ConfigureServices(IServiceCollection services, ISettingsStore settingsStore)
    {
        services.AddNavigationViewPageProvider();

        services.AddSingleton(settingsStore);
        services.AddSingleton<RunningTaskHost>();
        var captureHost = new CaptureHost();
        services.AddSingleton(captureHost);
        services.AddSingleton<OverlayController>();
        services.AddSingleton<GlobalHotkeyManager>();
        services.AddSingleton<ShellHotkeys>();
        var routeStore = new RouteStore();
        services.AddSingleton(routeStore);
        var scriptStore = new ScriptStore();
        services.AddSingleton(scriptStore);

        var tools = ToolRegistry.CreateTools(captureHost, settingsStore);
        foreach (var tool in tools)
        {
            services.AddSingleton(tool.GetType(), tool);
        }

        var throwTool = tools.Single(t => t.Id == RocoPilot.Tools.AutoThrow.AutoThrowTool.ToolId);
        services.AddSingleton(new DispatcherHost(captureHost, settingsStore, throwTool, routeStore, scriptStore));

        services.AddSingleton(_ => PetCatalog.LoadEmbedded());

        services.AddTransient<LaunchPage>();
        services.AddTransient<HotkeysPage>();
        services.AddSingleton<EggQueryPage>();
        services.AddSingleton<RealtimePage>();
        services.AddSingleton<RoutePage>();
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
