namespace IntegrationIncidentInvestigator.Core.Models;

public sealed class InvestigationSettings
{
    public int MaxFileSizeMB { get; set; } = 100;
    public int MaxContextCharacters { get; set; } = 100_000;
    public double SimilarityThreshold { get; set; } = 0.75;
    public bool AllowImageUpload { get; set; }
    public int MaxImagePreviewMB { get; set; } = 20;
}
