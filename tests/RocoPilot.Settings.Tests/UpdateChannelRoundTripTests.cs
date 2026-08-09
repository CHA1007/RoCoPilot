using RocoPilot.Settings;

namespace RocoPilot.Settings.Tests;

public class UpdateChannelRoundTripTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "RocoPilot-Settings-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void DefaultChannel_IsStable()
    {
        var settings = new ShellSettings();

        Assert.Equal(UpdateChannel.Stable, settings.UpdateChannel);
    }

    [Fact]
    public void BetaChannel_RoundTripsThroughSaveAndLoad()
    {
        var path = TempPath();
        try
        {
            var store = new JsonSettingsStore(path);
            store.Load();
            var shell = store.GetShellSettings();
            shell.UpdateChannel = UpdateChannel.Beta;
            store.SetShellSettings(shell);
            store.Save();

            var reloaded = new JsonSettingsStore(path);
            reloaded.Load();

            Assert.Equal(UpdateChannel.Beta, reloaded.GetShellSettings().UpdateChannel);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void LegacyFileWithoutChannel_DefaultsToStable()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, """{"shell":{"theme":"dark"},"tools":{}}""");

            var store = new JsonSettingsStore(path);
            store.Load();

            Assert.Equal(UpdateChannel.Stable, store.GetShellSettings().UpdateChannel);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ChannelPersistsAsCamelCaseString()
    {
        var path = TempPath();
        try
        {
            var store = new JsonSettingsStore(path);
            store.Load();
            var shell = store.GetShellSettings();
            shell.UpdateChannel = UpdateChannel.Beta;
            store.SetShellSettings(shell);
            store.Save();

            var text = File.ReadAllText(path);

            Assert.Contains("\"updateChannel\": \"beta\"", text);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}