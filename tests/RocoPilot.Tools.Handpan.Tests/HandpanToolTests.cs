using RocoPilot.Core;
using RocoPilot.ToolUi;
using Wpf.Ui.Controls;

namespace RocoPilot.Tools.Handpan.Tests;

public class HandpanToolTests
{
    [Fact]
    public void The_tool_is_the_handpan()
    {
        var tool = new HandpanTool();

        Assert.Equal("handpan", tool.Id);
        Assert.Equal(HandpanTool.ToolId, tool.Id);
        Assert.Equal(typeof(HandpanSettings), tool.SettingsType);
        Assert.IsType<HandpanSettings>(tool.CreateDefaultSettings());
    }

    [Fact]
    public void The_tool_exposes_a_ui_surface()
    {
        var tool = new HandpanTool();

        var ui = Assert.IsAssignableFrom<IToolUi>(tool);
        Assert.Equal(SymbolRegular.MusicNote224, ui.Icon);
    }

    [Fact]
    public void Running_builds_a_task_for_the_tool()
    {
        var tool = new HandpanTool(() => new RecordingDriver());

        var launched = tool.Run(new HandpanSettings());
        using var task = Assert.IsType<HandpanRunningTask>(launched);

        Assert.Equal(HandpanTool.ToolId, task.ToolId);
        Assert.Equal(TaskState.Idle, task.State);
    }

    [Fact]
    public void Running_rejects_foreign_settings()
    {
        var tool = new HandpanTool(() => new RecordingDriver());

        Assert.Throws<ArgumentException>(() => tool.Run(new object()));
        Assert.Throws<ArgumentNullException>(() => tool.Run(null!));
    }
}
