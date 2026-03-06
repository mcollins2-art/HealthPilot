using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;

namespace HealthPilot.Api.DataPipeline;

public class Loader(ILogger<Loader> logger)
{
    public async Task InitializeSchemaAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        await EnsureOpenAsync(connection, cancellationToken);

        var commands = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS providers (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                city TEXT NOT NULL,
                state TEXT NOT NULL,
                UNIQUE(name, city, state)
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS procedures (
                cpt_code TEXT PRIMARY KEY,
                description TEXT NOT NULL,
                category TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS rates (
                provider_id INTEGER NOT NULL,
                cpt_code TEXT NOT NULL,
                payer TEXT NOT NULL,
                negotiated_rate NUMERIC NULL,
                cash_price NUMERIC NULL,
                location TEXT NOT NULL,
                UNIQUE(provider_id, cpt_code, payer, location),
                FOREIGN KEY(provider_id) REFERENCES providers(id),
                FOREIGN KEY(cpt_code) REFERENCES procedures(cpt_code)
            );
            """,
            "CREATE INDEX IF NOT EXISTS idx_rates_cpt_payer_location ON rates(cpt_code, payer, location);"
        };

        foreach (var sql in commands)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<int> LoadAsync(
        DbConnection connection,
        IAsyncEnumerable<TransparencyRecord> records,
        int batchSize = 500,
        CancellationToken cancellationToken = default)
    {
        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        await InitializeSchemaAsync(connection, cancellationToken);

        var ingested = 0;
        var batch = new List<TransparencyRecord>(batchSize);
        await foreach (var record in records.WithCancellation(cancellationToken))
        {
            batch.Add(record);
            if (batch.Count >= batchSize)
            {
                ingested += await FlushBatchAsync(connection, batch, cancellationToken);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            ingested += await FlushBatchAsync(connection, batch, cancellationToken);
        }

        logger.LogInformation("Loaded {RowCount} pricing rows", ingested);
        return ingested;
    }

    private async Task<int> FlushBatchAsync(DbConnection connection, List<TransparencyRecord> batch, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var inserted = 0;

        foreach (var record in batch)
        {
            try
            {
                var procedure = Normalizer.NormalizeProcedure(record);
                if (procedure is null || string.IsNullOrWhiteSpace(record.HospitalName) || string.IsNullOrWhiteSpace(record.Payer))
                {
                    logger.LogWarning("Skipping incomplete transparency record for hospital '{HospitalName}'", record.HospitalName);
                    continue;
                }

                var location = string.IsNullOrWhiteSpace(record.Location) ? string.Empty : record.Location.Trim();
                var (city, state) = ParseLocation(location);

                var providerId = await UpsertProviderAsync(connection, transaction, record.HospitalName.Trim(), city, state, cancellationToken);
                await UpsertProcedureAsync(connection, transaction, procedure, cancellationToken);
                await UpsertRateAsync(connection, transaction, providerId, procedure.CptCode, record.Payer.Trim(), record.NegotiatedRate, record.CashPrice, location, cancellationToken);
                inserted++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed loading transparency record for hospital '{HospitalName}'", record.HospitalName);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    private static async Task<int> UpsertProviderAsync(
        DbConnection connection,
        DbTransaction transaction,
        string name,
        string city,
        string state,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO providers(name, city, state) VALUES (@name, @city, @state) ON CONFLICT(name, city, state) DO NOTHING;";
            AddParameter(command, "@name", name);
            AddParameter(command, "@city", city);
            AddParameter(command, "@state", state);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT id FROM providers WHERE name = @name AND city = @city AND state = @state;";
            AddParameter(command, "@name", name);
            AddParameter(command, "@city", city);
            AddParameter(command, "@state", state);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result);
        }
    }

    private static async Task UpsertProcedureAsync(
        DbConnection connection,
        DbTransaction transaction,
        NormalizedProcedure procedure,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO procedures(cpt_code, description, category) VALUES (@cpt, @description, @category) " +
            "ON CONFLICT(cpt_code) DO UPDATE SET description = excluded.description, category = excluded.category;";
        AddParameter(command, "@cpt", procedure.CptCode);
        AddParameter(command, "@description", procedure.Description);
        AddParameter(command, "@category", procedure.Category);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertRateAsync(
        DbConnection connection,
        DbTransaction transaction,
        int providerId,
        string cptCode,
        string payer,
        decimal? negotiatedRate,
        decimal? cashPrice,
        string location,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO rates(provider_id, cpt_code, payer, negotiated_rate, cash_price, location) " +
            "VALUES (@providerId, @cptCode, @payer, @negotiatedRate, @cashPrice, @location) " +
            "ON CONFLICT(provider_id, cpt_code, payer, location) DO UPDATE SET " +
            "negotiated_rate = excluded.negotiated_rate, cash_price = excluded.cash_price;";

        AddParameter(command, "@providerId", providerId);
        AddParameter(command, "@cptCode", cptCode);
        AddParameter(command, "@payer", payer);
        AddParameter(command, "@negotiatedRate", negotiatedRate ?? (object)DBNull.Value);
        AddParameter(command, "@cashPrice", cashPrice ?? (object)DBNull.Value);
        AddParameter(command, "@location", location);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static (string City, string State) ParseLocation(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return (string.Empty, string.Empty);
        }

        var parts = location.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2)
        {
            return (parts[^2], parts[^1].Length >= 2 ? parts[^1][..2].ToUpperInvariant() : parts[^1].ToUpperInvariant());
        }

        return (location, string.Empty);
    }

    private static async Task EnsureOpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }
}
