using System.Reflection;
using System.Text.RegularExpressions;

using Guildsmith.Core.Configuration;
using Guildsmith.Core.Guards;
using Guildsmith.Server;
using Guildsmith.Server.Tools;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Guildsmith.Tests.Server.Tools;

/// <summary>
/// The instrument every tool round inherits: each MCP tool the built host registers declares one
/// level, sets all three hints, has hints agreeing with that level, and has a snake_case name. The
/// tools come from the host's registrations, not from a list kept here, and each rule is shown to
/// fire on a deliberately wrong fixture tool that is never registered with the server.
/// </summary>
public sealed class ToolRegistryTests
{
    [Fact]
    public void Every_registered_tool_conforms()
    {
        Assert.Equal([], RegisteredTools().SelectMany(Violations));
    }

    [Fact]
    public void The_host_registers_exactly_the_server_and_channel_tools()
    {
        // A tool that silently failed to register would leave the conformance test above green.
        Assert.Equal(["create_channel", "delete_channel", "edit_channel", "get_guild_info", "list_channels", "list_guilds"], RegisteredTools().Select(tool => tool.ProtocolTool.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(nameof(Fixtures.NoLevel), "no_level: declares no level")]
    [InlineData(nameof(Fixtures.ReadMarkedNotReadOnly), "read_marked_not_read_only: readOnlyHint/destructiveHint false/false disagree with level Read")]
    [InlineData(nameof(Fixtures.ReadMarkedDestructive), "read_marked_destructive: readOnlyHint/destructiveHint true/true disagree with level Read")]
    [InlineData(nameof(Fixtures.WriteMarkedReadOnly), "write_marked_read_only: readOnlyHint/destructiveHint true/false disagree with level Write")]
    [InlineData(nameof(Fixtures.WriteMarkedDestructive), "write_marked_destructive: readOnlyHint/destructiveHint false/true disagree with level Write")]
    [InlineData(nameof(Fixtures.DestructiveMarkedNotDestructive), "destructive_marked_not_destructive: readOnlyHint/destructiveHint false/false disagree with level Destructive")]
    [InlineData(nameof(Fixtures.DestructiveMarkedReadOnly), "destructive_marked_read_only: readOnlyHint/destructiveHint true/true disagree with level Destructive")]
    [InlineData(nameof(Fixtures.ReadOnlyUnset), "read_only_unset: leaves readOnlyHint unset")]
    [InlineData(nameof(Fixtures.DestructiveUnset), "destructive_unset: leaves destructiveHint unset")]
    [InlineData(nameof(Fixtures.IdempotentUnset), "idempotent_unset: leaves idempotentHint unset")]
    [InlineData(nameof(Fixtures.CamelCaseName), "listGuilds: name is not snake_case")]
    [InlineData(nameof(Fixtures.KebabCaseName), "list-guilds: name is not snake_case")]
    [InlineData(nameof(Fixtures.LeadingUnderscoreName), "_list_guilds: name is not snake_case")]
    [InlineData(nameof(Fixtures.TrailingUnderscoreName), "list_guilds_: name is not snake_case")]
    [InlineData(nameof(Fixtures.DoubleUnderscoreName), "list__guilds: name is not snake_case")]
    public void Each_rule_fires_on_its_wrong_fixture_tool_and_only_that_rule(string fixture, string expected)
    {
        McpServerTool tool = McpServerTool.Create(typeof(Fixtures).GetMethod(fixture, BindingFlags.Public | BindingFlags.Static)!);

        Assert.Equal([expected], Violations(tool));
    }

    [Fact]
    public void A_correct_fixture_tool_of_each_level_has_no_violation()
    {
        // The other direction: a rule that fires on everything would pass every fixture above.
        Assert.Equal([], new[] { nameof(Fixtures.CorrectRead), nameof(Fixtures.CorrectWrite), nameof(Fixtures.CorrectDestructive) }
            .Select(name => McpServerTool.Create(typeof(Fixtures).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!))
            .SelectMany(Violations));
    }

    // The rules. Each condition adds one line naming the tool, so a red run says which tool broke which rule.
    private static IEnumerable<string> Violations(McpServerTool tool)
    {
        string name = tool.ProtocolTool.Name;
        ToolAnnotations? hints = tool.ProtocolTool.Annotations;
        List<string> violations = [];

        if (!Regex.IsMatch(name, "^[a-z]+(_[a-z]+)*$"))
        {
            violations.Add($"{name}: name is not snake_case");
        }

        if (hints?.ReadOnlyHint is null)
        {
            violations.Add($"{name}: leaves readOnlyHint unset");
        }

        if (hints?.DestructiveHint is null)
        {
            violations.Add($"{name}: leaves destructiveHint unset");
        }

        if (hints?.IdempotentHint is null)
        {
            violations.Add($"{name}: leaves idempotentHint unset");
        }

        DeclaredLevelAttribute[] levels = tool.Metadata.OfType<DeclaredLevelAttribute>().ToArray();
        if (levels.Length != 1)
        {
            violations.Add($"{name}: declares no level");
        }
        else if (hints?.ReadOnlyHint is bool readOnly && hints.DestructiveHint is bool destructive
            && (readOnly, destructive) != ExpectedHints(levels[0].Level))
        {
            string text = $"{readOnly}/{destructive}".ToLowerInvariant();
            violations.Add($"{name}: readOnlyHint/destructiveHint {text} disagree with level {levels[0].Level}");
        }

        return violations;
    }

    // Typed in from spec section 6.2, not read from the tools.
    private static (bool ReadOnly, bool Destructive) ExpectedHints(ToolLevel level) => level switch
    {
        ToolLevel.Read => (true, false),
        ToolLevel.Write => (false, false),
        ToolLevel.Destructive => (false, true),
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Not a defined tool level."),
    };

    private static IReadOnlyList<McpServerTool> RegisteredTools()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        ServerStartup.ConfigureServices(builder, new GuildsmithOptions
        {
            DiscordToken = "FAKE-TOKEN-guildsmith-test-0008",
            AllowedGuildIds = new HashSet<ulong> { 1 },
            Mode = DiscordMcpMode.ReadWrite,
            AllowDestructive = false,
            AllowAdminGrant = false,
            MaxMessagesPerRead = 50,
            Transport = McpTransport.Stdio,
            LogLevel = LogLevel.Information,
        }, TextWriter.Null);
        using IHost host = builder.Build();
        return host.Services.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.ToList();
    }

    /// <summary>Wrong tools, one rule broken each, plus one correct tool per level. Never registered with the server.</summary>
    public static class Fixtures
    {
        [McpServerTool(Name = "no_level", ReadOnly = true, Destructive = false, Idempotent = true)]
        public static string NoLevel() => "";

        [McpServerTool(Name = "read_marked_not_read_only", ReadOnly = false, Destructive = false, Idempotent = true), DeclaredLevel(ToolLevel.Read)]
        public static string ReadMarkedNotReadOnly() => "";

        [McpServerTool(Name = "read_marked_destructive", ReadOnly = true, Destructive = true, Idempotent = true), DeclaredLevel(ToolLevel.Read)]
        public static string ReadMarkedDestructive() => "";

        [McpServerTool(Name = "write_marked_read_only", ReadOnly = true, Destructive = false, Idempotent = false), DeclaredLevel(ToolLevel.Write)]
        public static string WriteMarkedReadOnly() => "";

        [McpServerTool(Name = "write_marked_destructive", ReadOnly = false, Destructive = true, Idempotent = false), DeclaredLevel(ToolLevel.Write)]
        public static string WriteMarkedDestructive() => "";

        [McpServerTool(Name = "destructive_marked_not_destructive", ReadOnly = false, Destructive = false, Idempotent = false), DeclaredLevel(ToolLevel.Destructive)]
        public static string DestructiveMarkedNotDestructive() => "";

        [McpServerTool(Name = "destructive_marked_read_only", ReadOnly = true, Destructive = true, Idempotent = false), DeclaredLevel(ToolLevel.Destructive)]
        public static string DestructiveMarkedReadOnly() => "";

        [McpServerTool(Name = "read_only_unset", Destructive = false, Idempotent = true), DeclaredLevel(ToolLevel.Read)]
        public static string ReadOnlyUnset() => "";

        [McpServerTool(Name = "destructive_unset", ReadOnly = true, Idempotent = true), DeclaredLevel(ToolLevel.Read)]
        public static string DestructiveUnset() => "";

        [McpServerTool(Name = "idempotent_unset", ReadOnly = true, Destructive = false), DeclaredLevel(ToolLevel.Read)]
        public static string IdempotentUnset() => "";

        [McpServerTool(Name = "listGuilds", ReadOnly = true, Destructive = false, Idempotent = true), DeclaredLevel(ToolLevel.Read)]
        public static string CamelCaseName() => "";

        [McpServerTool(Name = "list-guilds", ReadOnly = true, Destructive = false, Idempotent = true), DeclaredLevel(ToolLevel.Read)]
        public static string KebabCaseName() => "";

        [McpServerTool(Name = "_list_guilds", ReadOnly = true, Destructive = false, Idempotent = true), DeclaredLevel(ToolLevel.Read)]
        public static string LeadingUnderscoreName() => "";

        [McpServerTool(Name = "list_guilds_", ReadOnly = true, Destructive = false, Idempotent = true), DeclaredLevel(ToolLevel.Read)]
        public static string TrailingUnderscoreName() => "";

        [McpServerTool(Name = "list__guilds", ReadOnly = true, Destructive = false, Idempotent = true), DeclaredLevel(ToolLevel.Read)]
        public static string DoubleUnderscoreName() => "";

        [McpServerTool(Name = "correct_read", ReadOnly = true, Destructive = false, Idempotent = true), DeclaredLevel(ToolLevel.Read)]
        public static string CorrectRead() => "";

        [McpServerTool(Name = "correct_write", ReadOnly = false, Destructive = false, Idempotent = false), DeclaredLevel(ToolLevel.Write)]
        public static string CorrectWrite() => "";

        [McpServerTool(Name = "correct_destructive", ReadOnly = false, Destructive = true, Idempotent = false), DeclaredLevel(ToolLevel.Destructive)]
        public static string CorrectDestructive() => "";
    }
}
