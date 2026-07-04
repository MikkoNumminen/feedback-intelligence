using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeedbackIntelligence.Generator;

/// <summary>
/// One corpus JSONL line — shared shape for the hand-written core, the
/// committed LLM variants, and the generated output. `story` tags an item as
/// raw material for a planted story (must match a story id in the active domain's
/// stories.json); it is NEVER written into generated output — the analyzer meets
/// the data cold.
/// </summary>
public sealed record CorpusItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("timestamp")] string? Timestamp = null,
    [property: JsonPropertyName("story")] string? Story = null,
    [property: JsonPropertyName("sourceId")] string? SourceId = null,
    [property: JsonPropertyName("sequence")] int? Sequence = null)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Finnish text stays readable in committed corpora — no \uXXXX escapes.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static List<CorpusItem> LoadJsonl(string path)
    {
        var items = new List<CorpusItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lineNo = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            CorpusItem? item;
            try
            {
                item = JsonSerializer.Deserialize<CorpusItem>(line);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"{path}:{lineNo}: invalid JSON — {ex.Message}");
            }
            if (item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Text))
                throw new InvalidDataException($"{path}:{lineNo}: 'id' and 'text' are required.");
            if (!seen.Add(item.Id))
                throw new InvalidDataException($"{path}:{lineNo}: duplicate id '{item.Id}'.");
            items.Add(item);
        }
        return items;
    }

    public static async Task SaveJsonlAsync(string path, IEnumerable<CorpusItem> items, CancellationToken ct)
    {
        var lines = items.Select(i => JsonSerializer.Serialize(i, Options));
        await File.WriteAllLinesAsync(path, lines, ct);
    }
}
