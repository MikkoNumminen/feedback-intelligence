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
    IReadOnlyList<FieldCorrection>? Corrections,
    // Injection hardening (ADR-0021 A2): the raw text showed prompt-injection
    // symptoms (and/or a model-assigned severe rating under those symptoms), so a
    // human should validate the structure. The item is never dropped — flagged and
    // preserved. Trailing/optional so no other construction site had to change.
    bool NeedsReview = false,
    IReadOnlyList<string>? ReviewFlags = null);

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
                corrections_json TEXT NULL,
                needs_review INTEGER NOT NULL DEFAULT 0,
                review_flags_json TEXT NOT NULL DEFAULT '[]'
            );
            CREATE INDEX IF NOT EXISTS ix_feedback_timestamp ON feedback(timestamp);
            """;
        command.ExecuteNonQuery();

        // Additive migration for DBs created before A2: CREATE TABLE IF NOT EXISTS
        // won't add columns to an existing table, so add them idempotently. SQLite
        // has no ADD COLUMN IF NOT EXISTS, so probe first.
        EnsureColumn(connection, "needs_review", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "review_flags_json", "TEXT NOT NULL DEFAULT '[]'");
    }

    private static void EnsureColumn(SqliteConnection connection, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('feedback') WHERE name = $name";
        check.Parameters.AddWithValue("$name", column);
        if (Convert.ToInt64(check.ExecuteScalar()) > 0)
            return;
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE feedback ADD COLUMN {column} {definition}";
        try
        {
            alter.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
            // Probe-then-ALTER isn't atomic: if a second process added the column
            // between our probe and here, ADD COLUMN throws "duplicate column name".
            // The column exists either way, which is all Initialize needs — swallow
            // it (same spirit as the InsertAsync duplicate-key catch).
        }
    }

    public async Task InsertAsync(StoredFeedback item, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO feedback
                (id, source, text, timestamp, created_at, structure_json, structure_failed,
                 model_failed, salvage_notes_json, alerts_json, corrections_json,
                 needs_review, review_flags_json)
            VALUES ($id, $source, $text, $timestamp, $createdAt, $structure, $failed, $modelFailed, $notes, $alerts, $corrections,
                 $needsReview, $reviewFlags)
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
        command.Parameters.AddWithValue("$needsReview", item.NeedsReview ? 1 : 0);
        command.Parameters.AddWithValue("$reviewFlags", JsonSerializer.Serialize(item.ReviewFlags ?? [], Json));
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

    /// <summary>Accurate count of needs_review items in the window across ALL
    /// sources (not the desk-only correction population) — a COUNT so the query cap
    /// can't undercount it. Backed by the timestamp index.</summary>
    public async Task<int> CountNeedsReviewAsync(string? fromIso, string? toIso, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM feedback
            WHERE needs_review = 1
              AND ($from IS NULL OR timestamp >= $from)
              AND ($to IS NULL OR timestamp <= $to)
            """;
        command.Parameters.AddWithValue("$from", (object?)fromIso ?? DBNull.Value);
        command.Parameters.AddWithValue("$to", (object?)toIso ?? DBNull.Value);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
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
            Read("corrections_json") is { } c ? JsonSerializer.Deserialize<List<FieldCorrection>>(c, Json) : null,
            (long)reader["needs_review"] != 0,
            JsonSerializer.Deserialize<List<string>>((string)reader["review_flags_json"], Json) ?? []);
    }
}
