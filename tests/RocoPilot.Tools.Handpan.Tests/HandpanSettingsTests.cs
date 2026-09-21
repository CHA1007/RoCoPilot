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
        Assert.Equal(0, settings.MinIntervalMs);
        Assert.False(settings.SpeedByPercent);
        Assert.Equal(100, settings.SpeedPercent);
        Assert.Equal(0, settings.BpmOverride);
        Assert.False(settings.Loop);
        Assert.Equal(3, settings.CountdownSeconds);
        Assert.Equal(50, settings.HoldMs);
        Assert.Equal(60, settings.ChordHoldMs);
        Assert.Equal(12, settings.ChordStaggerMs);
        Assert.Equal(0, settings.RestrikeIntervalBeats);
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
        var settings = new HandpanSettings
        {
            HoldMs = 80,
            ChordHoldMs = 90,
            ChordStaggerMs = 20,
            RestrikeIntervalBeats = 1.5,
        };

        Assert.Equal(new PlaybackTiming(0.08, 0.09, 0.02, 1.5), settings.ToTiming());
    }

    [Fact]
    public void The_arrangement_carries_every_fitting_knob()
    {
        var settings = new HandpanSettings
        {
            Transpose = -3,
            MinIntervalMs = 250,
            SpeedByPercent = true,
            SpeedPercent = 85,
            HoldMs = 70,
        };

        var options = settings.ToArrangement();

        Assert.Equal(-3, options.Transpose);
        Assert.Equal(0.25, options.MinIntervalSeconds);
        Assert.Equal(0, options.BpmOverride);
        Assert.Equal(85, options.SpeedPercent);
        Assert.Equal(0.07, options.Timing!.HoldSeconds);
    }

    [Fact]
    public void The_absolute_speed_mode_ignores_the_percent()
    {
        var settings = new HandpanSettings { BpmOverride = 96, SpeedPercent = 85 };

        var options = settings.ToArrangement();

        Assert.Equal(96, options.BpmOverride);
        Assert.Equal(100, options.SpeedPercent);
    }

    [Fact]
    public void Switching_to_percent_converts_the_override()
    {
        var settings = new HandpanSettings { BpmOverride = 96 };

        Assert.Equal(80, settings.SpeedPercentFor(120));
        Assert.Equal(150, settings.SpeedPercentFor(64));
    }

    [Fact]
    public void Switching_to_percent_without_an_override_reads_full_speed()
    {
        var settings = new HandpanSettings { BpmOverride = 0 };

        Assert.Equal(100, settings.SpeedPercentFor(120));
        Assert.Equal(100, settings.SpeedPercentFor(0));
    }

    [Fact]
    public void Switching_to_bpm_converts_the_percent()
    {
        var settings = new HandpanSettings { SpeedPercent = 80 };

        Assert.Equal(96, settings.BpmOverrideFor(120));
    }

    [Fact]
    public void Switching_to_bpm_at_full_speed_follows_the_score()
    {
        var settings = new HandpanSettings { SpeedPercent = 100 };

        Assert.Equal(0, settings.BpmOverrideFor(120));
        Assert.Equal(0, settings.BpmOverrideFor(0));
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
    public void An_unknown_score_reads_the_default_profile()
    {
        var settings = new HandpanSettings();

        Assert.Equal(new ScoreProfile(), settings.ProfileOf("晴天"));
        Assert.Equal(new ScoreProfile(), settings.ProfileOf(null));
        Assert.Equal(new ScoreProfile(), settings.ProfileOf("  "));
    }

    [Fact]
    public void Saving_a_profile_round_trips_the_knobs()
    {
        var settings = new HandpanSettings
        {
            ScoreName = "晴天",
            Transpose = 2,
            MinIntervalMs = 120,
            RestrikeIntervalBeats = 2,
            SpeedByPercent = true,
            SpeedPercent = 85,
        };

        settings.SaveProfile(settings.ScoreName);
        settings.Transpose = -5;
        settings.RestrikeIntervalBeats = 0.5;
        settings.SpeedPercent = 100;

        var profile = settings.ProfileOf("晴天");
        Assert.Equal(2, profile.Transpose);
        Assert.Equal(120, profile.MinIntervalMs);
        Assert.Equal(2, profile.RestrikeIntervalBeats);
        Assert.True(profile.SpeedByPercent);
        Assert.Equal(85, profile.SpeedPercent);
    }

    [Fact]
    public void Saving_without_a_score_name_keeps_the_map_empty()
    {
        var settings = new HandpanSettings { Transpose = 2 };

        settings.SaveProfile("");
        settings.SaveProfile(null);

        Assert.Empty(settings.ScoreProfiles);
    }

    [Fact]
    public void Sanitizing_clamps_profile_values_and_drops_broken_entries()
    {
        var settings = new HandpanSettings
        {
            ScoreProfiles = new Dictionary<string, ScoreProfile>
            {
                ["晴天"] = new(40, 600, 12, true, 999, 1e6),
                ["  "] = new(),
            },
        };

        settings.SanitizeInPlace();

        var profile = Assert.Single(settings.ScoreProfiles);
        Assert.Equal(new ScoreProfile(11, 300, 8, true, 200, 240), profile.Value);
    }

    [Fact]
    public void Sanitizing_clamps_the_speed_and_spacing_knobs()
    {
        var settings = new HandpanSettings
        {
            MinIntervalMs = 600,
            BpmOverride = 1e6,
            SpeedPercent = 10,
        };

        settings.SanitizeInPlace();

        Assert.Equal(300, settings.MinIntervalMs);
        Assert.Equal(240, settings.BpmOverride);
        Assert.Equal(50, settings.SpeedPercent);
    }

    [Fact]
    public void Sanitizing_caps_the_percent_from_above()
    {
        var settings = new HandpanSettings { SpeedPercent = 999 };

        settings.SanitizeInPlace();

        Assert.Equal(200, settings.SpeedPercent);
    }

    [Fact]
    public void Sanitizing_replaces_broken_numbers_with_the_defaults()
    {
        var settings = new HandpanSettings { BpmOverride = double.NaN, SpeedPercent = double.PositiveInfinity };

        settings.SanitizeInPlace();

        Assert.Equal(0, settings.BpmOverride);
        Assert.Equal(100, settings.SpeedPercent);
    }
}
