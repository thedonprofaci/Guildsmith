using Guildsmith.Core.Errors;

namespace Guildsmith.Tests.Core.Errors;

public sealed class ToolResultTests
{
    [Fact]
    public void Success_holds_the_value_and_no_error()
    {
        ToolResult<ulong> result = ToolResult<ulong>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42UL, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_holds_the_error_and_the_default_value()
    {
        ToolError error = new(ErrorCode.NotFound, "gone.");

        ToolResult<string> result = ToolResult<string>.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Failure_rejects_a_null_error()
    {
        Assert.Throws<ArgumentNullException>(() => ToolResult<string>.Failure(null!));
    }
}
