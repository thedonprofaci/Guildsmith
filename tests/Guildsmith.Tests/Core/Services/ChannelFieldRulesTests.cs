using Guildsmith.Core.Models;
using Guildsmith.Core.Services;

namespace Guildsmith.Tests.Core.Services;

/// <summary>The field table, typed in from Discord.Net 3.20.2's property classes, not read from the rules.</summary>
public sealed class ChannelFieldRulesTests
{
    [Fact]
    public void A_text_channel_carries_every_field()
    {
        Assert.Equal([], ChannelFieldRules.UnsupportedFields(ChannelKind.Text));
    }

    [Fact]
    public void A_voice_channel_carries_no_topic()
    {
        Assert.Equal(["topic"], ChannelFieldRules.UnsupportedFields(ChannelKind.Voice));
    }

    [Fact]
    public void A_category_carries_no_topic_parent_slow_mode_or_nsfw_flag()
    {
        Assert.Equal(["topic", "parent_id", "slowmode_seconds", "nsfw"], ChannelFieldRules.UnsupportedFields(ChannelKind.Category));
    }

    [Fact]
    public void An_announcement_channel_carries_every_field()
    {
        Assert.Equal([], ChannelFieldRules.UnsupportedFields(ChannelKind.Announcement));
    }

    [Fact]
    public void A_forum_channel_carries_no_slow_mode()
    {
        Assert.Equal(["slowmode_seconds"], ChannelFieldRules.UnsupportedFields(ChannelKind.Forum));
    }

    [Fact]
    public void UnsupportedFields_rejects_an_undefined_kind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChannelFieldRules.UnsupportedFields((ChannelKind)99));
    }

    [Fact]
    public void FirstUnsupported_names_the_first_given_field_the_kind_cannot_carry()
    {
        Assert.Equal("parent_id", ChannelFieldRules.FirstUnsupported(ChannelKind.Category, ["position", "parent_id", "nsfw"]));
    }

    [Fact]
    public void FirstUnsupported_is_null_when_the_kind_carries_every_given_field()
    {
        Assert.Null(ChannelFieldRules.FirstUnsupported(ChannelKind.Voice, ["name", "parent_id", "nsfw", "slowmode_seconds"]));
    }

    [Fact]
    public void FirstUnsupported_rejects_null_fields()
    {
        Assert.Throws<ArgumentNullException>(() => ChannelFieldRules.FirstUnsupported(ChannelKind.Text, null!));
    }
}
