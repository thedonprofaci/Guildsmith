using Guildsmith.Core.Audit;
using Guildsmith.Core.Errors;

namespace Guildsmith.Tests.Core.Audit;

public sealed class AuditOutcomeTests
{
    [Fact]
    public void Success_is_the_text_success()
    {
        Assert.Equal("success", AuditOutcome.Success.Text);
    }

    [Fact]
    public void Cancelled_is_the_text_cancelled()
    {
        Assert.Equal("cancelled", AuditOutcome.Cancelled.Text);
    }

    // The wire strings are typed in, not read from ErrorCodeExtensions.
    [Theory]
    [InlineData(ErrorCode.ForbiddenGuild, "forbidden_guild")]
    [InlineData(ErrorCode.ReadOnlyMode, "read_only_mode")]
    [InlineData(ErrorCode.DestructiveDisabled, "destructive_disabled")]
    [InlineData(ErrorCode.NotFound, "not_found")]
    [InlineData(ErrorCode.MissingPermission, "missing_permission")]
    [InlineData(ErrorCode.RoleHierarchy, "role_hierarchy")]
    [InlineData(ErrorCode.RateLimited, "rate_limited")]
    [InlineData(ErrorCode.InvalidArgument, "invalid_argument")]
    [InlineData(ErrorCode.InternalError, "internal_error")]
    public void Failed_is_the_wire_string_of_the_error_code(ErrorCode code, string expected)
    {
        Assert.Equal(expected, AuditOutcome.Failed(code).Text);
    }
}
