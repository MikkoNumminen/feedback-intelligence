using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeedbackIntelligence.StructuringEval;

/// <summary>One line of data/eval/structuring-inputs.jsonl (format: data/eval/README.md).</summary>
public sealed record EvalInput(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("timestamp")] string Timestamp)
{
    public static List<EvalInput> LoadJsonl(string path)
    {
        var items = new List<EvalInput>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var lineNo = 0;

        foreach (var line in File.ReadLines(path))
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            EvalInput? item;
            try
            {
                item = JsonSerializer.Deserialize<EvalInput>(line);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"{path}:{lineNo}: invalid JSON — {ex.Message}");
            }

            if (item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Text))
                throw new InvalidDataException($"{path}:{lineNo}: 'id' and 'text' are required.");
            if (!seenIds.Add(item.Id))
                throw new InvalidDataException($"{path}:{lineNo}: duplicate id '{item.Id}'.");

            items.Add(item);
        }

        return items;
    }
}
