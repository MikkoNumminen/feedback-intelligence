using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using FeedbackIntelligence.Core.Alerts;
using FeedbackIntelligence.Core.Structuring;

namespace FeedbackIntelligence.Api.Storage;

/// <summary>One row per feedback item: raw text always preserved; structure is
/// the AI's OUTPUT stored as a JSON column — deliberately no normalized
/// category hierarchy (see ADR-0008).</summary>
public sealed record StoredFeedback(
    string Id,
    string Source,
    string Text,
    string Timestamp,
    string CreatedAt,
    FeedbackStructure? Structure,
    bool StructureFailed,
    bool ModelFailed,
    IReadOnlyList<string> SalvageNotes,
    IReadOnlyList<AlertHit> Alerts,
    IReadOnlyList<FieldCorrection>? Corrections);

public sealed class DuplicateFeedbackIdException(string id)
    : Exception($"Feedback id '{id}' already exists.")
{
    public string Id { get; } = id;
}

public sealed class FeedbackStore(IOptions<IngestOptions> options)
{
    private const int SqliteConstraintErrorCode = 19;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string ConnectionString => $"Data Source={options.Value.DbPath}";

    public void Initialize()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(options.Value.DbPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS feedback (
                id TEXT PRIMARY KEY,
                source TEXT NOT NULL,
                text TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                created_at TEXT NOT NULL,
                structure_json TEXT NULL,
                structure_failed INTEGER NOT NULL DEFAULT 0,
                model_failed INTEGER NOT NULL DEFAULT 0,
                salvage_notes_json TEXT NOT NULL DEFAULT '[]',
                alerts_json TEXT NOT NULL DEFAULT '[]',
                corrections_json TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_feedback_timestamp ON feedback(timestamp);
            """;
        command.ExecuteNonQuery();
    }

    public async Task InsertAsync(StoredFeedback item, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO feedback
                (id, source, text, timestamp, created_at, structure_json, structure_failed,
                 model_failed, salvage_notes_json, alerts_json, corrections_json)
            VALUES ($id, $source, $text, $timestamp, $createdAt, $structure, $failed, $modelFailed, $notes, $alerts, $corrections)
            """;
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$source", item.Source);
        command.Parameters.AddWithValue("$text", item.Text);
        command.Parameters.AddWithValue("$timestamp", item.Timestamp);
        command.Parameters.AddWithValue("$createdAt", item.CreatedAt);
        command.Parameters.AddWithValue("$structure",
            item.Structure is null ? DBNull.Value : JsonSerializer.Serialize(item.Structure, Json));
        command.Parameters.AddWithValue("$failed", item.StructureFailed ? 1 : 0);
        command.Parameters.AddWithValue("$modelFailed", item.ModelFailed ? 1 : 0);
        command.Parameters.AddWithValue("$notes", JsonSerializer.Serialize(item.SalvageNotes, Json));
        command.Parameters.AddWithValue("$alerts", JsonSerializer.Serialize(item.Alerts, Json));
        command.Parameters.AddWithValue("$corrections",
            item.Corrections is null ? DBNull.Value : JsonSerializer.Serialize(item.Corrections, Json));
        try
        {
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraintErrorCode)
        {
            // Race-safe complement to the ingest pre-check: a concurrent retry
            // with the same client id maps to 409, never a 500.
            throw new DuplicateFeedbackIdException(item.Id);
        }
    }

    public async Task<StoredFeedback?> GetAsync(string id, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM feedback WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<List<StoredFeedback>> QueryAsync(
        string? fromIso, string? toIso, int limit, CancellationToken ct, string? source = null)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM feedback
            WHERE ($from IS NULL OR timestamp >= $from)
              AND ($to IS NULL OR timestamp <= $to)
              AND ($source IS NULL OR source = $source)
            ORDER BY timestamp DESC
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$from", (object?)fromIso ?? DBNull.Value);
        command.Parameters.AddWithValue("$to", (object?)toIso ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", (object?)source ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);
        var items = new List<StoredFeedback>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(Map(reader));
        return items;
    }

    private static StoredFeedback Map(SqliteDataReader reader)
    {
        string? Read(string column) =>
            reader[column] is DBNull ? null : (string)reader[column];

        return new StoredFeedback(
            (string)reader["id"],
            (string)reader["source"],
            (string)reader["text"],
            (string)reader["timestamp"],
            (string)reader["created_at"],
            Read("structure_json") is { } s ? JsonSerializer.Deserialize<FeedbackStructure>(s, Json) : null,
            (long)reader["structure_failed"] != 0,
            (long)reader["model_failed"] != 0,
            JsonSerializer.Deserialize<List<string>>((string)reader["salvage_notes_json"], Json) ?? [],
            JsonSerializer.Deserialize<List<AlertHit>>((string)reader["alerts_json"], Json) ?? [],
            Read("corrections_json") is { } c ? JsonSerializer.Deserialize<List<FieldCorrection>>(c, Json) : null);
    }
}
