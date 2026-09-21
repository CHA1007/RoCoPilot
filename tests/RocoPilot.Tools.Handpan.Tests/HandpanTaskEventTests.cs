using RocoPilot.Handpan;

namespace RocoPilot.Tools.Handpan.Tests;

public class HandpanTaskEventTests
{
    private static IReadOnlyList<HandpanTaskEvent> All() =>
    [
        new HandpanArranged(3, 1, -2, 0.75, 12.5),
        new HandpanCountdown(2),
        new HandpanPaused(PauseSource.Manual),
        new HandpanPaused(PauseSource.FocusLost),
        new HandpanResumed(PauseSource.Manual),
        new HandpanResumed(PauseSource.FocusLost),
        new HandpanRoundCompleted(1),
        new HandpanProbeKey("B", "A3"),
        new HandpanProbeCompleted(9),
        new HandpanCompleted(),
        new HandpanFaulted("注入失败", "装驱动"),
    ];

    public static IEnumerable<object[]> EventCases() => All().Select(taskEvent => new object[] { taskEvent });

    [Theory]
    [MemberData(nameof(EventCases))]
    public void An_event_travels_as_its_own_payload(HandpanTaskEvent taskEvent)
    {
        var toolEvent = taskEvent.AsToolEvent();

        Assert.Equal(taskEvent.Name, toolEvent.Name);
        Assert.Same(taskEvent, toolEvent.Payload);
        Assert.Null(toolEvent.Data);
    }

    [Fact]
    public void Event_names_are_prefixed_and_stable()
    {
        var names = All().Select(taskEvent => taskEvent.Name).Distinct().ToList();

        Assert.All(names, name => Assert.StartsWith("handpan_", name, StringComparison.Ordinal));
        Assert.Equal(
            ["handpan_arranged", "handpan_countdown", "handpan_paused", "handpan_resumed",
                "handpan_round_completed", "handpan_probe_key", "handpan_probe_completed",
                "handpan_completed", "handpan_faulted"],
            names);
    }

    [Fact]
    public void A_fault_carries_its_error_and_remedy()
    {
        var fault = new HandpanFaulted("注入失败", "装驱动");

        Assert.Equal("注入失败", fault.Error);
        Assert.Equal("装驱动", fault.Remedy);
        Assert.Null(new HandpanFaulted("注入失败").Remedy);
        Assert.Equal("handpan_faulted", fault.Name);
    }

    [Fact]
    public void A_pause_names_its_source()
    {
        Assert.Equal(PauseSource.FocusLost, new HandpanPaused(PauseSource.FocusLost).Source);
        Assert.Equal("handpan_paused", new HandpanPaused(PauseSource.Manual).Name);
        Assert.Equal("handpan_resumed", new HandpanResumed(PauseSource.Manual).Name);
    }

    [Fact]
    public void A_probe_key_pairs_the_key_with_the_note_it_should_sound()
    {
        var probeKey = new HandpanProbeKey("B", "A3");

        Assert.Equal("B", probeKey.Key);
        Assert.Equal("A3", probeKey.Note);
        Assert.Equal("handpan_probe_key", probeKey.Name);
    }

    [Fact]
    public void A_probe_completion_reports_how_many_keys_were_probed()
    {
        var completed = new HandpanProbeCompleted(9);

        Assert.Equal(9, completed.KeyCount);
        Assert.Equal("handpan_probe_completed", completed.Name);
    }
}
