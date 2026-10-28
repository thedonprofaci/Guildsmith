using Guildsmith.Core.Errors;
using Guildsmith.Core.Gateway;

namespace Guildsmith.Tests.Core.Gateway;

public sealed class DiscordFailureExceptionTests
{
    [Fact]
    public void Constructor_carries_the_error_and_the_inner_exception()
    {
        ToolError error = new(ErrorCode.NotFound, "error-marker");
        InvalidOperationException inner = new("inner-marker");

        DiscordFailureException exception = new(error, inner);

        Assert.Same(error, exception.Error);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal("error-marker", exception.Message);
    }

    [Fact]
    public void Constructor_rejects_a_null_error()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => new DiscordFailureException(null!, null));

        Assert.Equal("error", exception.ParamName);
    }

    [Fact]
    public void Constructor_rejects_an_internal_error()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new DiscordFailureException(ToolError.Internal("abc"), null));

        Assert.Equal("error", exception.ParamName);
    }
}
