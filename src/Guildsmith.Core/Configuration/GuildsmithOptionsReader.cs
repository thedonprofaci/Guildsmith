using System.Globalization;

using Guildsmith.Core.Arguments;

using Microsoft.Extensions.Logging;

namespace Guildsmith.Core.Configuration;

/// <summary>
/// Reads and validates the startup configuration. Fails closed: a value that does not parse is
/// an error, never a silent default, and every invalid variable is reported, not only the first.
/// </summary>
public static class GuildsmithOptionsReader
{
    private const string DiscordToken = "DISCORD_TOKEN";
    private const string AllowedGuildIds = "ALLOWED_GUILD_IDS";
    private const string Mode = "DISCORD_MCP_MODE";
    private const string AllowDestructive = "ALLOW_DESTRUCTIVE";
    private const string AllowAdminGrant = "ALLOW_ADMIN_GRANT";
    private const string MaxMessages = "MAX_MESSAGES_PER_READ";
    private const string Transport = "MCP_TRANSPORT";
    private const string LogLevelVariable = "LOG_LEVEL";

    private const int DefaultMaxMessages = 50;
    private const int MinMaxMessages = 1;
    private const int MaxMaxMessages = 100;

    /// <summary>
    /// Reads every setting through <paramref name="lookup"/>, which maps a variable name to its
    /// raw value, or to <see langword="null"/> when the variable is not set.
    /// </summary>
    public static ConfigurationResult Read(Func<string, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        List<ConfigurationError> errors = [];

        string? token = ReadToken(lookup(DiscordToken), errors);
        IReadOnlySet<ulong>? guildIds = ReadGuildIds(lookup(AllowedGuildIds), errors);
        DiscordMcpMode mode = ReadName(Mode, lookup(Mode), DiscordMcpMode.ReadWrite, errors);
        bool allowDestructive = ReadBoolean(AllowDestructive, lookup(AllowDestructive), errors);
        bool allowAdminGrant = ReadBoolean(AllowAdminGrant, lookup(AllowAdminGrant), errors);
        int maxMessages = ReadMaxMessages(lookup(MaxMessages), errors);
        McpTransport transport = ReadTransport(lookup(Transport), errors);
        LogLevel logLevel = ReadName(LogLevelVariable, lookup(LogLevelVariable), LogLevel.Information, errors);

        if (errors.Count > 0 || token is null || guildIds is null)
        {
            return new ConfigurationResult(null, errors);
        }

        GuildsmithOptions options = new()
        {
            DiscordToken = token,
            AllowedGuildIds = guildIds,
            Mode = mode,
            AllowDestructive = allowDestructive,
            AllowAdminGrant = allowAdminGrant,
            MaxMessagesPerRead = maxMessages,
            Transport = transport,
            LogLevel = logLevel,
        };
        return new ConfigurationResult(options, errors);
    }

    private static string? ReadToken(string? value, List<ConfigurationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(DiscordToken, ConfigurationErrorKind.Missing,
                "is required: set it to the bot token of the Discord application."));
            return null;
        }

        return value;
    }

    private static IReadOnlySet<ulong>? ReadGuildIds(string? value, List<ConfigurationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new(AllowedGuildIds, ConfigurationErrorKind.Missing,
                "is required: set it to a comma-separated list of guild IDs; the server does not start without one."));
            return null;
        }

        string[] entries = value.Split(',');
        HashSet<ulong> ids = [];
        for (int i = 0; i < entries.Length; i++)
        {
            // The snowflake rule of a tool argument, applied after trimming the space around a comma.
            string entry = entries[i].Trim();
            if (!Snowflake.TryParse(entry, out ulong id))
            {
                errors.Add(new(AllowedGuildIds, ConfigurationErrorKind.Invalid,
                    $"entry {i + 1} of {entries.Length} is not a non-zero numeric guild ID."));
                return null;
            }

            ids.Add(id);
        }

        return ids;
    }

    // Matches defined enum names only: Enum.TryParse would also accept "1" or "99" as a value.
    private static TEnum ReadName<TEnum>(string variable, string? value, TEnum fallback, List<ConfigurationError> errors)
        where TEnum : struct, Enum
    {
        if (value is null)
        {
            return fallback;
        }

        foreach (TEnum candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        errors.Add(new(variable, ConfigurationErrorKind.Invalid,
            $"must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}."));
        return fallback;
    }

    // Not bool.TryParse: it trims whitespace, so " true " would be accepted.
    private static bool ReadBoolean(string variable, string? value, List<ConfigurationError> errors)
    {
        if (value is null)
        {
            return false;
        }

        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new(variable, ConfigurationErrorKind.Invalid, "must be true or false."));
        }

        return false;
    }

    private static int ReadMaxMessages(string? value, List<ConfigurationError> errors)
    {
        if (value is null)
        {
            return DefaultMaxMessages;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int max))
        {
            errors.Add(new(MaxMessages, ConfigurationErrorKind.Invalid, "must be a whole number."));
            return DefaultMaxMessages;
        }

        // Out of range fails startup instead of being clamped: a silently changed limit is a
        // limit the operator did not set.
        if (max < MinMaxMessages || max > MaxMaxMessages)
        {
            errors.Add(new(MaxMessages, ConfigurationErrorKind.OutOfRange,
                $"must be between {MinMaxMessages} and {MaxMaxMessages}."));
            return DefaultMaxMessages;
        }

        return max;
    }

    private static McpTransport ReadTransport(string? value, List<ConfigurationError> errors)
    {
        if (value is null || string.Equals(value, "stdio", StringComparison.OrdinalIgnoreCase))
        {
            return McpTransport.Stdio;
        }

        if (string.Equals(value, "http", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new(Transport, ConfigurationErrorKind.NotSupported,
                "http is not supported until phase 3; use stdio."));
        }
        else
        {
            errors.Add(new(Transport, ConfigurationErrorKind.Invalid, "must be stdio."));
        }

        return McpTransport.Stdio;
    }
}
