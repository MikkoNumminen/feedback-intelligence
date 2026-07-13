using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FeedbackIntelligence.Core.Domain;

/// <summary>The loaded active domain module. Resolves data files under
/// <c>&lt;Root&gt;/&lt;Active&gt;/</c> and exposes the taxonomy + the paths of the
/// domain's alert lexicon and story definitions.</summary>
public interface IActiveDomain
{
    DomainDescriptor Descriptor { get; }
    string Name { get; }
    string AlertKeywordsPath { get; }
    string StoriesPath { get; }

    /// <summary>Domain-owned prompt files by role (e.g. "synthesis",
    /// "alertNomination"), resolved to absolute paths. Prompts that carry a
    /// domain's voice/language live in the domain module, not the neutral core.</summary>
    IReadOnlyDictionary<string, string> PromptPaths { get; }

    /// <summary>Absolute path of a domain prompt by role; throws if the active
    /// domain does not declare it.</summary>
    string PromptPath(string role);
}

public sealed class ActiveDomain : IActiveDomain
{
    public DomainDescriptor Descriptor { get; }
    public string Name => Descriptor.Name;
    public string AlertKeywordsPath { get; }
    public string StoriesPath { get; }
    public IReadOnlyDictionary<string, string> PromptPaths { get; }

    public ActiveDomain(IOptions<DomainOptions> options)
    {
        var o = options.Value;
        var dir = ResolveDir(o.Root, o.Active);
        var domainJson = Path.Combine(dir, "domain.json");
        if (!File.Exists(domainJson))
            throw new InvalidOperationException($"Domain descriptor not found: {domainJson}");

        // Parse domain.json ONCE — the descriptor and the prompt map both read it,
        // and a double read opens a (small) window for them to disagree.
        using var doc = JsonDocument.Parse(File.ReadAllText(domainJson));
        var root = doc.RootElement;

        Descriptor = BuildDescriptor(root, o.Active, domainJson);
        AlertKeywordsPath = Path.Combine(dir, "alert-keywords.json");
        StoriesPath = Path.Combine(dir, "stories.json");
        PromptPaths = BuildPromptPaths(root, dir);
    }

    public string PromptPath(string role) =>
        PromptPaths.TryGetValue(role, out var path)
            ? path
            : throw new InvalidOperationException(
                $"Domain '{Name}' declares no '{role}' prompt (domain.json 'prompts' map).");

    /// <summary>Resolve the domain folder against the working directory first
    /// (repo checkout), then the binary's own directory. Returns an ABSOLUTE path
    /// so every derived data-file and prompt path honors the absolute contract.</summary>
    private static string ResolveDir(string root, string active)
    {
        var cwd = Path.Combine(root, active);
        if (Directory.Exists(cwd))
            return Path.GetFullPath(cwd);
        var beside = Path.Combine(AppContext.BaseDirectory, root, active);
        if (Directory.Exists(beside))
            return Path.GetFullPath(beside);
        throw new InvalidOperationException(
            $"Domain module '{active}' not found under '{root}/' (cwd: {Environment.CurrentDirectory}).");
    }

    /// <summary>Loads a descriptor from a domain.json path (reads + parses the
    /// file). Kept for callers that only need the descriptor (tests); the runtime
    /// constructor parses once and calls <see cref="BuildDescriptor"/> directly.</summary>
    public static DomainDescriptor LoadDescriptor(string domainJsonPath, string fallbackName)
    {
        if (!File.Exists(domainJsonPath))
            throw new InvalidOperationException($"Domain descriptor not found: {domainJsonPath}");
        using var doc = JsonDocument.Parse(File.ReadAllText(domainJsonPath));
        return BuildDescriptor(doc.RootElement, fallbackName, domainJsonPath);
    }

    private static DomainDescriptor BuildDescriptor(JsonElement root, string fallbackName, string sourcePath)
    {
        var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString()! : fallbackName;
        // Output language: domain property, English default (retail overrides to fi).
        var language = root.TryGetProperty("language", out var lg) && lg.ValueKind == JsonValueKind.String
            ? lg.GetString()! : "en";
        var label = root.TryGetProperty("categoryFieldLabel", out var l) && l.ValueKind == JsonValueKind.String
            ? l.GetString()! : "category";

        var categories = ReadMap(root, "categories")
            ?? throw new InvalidOperationException($"{sourcePath}: 'categories' is required.");
        if (categories.Count == 0)
            throw new InvalidOperationException($"{sourcePath}: 'categories' must be non-empty.");

        // A missing OR EMPTY severities/types map falls back to the core defaults —
        // an empty "{}" must not silently produce an empty enum set (which would
        // reject every item at structuring time).
        var severities = ReadMap(root, "severities") is { Count: > 0 } sev
            ? sev : new Dictionary<string, string>(CoreDefaults.Severities, StringComparer.Ordinal);
        var types = ReadMap(root, "types") is { Count: > 0 } typ
            ? typ : new Dictionary<string, string>(CoreDefaults.Types, StringComparer.Ordinal);

        // Ingest channels are domain-specific (no universal default), so required.
        var sources = ReadStringArray(root, "sources")
            ?? throw new InvalidOperationException($"{sourcePath}: 'sources' is required.");
        if (sources.Count == 0)
            throw new InvalidOperationException($"{sourcePath}: 'sources' must be non-empty.");

        // Optional per-category hints for the STRUCTURING PROMPT only: the label
        // stays short for UIs, the hint tells the model what belongs in a
        // non-obvious category (e.g. "asiaton"). Hints for unknown keys are
        // rejected — a typo would otherwise silently guide nothing.
        var hints = ReadMap(root, "categoryHints") ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var unknownHints = hints.Keys.Where(k => !categories.ContainsKey(k)).ToList();
        if (unknownHints.Count > 0)
            throw new InvalidOperationException(
                $"{sourcePath}: 'categoryHints' has key(s) not in 'categories': {string.Join(", ", unknownHints)}.");

        // Optional catch-all key (retail's "muu") — must be a real category, or
        // the live summary would silently never split emergent topics.
        var catchAll = root.TryGetProperty("catchAllCategory", out var ca) && ca.ValueKind == JsonValueKind.String
            ? ca.GetString() : null;
        if (catchAll is not null && !categories.ContainsKey(catchAll))
            throw new InvalidOperationException(
                $"{sourcePath}: 'catchAllCategory' ('{catchAll}') is not in 'categories'.");

        // Optional presentation demotion (views sort these last); a typo'd key
        // would silently demote nothing, so reject it like the hints above.
        var demoted = ReadStringArray(root, "demotedCategories") ?? [];
        var unknownDemoted = demoted.Where(k => !categories.ContainsKey(k)).ToList();
        if (unknownDemoted.Count > 0)
            throw new InvalidOperationException(
                $"{sourcePath}: 'demotedCategories' has key(s) not in 'categories': {string.Join(", ", unknownDemoted)}.");

        return new DomainDescriptor
        {
            Name = name,
            Language = language,
            CategoryFieldLabel = label,
            Categories = categories.Keys.ToHashSet(StringComparer.Ordinal),
            Severities = severities.Keys.ToHashSet(StringComparer.Ordinal),
            Types = types.Keys.ToHashSet(StringComparer.Ordinal),
            CategoryLabels = categories,
            SeverityLabels = severities,
            TypeLabels = types,
            Sources = sources,
            CategoryHints = hints,
            CatchAllCategory = catchAll,
            DemotedCategories = demoted,
        };
    }

    /// <summary>Reads a JSON array of strings (order preserved); null if absent
    /// or not an array.</summary>
    private static List<string>? ReadStringArray(JsonElement root, string prop)
    {
        if (!root.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.Array)
            return null;
        return el.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    /// <summary>Reads the optional "prompts" role→file map and resolves each file
    /// relative to the (absolute) domain directory. Absent map = no domain prompts.</summary>
    private static IReadOnlyDictionary<string, string> BuildPromptPaths(JsonElement root, string dir)
    {
        var map = ReadMap(root, "prompts");
        if (map is null)
            return new Dictionary<string, string>(StringComparer.Ordinal);
        return map.ToDictionary(p => p.Key, p => Path.Combine(dir, p.Value), StringComparer.Ordinal);
    }

    private static Dictionary<string, string>? ReadMap(JsonElement root, string prop)
    {
        if (!root.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.Object)
            return null;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in el.EnumerateObject())
            map[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Name;
        return map;
    }
}
