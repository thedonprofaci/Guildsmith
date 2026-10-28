using System.Reflection;

using Guildsmith.Core.Audit;
using Guildsmith.Core.Errors;
using Guildsmith.Core.Guards;

namespace Guildsmith.Tests.Core.Audit;

public sealed class AuditEntryTests
{
    // Every expected string below is typed in, not built from AuditEntry or AuditOutcome.

    [Fact]
    public void Render_writes_a_success_that_knows_only_the_guild()
    {
        string entry = AuditEntry.Render("create_channel", ToolLevel.Write, 111, null, null, null, AuditOutcome.Success);

        Assert.Equal("tool=create_channel level=write guild=111 outcome=success", entry);
    }

    [Fact]
    public void Render_writes_a_success_with_a_channel_and_a_message()
    {
        string entry = AuditEntry.Render("delete_message", ToolLevel.Destructive, 111, 5001, null, 7001, AuditOutcome.Success);

        Assert.Equal("tool=delete_message level=destructive guild=111 channel=5001 message=7001 outcome=success", entry);
    }

    [Fact]
    public void Render_writes_every_id_in_the_order_guild_channel_role_message()
    {
        // Four distinct values, so a swap of any two adjacent slots changes the string. Without
        // this case swapping the channel and role lines, or the role and message lines, left the
        // whole suite green: no other case passes those pairs together.
        string entry = AuditEntry.Render("set_channel_permissions", ToolLevel.Write, 111, 5001, 6001, 7001, AuditOutcome.Success);

        Assert.Equal("tool=set_channel_permissions level=write guild=111 channel=5001 role=6001 message=7001 outcome=success", entry);
    }

    [Fact]
    public void Render_writes_a_refusal_with_its_error_code()
    {
        string entry = AuditEntry.Render("edit_role", ToolLevel.Write, 222, null, 6002, null, AuditOutcome.Failed(ErrorCode.ForbiddenGuild));

        Assert.Equal("tool=edit_role level=write guild=222 role=6002 outcome=forbidden_guild", entry);
    }

    [Fact]
    public void Render_writes_a_cancelled_call()
    {
        string entry = AuditEntry.Render("send_message", ToolLevel.Write, 111, 5001, null, null, AuditOutcome.Cancelled);

        Assert.Equal("tool=send_message level=write guild=111 channel=5001 outcome=cancelled", entry);
    }

    [Fact]
    public void Render_leaves_out_every_id_that_is_not_known()
    {
        string entry = AuditEntry.Render("create_channel", ToolLevel.Write, null, null, null, null, AuditOutcome.Failed(ErrorCode.InvalidArgument));

        Assert.Equal("tool=create_channel level=write outcome=invalid_argument", entry);
    }

    [Fact]
    public void Render_writes_the_read_level_as_read()
    {
        string entry = AuditEntry.Render("list_channels", ToolLevel.Read, 1, null, null, null, AuditOutcome.Success);

        Assert.Equal("tool=list_channels level=read guild=1 outcome=success", entry);
    }

    [Fact]
    public void Render_writes_the_largest_snowflake_in_full()
    {
        string entry = AuditEntry.Render("t", ToolLevel.Write, ulong.MaxValue, null, null, null, AuditOutcome.Success);

        Assert.Equal("tool=t level=write guild=18446744073709551615 outcome=success", entry);
    }

    [Fact]
    public void Render_rejects_an_undefined_level()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AuditEntry.Render("t", (ToolLevel)7, 1, null, null, null, AuditOutcome.Success));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Render_rejects_a_null_empty_or_blank_tool_name(string? toolName)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => AuditEntry.Render(toolName!, ToolLevel.Write, 1, null, null, null, AuditOutcome.Success));
    }

    [Fact]
    public void Render_rejects_a_null_outcome()
    {
        Assert.Throws<ArgumentNullException>(
            () => AuditEntry.Render("t", ToolLevel.Write, 1, null, null, null, null!));
    }

    [Fact]
    public void Render_takes_the_ids_as_parsed_snowflakes_so_no_raw_argument_text_can_reach_the_entry()
    {
        MethodInfo render = typeof(AuditEntry).GetMethod(nameof(AuditEntry.Render))!;

        Assert.Equal(
            [
                ("toolName", typeof(string)),
                ("level", typeof(ToolLevel)),
                ("guildId", typeof(ulong?)),
                ("channelId", typeof(ulong?)),
                ("roleId", typeof(ulong?)),
                ("messageId", typeof(ulong?)),
                ("outcome", typeof(AuditOutcome)),
            ],
            render.GetParameters().Select(p => (p.Name, p.ParameterType)));
    }

    [Fact]
    public void Category_is_the_literal_Guildsmith_Audit()
    {
        Assert.Equal("Guildsmith.Audit", AuditEntry.Category);
    }
}
