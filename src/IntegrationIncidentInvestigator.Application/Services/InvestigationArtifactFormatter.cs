using System.Text;
using System.Text.Json;
using IntegrationIncidentInvestigator.Application.Contracts;
using IntegrationIncidentInvestigator.Application.Models;
using IntegrationIncidentInvestigator.Core.Models;

namespace IntegrationIncidentInvestigator.Application.Services;

public sealed class InvestigationArtifactFormatter : IInvestigationArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string FormatStory(JiraStory story) => $"""
        {story.Title}

        Objective
        {story.Objective}

        User story
        {story.UserStory}

        Description
        {story.Description}

        Errors observed
        {string.Join(Environment.NewLine, story.ErrorsObserved.Select(error => $"- {error}"))}

        Scope
        {string.Join(Environment.NewLine, story.Scope.Select(item => $"- {item}"))}

        Acceptance criteria
        {string.Join(Environment.NewLine, story.AcceptanceCriteria.Select((item, index) => $"{index + 1}. {item}"))}
        """;

    public string GetJiraText(IncidentInvestigationResult result, JiraMode jiraMode)
    {
        var content = jiraMode == JiraMode.CreateNew ? result.JiraContent : result.JiraComment;
        var attachments = result.Attachments.Where(item => item.Recommended).ToArray();
        var attachmentText = attachments.Length == 0
            ? "None selected."
            : string.Join(Environment.NewLine, attachments.Select(item => $"- {item.FileName}: {item.Reason}"));
        var jiraReference = jiraMode == JiraMode.CommentOnExisting && !string.IsNullOrWhiteSpace(result.ExistingIssueReference)
            ? $"Jira: {result.ExistingIssueReference}{Environment.NewLine}{Environment.NewLine}"
            : "";
        return $"{jiraReference}{content}{Environment.NewLine}{Environment.NewLine}User-confirmed attachments (authoritative):{Environment.NewLine}{attachmentText}";
    }

    public async Task SaveAsync(string sourceFolder, IncidentInvestigationResult result, JiraMode jiraMode,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceFolder))
        {
            throw new DirectoryNotFoundException("The incident folder no longer exists; results were not saved.");
        }

        await WriteTextAsync(Path.Combine(sourceFolder, "investigation.json"), JsonSerializer.Serialize(result, JsonOptions), cancellationToken);
        await WriteTextAsync(Path.Combine(sourceFolder, "investigation.txt"), BuildReport(result), cancellationToken);
        await WriteTextAsync(Path.Combine(sourceFolder, "teams-summary.txt"), result.TeamsSummary, cancellationToken);
        await WriteTextAsync(Path.Combine(sourceFolder, "attachment-recommendations.txt"), BuildAttachments(result.Attachments), cancellationToken);

        var jiraFileName = jiraMode == JiraMode.CreateNew ? "jira.txt" : "jira-comment.txt";
        await WriteTextAsync(Path.Combine(sourceFolder, jiraFileName), GetJiraText(result, jiraMode), cancellationToken);
    }

    private static string BuildReport(IncidentInvestigationResult result) => $"""
        Incident investigation
        ======================

        Summary: {result.IncidentSummary}
        Root cause confidence: {result.RootCauseConfidence}
        Recurrence: {result.Recurrence}
        Existing issue: {result.ExistingIssueReference ?? "None identified"}
        Root cause: {result.RootCause}

        Evidence:
        {string.Join(Environment.NewLine, result.Evidence.Select(item => $"- {item}"))}

        Recommended actions:
        {string.Join(Environment.NewLine, result.RecommendedActions.Select(item => $"- {item}"))}

        Error signature: {result.ErrorSignature}
        Fingerprint: {result.ErrorFingerprint}
        """;

    private static string BuildAttachments(IEnumerable<AttachmentRecommendation> recommendations) =>
        string.Join(Environment.NewLine + Environment.NewLine, recommendations.Select(item =>
            $"{(item.Recommended ? "Recommended" : "Not recommended")}: {item.FileName}{Environment.NewLine}{item.Reason}"));

    private static Task WriteTextAsync(string path, string content, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, content, new UTF8Encoding(false), cancellationToken);
}
