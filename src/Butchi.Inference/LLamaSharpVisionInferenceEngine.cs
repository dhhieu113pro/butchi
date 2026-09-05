using Butchi.Core.Configuration;
using Butchi.Core.Vision;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

namespace Butchi.Inference;

public sealed class LLamaSharpVisionInferenceEngine : IVisionInferenceEngine
{
    private readonly ModelDownloader _downloader;
    private readonly Func<string, string, string> _pathResolver;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Runtime? _runtime;
    private RuntimeKey? _runtimeKey;
    private int _disposed;

    public LLamaSharpVisionInferenceEngine(
        ModelDownloader downloader,
        Func<string, string, string> pathResolver)
    {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
    }

    public async IAsyncEnumerable<string> GenerateAsync(
        VisionInferenceRequest request,
        AppConfig config,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(config);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (request.ImagePng.Length == 0)
            throw new ArgumentException("Vision input image cannot be empty.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Vision prompt cannot be empty.", nameof(request));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var runtime = await EnsureRuntimeAsync(config, cancellationToken).ConfigureAwait(false);
            await foreach (var chunk in runtime.GenerateAsync(request, cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return chunk;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Runtime> EnsureRuntimeAsync(AppConfig config, CancellationToken cancellationToken)
    {
        var contextSize = Math.Max(4_096u, checked(config.MaxTokens + 2_048u));
        var key = new RuntimeKey(config.GpuLayers, contextSize);
        if (_runtime is not null && _runtimeKey == key)
            return _runtime;

        _runtime?.Dispose();
        _runtime = null;
        _runtimeKey = null;

        var model = VisionModelCatalog.Default;
        var modelPath = await EnsureFileAsync(model.Repo, model.ModelFile, cancellationToken).ConfigureAwait(false);
        var projectorPath = await EnsureFileAsync(model.Repo, model.ProjectorFile, cancellationToken).ConfigureAwait(false);

        var parameters = new ModelParams(modelPath)
        {
            ContextSize = contextSize,
            GpuLayerCount = checked((int)config.GpuLayers)
        };

        var weights = await LLamaWeights.LoadFromFileAsync(parameters).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var context = weights.CreateContext(parameters);
        var mtmdParameters = MtmdContextParams.Default();
        mtmdParameters.UseGpu = false;

        MtmdWeights? projector = null;
        try
        {
            projector = await MtmdWeights.LoadFromFileAsync(
                projectorPath,
                weights,
                mtmdParameters,
                cancellationToken).ConfigureAwait(false);
            if (!projector.SupportsVision)
                throw new InvalidOperationException("The configured multimodal projector does not support vision input.");

            var marker = mtmdParameters.MediaMarker ?? NativeApi.MtmdDefaultMarker() ?? "<media>";
            _runtime = new Runtime(weights, context, projector, marker);
            _runtimeKey = key;
            return _runtime;
        }
        catch
        {
            projector?.Dispose();
            context.Dispose();
            weights.Dispose();
            throw;
        }
    }

    private async Task<string> EnsureFileAsync(
        string repo,
        string file,
        CancellationToken cancellationToken)
    {
        var path = _pathResolver(repo, file);
        if (File.Exists(path))
            return path;

        return await _downloader.DownloadAsync(
            repo,
            file,
            path,
            progress: null,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _runtime?.Dispose();
            _runtime = null;
            _runtimeKey = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private sealed class Runtime(
        LLamaWeights weights,
        LLamaContext context,
        MtmdWeights projector,
        string mediaMarker) : IDisposable
    {
        private readonly LLamaWeights _weights = weights;
        private readonly LLamaContext _context = context;
        private readonly MtmdWeights _projector = projector;
        private readonly string _mediaMarker = mediaMarker;

        public async IAsyncEnumerable<string> GenerateAsync(
            VisionInferenceRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _context.NativeHandle.MemoryClear();
            _projector.ClearMedia();
            using var embed = _projector.LoadMedia(request.ImagePng);
            var executor = new InteractiveExecutor(_context, _projector);
            executor.Embeds.Add(embed);

            var template = new LLamaTemplate(_weights.NativeHandle)
            {
                AddAssistant = true
            };
            template.Add("system", "You are a concise visual assistant. Analyze only the supplied screenshot and answer the user's request.");
            template.Add("user", $"{_mediaMarker}{request.Prompt.Trim()}");
            var prompt = LLamaTemplate.Encoding.GetString(template.Apply());

            using var sampling = new DefaultSamplingPipeline
            {
                Temperature = request.Temperature,
                Seed = request.Seed
            };
            var inferenceParameters = new InferenceParams
            {
                MaxTokens = checked((int)request.MaxTokens),
                SamplingPipeline = sampling
            };

            await foreach (var chunk in executor.InferAsync(prompt, inferenceParameters, cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
            }

            _projector.ClearMedia();
        }

        public void Dispose()
        {
            _projector.Dispose();
            _context.Dispose();
            _weights.Dispose();
        }
    }

    private sealed record RuntimeKey(uint GpuLayers, uint ContextSize);
}
