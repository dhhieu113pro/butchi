using Butchi.Core.Configuration;
using Butchi.Inference;
using Xunit;

namespace Butchi.Inference.Tests;

public sealed class BackendResolverTests
{
    [Fact]
    public void Auto_x64_prefers_cuda_then_vulkan_then_cpu()
    {
        var attempts = BackendResolver.GetAttempts(BackendPreference.Auto, CpuArchitecture.X64, cudaSupported: true, vulkanSupported: true);

        Assert.Equal([InferenceBackend.Cuda, InferenceBackend.Vulkan, InferenceBackend.Cpu], attempts);
    }

    [Fact]
    public void Auto_x64_without_cuda_uses_vulkan_then_cpu()
    {
        var attempts = BackendResolver.GetAttempts(BackendPreference.Auto, CpuArchitecture.X64, cudaSupported: false, vulkanSupported: true);

        Assert.Equal([InferenceBackend.Vulkan, InferenceBackend.Cpu], attempts);
    }

    [Fact]
    public void Auto_arm64_never_attempts_cuda_and_falls_back_to_cpu()
    {
        var attempts = BackendResolver.GetAttempts(BackendPreference.Auto, CpuArchitecture.Arm64, cudaSupported: true, vulkanSupported: true);

        Assert.Equal([InferenceBackend.Vulkan, InferenceBackend.Cpu], attempts);
    }

    [Fact]
    public void Explicit_cpu_never_probes_gpu()
    {
        var attempts = BackendResolver.GetAttempts(BackendPreference.Cpu, CpuArchitecture.X64, cudaSupported: true, vulkanSupported: true);

        Assert.Equal([InferenceBackend.Cpu], attempts);
    }

    [Fact]
    public void Explicit_gpu_x64_prefers_cuda_then_vulkan_without_cpu_fallback()
    {
        var attempts = BackendResolver.GetAttempts(BackendPreference.Gpu, CpuArchitecture.X64, cudaSupported: true, vulkanSupported: true);

        Assert.Equal([InferenceBackend.Cuda, InferenceBackend.Vulkan], attempts);
    }

    [Fact]
    public void Explicit_gpu_throws_actionable_error_when_no_gpu_backend_is_supported()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BackendResolver.GetAttempts(BackendPreference.Gpu, CpuArchitecture.X64, cudaSupported: false, vulkanSupported: false));

        Assert.Contains("GPU", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CUDA", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Vulkan", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Auto_with_no_gpu_backend_returns_cpu_only()
    {
        var attempts = BackendResolver.GetAttempts(BackendPreference.Auto, CpuArchitecture.X64, cudaSupported: false, vulkanSupported: false);

        Assert.Equal([InferenceBackend.Cpu], attempts);
    }
}
