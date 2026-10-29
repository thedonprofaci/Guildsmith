using Guildsmith.Core.Models;

namespace Guildsmith.Tests.Core.Models;

public sealed class ChannelInfoTests
{
    [Fact]
    public void ToJson_writes_the_ids_as_strings_and_the_type_by_its_name()
    {
        ChannelInfo channel = new(5001, 111, "general", ChannelKind.Announcement, 900, 3);

        Assert.Equal("""{"id":"5001","name":"general","type":"announcement","parent_id":"900","position":3}""", channel.ToJson());
    }

    [Fact]
    public void ToJson_writes_a_missing_parent_as_null()
    {
        ChannelInfo channel = new(900, 111, "Projektek", ChannelKind.Category, null, 0);

        Assert.Equal("""{"id":"900","name":"Projektek","type":"category","parent_id":null,"position":0}""", channel.ToJson());
    }

    [Fact]
    public void ToJson_writes_a_kind_outside_the_five_as_a_null_type()
    {
        ChannelInfo channel = new(5003, 111, "stage", null, null, 1);

        Assert.Equal("""{"id":"5003","name":"stage","type":null,"parent_id":null,"position":1}""", channel.ToJson());
    }

    [Fact]
    public void ToJson_of_a_list_writes_each_channel_in_the_given_order()
    {
        ChannelInfo[] channels = [new(20, 111, "b", ChannelKind.Voice, 900, 1), new(10, 111, "a", ChannelKind.Text, null, 0)];

        Assert.Equal(
            """[{"id":"20","name":"b","type":"voice","parent_id":"900","position":1},{"id":"10","name":"a","type":"text","parent_id":null,"position":0}]""",
            ChannelInfo.ToJson(channels));
    }

    [Fact]
    public void ToJson_of_an_empty_list_is_an_empty_array()
    {
        Assert.Equal("[]", ChannelInfo.ToJson([]));
    }

    [Fact]
    public void ToJson_of_a_list_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => ChannelInfo.ToJson(null!));
    }
}
