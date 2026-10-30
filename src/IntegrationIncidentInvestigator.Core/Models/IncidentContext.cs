namespace IntegrationIncidentInvestigator.Core.Models;

public sealed record IncidentContext(
    string SourceFolder,
    string IncidentId,
    string ProblemDescription,
    IReadOnlyList<IncidentFile> Files,
    string? SystemName = null,
    string? IntegrationName = null);
