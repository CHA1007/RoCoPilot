namespace RocoPilot.Tools.AutoThrow.Tests;

public class AutoThrowSettingsTests
{
    [Fact]
    public void DefaultsSanitizeWithoutChange()
    {
        var settings = new AutoThrowSettings();
        settings.SanitizeInPlace();

        Assert.False(settings.FastThrowEnabled);
        Assert.Equal(200, settings.ChargeMs);
    }

    [Fact]
    public void SanitizeKeepsFastThrowEnabledFlag()
    {
        var settings = new AutoThrowSettings { FastThrowEnabled = true };
        settings.SanitizeInPlace();
        Assert.True(settings.FastThrowEnabled);
    }
}
