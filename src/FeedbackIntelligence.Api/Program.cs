using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using FeedbackIntelligence.Api;
using FeedbackIntelligence.Api.Alerts;
using FeedbackIntelligence.Api.Analysis;
using FeedbackIntelligence.Api.Ingest;
using FeedbackIntelligence.Api.Storage;
using FeedbackIntelligence.Core.Domain;
using FeedbackIntelligence.Llm;
using FeedbackIntelligence.Llm.Structuring;

var builder = WebApplication.CreateBuilder(args);

var ingestConfig = builder.Configuration.GetSection(IngestOptions.SectionName).Get<IngestOptions>() ?? new IngestOptions();
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = ingestConfig.MaxBodyBytes);

builder.Services.AddCors();
builder.Services.AddFeedbackIntelligenceLlm(builder.Configuration);
builder.Services.AddOptions<IngestOptions>()
    .Bind(builder.Configuration.GetSection(IngestOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<IngestOptions>, IngestOptionsValidator>();
// The alert lexicon is domain data — loaded from the active domain module, not
// a fixed path. Switching Domain:Active swaps the keyword list with everything else.
builder.Services.AddSingleton(sp =>
    AlertKeywordSet.LoadFrom(sp.GetRequiredService<IActiveDomain>().AlertKeywordsPath));
builder.Services.AddSingleton<FeedbackStore>();
builder.Services.AddSingleton<LlmGate>();
builder.Services.AddSingleton<IngestService>();
builder.Services.AddOptions<ReportOptions>()
    .Bind(builder.Configuration.GetSection(ReportOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ReportOptions>, ReportOptionsValidator>();
builder.Services.AddSingleton<ReportCache>();
builder.Services.AddSingleton<ReportService>();
builder.Services.AddSingleton<FeedbackIntelligence.Api.Telemetry.CorrectionTelemetryService>();

// Per-IP fixed window; protects the machine while the tunnel is open.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var ip = context.Connection.RemoteIpAddress;
        // ForwardedHeaders runs before this, so tunneled clients carry their
        // REAL IP here and stay limited; only genuine local callers are exempt.
        if (ingestConfig.RateLimitExemptLoopback && ip is not null && System.Net.IPAddress.IsLoopback(ip))
            return RateLimitPartition.GetNoLimiter("loopback");
        return RateLimitPartition.GetFixedWindowLimiter(
            ip?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = ingestConfig.RateLimitRequests,
                Window = TimeSpan.FromSeconds(ingestConfig.RateLimitWindowSeconds),
                QueueLimit = 0,
            });
    });
});

var app = builder.Build();

// Fail fast on config errors — the validated-at-startup rule.
_ = app.Services.GetRequiredService<IOptions<LlmOptions>>().Value;
_ = app.Services.GetRequiredService<IOptions<IngestOptions>>().Value;
var reportOptions = app.Services.GetRequiredService<IOptions<ReportOptions>>().Value;
_ = app.Services.GetRequiredService<AlertKeywordSet>();

// The report resolves its prompts from the active domain at generation time.
// Validate those roles (and their files) HERE so a domain that omits/misspells a
// prompt fails the boot, not mid-report with a 500 — the "report always renders"
// guarantee only covers an unreachable LLM, never a misconfigured domain.
var activeDomain = app.Services.GetRequiredService<IActiveDomain>();
var requiredPromptRoles = reportOptions.AlertNominationEnabled
    ? new[] { "synthesis", "alertNomination", "alertVerify" }
    : new[] { "synthesis" };
foreach (var role in requiredPromptRoles)
{
    var promptPath = activeDomain.PromptPath(role); // throws if the role is undeclared
    if (!File.Exists(FeedbackIntelligence.Llm.AppPathResolver.Resolve(promptPath)))
        throw new InvalidOperationException(
            $"Domain '{activeDomain.Name}' declares a '{role}' prompt but the file is missing: {promptPath}");
}

// The report layer (the yes/no alert-screen parser and ReportText) speaks fi and
// en only. A domain declaring another language would mis-parse the screen (its
// "no" token) and render fallback text in the wrong language — fail the boot
// rather than silently misbehave. Add the language to both places to support it.
if (reportOptions.AlertNominationEnabled && activeDomain.Descriptor.Language is not ("fi" or "en"))
    throw new InvalidOperationException(
        $"Domain '{activeDomain.Name}' language '{activeDomain.Descriptor.Language}' is not supported by the report " +
        "alert screen (supported: fi, en). Add it to the screen parser and ReportText before shipping the domain.");

// The desk-entry UI is always served and posts source="desk"; require the active
// domain to accept that channel, or every desk save would 400 despite a working
// preview. "desk" is the core-reserved human-in-the-loop channel.
if (!activeDomain.Descriptor.Sources.Contains("desk", StringComparer.Ordinal))
    throw new InvalidOperationException(
        $"Domain '{activeDomain.Name}' must include \"desk\" in its sources — the desk-entry UI posts source=desk.");

app.Services.GetRequiredService<FeedbackStore>().Initialize();

// The public deployment sits behind a local tunnel daemon: without forwarded
// headers every request would arrive as loopback and the per-IP rate limit
// would collapse into one global bucket. Loopback proxies are trusted by default.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});
// The static-host frontend (Azure SWA) is a different origin than the Funnel
// backend; the allowlist is config — empty means same-origin only.
if (ingestConfig.AllowedCorsOrigins.Count > 0)
    app.UseCors(policy => policy
        .WithOrigins([.. ingestConfig.AllowedCorsOrigins])
        .WithMethods("GET", "POST")
        .WithHeaders("Content-Type"));
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/feedback", async (
    FeedbackRequest request,
    IngestService ingest,
    IOptions<IngestOptions> options,
    IActiveDomain domain,
    CancellationToken ct) =>
{
    var errors = RequestValidator.Validate(request, options.Value, domain.Descriptor);
    if (errors.Count > 0)
        return Results.BadRequest(new { errors });
    try
    {
        var stored = await ingest.IngestAsync(request, ct);
        return Results.Created($"/feedback/{stored.Id}", new FeedbackResponse(
            stored.Id, stored.Structure, stored.StructureFailed, stored.SalvageNotes, stored.Alerts,
            stored.NeedsReview, stored.ReviewFlags));
    }
    catch (LlmBusyException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch (DuplicateFeedbackIdException ex)
    {
        return Results.Conflict(new { error = "duplicate id", id = ex.Id });
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

// Schema enums + display labels for UI dropdowns — single source of truth is the
// active domain descriptor, never a copy in the frontend. The frontend renders
// labels from here, so switching Domain:Active reskins the desk with no code change.
app.MapGet("/schema", (IActiveDomain domain) =>
{
    var d = domain.Descriptor;
    return Results.Ok(new
    {
        language = d.Language,
        categoryField = d.CategoryFieldLabel,
        categories = d.CategoryLabels.Keys,
        severities = d.SeverityLabels.Keys,
        types = d.TypeLabels.Keys,
        sources = d.Sources,
        categoryLabels = d.CategoryLabels,
        severityLabels = d.SeverityLabels,
        typeLabels = d.TypeLabels,
    });
});

app.MapGet("/feedback/{id}", async (string id, FeedbackStore store, CancellationToken ct) =>
    await store.GetAsync(id, ct) is { } item ? Results.Ok(item) : Results.NotFound());

app.MapGet("/feedback", async (
    FeedbackStore store,
    IOptions<IngestOptions> options,
    CancellationToken ct,
    [FromQuery] string? from = null,
    [FromQuery] string? to = null,
    [FromQuery] int? limit = null) =>
{
    // Window bounds normalize to the same UTC round-trip format the store
    // uses, so lexical range filtering is instant-correct across offsets.
    string? fromNormalized = null, toNormalized = null;
    if (from is not null && !TimestampNormalizer.TryNormalize(from, out fromNormalized!))
        return Results.BadRequest(new { errors = new[] { $"from must be ISO-8601, got '{from}'." } });
    if (to is not null && !TimestampNormalizer.TryNormalize(to, out toNormalized!))
        return Results.BadRequest(new { errors = new[] { $"to must be ISO-8601, got '{to}'." } });
    var effectiveLimit = Math.Clamp(
        limit ?? options.Value.QueryDefaultLimit, 1, options.Value.QueryMaxLimit);
    return Results.Ok(await store.QueryAsync(fromNormalized, toNormalized, effectiveLimit, ct));
});

// The management view: two-layer analysis over a selectable window. Always
// renders (deterministic layer 1); LLM narratives/nominations degrade to
// deterministic fallbacks. Every generation persists a snapshot.
app.MapGet("/report", async (
    ReportService reports,
    IOptions<ReportOptions> reportOptions,
    CancellationToken ct,
    [FromQuery] string? from = null,
    [FromQuery] string? to = null,
    // Opt-in: overwrite the offline fallback snapshot. Only the operator's `ctl report`
    // sets it; an ephemeral frontend view must not clobber the shared-link page.
    [FromQuery] bool snapshot = false) =>
{
    var toRaw = to ?? DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    if (!TimestampNormalizer.TryNormalize(toRaw, out var toNormalized))
        return Results.BadRequest(new { errors = new[] { $"to must be ISO-8601, got '{to}'." } });
    var toInstant = DateTimeOffset.Parse(toNormalized, CultureInfo.InvariantCulture);

    var fromRaw = from ?? toInstant.AddDays(-reportOptions.Value.DefaultWindowDays).ToString("O", CultureInfo.InvariantCulture);
    if (!TimestampNormalizer.TryNormalize(fromRaw, out var fromNormalized))
        return Results.BadRequest(new { errors = new[] { $"from must be ISO-8601, got '{from}'." } });
    var fromInstant = DateTimeOffset.Parse(fromNormalized, CultureInfo.InvariantCulture);

    if (fromInstant >= toInstant || (toInstant - fromInstant).TotalDays > reportOptions.Value.MaxWindowDays)
        return Results.BadRequest(new { errors = new[] { $"window must be positive and at most {reportOptions.Value.MaxWindowDays} days." } });

    // Snap the window to a 10-minute bucket so repeated browser loads (each with a
    // slightly different `to=now`) share ONE cached report instead of each firing a
    // fresh ~40 s synthesis. Freshness is preserved by ingest invalidation, not TTL.
    var bucket = TimeSpan.FromMinutes(10).Ticks;
    var keyFrom = new DateTimeOffset(fromInstant.UtcTicks - fromInstant.UtcTicks % bucket, TimeSpan.Zero)
        .ToString("O", CultureInfo.InvariantCulture);
    var keyTo = new DateTimeOffset(toInstant.UtcTicks - toInstant.UtcTicks % bucket, TimeSpan.Zero)
        .ToString("O", CultureInfo.InvariantCulture);
    return Results.Ok(await reports.GenerateAsync(keyFrom, keyTo, ct, persistSnapshot: snapshot));
});

// The ongoing quality measure that replaced the cancelled model eval: per-field
// desk correction rates over time (drift detector; the quality measure that
// replaced the cancelled up-front model eval — see ADR-0003).
app.MapGet("/telemetry/corrections", async (
    FeedbackIntelligence.Api.Telemetry.CorrectionTelemetryService telemetry,
    IOptions<ReportOptions> reportOptions,
    CancellationToken ct,
    [FromQuery] string? from = null,
    [FromQuery] string? to = null) =>
{
    var toRaw = to ?? DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    if (!TimestampNormalizer.TryNormalize(toRaw, out var toNormalized))
        return Results.BadRequest(new { errors = new[] { $"to must be ISO-8601, got '{to}'." } });
    var telemetryTo = DateTimeOffset.Parse(toNormalized, CultureInfo.InvariantCulture);
    var fromRaw = from ?? telemetryTo
        .AddDays(-reportOptions.Value.DefaultWindowDays).ToString("O", CultureInfo.InvariantCulture);
    if (!TimestampNormalizer.TryNormalize(fromRaw, out var fromNormalized))
        return Results.BadRequest(new { errors = new[] { $"from must be ISO-8601, got '{from}'." } });
    var telemetryFrom = DateTimeOffset.Parse(fromNormalized, CultureInfo.InvariantCulture);
    // Window parity with /report: an inverted window here would read as
    // "model perfect, zero corrections" — garbage in must 400, not go green.
    if (telemetryFrom >= telemetryTo || (telemetryTo - telemetryFrom).TotalDays > reportOptions.Value.MaxWindowDays)
        return Results.BadRequest(new { errors = new[] { $"window must be positive and at most {reportOptions.Value.MaxWindowDays} days." } });
    return Results.Ok(await telemetry.SummarizeAsync(fromNormalized, toNormalized, ct));
});

app.MapGet("/report/snapshot", async (ReportService reports, CancellationToken ct) =>
    await reports.ReadLatestSnapshotJsonAsync(ct) is { } json
        ? Results.Text(json, "application/json")
        : Results.NotFound());

// The rendered, self-contained snapshot page. For a truly backend-down shared
// link this file (plus report-latest.json) is published to the static host at
// deploy time — see Phase 5 / docs/TODO.md.
app.MapGet("/report/snapshot.html", async (ReportService reports, CancellationToken ct) =>
    await reports.ReadLatestSnapshotHtmlAsync(ct) is { } html
        ? Results.Text(html, "text/html; charset=utf-8")
        : Results.NotFound());

// Health = a 1-token REAL completion (RAG-measured pattern): "server up" does
// not mean "model loaded and generating".
app.MapGet("/health", async (IServiceProvider services, IOptions<IngestOptions> options, CancellationToken ct) =>
{
    var client = services.GetRequiredKeyedService<IChatClient>(LlmServiceCollectionExtensions.StructuringKey);
    try
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.HealthTimeoutSeconds));
        _ = await client.GetResponseAsync("ping", new ChatOptions { MaxOutputTokens = 1 }, timeout.Token);
        return Results.Ok(new { status = "ok" });
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
    {
        return Results.Json(new { status = "llm_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();
