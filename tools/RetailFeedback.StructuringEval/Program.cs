using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RetailFeedback.Llm;
using RetailFeedback.StructuringEval;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    // Config and prompts ship with the tool; data paths inside that config are
    // resolved against the caller's working directory (run from the repo root).
    ContentRootPath = AppContext.BaseDirectory,
    Args = args,
});
builder.Logging.ClearProviders(); // keep stdout clean for eval tables

builder.Services.AddRetailFeedbackLlm(builder.Configuration);
builder.Services.AddOptions<EvalOptions>()
    .Bind(builder.Configuration.GetSection(EvalOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<EvalOptions>, EvalOptionsValidator>();
builder.Services.AddSingleton<StructuringEvalRunner>();

using var host = builder.Build();

try
{
    // Fail fast on config errors regardless of verb — the validated-at-startup rule.
    _ = host.Services.GetRequiredService<IOptions<LlmOptions>>().Value;
    _ = host.Services.GetRequiredService<IOptions<EvalOptions>>().Value;
}
catch (OptionsValidationException ex)
{
    Console.Error.WriteLine("Configuration invalid:");
    foreach (var failure in ex.Failures)
        Console.Error.WriteLine($"  - {failure}");
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var runner = host.Services.GetRequiredService<StructuringEvalRunner>();
try
{
    switch (args.FirstOrDefault()?.ToLowerInvariant())
    {
        case "ping":
            return await runner.PingAsync(cts.Token);
        case "eval":
            return await runner.EvalAsync(cts.Token);
        default:
            Console.WriteLine("""
                RetailFeedback.StructuringEval — Phase 0 spike + structuring-model eval

                Usage (from the repo root):
                  dotnet run --project tools/RetailFeedback.StructuringEval -- ping
                  dotnet run --project tools/RetailFeedback.StructuringEval -- eval

                  ping   calls every configured model through the LLM abstraction ("pong" check)
                  eval   runs the structuring eval over data/eval/structuring-inputs.jsonl

                NOTE: the GPU is shared with the live RAG stack — coordinate before running.
                """);
            return args.Length == 0 ? 0 : 1;
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
