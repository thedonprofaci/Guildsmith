using Discord;

using Guildsmith.Discord;

namespace Guildsmith.Tests.Discord;

public sealed class WriteRequestOptionsTests
{
    [Fact]
    public void Create_sets_the_audit_log_reason_to_via_Discord_MCP()
    {
        RequestOptions options = WriteRequestOptions.Create(CancellationToken.None);

        Assert.Equal("via Discord MCP", options.AuditLogReason);
    }

    [Fact]
    public void Create_sets_the_cancel_token_to_the_token_it_was_given()
    {
        using CancellationTokenSource cancel = new();

        RequestOptions options = WriteRequestOptions.Create(cancel.Token);

        Assert.Equal(cancel.Token, options.CancelToken);
    }

    [Fact]
    public void Create_returns_a_fresh_instance_on_every_call()
    {
        RequestOptions first = WriteRequestOptions.Create(CancellationToken.None);
        first.AuditLogReason = "changed-by-one-request";

        RequestOptions second = WriteRequestOptions.Create(CancellationToken.None);

        Assert.NotSame(first, second);
        Assert.Equal("via Discord MCP", second.AuditLogReason);
    }
}
