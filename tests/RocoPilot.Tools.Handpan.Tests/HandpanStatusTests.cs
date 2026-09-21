using RocoPilot.Core;
using RocoPilot.Handpan;

namespace RocoPilot.Tools.Handpan.Tests;

public class HandpanStatusTests
{
    [Fact]
    public void An_arrangement_reads_as_playing_with_its_shape()
    {
        Assert.Equal(
            new HandpanStatusView("演奏中", "394 音"),
            HandpanStatus.Of(new HandpanArranged(394, 0, -3, 0.75, 96.53)));
        Assert.Equal(
            new HandpanStatusView("演奏中", "10 音 · 缺音 2 个"),
            HandpanStatus.Of(new HandpanArranged(10, 2, 0, 1, 4.04)));
    }

    [Fact]
    public void A_countdown_reads_as_the_seconds_until_the_start()
    {
        Assert.Equal(
            new HandpanStatusView("3 秒后开始"),
            HandpanStatus.Of(new HandpanCountdown(3)));
    }

    [Fact]
    public void A_pause_is_cautioned_and_names_its_source()
    {
        Assert.Equal(
            new HandpanStatusView("已暂停", "游戏失焦", HandpanStatusLevel.Caution),
            HandpanStatus.Of(new HandpanPaused(PauseSource.FocusLost)));
        Assert.Equal(
            new HandpanStatusView("已暂停", "手动暂停", HandpanStatusLevel.Caution),
            HandpanStatus.Of(new HandpanPaused(PauseSource.Manual)));
    }

    [Fact]
    public void Resuming_and_finishing_a_round_both_read_as_playing()
    {
        Assert.Equal(
            new HandpanStatusView("演奏中"),
            HandpanStatus.Of(new HandpanResumed(PauseSource.FocusLost)));
        Assert.Equal(
            new HandpanStatusView("演奏中"),
            HandpanStatus.Of(new HandpanRoundCompleted(1)));
    }

    [Fact]
    public void Completion_and_faults_have_their_own_headlines()
    {
        Assert.Equal(
            new HandpanStatusView("演奏完成"),
            HandpanStatus.Of(new HandpanCompleted()));
        Assert.Equal(
            new HandpanStatusView("故障", "注入失败。装驱动", HandpanStatusLevel.Critical),
            HandpanStatus.Of(new HandpanFaulted("注入失败", "装驱动")));
        Assert.Equal(
            new HandpanStatusView("故障", "注入失败", HandpanStatusLevel.Critical),
            HandpanStatus.Of(new HandpanFaulted("注入失败")));
    }

    [Fact]
    public void A_probe_key_reads_as_the_key_with_the_note_it_should_sound()
    {
        Assert.Equal(
            new HandpanStatusView("B", "应发 A3"),
            HandpanStatus.Of(new HandpanProbeKey("B", "A3")));
        Assert.Equal(
            new HandpanStatusView("自检完成", "9 个键"),
            HandpanStatus.Of(new HandpanProbeCompleted(9)));
    }

    [Fact]
    public void An_arming_step_reads_as_the_step_in_progress()
    {
        var step = new ToolEvent(Arming.StepEvent, new Dictionary<string, object?>
        {
            ["step"] = "激活游戏窗口",
            ["hint"] = "把《洛克王国：世界》切到前台",
        });

        Assert.Equal(
            new HandpanStatusView("正在激活游戏窗口", "把《洛克王国：世界》切到前台"),
            HandpanStatus.ArmingStep(step));
        Assert.Equal(
            new HandpanStatusView("准备中"),
            HandpanStatus.ArmingStep(new ToolEvent(Arming.StepEvent)));
    }

    [Fact]
    public void An_arming_failure_is_critical_with_the_error_and_remedy()
    {
        var failed = new ToolEvent(Arming.FailedEvent, new Dictionary<string, object?>
        {
            ["step"] = "挂载输入驱动",
            ["error"] = "驱动不可用",
            ["remedy"] = "以管理员身份运行安装器",
        });

        Assert.Equal(
            new HandpanStatusView("启动失败", "驱动不可用。以管理员身份运行安装器", HandpanStatusLevel.Critical),
            HandpanStatus.ArmingFailed(failed));
        Assert.Equal(
            new HandpanStatusView("启动失败", "驱动不可用", HandpanStatusLevel.Critical),
            HandpanStatus.ArmingFailed(new ToolEvent(Arming.FailedEvent, new Dictionary<string, object?> { ["error"] = "驱动不可用" })));
        Assert.Equal(
            new HandpanStatusView("启动失败", null, HandpanStatusLevel.Critical),
            HandpanStatus.ArmingFailed(new ToolEvent(Arming.FailedEvent)));
    }

    [Fact]
    public void An_imported_score_reads_as_imported()
    {
        Assert.Equal(
            new HandpanStatusView("已导入曲谱"),
            HandpanStatus.Imported());
    }
}
