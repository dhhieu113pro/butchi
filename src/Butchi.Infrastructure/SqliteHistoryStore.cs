using System.Text.Json;
using Butchi.Core.History;
using Microsoft.Data.Sqlite;

namespace Butchi.Infrastructure;

public sealed class SqliteHistoryStore
{
    private const int DefaultLimit = 200;
    private const int MaxLimit = 500;
    private readonly AppPaths _paths;

    public SqliteHistoryStore(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await ImportLegacyJsonAsync(connection, cancellationToken);
    }

    public async Task AppendAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO history (id, ts, action, source, result, message, target_language) VALUES ($id, $ts, $action, $source, $result, $message, $language);";
        AddEntryParameters(command, entry);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HistoryEntry>> SearchAsync(
        string? query = null,
        string? action = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await ImportLegacyJsonAsync(connection, cancellationToken);

        var normalizedQuery = query?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedAction = action?.Trim().ToLowerInvariant() ?? string.Empty;
        var pattern = $"%{normalizedQuery}%";
        var clampedLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, ts, action, source, result, message, target_language
            FROM history
            WHERE ($query = '' OR lower(source) LIKE $pattern OR lower(result) LIKE $pattern)
              AND ($action = '' OR action = $action)
            ORDER BY ts DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$query", normalizedQuery);
        command.Parameters.AddWithValue("$pattern", pattern);
        command.Parameters.AddWithValue("$action", normalizedAction);
        command.Parameters.AddWithValue("$limit", clampedLimit);

        var results = new List<HistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new HistoryEntry(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return results;
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM history WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM history;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ApplyRetentionAsync(int retentionDays, long nowMs, CancellationToken cancellationToken = default)
    {
        if (retentionDays < 0)
        {
            return;
        }

        _paths.EnsureDirectories();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        if (retentionDays == 0)
        {
            command.CommandText = "DELETE FROM history;";
        }
        else
        {
            var retentionMs = (long)retentionDays * 86_400_000L;
            var cutoff = Math.Max(0L, nowMs - retentionMs);
            command.CommandText = "DELETE FROM history WHERE ts < $cutoff;";
            command.Parameters.AddWithValue("$cutoff", cutoff);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.HistoryDbPath,
            Pooling = false
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS history (
                id TEXT PRIMARY KEY,
                ts INTEGER NOT NULL,
                action TEXT NOT NULL,
                source TEXT NOT NULL,
                result TEXT NOT NULL,
                message TEXT NOT NULL,
                target_language TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_history_ts ON history(ts DESC);
            CREATE INDEX IF NOT EXISTS idx_history_action ON history(action);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private async Task ImportLegacyJsonAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.LegacyHistoryPath))
        {
            return;
        }

        var raw = await File.ReadAllTextAsync(_paths.LegacyHistoryPath, cancellationToken);
        var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var entry in entries)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT OR IGNORE INTO history (id, ts, action, source, result, message, target_language) VALUES ($id, $ts, $action, $source, $result, $message, $language);";
                AddEntryParameters(command, entry);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            File.Move(_paths.LegacyHistoryPath, _paths.MigratedLegacyHistoryPath, overwrite: true);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void AddEntryParameters(SqliteCommand command, HistoryEntry entry)
    {
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$ts", entry.TimestampMs);
        command.Parameters.AddWithValue("$action", entry.Action);
        command.Parameters.AddWithValue("$source", entry.Source);
        command.Parameters.AddWithValue("$result", entry.Result);
        command.Parameters.AddWithValue("$message", entry.Message);
        command.Parameters.AddWithValue("$language", (object?)entry.TargetLanguage ?? DBNull.Value);
    }
}
