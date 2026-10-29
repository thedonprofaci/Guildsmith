using Guildsmith.Core.Arguments;
using Guildsmith.Core.Errors;
using Guildsmith.Core.Models;

namespace Guildsmith.Tests.Core.Arguments;

public sealed class ChannelTypeArgumentTests
{
    [Fact]
    public void Name_is_type()
    {
        Assert.Equal("type", ChannelTypeArgument.Name);
    }

    [Fact]
    public void ValidValues_are_the_five_types_of_spec_section_6_4_in_its_order()
    {
        Assert.Equal(["text", "voice", "category", "announcement", "forum"], ChannelTypeArgument.ValidValues);
    }

    [Theory]
    [InlineData("text", ChannelKind.Text)]
    [InlineData("voice", ChannelKind.Voice)]
    [InlineData("category", ChannelKind.Category)]
    [InlineData("announcement", ChannelKind.Announcement)]
    [InlineData("forum", ChannelKind.Forum)]
    public void Parse_reads_each_type_name_as_its_kind(string value, ChannelKind expected)
    {
        Assert.Equal(expected, ChannelTypeArgument.Parse(value).Value);
    }

    [Theory]
    [InlineData("Text")]
    [InlineData(" text")]
    [InlineData("news")]
    [InlineData("stage")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_rejects_anything_else_with_invalid_argument_listing_the_five_types(string? value)
    {
        ToolResult<ChannelKind> result = ChannelTypeArgument.Parse(value);

        Assert.Equal(ErrorCode.InvalidArgument, result.Error!.Code);
        Assert.Equal(["text", "voice", "category", "announcement", "forum"], result.Error.ValidValues);
    }

    [Theory]
    [InlineData(ChannelKind.Text, "text")]
    [InlineData(ChannelKind.Voice, "voice")]
    [InlineData(ChannelKind.Category, "category")]
    [InlineData(ChannelKind.Announcement, "announcement")]
    [InlineData(ChannelKind.Forum, "forum")]
    public void Format_writes_each_kind_as_its_type_name(ChannelKind kind, string expected)
    {
        Assert.Equal(expected, ChannelTypeArgument.Format(kind));
    }

    [Fact]
    public void Format_rejects_an_undefined_kind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChannelTypeArgument.Format((ChannelKind)99));
    }
}
