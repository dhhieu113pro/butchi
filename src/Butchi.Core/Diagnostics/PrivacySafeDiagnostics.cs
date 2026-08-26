using System.Collections.ObjectModel;
using System.Globalization;

namespace Butchi.Core.Diagnostics;

public sealed record DiagnosticRecord(
    string EventName,
    string ErrorType,
    IReadOnlyDictionary<string, object?> Properties)
{
    public override string ToString()
    {
        var properties = Properties.Count == 0
            ? string.Empty
            : " " + string.Join(" ", Properties.Select(pair => $"{pair.Key}={Format(pair.Value)}"));

        return $"{EventName} errorType={ErrorType}{properties}";
    }

    private static string Format(object? value) => value switch
    {
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        null => "null",
        _ => value.ToString() ?? string.Empty
    };
}

public static class PrivacySafeDiagnostics
{
    public static DiagnosticRecord CreateFailure(
        string eventName,
        Exception error,
        string? sensitiveContent = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(error);

        _ = sensitiveContent; // Deliberately never persisted or rendered.

        var safeProperties = properties is null
            ? new Dictionary<string, object?>()
            : properties
                .Where(pair => IsSafeScalar(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        return new DiagnosticRecord(
            eventName,
            error.GetType().Name,
            new ReadOnlyDictionary<string, object?>(safeProperties));
    }

    private static bool IsSafeScalar(object? value) => value is
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
}

public enum UserErrorCode
{
    Cancelled,
    ModelNotFound,
    AccessDenied,
    Unexpected
}

public sealed record UserError(UserErrorCode Code, string Message);

public static class UserErrorMapper
{
    public static UserError Map(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error switch
        {
            OperationCanceledException => new UserError(UserErrorCode.Cancelled, "The operation was cancelled."),
            FileNotFoundException => new UserError(UserErrorCode.ModelNotFound, "The required model file could not be found."),
            UnauthorizedAccessException => new UserError(UserErrorCode.AccessDenied, "Butchi does not have permission to access the required resource."),
            _ => new UserError(UserErrorCode.Unexpected, "An unexpected error occurred. Try the operation again.")
        };
    }
}
