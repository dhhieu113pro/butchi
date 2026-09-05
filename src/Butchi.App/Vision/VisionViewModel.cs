using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Butchi.App.Vision;

public sealed class VisionViewModel : INotifyPropertyChanged
{
    private const string DefaultPrompt = "Describe what is shown in this screenshot.";
    private byte[]? _screenshotPng;
    private string _prompt = DefaultPrompt;
    private string _output = string.Empty;
    private string? _errorMessage;
    private bool _isActive;
    private bool _isRunning;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? CaptureRequested;
    public event EventHandler<string>? AnalyzeRequested;

    public bool IsActive => _isActive;
    public bool IsRunning => _isRunning;
    public bool HasScreenshot => _screenshotPng is { Length: > 0 };
    public byte[]? ScreenshotPng => _screenshotPng;
    public string Prompt => _prompt;
    public string Output => _output;
    public string? ErrorMessage => _errorMessage;

    public void RequestCapture()
    {
        SetActive(true);
        _errorMessage = null;
        OnPropertyChanged(nameof(ErrorMessage));
        CaptureRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Deactivate() => SetActive(false);

    public void SetScreenshot(byte[] imagePng)
    {
        ArgumentNullException.ThrowIfNull(imagePng);
        if (imagePng.Length == 0)
            throw new ArgumentException("Screenshot cannot be empty.", nameof(imagePng));

        _screenshotPng = imagePng;
        _output = string.Empty;
        _errorMessage = null;
        _isRunning = false;
        SetActive(true);
        OnPropertyChanged(nameof(ScreenshotPng));
        OnPropertyChanged(nameof(HasScreenshot));
        OnPropertyChanged(nameof(Output));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(IsRunning));
    }

    public void SetPrompt(string prompt)
    {
        var normalized = prompt ?? string.Empty;
        if (string.Equals(_prompt, normalized, StringComparison.Ordinal))
            return;
        _prompt = normalized;
        OnPropertyChanged(nameof(Prompt));
    }

    public void RequestAnalyze()
    {
        if (_isRunning || !HasScreenshot)
            return;

        var prompt = _prompt.Trim();
        if (prompt.Length == 0)
            return;

        _output = string.Empty;
        _errorMessage = null;
        _isRunning = true;
        OnPropertyChanged(nameof(Output));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(IsRunning));
        AnalyzeRequested?.Invoke(this, prompt);
    }

    public void Append(string chunk)
    {
        if (!_isRunning || string.IsNullOrEmpty(chunk))
            return;
        _output += chunk;
        OnPropertyChanged(nameof(Output));
    }

    public void Complete()
    {
        _isRunning = false;
        _errorMessage = null;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(ErrorMessage));
    }

    public void Fail(string message)
    {
        _isRunning = false;
        _errorMessage = string.IsNullOrWhiteSpace(message) ? "Vision analysis failed." : message;
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(ErrorMessage));
    }

    private void SetActive(bool value)
    {
        if (_isActive == value)
            return;
        _isActive = value;
        OnPropertyChanged(nameof(IsActive));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
