namespace Guildsmith.Tests;

/// <summary>The committed <c>.env.example</c>: all eight settings, placeholders only.</summary>
public sealed class EnvExampleTests
{
    [Fact]
    public void EnvExample_lists_the_eight_settings_with_placeholders_and_defaults_only()
    {
        string[] lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, ".env.example"));

        (string, string)[] settings = lines
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split('=', 2))
            .Select(pair => (pair[0], pair[1]))
            .ToArray();

        Assert.Equal(
            [
                ("DISCORD_TOKEN", "<your-bot-token>"),
                ("ALLOWED_GUILD_IDS", "<guild-id>,<another-guild-id>"),
                ("DISCORD_MCP_MODE", "ReadWrite"),
                ("ALLOW_DESTRUCTIVE", "false"),
                ("ALLOW_ADMIN_GRANT", "false"),
                ("MAX_MESSAGES_PER_READ", "50"),
                ("MCP_TRANSPORT", "stdio"),
                ("LOG_LEVEL", "Information"),
            ],
            settings);
    }
}
