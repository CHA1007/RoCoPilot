using System.IO;
using RocoPilot.Handpan;
using RocoPilot.Settings;

namespace RocoPilot.Tools.Handpan.Tests;

public class HandpanSettingsTests
{
    [Fact]
    public void The_defaults_are_the_measured_constants()
    {
        var settings = new HandpanSettings();

        Assert.Equal(0, settings.Transpose);
        Assert.Equal(0, settings.GapBeats);
        Assert.Equal(0, settings.BpmOverride);
        Assert.False(settings.Loop);
        Assert.Equal(3, settings.CountdownSeconds);
        Assert.Equal(50, settings.HoldMs);
        Assert.Equal(60, settings.ChordHoldMs);
        Assert.Equal(12, settings.ChordStaggerMs);
        Assert.Equal(string.Empty, settings.ScoreName);
        Assert.Equal(HandpanMode.Play, settings.Mode);
    }

    [Fact]
    public void The_key_map_defaults_to_ding_and_the_ring()
    {
        var keyMap = new HandpanSettings().ToKeyMap();

        Assert.Equal(KeyMap.DefaultEntries.Count, keyMap.MidiNotes.Count);
        Assert.True(keyMap.TryGet(57, out var ding) && ding == "B");
        Assert.Empty(keyMap.InvalidEntries);
    }

    [Fact]
    public void Timing_converts_milliseconds_to_seconds()
    {
        var settings = new HandpanSettings { HoldMs = 80, ChordHoldMs = 90, ChordStaggerMs = 20 };

        Assert.Equal(new PlaybackTiming(0.08, 0.09, 0.02), settings.ToTiming());
    }

    [Fact]
    public void The_arrangement_carries_every_fitting_knob()
    {
        var settings = new HandpanSettings
        {
            Transpose = -3,
            GapBeats = 0.25,
            BpmOverride = 96,
            HoldMs = 70,
        };

        var options = settings.ToArrangement();

        Assert.Equal(-3, options.Transpose);
        Assert.Equal(0.25, options.GapBeats);
        Assert.Equal(96, options.BpmOverride);
        Assert.Equal(0.07, options.Timing!.HoldSeconds);
    }

    [Fact]
    public void The_mode_is_a_launch_parameter_and_never_persists()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rocopilot-handpan-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonSettingsStore(path);
            store.Load();
            store.SetToolSettings(HandpanTool.ToolId, new HandpanSettings
            {
                Mode = HandpanMode.Probe,
                ScoreName = "晴天",
            });
            store.Save();

            var reloaded = new JsonSettingsStore(path);
            reloaded.Load();
            var restored = Assert.IsType<HandpanSettings>(reloaded.GetToolSettings(
                HandpanTool.ToolId, typeof(HandpanSettings), () => new HandpanSettings()));

            Assert.Equal(HandpanMode.Play, restored.Mode);
            Assert.Equal("晴天", restored.ScoreName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Sanitizing_trims_the_score_name_and_keeps_the_key_map()
    {
        var settings = new HandpanSettings { ScoreName = "  晴天  " };

        settings.SanitizeInPlace();

        Assert.Equal("晴天", settings.ScoreName);
        Assert.Equal(KeyMap.DefaultEntries.Count, settings.KeyMapEntries.Count);
    }

    [Fact]
    public void Sanitizing_restores_the_default_key_map_when_it_is_unusable()
    {
        var emptied = new HandpanSettings { KeyMapEntries = [] };
        var nulled = new HandpanSettings { KeyMapEntries = null! };

        emptied.SanitizeInPlace();
        nulled.SanitizeInPlace();

        Assert.Equal(KeyMap.DefaultEntries.Count, emptied.KeyMapEntries.Count);
        Assert.Equal(KeyMap.DefaultEntries.Count, nulled.KeyMapEntries.Count);
    }

    [Fact]
    public void Sanitizing_keeps_the_first_entry_per_note()
    {
        var settings = new HandpanSettings
        {
            KeyMapEntries =
            [
                new KeyMapEntry("C5", "T"),
                new KeyMapEntry("c5", "Y"),
                null!,
            ],
        };

        settings.SanitizeInPlace();

        Assert.Equal(new KeyMapEntry("C5", "T"), Assert.Single(settings.KeyMapEntries));
    }

    [Theory]
    [InlineData(-40, -11)]
    [InlineData(40, 11)]
    [InlineData(5, 5)]
    public void Sanitizing_clamps_the_transposition(int transpose, int expected)
    {
        var settings = new HandpanSettings { Transpose = transpose };

        settings.SanitizeInPlace();

        Assert.Equal(expected, settings.Transpose);
    }

    [Theory]
    [InlineData(nameof(HandpanSettings.HoldMs), 5000, 500)]
    [InlineData(nameof(HandpanSettings.HoldMs), 1, 20)]
    [InlineData(nameof(HandpanSettings.ChordHoldMs), 5000, 500)]
    [InlineData(nameof(HandpanSettings.ChordStaggerMs), 5000, 200)]
    [InlineData(nameof(HandpanSettings.CountdownSeconds), 99, 10)]
    [InlineData(nameof(HandpanSettings.CountdownSeconds), -9, 0)]
    public void Sanitizing_clamps_the_playback_knobs(string property, int value, int expected)
    {
        var settings = new HandpanSettings();
        settings.GetType().GetProperty(property)!.SetValue(settings, value);

        settings.SanitizeInPlace();

        Assert.Equal(expected, settings.GetType().GetProperty(property)!.GetValue(settings));
    }

    [Fact]
    public void Sanitizing_clamps_the_beat_knobs()
    {
        var settings = new HandpanSettings { GapBeats = 40, BpmOverride = 1e6 };

        settings.SanitizeInPlace();

        Assert.Equal(4, settings.GapBeats);
        Assert.Equal(600, settings.BpmOverride);
    }

    [Fact]
    public void Sanitizing_replaces_broken_numbers_with_the_defaults()
    {
        var settings = new HandpanSettings { GapBeats = double.NaN, BpmOverride = double.PositiveInfinity };

        settings.SanitizeInPlace();

        Assert.Equal(0, settings.GapBeats);
        Assert.Equal(0, settings.BpmOverride);
    }
}
