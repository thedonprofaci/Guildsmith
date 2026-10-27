using Guildsmith.Core.Errors;
using Guildsmith.Core.Guards;

namespace Guildsmith.Tests.Core.Guards;

public sealed class GuildAllowlistGuardTests
{
    [Fact]
    public void Check_passes_an_allowed_guild()
    {
        Assert.Null(GuildAllowlistGuard.Check(222, new HashSet<ulong> { 111, 222 }));
    }

    [Fact]
    public void Check_refuses_a_guild_that_is_not_allowed_with_forbidden_guild()
    {
        ToolError? error = GuildAllowlistGuard.Check(333, new HashSet<ulong> { 111, 222 });

        Assert.NotNull(error);
        Assert.Equal(ErrorCode.ForbiddenGuild, error.Code);
    }

    [Fact]
    public void Check_rejects_a_null_allowlist()
    {
        Assert.Throws<ArgumentNullException>(() => GuildAllowlistGuard.Check(1, null!));
    }

    [Fact]
    public void CheckResolved_passes_an_allowed_guild()
    {
        Assert.Null(GuildAllowlistGuard.CheckResolved(222, new HashSet<ulong> { 111, 222 }));
    }

    [Fact]
    public void CheckResolved_refuses_a_guild_that_is_not_allowed_with_forbidden_guild_without_naming_it()
    {
        ToolError? error = GuildAllowlistGuard.CheckResolved(987654321, new HashSet<ulong> { 111, 222 });

        Assert.NotNull(error);
        Assert.Equal(ErrorCode.ForbiddenGuild, error.Code);
        Assert.DoesNotContain("987654321", ToolErrorSerializer.Serialize(error), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckResolved_rejects_a_null_allowlist()
    {
        Assert.Throws<ArgumentNullException>(() => GuildAllowlistGuard.CheckResolved(1, null!));
    }
}
