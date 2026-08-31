using System.Globalization;
using KCrashLab.Contracts;
using Microsoft.Data.Sqlite;

namespace KCrashLab.Storage;

public sealed class SqliteCampaignEventStore : ICampaignEventStore
{
    private readonly string connectionString;

    public SqliteCampaignEventStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Database path has no parent."));
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS campaign_events (
                campaign_id TEXT NOT NULL,
                sequence INTEGER NOT NULL CHECK (sequence > 0),
                schema_version INTEGER NOT NULL,
                event_id TEXT NOT NULL UNIQUE,
                from_state TEXT NOT NULL,
                to_state TEXT NOT NULL,
                reason TEXT NOT NULL,
                actor TEXT NOT NULL,
                occurred_utc TEXT NOT NULL,
                virtual_elapsed_ms INTEGER NOT NULL CHECK (virtual_elapsed_ms >= 0),
                correlation_id TEXT NOT NULL,
                PRIMARY KEY (campaign_id, sequence)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CampaignEvent>> ReadAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT schema_version, event_id, campaign_id, sequence, from_state, to_state,
                   reason, actor, occurred_utc, virtual_elapsed_ms, correlation_id
            FROM campaign_events
            WHERE campaign_id = $campaign_id
            ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$campaign_id", campaignId.ToString("D"));

        var result = new List<CampaignEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new CampaignEvent(
                reader.GetInt32(0),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetInt64(3),
                Enum.Parse<CampaignState>(reader.GetString(4), ignoreCase: false),
                Enum.Parse<CampaignState>(reader.GetString(5), ignoreCase: false),
                reader.GetString(6),
                reader.GetString(7),
                DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetInt64(9),
                Guid.Parse(reader.GetString(10))));
        }

        return result;
    }

    public async Task<bool> AppendAsync(CampaignEvent campaignEvent, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var duplicate = connection.CreateCommand())
        {
            duplicate.Transaction = (SqliteTransaction)transaction;
            duplicate.CommandText =
                """
                SELECT schema_version, event_id, campaign_id, sequence, from_state, to_state,
                       reason, actor, occurred_utc, virtual_elapsed_ms, correlation_id
                FROM campaign_events
                WHERE event_id = $event_id;
                """;
            duplicate.Parameters.AddWithValue("$event_id", campaignEvent.EventId.ToString("D"));
            await using var reader = await duplicate.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var stored = new CampaignEvent(
                    reader.GetInt32(0),
                    Guid.Parse(reader.GetString(1)),
                    Guid.Parse(reader.GetString(2)),
                    reader.GetInt64(3),
                    Enum.Parse<CampaignState>(reader.GetString(4), ignoreCase: false),
                    Enum.Parse<CampaignState>(reader.GetString(5), ignoreCase: false),
                    reader.GetString(6),
                    reader.GetString(7),
                    DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    reader.GetInt64(9),
                    Guid.Parse(reader.GetString(10)));
                var isSame = stored == campaignEvent;
                if (!isSame)
                {
                    throw new InvalidDataException("Event ID collision contains different event data.");
                }

                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }
        }

        await using (var sequence = connection.CreateCommand())
        {
            sequence.Transaction = (SqliteTransaction)transaction;
            sequence.CommandText = "SELECT COALESCE(MAX(sequence), 0) FROM campaign_events WHERE campaign_id = $campaign_id;";
            sequence.Parameters.AddWithValue("$campaign_id", campaignEvent.CampaignId.ToString("D"));
            var current = Convert.ToInt64(await sequence.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (campaignEvent.Sequence != current + 1)
            {
                throw new InvalidOperationException($"Expected sequence {current + 1}, received {campaignEvent.Sequence}.");
            }
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText =
                """
                INSERT INTO campaign_events (
                    campaign_id, sequence, schema_version, event_id, from_state, to_state,
                    reason, actor, occurred_utc, virtual_elapsed_ms, correlation_id)
                VALUES (
                    $campaign_id, $sequence, $schema_version, $event_id, $from_state, $to_state,
                    $reason, $actor, $occurred_utc, $virtual_elapsed_ms, $correlation_id);
                """;
            insert.Parameters.AddWithValue("$campaign_id", campaignEvent.CampaignId.ToString("D"));
            insert.Parameters.AddWithValue("$sequence", campaignEvent.Sequence);
            insert.Parameters.AddWithValue("$schema_version", campaignEvent.SchemaVersion);
            insert.Parameters.AddWithValue("$event_id", campaignEvent.EventId.ToString("D"));
            insert.Parameters.AddWithValue("$from_state", campaignEvent.FromState.ToString());
            insert.Parameters.AddWithValue("$to_state", campaignEvent.ToState.ToString());
            insert.Parameters.AddWithValue("$reason", campaignEvent.Reason);
            insert.Parameters.AddWithValue("$actor", campaignEvent.Actor);
            insert.Parameters.AddWithValue("$occurred_utc", campaignEvent.OccurredUtc.ToString("O", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$virtual_elapsed_ms", campaignEvent.VirtualElapsedMs);
            insert.Parameters.AddWithValue("$correlation_id", campaignEvent.CorrelationId.ToString("D"));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
