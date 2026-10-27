using Guildsmith.Core.Arguments;
using Guildsmith.Core.Errors;

namespace Guildsmith.Tests.Core.Arguments;

public sealed class GuildArgumentTests
{
    [Fact]
    public void Resolve_gives_the_only_allowed_guild_when_guild_id_is_missing()
    {
        ToolResult<ulong> result = GuildArgument.Resolve(null, new HashSet<ulong> { 111 });

        Assert.True(result.IsSuccess);
        Assert.Equal(111UL, result.Value);
    }

    [Fact]
    public void Resolve_gives_invalid_argument_listing_every_allowed_guild_when_guild_id_is_missing_among_several()
    {
        // 9, 10 and 200 sort differently as numbers than as strings ("10", "200", "9").
        ToolResult<ulong> result = GuildArgument.Resolve(null, new HashSet<ulong> { 200, 9, 10 });

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidArgument, result.Error.Code);
        Assert.Equal(["9", "10", "200"], result.Error.ValidValues);
    }

    [Fact]
    public void Resolve_gives_invalid_argument_with_no_valid_values_when_no_guild_is_allowed()
    {
        ToolResult<ulong> result = GuildArgument.Resolve(null, new HashSet<ulong>());

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidArgument, result.Error.Code);
        Assert.Equal([], result.Error.ValidValues!);
    }

    [Fact]
    public void Resolve_parses_a_supplied_guild_id_even_when_it_is_not_allowed()
    {
        ToolResult<ulong> result = GuildArgument.Resolve("222", new HashSet<ulong> { 111 });

        Assert.True(result.IsSuccess);
        Assert.Equal(222UL, result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("0")]
    public void Resolve_gives_invalid_argument_for_a_supplied_guild_id_that_is_not_a_snowflake(string guildId)
    {
        // An empty string is a supplied value, so it is not replaced by the only allowed guild.
        ToolResult<ulong> result = GuildArgument.Resolve(guildId, new HashSet<ulong> { 111 });

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidArgument, result.Error.Code);
    }

    [Fact]
    public void Resolve_rejects_a_null_allowlist()
    {
        Assert.Throws<ArgumentNullException>(() => GuildArgument.Resolve("1", null!));
    }
}
