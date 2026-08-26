using Butchi.Core.Configuration;

namespace Butchi.Inference;

public enum CpuArchitecture
{
    X64,
    Arm64
}

public enum InferenceBackend
{
    Cpu,
    Cuda,
    Vulkan
}

public static class BackendResolver
{
    public static IReadOnlyList<InferenceBackend> GetAttempts(
        BackendPreference preference,
        CpuArchitecture architecture,
        bool cudaSupported,
        bool vulkanSupported)
    {
        return preference switch
        {
            BackendPreference.Cpu => [InferenceBackend.Cpu],
            BackendPreference.Gpu => GetGpuAttempts(architecture, cudaSupported, vulkanSupported),
            _ => GetAutoAttempts(architecture, cudaSupported, vulkanSupported)
        };
    }

    private static IReadOnlyList<InferenceBackend> GetAutoAttempts(
        CpuArchitecture architecture,
        bool cudaSupported,
        bool vulkanSupported)
    {
        var attempts = new List<InferenceBackend>(3);

        if (architecture == CpuArchitecture.X64 && cudaSupported)
        {
            attempts.Add(InferenceBackend.Cuda);
        }

        if (vulkanSupported)
        {
            attempts.Add(InferenceBackend.Vulkan);
        }

        attempts.Add(InferenceBackend.Cpu);
        return attempts;
    }

    private static IReadOnlyList<InferenceBackend> GetGpuAttempts(
        CpuArchitecture architecture,
        bool cudaSupported,
        bool vulkanSupported)
    {
        var attempts = new List<InferenceBackend>(2);

        if (architecture == CpuArchitecture.X64 && cudaSupported)
        {
            attempts.Add(InferenceBackend.Cuda);
        }

        if (vulkanSupported)
        {
            attempts.Add(InferenceBackend.Vulkan);
        }

        if (attempts.Count == 0)
        {
            throw new InvalidOperationException(
                "GPU backend requested, but neither CUDA nor Vulkan is supported on this system.");
        }

        return attempts;
    }
}
