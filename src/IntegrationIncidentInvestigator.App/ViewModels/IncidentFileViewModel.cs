using IntegrationIncidentInvestigator.Core.Models;

namespace IntegrationIncidentInvestigator.App.ViewModels;

public sealed class IncidentFileViewModel : ObservableObject
{
    private bool _includeInInvestigation;
    private bool _includeAsAttachment;
    private bool _attachmentSelectionOverridden;
    private string _attachmentReason;

    public IncidentFileViewModel(IncidentFile file)
    {
        File = file;
        _includeInInvestigation = file.IncludeInInvestigation;
        _includeAsAttachment = file.RecommendAttachment;
        _attachmentReason = file.RelevanceReason;
    }

    public IncidentFile File { get; }
    public string Name => File.Name;
    public string Category => File.Category.ToString();
    public string Size => File.SizeBytes < 1024 * 1024 ? $"{File.SizeBytes / 1024d:N1} KB" : $"{File.SizeBytes / 1024d / 1024d:N1} MB";
    public DateTime LastModified => File.LastModifiedUtc;
    public string Relevance => File.IsLikelyRelevant ? "Likely" : "Review";
    public string RelevanceReason => File.ScanError ?? File.RelevanceReason;
    public bool IsImage => File.Category == FileCategory.Screenshot;

    public bool IncludeInInvestigation
    {
        get => _includeInInvestigation;
        set => SetProperty(ref _includeInInvestigation, value);
    }

    public bool IncludeAsAttachment
    {
        get => _includeAsAttachment;
        set
        {
            if (SetProperty(ref _includeAsAttachment, value))
            {
                _attachmentSelectionOverridden = true;
            }
        }
    }

    public void ApplyRecommendation(bool recommended, string reason)
    {
        AttachmentReason = reason;
        if (!_attachmentSelectionOverridden)
        {
            SetProperty(ref _includeAsAttachment, recommended, nameof(IncludeAsAttachment));
        }
    }

    public string AttachmentReason
    {
        get => _attachmentReason;
        set => SetProperty(ref _attachmentReason, value);
    }
}
