using System.Globalization;
using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Core.Models;
using Microsoft.Data.Sqlite;

namespace IntegrationIncidentInvestigator.Infrastructure.Services;

public sealed class SqliteIncidentHistoryRepository : IIncidentHistoryRepository
{
    private readonly string _connectionString;

    public SqliteIncidentHistoryRepository(string databasePath)
    {
        var resolvedPath = Path.IsPathRooted(databasePath)
            ? databasePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntegrationIncidentInvestigator", databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = resolvedPath }.ToString();
    }

    public async Task<IReadOnlyList<HistoricalIncident>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureTableAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT IncidentId, CreatedUtc, Title, SystemName, IntegrationName, ErrorFingerprint, ErrorSignature, RootCause, JiraKey, JiraUrl, Status, Keywords, Resolution FROM Incidents ORDER BY CreatedUtc DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 10_000));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var incidents = new List<HistoricalIncident>();
        while (await reader.ReadAsync(cancellationToken))
        {
            incidents.Add(new HistoricalIncident(reader.GetString(0), DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetString(10), reader.GetString(11), reader.GetString(12)));
        }

        return incidents;
    }

    public async Task SaveAsync(HistoricalIncident incident, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureTableAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Incidents (IncidentId, CreatedUtc, Title, SystemName, IntegrationName, ErrorFingerprint, ErrorSignature, RootCause, JiraKey, JiraUrl, Status, Keywords, Resolution)
            VALUES ($id, $date, $title, $system, $integration, $fingerprint, $signature, $cause, $jiraKey, $jiraUrl, $status, $keywords, $resolution)
            ON CONFLICT(IncidentId) DO UPDATE SET CreatedUtc=$date, Title=$title, SystemName=$system, IntegrationName=$integration,
                ErrorFingerprint=$fingerprint, ErrorSignature=$signature, RootCause=$cause,
                JiraKey=COALESCE($jiraKey, Incidents.JiraKey), JiraUrl=COALESCE($jiraUrl, Incidents.JiraUrl),
                Status=$status, Keywords=$keywords, Resolution=$resolution;
            """;
        command.Parameters.AddWithValue("$id", incident.IncidentId);
        command.Parameters.AddWithValue("$date", incident.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$title", incident.Title);
        command.Parameters.AddWithValue("$system", incident.SystemName);
        command.Parameters.AddWithValue("$integration", incident.IntegrationName);
        command.Parameters.AddWithValue("$fingerprint", incident.ErrorFingerprint);
        command.Parameters.AddWithValue("$signature", incident.ErrorSignature);
        command.Parameters.AddWithValue("$cause", incident.RootCause);
        command.Parameters.AddWithValue("$jiraKey", (object?)incident.JiraKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$jiraUrl", (object?)incident.JiraUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", incident.Status);
        command.Parameters.AddWithValue("$keywords", incident.Keywords);
        command.Parameters.AddWithValue("$resolution", incident.Resolution);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateJiraReferenceAsync(string incidentId, string jiraKey, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureTableAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Incidents SET JiraKey=$jiraKey WHERE IncidentId=$id;";
        command.Parameters.AddWithValue("$jiraKey", jiraKey);
        command.Parameters.AddWithValue("$id", incidentId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new KeyNotFoundException("The investigated incident was not found in local history.");
        }
    }

    private static async Task EnsureTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Incidents (
                IncidentId TEXT PRIMARY KEY,
                CreatedUtc TEXT NOT NULL,
                Title TEXT NOT NULL,
                SystemName TEXT NOT NULL,
                IntegrationName TEXT NOT NULL,
                ErrorFingerprint TEXT NOT NULL,
                ErrorSignature TEXT NOT NULL,
                RootCause TEXT NOT NULL,
                JiraKey TEXT NULL,
                JiraUrl TEXT NULL,
                Status TEXT NOT NULL,
                Keywords TEXT NOT NULL,
                Resolution TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
