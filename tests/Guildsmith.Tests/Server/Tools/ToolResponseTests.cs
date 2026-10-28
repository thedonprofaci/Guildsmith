using Guildsmith.Core.Errors;
using Guildsmith.Server.Tools;

using ModelContextProtocol.Protocol;

namespace Guildsmith.Tests.Server.Tools;

public sealed class ToolResponseTests
{
    [Fact]
    public void From_writes_a_success_as_one_text_block_of_its_json_without_the_error_flag()
    {
        CallToolResult result = ToolResponse.From(ToolResult<int>.Success(7), value => $"json-{value}");

        Assert.Equal("json-7", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.False(result.IsError);
    }

    [Fact]
    public void From_writes_a_failure_as_one_text_block_of_the_unified_error_response_with_the_error_flag()
    {
        bool serialized = false;

        CallToolResult result = ToolResponse.From(
            ToolResult<int>.Failure(new ToolError(ErrorCode.ForbiddenGuild, "Guild 9 is not in ALLOWED_GUILD_IDS.")),
            _ =>
            {
                serialized = true;
                return "value";
            });

        Assert.Equal(
            """{"error":"forbidden_guild","message":"Guild 9 is not in ALLOWED_GUILD_IDS."}""",
            Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.True(result.IsError);
        Assert.False(serialized);
    }

    [Fact]
    public void From_rejects_a_null_result()
    {
        Assert.Throws<ArgumentNullException>(() => ToolResponse.From<int>(null!, value => ""));
    }

    [Fact]
    public void From_rejects_a_null_serializer()
    {
        Assert.Throws<ArgumentNullException>(() => ToolResponse.From(ToolResult<int>.Success(1), null!));
    }
}
