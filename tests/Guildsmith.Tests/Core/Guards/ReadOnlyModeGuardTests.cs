using Guildsmith.Core.Configuration;
using Guildsmith.Core.Errors;
using Guildsmith.Core.Guards;

namespace Guildsmith.Tests.Core.Guards;

public sealed class ReadOnlyModeGuardTests
{
    [Theory]
    [InlineData(ToolLevel.Write)]
    [InlineData(ToolLevel.Destructive)]
    [InlineData((ToolLevel)99)]
    public void Check_refuses_every_level_but_read_in_read_only_mode(ToolLevel level)
    {
        ToolError? error = ReadOnlyModeGuard.Check(level, DiscordMcpMode.ReadOnly);

        Assert.NotNull(error);
        Assert.Equal(ErrorCode.ReadOnlyMode, error.Code);
    }

    [Fact]
    public void Check_passes_read_in_read_only_mode()
    {
        Assert.Null(ReadOnlyModeGuard.Check(ToolLevel.Read, DiscordMcpMode.ReadOnly));
    }

    [Theory]
    [InlineData(ToolLevel.Read)]
    [InlineData(ToolLevel.Write)]
    [InlineData(ToolLevel.Destructive)]
    public void Check_passes_every_level_in_read_write_mode(ToolLevel level)
    {
        Assert.Null(ReadOnlyModeGuard.Check(level, DiscordMcpMode.ReadWrite));
    }

    [Fact]
    public void Check_refuses_write_in_an_undefined_mode()
    {
        ToolError? error = ReadOnlyModeGuard.Check(ToolLevel.Write, (DiscordMcpMode)99);

        Assert.NotNull(error);
        Assert.Equal(ErrorCode.ReadOnlyMode, error.Code);
    }
}
