using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RetailFeedback.Generator;
using RetailFeedback.Llm;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    // Config and prompts ship with the tool; data paths inside that config are
    // resolved against the caller's working directory (run from the repo root).
    ContentRootPath = AppContext.BaseDirectory,
    Args = args,
});
builder.Logging.ClearProviders();

builder.Services.AddRetailFeedbackLlm(builder.Configuration);
builder.Services.AddOptions<GeneratorOptions>()
    .Bind(builder.Configuration.GetSection(GeneratorOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<GeneratorOptions>, GeneratorOptionsValidator>();
builder.Services.AddSingleton<VariantsRunner>();
builder.Services.AddSingleton<GenerateRunner>();

using var host = builder.Build();

try
{
    _ = host.Services.GetRequiredService<IOptions<LlmOptions>>().Value;
    _ = host.Services.GetRequiredService<IOptions<GeneratorOptions>>().Value;
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

try
{
    switch (args.FirstOrDefault()?.ToLowerInvariant())
    {
        case "variants":
            return await host.Services.GetRequiredService<VariantsRunner>()
                .RunAsync(force: args.Contains("--force", StringComparer.OrdinalIgnoreCase), cts.Token);

        case "generate":
            var seedIndex = Array.FindIndex(args, a => a.Equals("--seed", StringComparison.OrdinalIgnoreCase));
            if (seedIndex < 0 || seedIndex + 1 >= args.Length || !int.TryParse(args[seedIndex + 1], out var seed))
            {
                Console.Error.WriteLine("generate requires --seed <int>.");
                return 1;
            }
            return await host.Services.GetRequiredService<GenerateRunner>().RunAsync(seed, cts.Token);

        default:
            Console.WriteLine("""
                RetailFeedback.Generator — Phase 1 seeded demo-data generator

                Usage (from the repo root):
                  dotnet run --project tools/RetailFeedback.Generator -- variants [--force]
                  dotnet run --project tools/RetailFeedback.Generator -- generate --seed 42

                  variants   OFFLINE LLM multiplication of the hand-written core corpus
                             (data/corpus/core.jsonl -> variants.jsonl, committed).
                             Runs the LLM: announce first, GPU is shared with the live RAG.
                  generate   deterministic seeded composition from the COMMITTED variants
                             pool -> generated-<seed>.jsonl + ground-truth-<seed>.json.
                             Never calls the LLM.
                """);
            return args.Length == 0 ? 0 : 1;
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
