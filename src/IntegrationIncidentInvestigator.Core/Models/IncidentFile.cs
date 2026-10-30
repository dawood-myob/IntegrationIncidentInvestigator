namespace IntegrationIncidentInvestigator.Core.Models;

public sealed record IncidentFile(
    string FullPath,
    string Name,
    long SizeBytes,
    DateTime LastModifiedUtc,
    FileCategory Category,
    bool IsLikelyRelevant,
    string RelevanceReason,
    bool IncludeInInvestigation = true,
    bool RecommendAttachment = false,
    string? ScanError = null);
