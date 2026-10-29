using Guildsmith.Core.Models;

namespace Guildsmith.Tests.Core.Models;

public sealed class GuildSummaryTests
{
    [Fact]
    public void ToJson_writes_each_guild_as_a_compact_object_with_the_id_as_a_string_in_the_given_order()
    {
        // 2^64 - 1: an ID no JavaScript number holds exactly, so only a string carries it.
        GuildSummary[] guilds = [new(18446744073709551615, "Zeta"), new(111, "Alpha")];

        Assert.Equal(
            """[{"id":"18446744073709551615","name":"Zeta"},{"id":"111","name":"Alpha"}]""",
            GuildSummary.ToJson(guilds));
    }

    [Fact]
    public void ToJson_writes_an_empty_array_for_no_guilds()
    {
        Assert.Equal("[]", GuildSummary.ToJson([]));
    }

    [Fact]
    public void ToJson_rejects_a_null_list()
    {
        Assert.Throws<ArgumentNullException>(() => GuildSummary.ToJson(null!));
    }
}
