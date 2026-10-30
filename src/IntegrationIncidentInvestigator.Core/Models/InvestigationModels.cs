namespace IntegrationIncidentInvestigator.Core.Models;

public enum EvidenceConfidence
{
    Unknown,
    Possible,
    Likely,
    Confirmed
}

public enum RecurrenceStatus
{
    NewIssue,
    PossibleRecurrence,
    ConfirmedRecurrence
}

public enum JiraMode
{
    CreateNew,
    CommentOnExisting
}

public sealed record HistoricalMatch(
    HistoricalIncident Incident,
    double Similarity,
    IReadOnlyList<string> Reasons);

public sealed record AttachmentRecommendation(
    string FileName,
    bool Recommended,
    string Reason);

public sealed record IncidentInvestigationResult(
    string IncidentSummary,
    string TeamsSummary,
    string ProblemStatement,
    string RootCause,
    EvidenceConfidence RootCauseConfidence,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> RecommendedActions,
    IReadOnlyList<string> AffectedSystems,
    IReadOnlyList<string> ErrorMessages,
    IReadOnlyList<string> CorrelationIds,
    IReadOnlyList<AttachmentRecommendation> Attachments,
    RecurrenceStatus Recurrence,
    IReadOnlyList<HistoricalMatch> HistoricalMatches,
    string? ExistingIssueReference,
    string JiraContent,
    string JiraComment,
    string ErrorSignature,
    string ErrorFingerprint,
    DateTime CreatedUtc);

public sealed record InvestigationProgress(string Stage, string Message, int CompletedSteps, int TotalSteps);
