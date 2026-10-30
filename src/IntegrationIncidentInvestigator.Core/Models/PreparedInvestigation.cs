namespace IntegrationIncidentInvestigator.Core.Models;

public sealed record PreparedInvestigation(
    IncidentContext Context,
    IReadOnlyList<EvidenceExcerpt> Evidence,
    IReadOnlyList<HistoricalMatch> HistoricalMatches,
    string ErrorSignature,
    string ErrorFingerprint);
