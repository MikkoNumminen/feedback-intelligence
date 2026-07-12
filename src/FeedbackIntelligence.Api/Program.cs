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

// The live desk channel (ADR-0024): desk entries are real evidence with their own
// database and report pipeline, so the seeded corpus and the live channel can never
// contaminate each other. Keyed clones of store/cache/ingest/report; the LlmGate and
// chat clients stay SHARED so GPU containment remains global across both channels.
builder.Services.AddKeyedSingleton(Channels.Live, (sp, _) =>
{
    var ingestOptions = sp.GetRequiredService<IOptions<IngestOptions>>();
    return new FeedbackStore(ingestOptions, ingestOptions.Value.LiveDbPath);
});
builder.Services.AddKeyedSingleton(Channels.Live, (_, _) => new ReportCache());
builder.Services.AddKeyedSingleton(Channels.Live, (sp, _) => new IngestService(
    sp.GetRequiredKeyedService<FeedbackStore>(Channels.Live),
    sp.GetRequiredService<IStructuringService>(),
    sp.GetRequiredService<LlmGate>(),
    sp.GetRequiredService<AlertKeywordSet>(),
    sp.GetRequiredKeyedService<ReportCache>(Channels.Live),
    sp.GetRequiredService<ILogger<IngestService>>()));
builder.Services.AddKeyedSingleton(Channels.Live, (sp, _) => new ReportService(
    sp.GetRequiredKeyedService<FeedbackStore>(Channels.Live),
    sp.GetRequiredKeyedService<IChatClient>(LlmServiceCollectionExtensions.SynthesisKey),
    sp.GetRequiredService<LlmGate>(),
    sp.GetRequiredKeyedService<ReportCache>(Channels.Live),
    sp.GetRequiredService<IOptions<ReportOptions>>(),
    sp.GetRequiredService<IActiveDomain>(),
    sp.GetRequiredService<ILogger<ReportService>>()));
// Correction telemetry reads BOTH channels: real desk corrections live in the live
// channel, while the corpus channel holds the other sources whose needs_review
// count is the ADR-0021 A2 injection-drift signal — either store alone goes blind.
builder.Services.AddSingleton(sp => new FeedbackIntelligence.Api.Telemetry.CorrectionTelemetryService(
    sp.GetRequiredService<FeedbackStore>(),
    sp.GetRequiredService<IOptions<IngestOptions>>(),
    sp.GetRequiredKeyedService<FeedbackStore>(Channels.Live)));

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
app.Services.GetRequiredKeyedService<FeedbackStore>(Channels.Live).Initialize();

// The public deployment sits behind a local tunnel daemon: without forwarded
// headers every request would arrive as loopback and the per-IP rate limit
// would collapse into one global bucket. Loopback proxies are trusted by default.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});
// Chrome Private Network Access: when the SWA (a PUBLIC origin) fetches this backend
// at a PRIVATE address — which happens when the operator opens the demo on a machine
// whose MagicDNS resolves the Funnel to a tailnet 100.x IP — Chrome sends an extra
// preflight and blocks the request ("Permission was denied") unless the target grants
// private-network access. Answer that preflight; the CORS allowlist below still gates
// which origins may actually read the response.
app.Use(async (ctx, next) =>
{
    if (HttpMethods.IsOptions(ctx.Request.Method) &&
        ctx.Request.Headers.ContainsKey("Access-Control-Request-Private-Network"))
    {
        // Grant private-network access ONLY to an allowlisted origin — the same
        // allowlist CORS enforces for response reads. An unlisted or absent Origin
        // gets no grant, so the PNA answer can't be handed out unconditionally.
        var origin = ctx.Request.Headers["Origin"].ToString();
        if (ingestConfig.AllowedCorsOrigins.Contains(origin, StringComparer.Ordinal))
            ctx.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
    }
    await next();
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

app.MapPost("/feedback", (
    FeedbackRequest request,
    IngestService ingest,
    IOptions<IngestOptions> options,
    IActiveDomain domain,
    CancellationToken ct) => IngestEndpoint(request, ingest, options.Value, domain, ct));

// The live desk channel's ingest (ADR-0024): identical contract, its own database.
// The desk UI posts here; the corpus loader keeps posting to /feedback, so seeded
// data and real desk entries can never mix.
app.MapPost("/live/feedback", (
    FeedbackRequest request,
    [FromKeyedServices(Channels.Live)] IngestService ingest,
    IOptions<IngestOptions> options,
    IActiveDomain domain,
    CancellationToken ct) => IngestEndpoint(request, ingest, options.Value, domain, ct));

// Desk-entry preview (Phase 3): interpretation shown BEFORE saving; nothing stored.
app.MapPost("/interpret", async (
    InterpretRequest request,
    IStructuringService structuring,
    LlmGate gate,
    IOptions<IngestOptions> options,
    ILoggerFactory loggerFactory,
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
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        throw; // genuine client disconnect — let the framework abort the request
    }
    catch (Exception ex)
    {
        // Parity with /feedback's resilience: an LLM outage (HttpRequestException),
        // a stalled model hitting the LlmGate timeout, or a prompt-load fault must
        // not leak a raw 500. /interpret is a preview that stores nothing, so there
        // is no structure_failed to persist — shed with a clean 503 instead.
        loggerFactory.CreateLogger("Interpret").LogWarning(ex, "Interpret failed; returning 503.");
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

app.MapGet("/feedback", (
    FeedbackStore store,
    IOptions<IngestOptions> options,
    CancellationToken ct,
    [FromQuery] string? from = null,
    [FromQuery] string? to = null,
    [FromQuery] int? limit = null) => QueryEndpoint(store, options.Value, from, to, limit, ct));

// The live desk channel's item list — the desk segment's "categorized stuff".
app.MapGet("/live/feedback", (
    [FromKeyedServices(Channels.Live)] FeedbackStore store,
    IOptions<IngestOptions> options,
    CancellationToken ct,
    [FromQuery] string? from = null,
    [FromQuery] string? to = null,
    [FromQuery] int? limit = null) => QueryEndpoint(store, options.Value, from, to, limit, ct));

// The management view: two-layer analysis over a selectable window. Always
// renders (deterministic layer 1); LLM narratives/nominations degrade to
// deterministic fallbacks. A snapshot is persisted only when `?snapshot=true`.
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
    return await ReportEndpoint(reports, reportOptions.Value, from, to, snapshot, ct);
});

// The live desk channel's report — the desk segment's grounded summary. Never
// persists a snapshot: the shared-link fallback page belongs to the demo channel.
app.MapGet("/live/report", async (
    [FromKeyedServices(Channels.Live)] ReportService reports,
    IOptions<ReportOptions> reportOptions,
    CancellationToken ct,
    [FromQuery] string? from = null,
    [FromQuery] string? to = null) =>
    await ReportEndpoint(reports, reportOptions.Value, from, to, persistSnapshot: false, ct));

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
    // Shared window parse/validate with /report (parity is load-bearing: an inverted
    // window here would read as "model perfect, zero corrections" — garbage must 400).
    if (TryBuildWindow(from, to, reportOptions.Value, out var fromNormalized, out var toNormalized, out _, out _) is { } badWindow)
        return badWindow;
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

// One ingest contract, two channels: /feedback (corpus/demo) and /live/feedback
// (desk, ADR-0024) share this body so validation and failure semantics can't drift.
static async Task<IResult> IngestEndpoint(
    FeedbackRequest request, IngestService ingest, IngestOptions options, IActiveDomain domain, CancellationToken ct)
{
    var errors = RequestValidator.Validate(request, options, domain.Descriptor);
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
}

// Shared list body for /feedback and /live/feedback (GET). Window bounds normalize
// to the same UTC round-trip format the store uses, so lexical range filtering is
// instant-correct across offsets.
static async Task<IResult> QueryEndpoint(
    FeedbackStore store, IngestOptions options, string? from, string? to, int? limit, CancellationToken ct)
{
    string? fromNormalized = null, toNormalized = null;
    if (from is not null && !TimestampNormalizer.TryNormalize(from, out fromNormalized!))
        return Results.BadRequest(new { errors = new[] { $"from must be ISO-8601, got '{from}'." } });
    if (to is not null && !TimestampNormalizer.TryNormalize(to, out toNormalized!))
        return Results.BadRequest(new { errors = new[] { $"to must be ISO-8601, got '{to}'." } });
    var effectiveLimit = Math.Clamp(limit ?? options.QueryDefaultLimit, 1, options.QueryMaxLimit);
    return Results.Ok(await store.QueryAsync(fromNormalized, toNormalized, effectiveLimit, ct));
}

// Shared report body for /report and /live/report.
static async Task<IResult> ReportEndpoint(
    ReportService reports, ReportOptions options, string? from, string? to, bool persistSnapshot, CancellationToken ct)
{
    if (TryBuildWindow(from, to, options, out var fromNormalized, out var toNormalized, out var fromInstant, out var toInstant) is { } badWindow)
        return badWindow;

    // The 10-minute bucket is the CACHE KEY only, so repeated browser loads (each
    // with a slightly different `to=now`) share ONE cached report instead of each
    // firing a fresh ~40 s synthesis. The store QUERY uses the exact validated
    // window: the report never claims a window end in the future, a fresh entry is
    // inside the window on the very next refresh, and /report shares its window
    // semantics with /telemetry/corrections. Within-bucket freshness is preserved
    // by ingest invalidation (a cache hit proves no ingest happened), not TTL.
    var bucket = TimeSpan.FromMinutes(10).Ticks;
    var cacheKey = $"{fromInstant.UtcTicks / bucket}|{toInstant.UtcTicks / bucket}";
    return Results.Ok(await reports.GenerateAsync(fromNormalized, toNormalized, ct, persistSnapshot, cacheKey));
}

// Parse + validate a report/telemetry query window: default `to` to now and `from`
// to now-DefaultWindowDays, normalise both to the store's UTC round-trip form, and
// reject an inverted or over-long window. Returns a 400 IResult on failure, or null
// on success with the normalised strings and parsed instants written to the outs.
// Single source of truth so /report and /telemetry/corrections can't drift.
static IResult? TryBuildWindow(
    string? from, string? to, ReportOptions ro,
    out string fromNormalized, out string toNormalized,
    out DateTimeOffset fromInstant, out DateTimeOffset toInstant)
{
    fromNormalized = string.Empty;
    fromInstant = default;
    toInstant = default;

    var toRaw = to ?? DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    if (!TimestampNormalizer.TryNormalize(toRaw, out toNormalized))
        return Results.BadRequest(new { errors = new[] { $"to must be ISO-8601, got '{to}'." } });
    toInstant = DateTimeOffset.Parse(toNormalized, CultureInfo.InvariantCulture);

    var fromRaw = from ?? toInstant.AddDays(-ro.DefaultWindowDays).ToString("O", CultureInfo.InvariantCulture);
    if (!TimestampNormalizer.TryNormalize(fromRaw, out fromNormalized))
        return Results.BadRequest(new { errors = new[] { $"from must be ISO-8601, got '{from}'." } });
    fromInstant = DateTimeOffset.Parse(fromNormalized, CultureInfo.InvariantCulture);

    if (fromInstant >= toInstant || (toInstant - fromInstant).TotalDays > ro.MaxWindowDays)
        return Results.BadRequest(new { errors = new[] { $"window must be positive and at most {ro.MaxWindowDays} days." } });

    return null;
}
