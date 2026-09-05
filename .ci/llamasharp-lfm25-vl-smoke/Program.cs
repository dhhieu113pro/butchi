using System.Text;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: LLamaSharpLfm25VlSmoke <model.gguf> <mmproj.gguf> <image.png>");
    return 2;
}

var modelPath = args[0];
var projectorPath = args[1];
var imagePath = args[2];

var parameters = new ModelParams(modelPath)
{
    ContextSize = 2048,
    GpuLayerCount = 0,
};

var mtmdParameters = MtmdContextParams.Default();
mtmdParameters.UseGpu = false;

Console.WriteLine($"Loading text model: {modelPath}");
using var model = await LLamaWeights.LoadFromFileAsync(parameters);
using var context = model.CreateContext(parameters);

Console.WriteLine($"Loading vision projector with MtmdWeights: {projectorPath}");
using var projector = await MtmdWeights.LoadFromFileAsync(projectorPath, model, mtmdParameters);

if (!projector.SupportsVision)
    throw new InvalidOperationException("LLamaSharp loaded the MTMD projector, but it does not report vision support.");

var mediaMarker = mtmdParameters.MediaMarker ?? NativeApi.MtmdDefaultMarker() ?? "<media>";
using var imageEmbed = projector.LoadMedia(imagePath);
var executor = new InteractiveExecutor(context, projector);
executor.Embeds.Add(imageEmbed);

var template = new LLamaTemplate(model.NativeHandle)
{
    AddAssistant = true,
};
template.Add("system", "You are a helpful multimodal assistant by Liquid AI.");
template.Add("user", $"{mediaMarker}What color is the square? Answer with exactly one color word.");
var prompt = LLamaTemplate.Encoding.GetString(template.Apply());

var inferenceParams = new InferenceParams
{
    SamplingPipeline = new DefaultSamplingPipeline
    {
        Temperature = 0.1f,
    },
    MaxTokens = 16,
};

var response = new StringBuilder();
await foreach (var text in executor.InferAsync(prompt, inferenceParams))
{
    response.Append(text);
    Console.Write(text);
}

Console.WriteLine();
var answer = response.ToString().Trim();
Console.WriteLine($"LLamaSharp vision answer: {answer}");

if (!answer.Contains("red", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException($"Expected the vision response to identify the red square, but got: '{answer}'.");

Console.WriteLine("LFM2.5-VL vision smoke test passed through LLamaSharp MTMD.");
return 0;
