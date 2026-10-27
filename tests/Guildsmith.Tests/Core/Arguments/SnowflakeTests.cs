using Guildsmith.Core.Arguments;
using Guildsmith.Core.Errors;

namespace Guildsmith.Tests.Core.Arguments;

public sealed class SnowflakeTests
{
    [Theory]
    [InlineData("1", 1UL)]
    [InlineData("123456789012345678", 123456789012345678UL)]
    [InlineData("18446744073709551615", ulong.MaxValue)]
    public void TryParse_accepts_a_positive_decimal_ulong(string text, ulong expected)
    {
        Assert.True(Snowflake.TryParse(text, out ulong id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12a")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1,000")]
    [InlineData("0")]
    [InlineData("18446744073709551616")]
    public void TryParse_rejects_anything_but_a_positive_decimal_ulong(string? text)
    {
        Assert.False(Snowflake.TryParse(text, out _));
    }

    [Theory]
    [InlineData(1UL, "1")]
    [InlineData(123456789012345678UL, "123456789012345678")]
    [InlineData(ulong.MaxValue, "18446744073709551615")]
    public void Format_writes_the_id_as_its_decimal_string(ulong id, string expected)
    {
        Assert.Equal(expected, Snowflake.Format(id));
    }

    [Fact]
    public void ParseArgument_returns_the_parsed_id()
    {
        ToolResult<ulong> result = Snowflake.ParseArgument("987654321098765432", "channel_id");

        Assert.True(result.IsSuccess);
        Assert.Equal(987654321098765432UL, result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1,000")]
    [InlineData("0")]
    [InlineData("18446744073709551616")]
    public void ParseArgument_gives_invalid_argument_without_valid_values_for_a_non_snowflake(string? value)
    {
        ToolResult<ulong> result = Snowflake.ParseArgument(value, "channel_id");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidArgument, result.Error.Code);
        Assert.Null(result.Error.ValidValues);
    }
}
