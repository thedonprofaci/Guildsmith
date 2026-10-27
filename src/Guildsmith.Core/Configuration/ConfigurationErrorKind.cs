namespace Guildsmith.Core.Configuration;

/// <summary>Why a configuration variable was rejected. Tests and callers branch on this, never on message text.</summary>
public enum ConfigurationErrorKind
{
    /// <summary>A required variable is absent or blank.</summary>
    Missing,

    /// <summary>The value is not one the variable accepts.</summary>
    Invalid,

    /// <summary>The value parses, but lies outside the accepted range.</summary>
    OutOfRange,

    /// <summary>The value names a feature this build does not provide yet.</summary>
    NotSupported,
}
