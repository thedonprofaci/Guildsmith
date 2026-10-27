using Guildsmith.Core.Guards;

namespace Guildsmith.Tests.Core.Guards;

public sealed class ToolTargetTests
{
    [Fact]
    public void Guild_holds_only_the_guild_id()
    {
        ToolTarget target = ToolTarget.Guild("1");

        Assert.Equal(ToolTargetKind.Guild, target.Kind);
        Assert.Equal(("1", null, null, null), Ids(target));
    }

    [Fact]
    public void Channel_holds_the_channel_id_and_the_optional_guild_id()
    {
        ToolTarget target = ToolTarget.Channel("2", "1");

        Assert.Equal(ToolTargetKind.Channel, target.Kind);
        Assert.Equal(("1", "2", null, null), Ids(target));
    }

    [Fact]
    public void Role_holds_the_role_id_and_the_optional_guild_id()
    {
        ToolTarget target = ToolTarget.Role("3", "1");

        Assert.Equal(ToolTargetKind.Role, target.Kind);
        Assert.Equal(("1", null, "3", null), Ids(target));
    }

    [Fact]
    public void Message_holds_the_channel_id_the_message_id_and_the_optional_guild_id()
    {
        ToolTarget target = ToolTarget.Message("2", "4", "1");

        Assert.Equal(ToolTargetKind.Message, target.Kind);
        Assert.Equal(("1", "2", null, "4"), Ids(target));
    }

    private static (string?, string?, string?, string?) Ids(ToolTarget target) =>
        (target.GuildId, target.ChannelId, target.RoleId, target.MessageId);
}
