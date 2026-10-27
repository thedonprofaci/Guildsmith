using Guildsmith.Core.Errors;
using Guildsmith.Core.Guards;

namespace Guildsmith.Tests.Core.Guards;

public sealed class DestructiveGateTests
{
    [Theory]
    [InlineData(ToolLevel.Destructive)]
    [InlineData((ToolLevel)99)]
    public void Check_refuses_destructive_and_undefined_levels_while_destructive_tools_are_disabled(ToolLevel level)
    {
        ToolError? error = DestructiveGate.Check(level, allowDestructive: false);

        Assert.NotNull(error);
        Assert.Equal(ErrorCode.DestructiveDisabled, error.Code);
    }

    [Theory]
    [InlineData(ToolLevel.Read)]
    [InlineData(ToolLevel.Write)]
    public void Check_passes_read_and_write_while_destructive_tools_are_disabled(ToolLevel level)
    {
        Assert.Null(DestructiveGate.Check(level, allowDestructive: false));
    }

    [Fact]
    public void Check_passes_destructive_when_destructive_tools_are_allowed()
    {
        Assert.Null(DestructiveGate.Check(ToolLevel.Destructive, allowDestructive: true));
    }
}
