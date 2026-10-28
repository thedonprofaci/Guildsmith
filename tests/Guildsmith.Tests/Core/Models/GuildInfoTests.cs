using Guildsmith.Core.Models;

namespace Guildsmith.Tests.Core.Models;

public sealed class GuildInfoTests
{
    [Fact]
    public void ToJson_writes_a_compact_object_with_the_id_as_a_string_and_each_count_under_its_own_name()
    {
        // Three different counts, so a swap between two of them changes the text.
        GuildInfo info = new(18446744073709551615, "Guild", 42, 7, 3);

        Assert.Equal(
            """{"id":"18446744073709551615","name":"Guild","member_count":42,"channel_count":7,"role_count":3}""",
            info.ToJson());
    }
}
