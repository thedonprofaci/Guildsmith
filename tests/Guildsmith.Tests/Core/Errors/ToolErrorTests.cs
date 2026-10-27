using Guildsmith.Core.Errors;

namespace Guildsmith.Tests.Core.Errors;

public sealed class ToolErrorTests
{
    [Fact]
    public void Constructor_keeps_the_code_and_the_message_and_carries_no_optional_field()
    {
        ToolError error = new(ErrorCode.NotFound, "Channel 5 was not found.");

        Assert.Equal(ErrorCode.NotFound, error.Code);
        Assert.Equal("Channel 5 was not found.", error.Message);
        Assert.Null(error.ValidValues);
        Assert.Null(error.RetryAfterSeconds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_a_missing_or_blank_message(string? message)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ToolError(ErrorCode.NotFound, message!));
    }

    [Fact]
    public void InvalidArgument_carries_the_valid_values_in_the_order_given()
    {
        ToolError error = ToolError.InvalidArgument("guild_id is required.", ["30", "4", "100"]);

        Assert.Equal(ErrorCode.InvalidArgument, error.Code);
        Assert.Equal(["30", "4", "100"], error.ValidValues);
        Assert.Null(error.RetryAfterSeconds);
    }

    [Fact]
    public void InvalidArgument_copies_the_valid_values_so_a_later_change_to_the_source_does_not_reach_it()
    {
        List<string> source = ["1", "2"];
        ToolError error = ToolError.InvalidArgument("pick one.", source);

        source.Add("3");

        Assert.Equal(["1", "2"], error.ValidValues);
    }

    [Fact]
    public void InvalidArgument_rejects_null_valid_values()
    {
        Assert.Throws<ArgumentNullException>(() => ToolError.InvalidArgument("pick one.", null!));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(2.5)]
    public void RateLimited_carries_the_wait_in_seconds(double seconds)
    {
        ToolError error = ToolError.RateLimited("Slow down.", seconds);

        Assert.Equal(ErrorCode.RateLimited, error.Code);
        Assert.Equal(seconds, error.RetryAfterSeconds);
        Assert.Null(error.ValidValues);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RateLimited_rejects_a_negative_or_non_finite_wait(double seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ToolError.RateLimited("Slow down.", seconds));
    }

    [Fact]
    public void Constructor_carries_no_correlation_id()
    {
        Assert.Null(new ToolError(ErrorCode.NotFound, "Channel 5 was not found.").CorrelationId);
    }

    [Fact]
    public void Constructor_rejects_internal_error_which_needs_a_correlation_id()
    {
        Assert.Throws<ArgumentException>(() => new ToolError(ErrorCode.InternalError, "Something broke."));
    }

    [Fact]
    public void Internal_carries_the_code_the_correlation_id_and_the_fixed_message()
    {
        ToolError error = ToolError.Internal("4f1c0b9e");

        Assert.Equal(ErrorCode.InternalError, error.Code);
        Assert.Equal("4f1c0b9e", error.CorrelationId);
        Assert.Equal(ToolError.InternalErrorMessage, error.Message);
        Assert.Null(error.ValidValues);
        Assert.Null(error.RetryAfterSeconds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Internal_rejects_a_missing_or_blank_correlation_id(string? correlationId)
    {
        Assert.ThrowsAny<ArgumentException>(() => ToolError.Internal(correlationId!));
    }

    [Fact]
    public void InvalidArgument_and_RateLimited_carry_no_correlation_id()
    {
        Assert.Null(ToolError.InvalidArgument("guild_id is required.", ["1"]).CorrelationId);
        Assert.Null(ToolError.RateLimited("Slow down.", 1).CorrelationId);
    }
}
