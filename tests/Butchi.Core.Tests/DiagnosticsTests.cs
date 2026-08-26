using Butchi.Core.Diagnostics;
using Xunit;

namespace Butchi.Core.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void Diagnostic_record_never_includes_selected_text_or_prompt_content()
    {
        const string secret = "private selected text that must never be logged";
        var error = new InvalidOperationException($"backend failed while processing {secret}");

        var record = PrivacySafeDiagnostics.CreateFailure(
            eventName: "inference.failed",
            error,
            sensitiveContent: secret);

        var rendered = record.ToString();
        Assert.Contains("inference.failed", rendered, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(error.Message, rendered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(OperationCanceledException), UserErrorCode.Cancelled)]
    [InlineData(typeof(FileNotFoundException), UserErrorCode.ModelNotFound)]
    [InlineData(typeof(UnauthorizedAccessException), UserErrorCode.AccessDenied)]
    [InlineData(typeof(InvalidOperationException), UserErrorCode.Unexpected)]
    public void Error_mapper_returns_stable_user_safe_codes(Type exceptionType, UserErrorCode expected)
    {
        var error = (Exception)Activator.CreateInstance(exceptionType)!;

        var mapped = UserErrorMapper.Map(error);

        Assert.Equal(expected, mapped.Code);
        Assert.False(string.IsNullOrWhiteSpace(mapped.Message));
        Assert.DoesNotContain(error.Message, mapped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostic_record_can_include_non_sensitive_numeric_context()
    {
        var record = PrivacySafeDiagnostics.CreateFailure(
            "inference.failed",
            new InvalidOperationException("secret detail"),
            sensitiveContent: "secret prompt",
            new Dictionary<string, object?>
            {
                ["durationMs"] = 125,
                ["attempt"] = 2
            });

        Assert.Equal(125, record.Properties["durationMs"]);
        Assert.Equal(2, record.Properties["attempt"]);
    }
}
