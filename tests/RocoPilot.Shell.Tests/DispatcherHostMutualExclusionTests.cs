using System.IO;
using RocoPilot.Routing;
using RocoPilot.Scripting;
using RocoPilot.Shell.Services;

namespace RocoPilot.Shell.Tests;

public class DispatcherHostMutualExclusionTests : IDisposable
{
    private readonly CaptureHost _capture = new();
    private readonly InMemorySettingsStore _store = new();
    private readonly RouteStore _routeStore;
    private readonly ScriptStore _scriptStore;
    private readonly DispatcherHost _host;

    public DispatcherHostMutualExclusionTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "roco-test-" + Guid.NewGuid().ToString("N"));
        _routeStore = new RouteStore(root);
        _scriptStore = new ScriptStore(root);
        _host = new DispatcherHost(_capture, _store, new NoopTool(), _routeStore, _scriptStore);
    }

    [Fact]
    public void EnablingAutoThrowDisablesRouteExecution()
    {
        _host.RouteExecutionEnabled = true;
        _host.AutoThrowEnabled = true;

        Assert.True(_host.AutoThrowEnabled);
        Assert.False(_host.RouteExecutionEnabled);
    }

    [Fact]
    public void EnablingRouteExecutionDisablesAutoThrow()
    {
        _host.AutoThrowEnabled = true;
        _host.RouteExecutionEnabled = true;

        Assert.True(_host.RouteExecutionEnabled);
        Assert.False(_host.AutoThrowEnabled);
    }

    [Fact]
    public void DisablingActiveModuleLeavesBothOff()
    {
        _host.AutoThrowEnabled = true;
        _host.AutoThrowEnabled = false;

        Assert.False(_host.AutoThrowEnabled);
        Assert.False(_host.RouteExecutionEnabled);
    }

    [Fact]
    public void PendingRouteStartIsRoutedThroughRouteExecutionEnabled()
    {
        _host.AutoThrowEnabled = true;

        _host.StartRouteExecution(startNodeId: null, singleNode: false);

        Assert.True(_host.RouteExecutionEnabled);
        Assert.False(_host.AutoThrowEnabled);
    }

    [Fact]
    public void AutoThrowEnabledPersistsToShellSettings()
    {
        _host.AutoThrowEnabled = true;

        Assert.True(_store.GetShellSettings().AutoThrowEnabled);
    }

    [Fact]
    public void RouteExecutionIsNotPersistedAcrossInstances()
    {
        _host.RouteExecutionEnabled = true;

        var reloaded = new DispatcherHost(_capture, _store, new NoopTool(), _routeStore, _scriptStore);

        Assert.False(reloaded.RouteExecutionEnabled);
        Assert.False(reloaded.AutoThrowEnabled);
    }

    [Fact]
    public void AutoBattleEnabledPersistsToShellSettings()
    {
        _host.AutoBattleEnabled = true;

        Assert.True(_store.GetShellSettings().AutoBattleEnabled);
    }

    [Fact]
    public void FastTravelEnabledPersistsToShellSettings()
    {
        _host.FastTravelEnabled = true;

        Assert.True(_store.GetShellSettings().FastTravelEnabled);
    }

    public void Dispose()
    {
        _host.Dispose();
        _capture.Dispose();
    }
}