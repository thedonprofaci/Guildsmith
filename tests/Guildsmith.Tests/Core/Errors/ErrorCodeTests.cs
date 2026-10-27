using Guildsmith.Core.Errors;

namespace Guildsmith.Tests.Core.Errors;

public sealed class ErrorCodeTests
{
    // The eight codes of spec section 8, typed in here rather than read from ErrorCode.
    private static readonly string[] SpecCodes =
    [
        "forbidden_guild",
        "read_only_mode",
        "destructive_disabled",
        "not_found",
        "missing_permission",
        "role_hierarchy",
        "rate_limited",
        "invalid_argument",
    ];

    [Fact]
    public void ToWireString_maps_the_members_one_to_one_onto_exactly_the_eight_spec_codes()
    {
        ErrorCode[] members = Enum.GetValues<ErrorCode>();
        Dictionary<string, ErrorCode> byWire = members.ToDictionary(member => member.ToWireString());

        // Every literal maps to a member.
        Assert.All(SpecCodes, literal => Assert.True(byWire.ContainsKey(literal), literal));

        // Every member maps to one of the literals; ToDictionary above throws if two members share one.
        Assert.Equal(SpecCodes.Length, members.Length);
        Assert.Equal(SpecCodes.Order(StringComparer.Ordinal), byWire.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(ErrorCode.ForbiddenGuild, "forbidden_guild")]
    [InlineData(ErrorCode.ReadOnlyMode, "read_only_mode")]
    [InlineData(ErrorCode.DestructiveDisabled, "destructive_disabled")]
    [InlineData(ErrorCode.NotFound, "not_found")]
    [InlineData(ErrorCode.MissingPermission, "missing_permission")]
    [InlineData(ErrorCode.RoleHierarchy, "role_hierarchy")]
    [InlineData(ErrorCode.RateLimited, "rate_limited")]
    [InlineData(ErrorCode.InvalidArgument, "invalid_argument")]
    public void ToWireString_gives_each_member_its_spec_string(ErrorCode code, string expected)
    {
        Assert.Equal(expected, code.ToWireString());
    }

    [Fact]
    public void ToWireString_throws_on_an_undefined_code()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((ErrorCode)8).ToWireString());
    }
}
