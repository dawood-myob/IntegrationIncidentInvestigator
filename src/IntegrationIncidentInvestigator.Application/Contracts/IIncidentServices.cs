using IntegrationIncidentInvestigator.Application.Models;
using IntegrationIncidentInvestigator.Core.Models;

namespace IntegrationIncidentInvestigator.Application.Contracts;

public interface IIncidentFileScanner
{
    Task<IReadOnlyList<IncidentFile>> ScanAsync(string sourceFolder, CancellationToken cancellationToken);
}

public interface IFileContentExtractor
{
    Task<EvidenceExcerpt> ExtractAsync(IncidentFile file, int maximumFileBytes, int maximumCharacters, CancellationToken cancellationToken);
}

public interface IIncidentHistoryRepository
{
    Task<IReadOnlyList<HistoricalIncident>> GetRecentAsync(int limit, CancellationToken cancellationToken);
    Task SaveAsync(HistoricalIncident incident, CancellationToken cancellationToken);
    Task UpdateJiraReferenceAsync(string incidentId, string jiraKey, CancellationToken cancellationToken);
}

public interface IIncidentInvestigationService
{
    Task<PreparedInvestigation> PrepareAsync(
        IncidentContext context,
        IProgress<InvestigationProgress>? progress,
        CancellationToken cancellationToken);

    Task<IncidentInvestigationResult> InvestigateAsync(
        PreparedInvestigation prepared,
        IProgress<InvestigationProgress>? progress,
        CancellationToken cancellationToken);

    Task UpdateJiraReferenceAsync(string incidentId, string jiraKey, CancellationToken cancellationToken);
}

public interface IInvestigationPromptBuilder
{
    LlmRequest BuildInvestigationRequest(IncidentContext context, IReadOnlyList<EvidenceExcerpt> evidence,
        IReadOnlyList<HistoricalMatch> historicalMatches);
}

public interface ILlmClient
{
    Task<string> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}

public interface IInvestigationArtifactWriter
{
    string GetJiraText(IncidentInvestigationResult result, JiraMode jiraMode);

    Task SaveAsync(string sourceFolder, IncidentInvestigationResult result, JiraMode jiraMode,
        CancellationToken cancellationToken);
}
