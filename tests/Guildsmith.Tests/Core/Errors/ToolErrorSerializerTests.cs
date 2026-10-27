using System.Text.Json;

using Guildsmith.Core.Errors;

namespace Guildsmith.Tests.Core.Errors;

public sealed class ToolErrorSerializerTests
{
    [Theory]
    [InlineData(ErrorCode.ForbiddenGuild, "forbidden_guild")]
    [InlineData(ErrorCode.ReadOnlyMode, "read_only_mode")]
    [InlineData(ErrorCode.DestructiveDisabled, "destructive_disabled")]
    [InlineData(ErrorCode.NotFound, "not_found")]
    [InlineData(ErrorCode.MissingPermission, "missing_permission")]
    [InlineData(ErrorCode.RoleHierarchy, "role_hierarchy")]
    [InlineData(ErrorCode.RateLimited, "rate_limited")]
    [InlineData(ErrorCode.InvalidArgument, "invalid_argument")]
    public void Serialize_writes_a_plain_error_as_exactly_error_and_message(ErrorCode code, string wire)
    {
        using JsonDocument json = Parse(new ToolError(code, "Something was refused."));

        Assert.Equal(["error", "message"], PropertyNames(json));
        Assert.Equal(wire, json.RootElement.GetProperty("error").GetString());
        AssertMessagePresent(json);
    }

    [Fact]
    public void Serialize_adds_valid_values_as_an_array_of_strings_for_invalid_argument()
    {
        using JsonDocument json = Parse(ToolError.InvalidArgument("guild_id is required.", ["9", "10", "200"]));

        Assert.Equal(["error", "message", "valid_values"], PropertyNames(json));
        Assert.Equal("invalid_argument", json.RootElement.GetProperty("error").GetString());
        AssertMessagePresent(json);
        JsonElement values = json.RootElement.GetProperty("valid_values");
        Assert.Equal(JsonValueKind.Array, values.ValueKind);
        Assert.All(values.EnumerateArray(), value => Assert.Equal(JsonValueKind.String, value.ValueKind));
        Assert.Equal(["9", "10", "200"], values.EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public void Serialize_writes_an_empty_valid_values_list_as_an_empty_array()
    {
        using JsonDocument json = Parse(ToolError.InvalidArgument("guild_id is required.", []));

        Assert.Equal(["error", "message", "valid_values"], PropertyNames(json));
        Assert.Equal(0, json.RootElement.GetProperty("valid_values").GetArrayLength());
    }

    [Fact]
    public void Serialize_adds_retry_after_as_a_number_of_seconds_for_rate_limited()
    {
        using JsonDocument json = Parse(ToolError.RateLimited("Slow down.", 2.5));

        Assert.Equal(["error", "message", "retry_after"], PropertyNames(json));
        Assert.Equal("rate_limited", json.RootElement.GetProperty("error").GetString());
        AssertMessagePresent(json);
        JsonElement retryAfter = json.RootElement.GetProperty("retry_after");
        Assert.Equal(JsonValueKind.Number, retryAfter.ValueKind);
        Assert.Equal(2.5, retryAfter.GetDouble());
    }

    [Fact]
    public void Serialize_writes_a_zero_retry_after_rather_than_dropping_it()
    {
        using JsonDocument json = Parse(ToolError.RateLimited("Slow down.", 0));

        Assert.Equal(["error", "message", "retry_after"], PropertyNames(json));
        Assert.Equal(0.0, json.RootElement.GetProperty("retry_after").GetDouble());
    }

    [Fact]
    public void Serialize_rejects_a_null_error()
    {
        Assert.Throws<ArgumentNullException>(() => ToolErrorSerializer.Serialize(null!));
    }

    private static JsonDocument Parse(ToolError error) => JsonDocument.Parse(ToolErrorSerializer.Serialize(error));

    private static string[] PropertyNames(JsonDocument json) =>
        json.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static void AssertMessagePresent(JsonDocument json)
    {
        JsonElement message = json.RootElement.GetProperty("message");
        Assert.Equal(JsonValueKind.String, message.ValueKind);
        Assert.False(string.IsNullOrEmpty(message.GetString()));
    }
}
