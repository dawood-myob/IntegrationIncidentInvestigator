namespace IntegrationIncidentInvestigator.Core.Models;

public sealed record HistoricalIncident(
    string IncidentId,
    DateTime CreatedUtc,
    string Title,
    string SystemName,
    string IntegrationName,
    string ErrorFingerprint,
    string ErrorSignature,
    string RootCause,
    string? JiraKey,
    string? JiraUrl,
    string Status,
    string Keywords,
    string Resolution);
