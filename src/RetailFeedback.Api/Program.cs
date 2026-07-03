using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using RetailFeedback.Api;
using RetailFeedback.Api.Alerts;
using RetailFeedback.Api.Ingest;
using RetailFeedback.Api.Storage;
using RetailFeedback.Llm;
using RetailFeedback.Llm.Structuring;

var builder = WebApplication.CreateBuilder(args);

var ingestConfig = builder.Configuration.GetSection(IngestOptions.SectionName).Get<IngestOptions>() ?? new IngestOptions();
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = ingestConfig.MaxBodyBytes);

builder.Services.AddRetailFeedbackLlm(builder.Configuration);
builder.Services.AddOptions<IngestOptions>()
    .Bind(builder.Configuration.GetSection(IngestOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<IngestOptions>, IngestOptionsValidator>();
builder.Services.AddSingleton(sp =>
    AlertKeywordSet.LoadFrom(sp.GetRequiredService<IOptions<IngestOptions>>().Value.AlertKeywordsPath));
builder.Services.AddSingleton<FeedbackStore>();
builder.Services.AddSingleton<LlmGate>();
builder.Services.AddSingleton<IngestService>();

// Per-IP fixed window; protects the machine while the tunnel is open.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = ingestConfig.RateLimitRequests,
                Window = TimeSpan.FromSeconds(ingestConfig.RateLimitWindowSeconds),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

// Fail fast on config errors — the validated-at-startup rule.
_ = app.Services.GetRequiredService<IOptions<LlmOptions>>().Value;
_ = app.Services.GetRequiredService<IOptions<IngestOptions>>().Value;
_ = app.Services.GetRequiredService<AlertKeywordSet>();
app.Services.GetRequiredService<FeedbackStore>().Initialize();

app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/feedback", async (
    FeedbackRequest request,
    IngestService ingest,
    IOptions<IngestOptions> options,
    CancellationToken ct) =>
{
    var errors = RequestValidator.Validate(request, options.Value);
    if (errors.Count > 0)
        return Results.BadRequest(new { errors });
    try
    {
        var stored = await ingest.IngestAsync(request, ct);
        return Results.Created($"/feedback/{stored.Id}", new FeedbackResponse(
            stored.Id, stored.Structure, stored.StructureFailed, stored.SalvageNotes, stored.Alerts));
    }
    catch (LlmBusyException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

// Desk-entry preview (Phase 3): interpretation shown BEFORE saving; nothing stored.
app.MapPost("/interpret", async (
    InterpretRequest request,
    IStructuringService structuring,
    LlmGate gate,
    IOptions<IngestOptions> options,
    CancellationToken ct) =>
{
    var errors = RequestValidator.ValidateText(request.Text, options.Value);
    if (errors.Count > 0)
        return Results.BadRequest(new { errors });
    try
    {
        var result = await gate.RunAsync(innerCt => structuring.StructureAsync(request.Text, innerCt), ct);
        return Results.Ok(new
        {
            structure = result.Structure,
            failed = result.Failed,
            salvaged = result.Salvaged,
            notes = result.Notes,
        });
    }
    catch (LlmBusyException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/feedback/{id}", async (string id, FeedbackStore store, CancellationToken ct) =>
    await store.GetAsync(id, ct) is { } item ? Results.Ok(item) : Results.NotFound());

app.MapGet("/feedback", async (
    FeedbackStore store,
    CancellationToken ct,
    [FromQuery] string? from = null,
    [FromQuery] string? to = null,
    [FromQuery] int limit = 200) =>
{
    limit = Math.Clamp(limit, 1, 1000);
    return Results.Ok(await store.QueryAsync(from, to, limit, ct));
});

// Health = a 1-token REAL completion (RAG-measured pattern): "server up" does
// not mean "model loaded and generating".
app.MapGet("/health", async (IServiceProvider services, CancellationToken ct) =>
{
    var client = services.GetRequiredKeyedService<IChatClient>(LlmServiceCollectionExtensions.StructuringKey);
    try
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        _ = await client.GetResponseAsync("ping", new ChatOptions { MaxOutputTokens = 1 }, timeout.Token);
        return Results.Ok(new { status = "ok" });
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
        return Results.Json(new { status = "llm_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();
