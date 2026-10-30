namespace IntegrationIncidentInvestigator.Core.Models;

public sealed record EvidenceExcerpt(
    string FileName,
    FileCategory Category,
    string Content,
    string SelectionReason,
    long OriginalSizeBytes,
    int SelectedCharacters,
    bool IsImage = false);
