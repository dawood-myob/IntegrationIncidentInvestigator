using System.Text.RegularExpressions;
using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Application.Models;
using IntegrationIncidentInvestigator.Core.Models;
using IntegrationIncidentInvestigator.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntegrationIncidentInvestigator.Application.Services;

public sealed partial class IncidentInvestigationService(
    IFileContentExtractor extractor,
    IIncidentHistoryRepository historyRepository,
    IInvestigationPromptBuilder promptBuilder,
    ILlmClient llmClient,
    InvestigationResponseParser responseParser,
    ErrorFingerprinter fingerprinter,
    RecurrenceMatcher recurrenceMatcher,
    IOptions<InvestigationSettings> settings,
    ILogger<IncidentInvestigationService> logger) : IIncidentInvestigationService
{
    public async Task<PreparedInvestigation> PrepareAsync(
        IncidentContext context,
        IProgress<InvestigationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.SourceFolder);
        if (!Directory.Exists(context.SourceFolder))
        {
            throw new DirectoryNotFoundException("The selected incident folder no longer exists.");
        }

        var selectedFiles = context.Files.Where(file => file.IncludeInInvestigation).ToArray();
        if (selectedFiles.Length == 0 && string.IsNullOrWhiteSpace(context.ProblemDescription))
        {
            throw new InvalidOperationException("Select at least one evidence file or enter a problem description before investigating.");
        }

        Report(progress, "Classifying files", $"Reviewing {selectedFiles.Length} included files.", 1, 5);
        var maximumContext = Math.Clamp(settings.Value.MaxContextCharacters, 1_000, 1_000_000);
        var problemDescription = context.ProblemDescription.Length > maximumContext
            ? context.ProblemDescription[..maximumContext]
            : context.ProblemDescription;
        var remainingCharacters = Math.Max(0, maximumContext - problemDescription.Length);
        var fileLimitBytes = (int)Math.Min(int.MaxValue, Math.Max(1, settings.Value.MaxFileSizeMB) * 1024L * 1024L);
        var extracted = new List<EvidenceExcerpt>();

        var evidenceFiles = selectedFiles.Where(file => file.Category != FileCategory.ProblemDescription)
            .OrderBy(file => file.Category is FileCategory.ErrorOrException or FileCategory.Html ? 0
                : file.Category is FileCategory.IntegrationLog or FileCategory.ApplicationLog ? 1 : 2)
            .ToArray();
        for (var index = 0; index < evidenceFiles.Length; index++)
        {
            var file = evidenceFiles[index];
            cancellationToken.ThrowIfCancellationRequested();
            if (remainingCharacters <= 0)
            {
                extracted.Add(new EvidenceExcerpt(file.Name, file.Category, "",
                    "No content selected: the evidence text budget was exhausted. Increase the budget or exclude other files to review this content.", file.SizeBytes, 0));
                continue;
            }

            try
            {
                var fileBudget = Math.Min(remainingCharacters, Math.Max(1, remainingCharacters / (evidenceFiles.Length - index)));
                var excerpt = await extractor.ExtractAsync(file, fileLimitBytes, fileBudget, cancellationToken);
                extracted.Add(excerpt);
                remainingCharacters -= Math.Max(excerpt.SelectedCharacters, excerpt.Content.Length);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                logger.LogWarning("Evidence extraction failed for {FileName}: {ErrorType}", file.Name, exception.GetType().Name);
                extracted.Add(new EvidenceExcerpt(file.Name, file.Category,
                    "File could not be read.", "Extraction failed; no file content was selected.", file.SizeBytes, 0));
            }
        }

        Report(progress, "Extracting content", $"Prepared {extracted.Sum(item => item.SelectedCharacters):N0} text characters from {extracted.Count} files.", 2, 5);
        var errorSignature = BuildErrorSignature(extracted);
        var fingerprint = fingerprinter.CreateFingerprint(context.SystemName ?? "Unknown", context.IntegrationName ?? "Unknown", errorSignature);
        Report(progress, "Detecting errors", errorSignature.Length == 0 ? "No error signature was confidently extracted." : "Built a normalized error signature from selected evidence.", 3, 5);

        var history = await historyRepository.GetRecentAsync(500, cancellationToken);
        var priorIncidents = history.Where(item => !string.Equals(item.IncidentId, context.IncidentId, StringComparison.OrdinalIgnoreCase)).ToArray();
        var matches = recurrenceMatcher.FindMatches(context.SystemName ?? "", context.IntegrationName ?? "", errorSignature, priorIncidents, settings.Value.SimilarityThreshold);
        Report(progress, "Checking history", matches.Count == 0 ? "No historical match met the configured threshold." : $"Found {matches.Count} possible historical match(es).", 4, 5);

        var preparedContext = context with { Files = selectedFiles, ProblemDescription = problemDescription };
        Report(progress, "Evidence ready for review", "No evidence has been sent to an AI provider. Review the selected excerpts before continuing.", 5, 5);
        return new PreparedInvestigation(preparedContext, extracted, matches, errorSignature, fingerprint);
    }

    public async Task<IncidentInvestigationResult> InvestigateAsync(
        PreparedInvestigation prepared,
        IProgress<InvestigationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var context = prepared.Context;
        var request = promptBuilder.BuildInvestigationRequest(context, prepared.Evidence, prepared.HistoricalMatches);
        Report(progress, "AI investigation", $"Sending {request.UserContent.Length:N0} characters of reviewed evidence to the selected AI provider.", 1, 3);
        var rawResponse = await llmClient.CompleteAsync(request, cancellationToken);
        var ai = responseParser.Parse(rawResponse);
        ValidateEvidenceReferences(ai, prepared);

        var recurrence = prepared.HistoricalMatches.Count == 0 ? RecurrenceStatus.NewIssue : RecurrenceStatus.PossibleRecurrence;
        var bestMatch = prepared.HistoricalMatches.FirstOrDefault();
        if (bestMatch is not null
            && string.Equals(bestMatch.Incident.ErrorFingerprint, prepared.ErrorFingerprint, StringComparison.OrdinalIgnoreCase)
            && bestMatch.Similarity >= 0.9)
        {
            recurrence = RecurrenceStatus.ConfirmedRecurrence;
        }

        var result = new IncidentInvestigationResult(
            ai.IncidentSummary,
            ai.TeamsSummary,
            ai.ProblemStatement,
            ai.RootCause,
            Enum.Parse<EvidenceConfidence>(ai.RootCauseConfidence, true),
            ai.Evidence,
            ai.RecommendedActions,
            ai.AffectedSystems,
            ai.ErrorMessages,
            ai.CorrelationIds,
            ai.Attachments.Select(item => new AttachmentRecommendation(item.FileName, item.Recommended, item.Reason)).ToArray(),
            recurrence,
            prepared.HistoricalMatches,
            bestMatch?.Incident.JiraKey,
            InvestigationArtifactFormatter.FormatStory(ai.JiraStory),
            ai.JiraComment,
            prepared.ErrorSignature,
            prepared.ErrorFingerprint,
            DateTime.UtcNow);

        Report(progress, "Preparing results", "Validated the investigation response; no external ticket or message was created.", 2, 3);
        var historicalIncident = new HistoricalIncident(context.IncidentId, result.CreatedUtc, result.IncidentSummary,
            context.SystemName ?? "Unknown", context.IntegrationName ?? "Unknown", prepared.ErrorFingerprint, prepared.ErrorSignature,
            result.RootCause, null, null, recurrence.ToString(), string.Join(' ', ai.AffectedSystems),
            string.Join(Environment.NewLine, ai.RecommendedActions));
        await historyRepository.SaveAsync(historicalIncident, cancellationToken);
        Report(progress, "Investigation complete", "Results are ready for review; no external Jira or Teams action was taken.", 3, 3);
        return result;
    }

    public Task UpdateJiraReferenceAsync(string incidentId, string jiraKey, CancellationToken cancellationToken) =>
        historyRepository.UpdateJiraReferenceAsync(incidentId, jiraKey, cancellationToken);

    private static void ValidateEvidenceReferences(LlmInvestigationResponse response, PreparedInvestigation prepared)
    {
        var evidenceNames = prepared.Evidence.Select(item => item.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var historyIds = prepared.HistoricalMatches.Select(match => match.Incident.IncidentId).ToArray();
        foreach (var item in response.Evidence)
        {
            var referencesEvidence = evidenceNames.Any(name => item.Contains(name, StringComparison.OrdinalIgnoreCase))
                || item.Contains("problem description", StringComparison.OrdinalIgnoreCase)
                || historyIds.Any(id => item.Contains(id, StringComparison.OrdinalIgnoreCase));
            if (!referencesEvidence)
            {
                throw new InvalidDataException("The AI returned an evidence statement without a supplied source reference. No output was generated.");
            }
        }

        if (response.Attachments.Any(item => !evidenceNames.Contains(item.FileName)))
        {
            throw new InvalidDataException("The AI recommended an attachment that was not included in the reviewed evidence. No output was generated.");
        }
    }

    private static string BuildErrorSignature(IEnumerable<EvidenceExcerpt> evidence)
    {
        var matches = evidence.SelectMany(item => item.Content.Split('\n')
            .Where(line => ErrorPattern().IsMatch(line))
            .Select(line => line.Trim()))
            .Take(20)
            .ToArray();
        var signature = string.Join(Environment.NewLine, matches);
        return signature.Length > 4_000 ? signature[..4_000] : signature;
    }

    private static void Report(IProgress<InvestigationProgress>? progress, string stage, string message, int step, int total) =>
        progress?.Report(new InvestigationProgress(stage, message, step, total));

    [GeneratedRegex(@"\b(error|exception|fail(?:ed|ure)?|timeout|timed out|denied|invalid|fatal|stack trace|http\s*5\d\d)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ErrorPattern();
}
