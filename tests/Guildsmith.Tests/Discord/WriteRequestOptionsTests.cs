using Discord;

using Guildsmith.Discord;

namespace Guildsmith.Tests.Discord;

public sealed class WriteRequestOptionsTests
{
    [Fact]
    public void Create_sets_the_audit_log_reason_to_via_Discord_MCP()
    {
        RequestOptions options = WriteRequestOptions.Create();

        Assert.Equal("via Discord MCP", options.AuditLogReason);
    }

    [Fact]
    public void Create_returns_a_fresh_instance_on_every_call()
    {
        RequestOptions first = WriteRequestOptions.Create();
        first.AuditLogReason = "changed-by-one-request";

        RequestOptions second = WriteRequestOptions.Create();

        Assert.NotSame(first, second);
        Assert.Equal("via Discord MCP", second.AuditLogReason);
    }
}
