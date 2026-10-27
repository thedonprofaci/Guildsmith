using System.Diagnostics.CodeAnalysis;

namespace Guildsmith.Core.Errors;

/// <summary>Either a value or the <see cref="ToolError"/> that took its place.</summary>
public sealed class ToolResult<T>
{
    private ToolResult(T? value, ToolError? error)
    {
        Value = value;
        Error = error;
    }

    /// <summary>The value; the default of <typeparamref name="T"/> when <see cref="IsSuccess"/> is false.</summary>
    public T? Value { get; }

    /// <summary>The error; <see langword="null"/> on success.</summary>
    public ToolError? Error { get; }

    /// <summary>Whether this holds a value rather than an error.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>A successful result holding <paramref name="value"/>.</summary>
    public static ToolResult<T> Success(T value) => new(value, null);

    /// <summary>A failed result holding <paramref name="error"/>.</summary>
    public static ToolResult<T> Failure(ToolError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, error);
    }
}
