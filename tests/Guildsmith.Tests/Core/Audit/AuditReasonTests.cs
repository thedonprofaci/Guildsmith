using Guildsmith.Core.Audit;

namespace Guildsmith.Tests.Core.Audit;

public sealed class AuditReasonTests
{
    [Fact]
    public void Value_is_the_literal_text_via_Discord_MCP()
    {
        // A deliberate literal, like [REDACTED]: spec section 7.8 names this exact text, and the
        // expected side does not move when the constant does.
        Assert.Equal("via Discord MCP", AuditReason.Value);
    }
}
